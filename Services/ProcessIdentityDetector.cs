using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ClassScreenLock.Services;

/// <summary>
/// 进程身份识别器。
///
/// 背景：原拦截逻辑只按"进程名"与"绝对路径"匹配，攻击者把 powershell.exe（或 cmd.exe 等）
/// 复制出来改一个名字运行，进程名与路径都不命中任何规则，防护即被绕过。
///
/// 原理：复制/改名不会改变文件本身的身份，因此改为对进程的可执行文件做三重识别：
///   1. PE 资源中的 OriginalFilename（如 POWERSHELL.EXE）属于已知敏感系统工具；
///   2. 文件 SHA256 与官方目录中的敏感系统工具一致（防御 PE 资源被篡改的场景）；
///   3. 进程 EXE 路径读取优先使用 QueryFullProcessImageName，避免 32/64 位路径重定向干扰。
///
/// 语义：本类只负责"识别敏感工具身份"，不决定是否拦截。
///   官方目录内的官方工具（含改名）与复制出去的伪装工具都会被识别出来，
///   是否拦截由调用方（AppBlockingService）结合防护开关与子项规则决定。
/// </summary>
public static class ProcessIdentityDetector
{
    /// <summary>
    /// 已知敏感系统工具（不含路径，使用 OriginalFilename 匹配）。
    /// 注意：MUI 本地化资源可能导致官方文件 OriginalFilename 带 ".MUI" 后缀，
    /// 匹配前会先做规范化（去掉尾部 ".MUI"）。
    /// </summary>
    private static readonly HashSet<string> SensitiveFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "POWERSHELL.EXE",
        "PWSH.EXE",
        "POWERSHELL_ISE.EXE",
        "CMD.EXE",
        "CSCRIPT.EXE",
        "WSCRIPT.EXE",
        "TASKMGR.EXE",
        "REGEDIT.EXE",
        "REG.EXE",
        "TASKKILL.EXE",
        "TASKLIST.EXE",
        "MSHTA.EXE"
    };

    /// <summary>
    /// 敏感工具的"描述"（FileDescription，即文件属性→详细信息→"描述"栏）特征映射。
    /// 复制/改名不改变 PE 资源中的描述，且该字段不随系统语言本地化，
    /// 可作为 OriginalFilename 之外的第二个稳定身份特征。
    /// 实测值（Windows 10/11）：powershell → "Windows PowerShell"，cmd → "Windows Command Processor"。
    /// </summary>
    private static readonly Dictionary<string, string> DescriptionToTool = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Windows PowerShell"] = "POWERSHELL.EXE",
        ["PowerShell"] = "PWSH.EXE",
        ["Windows PowerShell ISE"] = "POWERSHELL_ISE.EXE",
        ["Windows Command Processor"] = "CMD.EXE",
        ["Windows 命令处理程序"] = "CMD.EXE",
        ["Microsoft ® Console Based Script Host"] = "CSCRIPT.EXE",
        ["Microsoft (R) Console Based Script Host"] = "CSCRIPT.EXE",
        ["Microsoft ® Windows Based Script Host"] = "WSCRIPT.EXE",
        ["Microsoft (R) Windows Based Script Host"] = "WSCRIPT.EXE",
        ["Task Manager"] = "TASKMGR.EXE",
        ["Windows Task Manager"] = "TASKMGR.EXE",
        ["Registry Editor"] = "REGEDIT.EXE",
        ["Registry Console Tool"] = "REG.EXE",
        ["Terminates Processes"] = "TASKKILL.EXE",
        ["Kills a process"] = "TASKKILL.EXE",
        ["Lists the current running tasks"] = "TASKLIST.EXE",
        ["Displays a list of processes"] = "TASKLIST.EXE",
        ["Microsoft (R) HTML Application host"] = "MSHTA.EXE"
    };

    /// <summary>
    /// 敏感工具 → 官方合法目录集合（绝对路径，基于真实系统目录构建）。
    /// </summary>
    private static readonly Lazy<Dictionary<string, HashSet<string>>> OfficialDirectoriesByTool =
        new(BuildOfficialDirectories);

    /// <summary>
    /// 官方敏感工具文件 → SHA256 哈希（懒加载，启动后只计算一次）。
    /// </summary>
    private static readonly Lazy<Dictionary<string, string>> OfficialFileHashes = new(BuildOfficialFileHashes);

    /// <summary>
    /// 检测结果缓存：exePath → 检测结果。短 TTL，避免监控循环每个周期重复读 PE 资源 / 算哈希。
    /// </summary>
    private struct DetectionEntry
    {
        public bool IsSensitiveTool;
        public string Identity;
        public bool InOfficialDirectory;
        public DateTime Stamp;
    }

    private static readonly ConcurrentDictionary<string, DetectionEntry> _detectCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan DetectCacheTtl = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 识别进程 EXE 是否属于敏感系统工具（powershell/cmd/wscript 等），
    /// 并给出其身份（如 POWERSHELL.EXE）与是否位于官方合法目录。
    ///
    /// 语义说明：复制/改名不改变文件身份（PE 资源 OriginalFilename/FileDescription、SHA256 均不变），
    /// 因此"官方目录内改名的官方工具"与"复制出去的伪装工具"都能被识别。
    /// 是否拦截由调用方结合防护开关/子项规则决定：
    ///   官方目录内 → 按对应子项规则开关拦截；非官方目录 → 作为伪装兜底拦截。
    /// </summary>
    /// <param name="exePath">进程可执行文件绝对路径（可为 null/空）。</param>
    /// <param name="matchedIdentity">命中时输出真实身份（如 POWERSHELL.EXE）。</param>
    /// <param name="inOfficialDirectory">命中时输出该 EXE 是否位于对应工具的官方合法目录。</param>
    /// <returns>true 表示识别为敏感系统工具。</returns>
    public static bool IdentifySensitiveTool(string? exePath, out string matchedIdentity, out bool inOfficialDirectory)
    {
        matchedIdentity = string.Empty;
        inOfficialDirectory = false;
        if (string.IsNullOrWhiteSpace(exePath)) return false;

        string fullPath;
        try { fullPath = Path.GetFullPath(exePath); }
        catch { return false; }

        if (_detectCache.TryGetValue(fullPath, out var hit) &&
            (DateTime.UtcNow - hit.Stamp) < DetectCacheTtl)
        {
            matchedIdentity = hit.Identity;
            inOfficialDirectory = hit.InOfficialDirectory;
            return hit.IsSensitiveTool;
        }

        var isTool = IdentifyCore(fullPath, out var identity, out var inOfficial);
        _detectCache[fullPath] = new DetectionEntry
        {
            IsSensitiveTool = isTool,
            Identity = identity,
            InOfficialDirectory = inOfficial,
            Stamp = DateTime.UtcNow
        };
        if (_detectCache.Count > 2048) _detectCache.Clear();

        matchedIdentity = identity;
        inOfficialDirectory = inOfficial;
        return isTool;
    }

    /// <summary>
    /// 判断进程 EXE 是否是 PowerShell 身份（官方原版或复制/改名伪装均可，供临时放行机制复用）。
    /// </summary>
    public static bool IsPowerShellIdentity(string? exePath)
    {
        if (!IdentifySensitiveTool(exePath, out var identity, out _)) return false;
        return string.Equals(identity, "POWERSHELL.EXE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(identity, "PWSH.EXE", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 读取进程可执行文件的真实路径（QueryFullProcessImageName，失败回退 MainModule）。
    /// 注意：调用前需保证 process 未被 Dispose，调用方负责释放。
    /// </summary>
    public static string? QueryProcessImageName(Process? process)
    {
        if (process == null) return null;
        try
        {
            var handle = process.Handle;
            if (handle == IntPtr.Zero) return null;

            var buffer = new char[1024];
            var size = buffer.Length;
            if (QueryFullProcessImageName(handle, 0, buffer, ref size) && size > 0)
            {
                return new string(buffer, 0, size);
            }
        }
        catch
        {
            // 受保护进程 / 权限不足时回退到 MainModule（由调用方处理）
        }
        return null;
    }

    /// <summary>
    /// 核心识别逻辑：只要文件身份是敏感系统工具即返回 true，并输出身份与"是否位于官方合法目录"。
    /// 拦截与否（按子项规则 / 伪装兜底）由调用方结合防护开关决定，不在此处过滤。
    /// </summary>
    private static bool IdentifyCore(string exePath, out string identity, out bool inOfficialDirectory)
    {
        identity = string.Empty;
        inOfficialDirectory = false;
        var dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(dir)) return false;

        // 1) PE 资源识别（OriginalFilename / FileDescription 任一命中敏感特征）
        //    复制/改名不改变 PE 资源；官方系统文件经 MUI 间接资源可能带 ".MUI" 后缀，需规范化。
        string? original = null, description = null;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            original = vi.OriginalFilename?.Trim();
            description = vi.FileDescription?.Trim();
        }
        catch
        {
            // 读不到资源（文件被锁定/权限不足）时交给哈希兜底
        }

        if (!string.IsNullOrWhiteSpace(original) &&
            original.EndsWith(".MUI", StringComparison.OrdinalIgnoreCase))
        {
            original = original[..^4];
        }

        var matchedTool = MatchSensitiveIdentity(original, description);
        if (matchedTool != null)
        {
            identity = matchedTool;
            inOfficialDirectory = IsInOfficialDirectory(dir, matchedTool);
            return true;
        }

        // 2) 哈希兜底：目标文件与官方敏感工具内容一致（防御 PE 资源被篡改后特征字段全部失效）
        var targetHash = ComputeFullSha256(exePath);
        if (string.IsNullOrEmpty(targetHash)) return false;

        foreach (var kv in OfficialFileHashes.Value)
        {
            if (!string.Equals(kv.Value, targetHash, StringComparison.OrdinalIgnoreCase)) continue;

            var toolName = Path.GetFileName(kv.Key);
            identity = toolName;
            inOfficialDirectory = IsInOfficialDirectory(dir, toolName);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 组合 PE 资源特征，识别敏感工具身份。OriginalFilename 优先，FileDescription 兜底。
    /// </summary>
    private static string? MatchSensitiveIdentity(string? originalFilename, string? fileDescription)
    {
        if (!string.IsNullOrWhiteSpace(originalFilename) &&
            SensitiveFileNames.Contains(originalFilename))
        {
            return originalFilename;
        }

        if (!string.IsNullOrWhiteSpace(fileDescription) &&
            DescriptionToTool.TryGetValue(fileDescription, out var tool))
        {
            return tool;
        }

        return null;
    }

    private static bool IsInOfficialDirectory(string dir, string fileName)
    {
        if (!OfficialDirectoriesByTool.Value.TryGetValue(fileName, out var officialDirs)) return false;

        var norm = dir.TrimEnd('\\');
        foreach (var official in officialDirs)
        {
            if (string.Equals(norm, official, StringComparison.OrdinalIgnoreCase)) return true;

            // pwsh（PowerShell Core）安装目录带版本子目录，如 "C:\Program Files\PowerShell\7"，做前缀匹配
            if (string.Equals(fileName, "PWSH.EXE", StringComparison.OrdinalIgnoreCase) &&
                norm.StartsWith(official.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static Dictionary<string, HashSet<string>> BuildOfficialDirectories()
    {
        var root = Path.GetDirectoryName(Environment.SystemDirectory) ?? @"C:\Windows";
        var system32 = Environment.SystemDirectory;
        var syswow64 = Path.Combine(root, "SysWOW64");
        var psDir = Path.Combine(system32, "WindowsPowerShell", "v1.0");
        var psWow = Path.Combine(syswow64, "WindowsPowerShell", "v1.0");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var psCoreRoot = Path.Combine(programFiles, "PowerShell");

        return new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["POWERSHELL.EXE"] = new(StringComparer.OrdinalIgnoreCase) { psDir, psWow },
            ["PWSH.EXE"] = new(StringComparer.OrdinalIgnoreCase) { psCoreRoot },
            ["POWERSHELL_ISE.EXE"] = new(StringComparer.OrdinalIgnoreCase) { psDir, psWow },
            ["CMD.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["CSCRIPT.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["WSCRIPT.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["TASKMGR.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["REGEDIT.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["REG.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["TASKKILL.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["TASKLIST.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 },
            ["MSHTA.EXE"] = new(StringComparer.OrdinalIgnoreCase) { system32, syswow64 }
        };
    }

    private static Dictionary<string, string> BuildOfficialFileHashes()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var root = Path.GetDirectoryName(Environment.SystemDirectory) ?? @"C:\Windows";
        var system32 = Environment.SystemDirectory;
        var syswow64 = Path.Combine(root, "SysWOW64");

        var candidates = new List<string>
        {
            Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(system32, "WindowsPowerShell", "v1.0", "powershell_ise.exe"),
            Path.Combine(system32, "cmd.exe"),
            Path.Combine(system32, "cscript.exe"),
            Path.Combine(system32, "wscript.exe"),
            Path.Combine(system32, "taskmgr.exe"),
            Path.Combine(system32, "regedit.exe"),
            Path.Combine(system32, "reg.exe"),
            Path.Combine(system32, "taskkill.exe"),
            Path.Combine(system32, "tasklist.exe"),
            Path.Combine(system32, "mshta.exe"),
            Path.Combine(syswow64, "cmd.exe"),
            Path.Combine(syswow64, "cscript.exe"),
            Path.Combine(syswow64, "wscript.exe"),
            Path.Combine(syswow64, "regedit.exe"),
            Path.Combine(syswow64, "reg.exe"),
            Path.Combine(syswow64, "taskkill.exe"),
            Path.Combine(syswow64, "tasklist.exe"),
            Path.Combine(syswow64, "mshta.exe"),
            Path.Combine(syswow64, "WindowsPowerShell", "v1.0", "powershell_ise.exe")
        };

        // pwsh（PowerShell Core）：扫描 "Program Files\PowerShell\<版本>\pwsh.exe"
        try
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var psCoreRoot = Path.Combine(programFiles, "PowerShell");
            if (Directory.Exists(psCoreRoot))
            {
                foreach (var versionDir in Directory.GetDirectories(psCoreRoot))
                {
                    var pwshPath = Path.Combine(versionDir, "pwsh.exe");
                    if (File.Exists(pwshPath)) candidates.Add(pwshPath);
                }
            }
        }
        catch
        {
            // 未安装 PowerShell Core 时忽略
        }

        foreach (var file in candidates)
        {
            if (!File.Exists(file)) continue;
            var hash = ComputeFullSha256(file);
            if (!string.IsNullOrEmpty(hash)) result[file] = hash;
        }
        return result;
    }

    private static string? ComputeFullSha256(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, char[] lpExeName, ref int lpdwSize);
}

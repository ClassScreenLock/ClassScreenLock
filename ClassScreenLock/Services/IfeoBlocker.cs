using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace ClassScreenLock.Services;

/// <summary>
/// IFEO（Image File Execution Options，映像劫持）拦截管理器。
///
/// 原理：系统启动任何进程前都会检查注册表
///   HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{进程名}.exe
/// 若存在 Debugger 值，则改为启动 Debugger 指向的程序（原 exe 路径与参数作为命令行附加），
/// 且该劫持在 CreateProcess 层生效——无论从资源管理器、开始菜单还是命令行启动都会被接管，
/// 没有任何轮询空窗期，也无法被"立刻关掉"绕过。
///
/// 架构（与 GroupPolicyBlocker 同模式，由 AppBlockingService.RefreshDynamicState 动态启停）：
///   1. 防护激活（锁屏 / 基础 / 手动 / 子项任一启用）时，对生效规则中的进程名写 Debugger 键，
///      指向同目录的独立重定向程序 CSL.IfeoRedirector.exe；
///   2. 放行（软件自身合法调用）由 IfeoRedirector 判定：读取共享临时放行标记
///      （HKCU\Software\ClassScreenLock\AllowPowerShellUntil，主程序 AllowPowerShellTemporarily
///      与看门狗调用前写入）或检查命令行 allow-marker，命中则删除 IFEO 键后拉起真实 EXE；
///   3. 拒绝（学生尝试运行）则直接退出，进程无法启动；
///   4. 防护关闭 / 程序退出时清理全部 Debugger 值，防策略残留。
///
/// 硬边界（由 WMI 实时拦截 + 高频轮询兜底覆盖）：IFEO 键按"文件名"精确匹配，
/// 复制后改名的 exe（如 copy.exe）不命中 IFEO，仍由身份识别 + 轮询拦截。
/// </summary>
public static class IfeoBlocker
{
    private const string IfeoRoot = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string DebuggerValueName = "Debugger";
    private const string DeploymentDirName = "ClassScreenLock";
    private const string RedirectorFileName = "CSL.IfeoRedirector.exe";

    // 已知敏感目标：SyncFromRegistry 扫描范围（默认保护规则的进程名超集）。
    private static readonly string[] KnownTargetNames =
    {
        "cmd", "powershell", "pwsh", "powershell_ise", "wscript", "cscript", "taskmgr", "resmon", "regedit"
    };

    private static readonly object _sync = new();
    private static readonly HashSet<string> _managedNames = new(StringComparer.OrdinalIgnoreCase);
    private static volatile bool _ifeoActive;
    // 上次成功应用的期望集合：集合未变且激活状态一致时跳过全部文件/注册表操作
    // （MonitorLoop 每 300-2000ms 调用一次 Enable，短路后避免每周期文件 I/O 与注册表键访问）
    private static HashSet<string>? _lastDesired;

    /// <summary>当前是否有我们写入的 IFEO Debugger 键（本进程内记忆）。</summary>
    public static bool IsActive => _ifeoActive;

    /// <summary>
    /// 重定向程序固定部署目录（机器级，所有用户可读可执行）：
    ///   %ProgramData%\ClassScreenLock\
    /// IFEO Debugger 值写入固定路径后，不随主程序安装目录变化而失效，
    /// 学生账户（非管理员）也能从该目录读取并执行重定向程序。
    /// </summary>
    private static string FixedDeployDir
    {
        get
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    DeploymentDirName);
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, DeploymentDirName);
            }
        }
    }

    /// <summary>
    /// 主程序实际 exe 所在目录。
    /// 单文件发布（PublishSingleFile）下 AppContext.BaseDirectory 指向临时解压目录
    /// （如 C:\Windows\SystemTemp\.net\ClassScreenLock\{hash}\），文件并不在那里；
    /// Environment.ProcessPath 始终指向真实运行的 exe（发布目录），必须优先用它。
    /// </summary>
    private static string ProcessDirectory
    {
        get
        {
            try
            {
                return Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            }
            catch
            {
                return AppContext.BaseDirectory;
            }
        }
    }

    /// <summary>
    /// 重定向程序路径：优先固定部署目录（%ProgramData%\ClassScreenLock\），
    /// 回退主程序实际 exe 所在目录（发布目录 / 开发目录）。
    /// </summary>
    private static string RedirectorPath
    {
        get
        {
            try
            {
                var fixedExe = Path.Combine(FixedDeployDir, RedirectorFileName);
                if (File.Exists(fixedExe)) return fixedExe;
                return Path.Combine(ProcessDirectory, RedirectorFileName);
            }
            catch
            {
                return RedirectorFileName;
            }
        }
    }

    /// <summary>
    /// 确保重定向程序已部署到固定目录并返回其实际路径：
    ///   1) 从主程序实际 exe 所在目录（单文件发布时为发布目录，含 CSL.IfeoRedirector.exe）
    ///      同步（覆盖）到 %ProgramData%\ClassScreenLock\，保证每次升级后固定目录中的
    ///      重定向程序是最新版本；
    ///   2) 固定目录已存在（如预部署）则直接使用；
    ///   3) 两者皆无则返回本地路径，由调用方判断是否可用。
    /// </summary>
    private static string EnsureRedirectorDeployed()
    {
        try
        {
            var fixedDir = FixedDeployDir;
            var fixedExe = Path.Combine(fixedDir, RedirectorFileName);
            var localDir = ProcessDirectory;
            var localExe = Path.Combine(localDir, RedirectorFileName);

            if (File.Exists(localExe))
            {
                Directory.CreateDirectory(fixedDir);
                foreach (var f in Directory.GetFiles(localDir, "CSL.IfeoRedirector.*"))
                {
                    try
                    {
                        File.Copy(f, Path.Combine(fixedDir, Path.GetFileName(f)), true);
                    }
                    catch
                    {
                        // 个别文件复制失败不影响主流程，下次启用时重试
                    }
                }
            }

            return File.Exists(fixedExe) ? fixedExe : localExe;
        }
        catch
        {
            return Path.Combine(ProcessDirectory, RedirectorFileName);
        }
    }

    private static string IfeoKeyPath(string processName) => $@"{IfeoRoot}\{processName}.exe";

    private static string NormalizeName(string name)
    {
        var t = name?.Trim() ?? string.Empty;
        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
        return t;
    }

    /// <summary>
    /// 启动时同步注册表现状：若存在指向我们重定向程序的 Debugger 键残留
    /// （上次进程异常退出未来得及清理），标记为 active，由调用方按当前
    /// 锁屏/防护状态决定保留或清除。
    /// </summary>
    public static void SyncFromRegistry()
    {
        lock (_sync)
        {
            var redirector = RedirectorPath;
            foreach (var name in KnownTargetNames)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(IfeoKeyPath(name), false);
                    var value = key?.GetValue(DebuggerValueName) as string;
                    if (string.IsNullOrWhiteSpace(value)) continue;

                    // 仅认领"指向我们重定向程序"的键；用户自己的调试器配置不得动。
                    if (value.Contains(redirector, StringComparison.OrdinalIgnoreCase) ||
                        value.Contains("CSL.IfeoRedirector", StringComparison.OrdinalIgnoreCase))
                    {
                        _managedNames.Add(name);
                    }
                }
                catch
                {
                    // 权限不足或键损坏：跳过，等待下次刷新
                }
            }

            _ifeoActive = _managedNames.Count > 0;
        }
    }

    /// <summary>
    /// 启用 IFEO 劫持：对目标进程名写 Debugger 键指向重定向程序。
    /// 幂等：已存在且值正确的键跳过；不再需要的旧键自动清理。
    /// </summary>
    public static void Enable(IEnumerable<string> processNames)
    {
        if (processNames == null) return;

        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in processNames)
        {
            var name = NormalizeName(raw);
            if (!string.IsNullOrWhiteSpace(name)) desired.Add(name);
        }

        // 短路：期望集合与上次一致且激活状态匹配时直接返回，
        // 避免 EnsureRedirectorDeployed 的文件 I/O 与逐键注册表访问每周期重复执行
        lock (_sync)
        {
            if (_lastDesired != null &&
                _lastDesired.SetEquals(desired) &&
                _ifeoActive == (desired.Count > 0 || _managedNames.Count > 0))
            {
                return;
            }
        }

        // 确保重定向程序已部署到固定目录（%ProgramData%\ClassScreenLock\），
        // 使 Debugger 值指向的路径稳定，不随主程序目录变化而失效
        var redirector = EnsureRedirectorDeployed();
        if (!File.Exists(redirector))
        {
            LogService.Instance.Log("Error", "Ifeo", "Enable",
                "进程保护组件缺失，部分防护功能不可用");
            return;
        }

        lock (_sync)
        {
            // 1) 写入/修正需要劫持的进程名
            foreach (var name in desired)
            {
                try
                {
                    using var key = Registry.LocalMachine.CreateSubKey(IfeoKeyPath(name), true);
                    var expected = $"\"{redirector}\"";
                    var current = key?.GetValue(DebuggerValueName) as string;
                    if (!string.Equals(current, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        key?.SetValue(DebuggerValueName, expected, RegistryValueKind.String);
                    }
                    _managedNames.Add(name);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Error", "Ifeo", "Enable",
                        $"进程保护规则应用失败（{name}.exe）: {ex.Message}");
                }
            }

            // 2) 清理已不在目标集合中的旧键（如某子项规则被关闭）
            foreach (var old in _managedNames.ToList())
            {
                if (!desired.Contains(old))
                {
                    RemoveDebuggerValue(old);
                    _managedNames.Remove(old);
                }
            }

            _ifeoActive = _managedNames.Count > 0;
            _lastDesired = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>停用 IFEO 劫持：删除所有由本模块写入的 Debugger 值。</summary>
    public static void Disable()
    {
        lock (_sync)
        {
            foreach (var name in _managedNames.ToList())
            {
                RemoveDebuggerValue(name);
                _managedNames.Remove(name);
            }
            _ifeoActive = false;
            _lastDesired = null;
        }
    }

    /// <summary>删除单个进程名的 Debugger 值（仅删除值，保留键，避免误删系统其他配置）。</summary>
    private static void RemoveDebuggerValue(string processName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(IfeoKeyPath(processName), true);
            if (key == null) return;

            var value = key.GetValue(DebuggerValueName) as string;
            if (string.IsNullOrWhiteSpace(value)) return;

            // 只删我们写的值（指向重定向程序），用户自己的调试器配置不动。
            var redirector = RedirectorPath;
            if (value.Contains(redirector, StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CSL.IfeoRedirector", StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(DebuggerValueName, false);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "Ifeo", "Disable",
                $"进程保护规则清理失败（{processName}.exe）: {ex.Message}");
        }
    }
}

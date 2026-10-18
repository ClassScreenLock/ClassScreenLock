using System;
using System.IO;

namespace ClassScreenLock.Uninstaller.Helpers;

/// <summary>
/// 提供正确的应用程序目录路径。
/// 在 .NET 单文件发布模式下，AppContext.BaseDirectory 指向临时解压目录，
/// 而 Environment.ProcessPath 指向实际的 exe 文件位置。
/// 
/// 卸载程序独立运行，需要找到主程序所在目录以读取 security.json 等配置。
/// </summary>
public static class AppPathHelper
{
    /// <summary>
    /// 卸载程序自身所在目录
    /// </summary>
    public static string UninstallerDirectory { get; } = ResolveUninstallerDirectory();

    /// <summary>
    /// 主程序目录（卸载程序与主程序同目录部署时为同一目录）
    /// </summary>
    public static string AppDirectory { get; } = ResolveAppDirectory();

    /// <summary>
    /// 主程序 Data 目录（存放 security.json 等配置文件）
    /// </summary>
    public static string DataDirectory => Path.Combine(AppDirectory, "Data");

    /// <summary>
    /// 安全设置文件路径
    /// </summary>
    public static string SecuritySettingsPath => Path.Combine(DataDirectory, "security.json");

    private static string ResolveUninstallerDirectory()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                return Path.GetDirectoryName(processPath)
                    ?? throw new InvalidOperationException("无法解析进程路径的目录");
            }
        }
        catch { }

        return AppContext.BaseDirectory;
    }

    /// <summary>
    /// 解析主程序目录：优先从卸载程序同目录找主程序 exe，
    /// 如果找不到则尝试注册表、ProgramData 等位置。
    /// </summary>
    private static string ResolveAppDirectory()
    {
        // 1. 卸载程序与主程序同目录部署
        var uninstallerDir = UninstallerDirectory;
        var mainExe = Path.Combine(uninstallerDir, "ClassScreenLock.exe");
        if (File.Exists(mainExe))
        {
            return uninstallerDir;
        }

        // 2. 尝试从卸载程序所在目录的父目录查找（开发环境）
        var parentDir = Directory.GetParent(uninstallerDir)?.FullName;
        if (parentDir != null)
        {
            var mainProjectDir = Path.Combine(parentDir, "ClassScreenLock");
            if (Directory.Exists(mainProjectDir))
            {
                // 开发环境：检查 bin 目录
                var debugDir = Path.Combine(mainProjectDir, "bin", "Debug", "net9.0-windows");
                if (File.Exists(Path.Combine(debugDir, "ClassScreenLock.exe")))
                {
                    return debugDir;
                }
                var releaseDir = Path.Combine(mainProjectDir, "bin", "Release", "net9.0-windows", "win-x64");
                if (File.Exists(Path.Combine(releaseDir, "ClassScreenLock.exe")))
                {
                    return releaseDir;
                }
            }
        }

        // 3. 回退：卸载程序所在目录
        return uninstallerDir;
    }

    /// <summary>
    /// ProgramData 中的 ClassScreenLock 部署目录（IFEO 重定向程序固定部署位置）
    /// </summary>
    public static string ProgramDataDir
    {
        get
        {
            try
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ClassScreenLock");
            }
            catch
            {
                return Path.Combine(AppDirectory, "ProgramData");
            }
        }
    }
}

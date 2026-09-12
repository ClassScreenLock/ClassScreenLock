using System;
using System.IO;

namespace ClassScreenLock.Helpers;

/// <summary>
/// 提供正确的应用程序目录路径。
/// 在 .NET 单文件发布模式下，AppContext.BaseDirectory 指向临时解压目录，
/// 而 Environment.ProcessPath 指向实际的 exe 文件位置。
/// </summary>
public static class AppPathHelper
{
    public static string AppDirectory { get; } = ResolveAppDirectory();

    public static string DataDirectory => Path.Combine(AppDirectory, "Data");

    private static string ResolveAppDirectory()
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
}

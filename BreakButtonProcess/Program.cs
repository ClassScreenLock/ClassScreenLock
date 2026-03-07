using Avalonia;
using System;
using System.Runtime.InteropServices;

namespace BreakButtonProcess;

internal static class Program
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [STAThread]
    public static void Main(string[] args)
    {
        // 设置独立的 AppUserModelID
        try
        {
            SetCurrentProcessExplicitAppUserModelID("CSL.BreakButton");
        }
        catch { }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont();

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}

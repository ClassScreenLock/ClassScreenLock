using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;
using System.IO;

namespace ClassScreenLock;

sealed class Program
{
    private static Mutex? _appMutex;
    private const string AppGuid = "ClassScreenLock-8A31-D0624A328FE5";

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var isRestart = Array.Exists(args, a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));

        // 单实例检测
        _appMutex = new Mutex(true, AppGuid, out bool createdNew);
        if (!createdNew)
        {
            if (isRestart)
            {
                var acquired = false;
                var start = DateTime.UtcNow;
                while (!acquired && (DateTime.UtcNow - start) < TimeSpan.FromSeconds(15))
                {
                    try
                    {
                        acquired = _appMutex.WaitOne(TimeSpan.FromMilliseconds(250));
                    }
                    catch
                    {
                        acquired = false;
                    }
                }

                if (!acquired)
                {
                    MessageBox(IntPtr.Zero, "重启超时：原实例未退出。", "提示", MB_OK | MB_ICONINFORMATION);
                    return;
                }
            }
            else
            {
                TryNotifyExistingInstanceToShowMain();
                return;
            }
        }

        try
        {
            // 全局未处理异常捕获
            AppDomain.CurrentDomain.UnhandledException += (s, e) => 
            {
                LogCrash(e.ExceptionObject as Exception, "AppDomain.UnhandledException");
            };

            TaskScheduler.UnobservedTaskException += (s, e) => 
            {
                LogCrash(e.Exception, "TaskScheduler.UnobservedTaskException");
                e.SetObserved();
            };

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash(ex, "Program.Main.Catch");
            // 发生致命错误时退出，并返回退出代码 500 (对应用户提到的错误)
            Environment.Exit(500);
        }
        finally
        {
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Dispose();
            }
        }
    }

    private static void TryNotifyExistingInstanceToShowMain()
    {
        try
        {
            using var client = new NamedPipeClientStream("ClassScreenLock_IPC");
            client.Connect(500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine("SHOW_MAIN");
        }
        catch
        {
        }
    }

    private static void LogCrash(Exception? ex, string source)
    {
        if (ex == null) return;
        
        var message = $"[CRASH][{source}] {ex.Message}\nStack: {ex.StackTrace}";
        System.Diagnostics.Debug.WriteLine(message);
        
        try 
        {
            // 尝试写入日志文件
            Services.LogService.Instance.Log("Error", "Crash", source, ex.Message);
        }
        catch { /* 忽略日志写入失败 */ }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect();

#if DEBUG
        builder = builder.LogToTrace();
#endif

        return builder;
    }
}

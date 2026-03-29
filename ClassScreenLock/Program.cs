using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;
using System.IO;
using System.Diagnostics;
using ClassScreenLock.Helpers;
using ClassScreenLock.Services;

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
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [STAThread]
    public static void Main(string[] args)
    {
        // 尽早设置独立的 AppUserModelID，避免与看门狗进程分到同一组
        try
        {
            SetCurrentProcessExplicitAppUserModelID("ClassScreenLock.Main");
        }
        catch
        {
            // 忽略失败，不影响功能
        }

        // 删除退出标记文件（如果存在）
        try
        {
            var exitFlagFile = Path.Combine(AppContext.BaseDirectory, "exit.flag");
            if (File.Exists(exitFlagFile))
            {
                File.Delete(exitFlagFile);
                Console.WriteLine("Deleted exit.flag file");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting exit.flag: {ex.Message}");
        }

        var isRestart = Array.Exists(args, a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));

        // 单实例检测
        _appMutex = new Mutex(true, AppGuid, out bool createdNew);
        if (!createdNew)
        {
            if (isRestart)
            {
                var acquired = false;
                var start = DateTime.UtcNow;
                while (!acquired && (DateTime.UtcNow - start) < TimeSpan.FromSeconds(5))
                {
                    try
                    {
                        acquired = _appMutex.WaitOne(TimeSpan.FromMilliseconds(100));
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
            Cleanup();
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

    public static void StartWatchdogProcess()
    {
        try
        {
            // 检查当前运行的看门狗实例数量
            var existingWatchdogs = Process.GetProcessesByName("CSL.Watchdog");
            if (existingWatchdogs.Length >= 3)
            {
                LogCrash(new Exception($"Already have {existingWatchdogs.Length} watchdog instances running. No need to start more."), "Program.StartWatchdog");
                return;
            }
            
            var baseDir = AppContext.BaseDirectory;
            string watchdogExe = Path.Combine(baseDir, "CSL.Watchdog.exe");
            
            if (File.Exists(watchdogExe))
            {
                // 启动缺少的看门狗实例
                for (int i = 0; i < 3; i++)
                {
                    // 检查是否已经有该实例ID的看门狗在运行
                    bool instanceExists = false;
                    foreach (var process in existingWatchdogs)
                    {
                        try
                        {
                            // 尝试获取进程命令行参数，检查是否包含实例ID
                            string commandLine = GetCommandLine(process.Id);
                            if (commandLine.Contains($" {i}"))
                            {
                                instanceExists = true;
                                break;
                            }
                        }
                        catch { }
                    }
                    
                    if (!instanceExists)
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c start \"Watchdog Instance {i}\" \"{watchdogExe}\" {i}",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        Process.Start(startInfo);
                        LogCrash(new Exception($"CSL.Watchdog instance {i} started"), "Program.StartWatchdog");
                    }
                }
            }
            else
            {
                LogCrash(new Exception($"Watchdog executable not found: {watchdogExe}"), "Program.StartWatchdog");
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex, "Program.StartWatchdog");
        }
    }
    
    private static string GetCommandLine(int processId)
    {
        try
        {
            using (var process = Process.GetProcessById(processId))
            {
                return process.MainModule?.FileName + " " + string.Join(" ", process.StartInfo.Arguments);
            }
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void LogCrash(Exception? ex, string source)
    {
        if (ex == null) return;
        
        var message = $"[CRASH][{source}] {ex}";
        System.Diagnostics.Debug.WriteLine(message);
        
        try 
        {
            // 尝试写入日志文件
            Services.LogService.Instance.Log("Error", "Crash", source, ex.ToString());
        }
        catch { /* 忽略日志写入失败 */ }
    }

    private static void Cleanup()
    {
        try
        {
            if (_appMutex != null)
            {
                _appMutex.ReleaseMutex();
                _appMutex.Dispose();
            }
        }
        catch { }
        
        ProcessProtector.Cleanup();
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

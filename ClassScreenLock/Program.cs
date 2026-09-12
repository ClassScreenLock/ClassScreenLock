using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Management;
using ClassScreenLock.Helpers;
using ClassScreenLock.Services;

namespace ClassScreenLock;

sealed class Program
{
    private static Mutex? _appMutex;
    private const string AppGuid = "ClassScreenLock-8A31-D0624A328FE5";
    private static string _exitFlagFile = Path.Combine(Helpers.AppPathHelper.AppDirectory, "exit.dat");
    private static bool _isStartingWatchdog = false;
    private static readonly object _watchdogStartLock = new object();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID("ClassScreenLock.Main");
        }
        catch
        {
        }

        var isRestart = Array.Exists(args, a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase));

        if (isRestart)
        {
            WaitForPreviousInstanceExit();
        }

        DeleteExitFlagFile();

        _appMutex = new Mutex(true, AppGuid, out bool createdNew);
        if (!createdNew)
        {
            var acquired = false;
            var start = DateTime.UtcNow;
            var timeout = isRestart ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(2);
            
            while (!acquired && (DateTime.UtcNow - start) < timeout)
            {
                try
                {
                    acquired = _appMutex.WaitOne(TimeSpan.FromMilliseconds(200));
                }
                catch
                {
                    acquired = false;
                }
            }

            if (!acquired)
            {
                if (isRestart)
                {
                    Console.WriteLine("Restart timeout: previous instance did not exit in time.");
                }
                else
                {
                    TryNotifyExistingInstanceToShowMain();
                }
                return;
            }
        }

        try
        {
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
            Environment.Exit(500);
        }
        finally
        {
            CreateExitFlag();
            Cleanup();
        }
    }

    private static void WaitForPreviousInstanceExit()
    {
        var start = DateTime.UtcNow;
        var timeout = TimeSpan.FromSeconds(15);
        
        while ((DateTime.UtcNow - start) < timeout)
        {
            if (!File.Exists(_exitFlagFile))
            {
                Console.WriteLine("Previous instance exit flag not found, proceeding with startup.");
                return;
            }
            
            var encryptedData = File.ReadAllBytes(_exitFlagFile);
            if (encryptedData.Length == 0)
            {
                Console.WriteLine("Exit flag file is empty, deleting and proceeding.");
                DeleteExitFlagFile();
                return;
            }
            
            var decryptedData = DecryptExitFlag(encryptedData);
            if (decryptedData == null)
            {
                Console.WriteLine("Exit flag decryption failed, deleting and proceeding.");
                DeleteExitFlagFile();
                return;
            }
            
            var content = Encoding.UTF8.GetString(decryptedData);
            var parts = content.Split('|');
            
            if (parts.Length == 2 && int.TryParse(parts[0], out var pid))
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (process.HasExited)
                    {
                        Console.WriteLine($"Previous instance (PID {pid}) has exited. Waiting for cleanup...");
                        Thread.Sleep(500);
                        DeleteExitFlagFile();
                        return;
                    }
                }
                catch (ArgumentException)
                {
                    Console.WriteLine($"Previous instance (PID {pid}) no longer exists. Proceeding.");
                    DeleteExitFlagFile();
                    return;
                }
                catch
                {
                    Console.WriteLine("Cannot check previous instance status, proceeding.");
                    DeleteExitFlagFile();
                    return;
                }
            }
            else
            {
                Console.WriteLine("Exit flag format invalid, deleting and proceeding.");
                DeleteExitFlagFile();
                return;
            }
            
            Thread.Sleep(200);
        }
        
        Console.WriteLine("Timeout waiting for previous instance, forcing cleanup.");
        DeleteExitFlagFile();
    }

    private static byte[]? DecryptExitFlag(byte[] encryptedData)
    {
        try
        {
            if (encryptedData.Length < 32)
            {
                return null;
            }
            
            var key = new byte[16];
            var iv = new byte[16];
            
            Array.Copy(encryptedData, 0, key, 0, 16);
            Array.Copy(encryptedData, 16, iv, 0, 16);
            
            var payload = new byte[encryptedData.Length - 32];
            Array.Copy(encryptedData, 32, payload, 0, payload.Length);
            
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            
            using var decryptor = aes.CreateDecryptor();
            var decrypted = decryptor.TransformFinalBlock(payload, 0, payload.Length);
            
            return decrypted;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteExitFlagFile()
    {
        try
        {
            if (File.Exists(_exitFlagFile))
            {
                File.Delete(_exitFlagFile);
                Console.WriteLine("Deleted exit.dat file");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting exit.dat: {ex.Message}");
        }
    }

    internal static void CreateExitFlag()
    {
        try
        {
            var pid = Environment.ProcessId;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var content = $"{pid}|{timestamp}";
            var contentBytes = Encoding.UTF8.GetBytes(content);
            
            var key = new byte[16];
            var iv = new byte[16];
            
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
                rng.GetBytes(iv);
            }
            
            byte[] encryptedPayload;
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                
                using var encryptor = aes.CreateEncryptor();
                encryptedPayload = encryptor.TransformFinalBlock(contentBytes, 0, contentBytes.Length);
            }
            
            var finalData = new byte[key.Length + iv.Length + encryptedPayload.Length];
            Array.Copy(key, 0, finalData, 0, key.Length);
            Array.Copy(iv, 0, finalData, key.Length, iv.Length);
            Array.Copy(encryptedPayload, 0, finalData, key.Length + iv.Length, encryptedPayload.Length);
            
            File.WriteAllBytes(_exitFlagFile, finalData);
            Console.WriteLine("Created encrypted exit flag file");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating exit flag: {ex.Message}");
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
        lock (_watchdogStartLock)
        {
            if (_isStartingWatchdog)
            {
                return;
            }
            
            _isStartingWatchdog = true;
        }
        
        try
        {
            var existingWatchdogs = Process.GetProcessesByName("CSL.Watchdog");
            
            if (existingWatchdogs.Length >= 3)
            {
                return;
            }
            
            int needToStart = 3 - existingWatchdogs.Length;
            
            // 使用 Environment.ProcessPath 获取主程序路径，然后定位到同目录
            var mainExePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(mainExePath))
            {
                mainExePath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            
            string baseDir = !string.IsNullOrWhiteSpace(mainExePath)
                ? Path.GetDirectoryName(mainExePath) ?? Helpers.AppPathHelper.AppDirectory
                : Helpers.AppPathHelper.AppDirectory;
            
            string watchdogExe = Path.Combine(baseDir, "CSL.Watchdog.exe");

            if (File.Exists(watchdogExe))
            {
                for (int i = 0; i < needToStart; i++)
                {
                    try
                    {
                        var instanceId = existingWatchdogs.Length + i;
                        // 关键修复：不能再用 Process.Start 直接启动看门狗。C# 直接
                        // Process.Start 会让看门狗成为本进程的直接子进程，被分到同一个
                        // 进程树 / Job Object 下——主程序退出或被"结束进程树"时看门狗
                        // 会被连带清理，看门功能失效。
                        // 改用 WMI Win32_Process.Create 拉起看门狗：
                        //   1) 看门狗继承调用者（本进程）的令牌：主程序经 UIAccess
                        //      提权为 SYSTEM（UiAccessService 复制 winlogon 令牌重启）
                        //      时，看门狗同为 SYSTEM 权限，且保持在主程序的用户会话中
                        //      （无 Session 0 问题，看门狗重启主程序时 UI 正常）；
                        //      主程序未提权（管理员）时看门狗同样继承管理员权限，
                        //      两种模式都自动与主程序一致，不降权；
                        //   2) 看门狗的父进程变成 WMI 服务进程（WmiPrvSE），完全脱离
                        //      本进程树与 Job，主程序退出不影响看门狗；
                        //   3) 不经过 cmd.exe，锁屏时 DisableCMD 组策略不会误伤；
                        //   4) CSL.Watchdog 不在 IFEO 劫持名单，且在本软件
                        //      OwnProcessNames 白名单内，实时进程拦截会直接放行。
                        // 注意：不能改用 schtasks 计划任务——由 SYSTEM 创建的默认任务
                        // 会在 Session 0 运行，看门狗会跑到错误的会话导致重启主程序时
                        // UI 不可见；WMI 创建则始终跟随调用者的令牌与会话。
                        var commandLine = $"\"{watchdogExe}\" {instanceId}";
                        using var processClass = new ManagementClass("Win32_Process");
                        using var methodParams = processClass.GetMethodParameters("Create");
                        methodParams["CommandLine"] = commandLine;
                        using var result = processClass.InvokeMethod("Create", methodParams, null);
                        var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
                        if (returnValue != 0)
                        {
                            LogService.Instance.Log("Error", "Watchdog", "StartWatchdog",
                                $"看门狗实例 {instanceId} 启动失败（代码 {returnValue}），将由监测机制重试");
                        }
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "Watchdog", "StartWatchdog",
                            $"启动看门狗实例失败: {ex.Message}");
                    }

                    System.Threading.Thread.Sleep(500);
                }

                System.Threading.Thread.Sleep(1000);
            }
            else
            {
                LogService.Instance.Log("Error", "Watchdog", "StartWatchdog", "看门狗程序未找到，无法启动");
            }
        }
        catch (Exception ex)
        {
            LogCrash(ex, "Program.StartWatchdog");
        }
        finally
        {
            lock (_watchdogStartLock)
            {
                _isStartingWatchdog = false;
            }
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
            Services.LogService.Instance.Log("Error", "Crash", source, ex.ToString());
        }
        catch { }
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

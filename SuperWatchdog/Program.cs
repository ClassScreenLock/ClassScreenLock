using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CSL.SuperWatchdog;

class Program
{
    private static Process? _mainProcess;
    private static Process? _watchdogProcess;
    private static int _instanceId = 0;
    private static bool _shouldExit = false;
    private static readonly string _appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassScreenLock");
    private static readonly string _superWatchdogExe = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassScreenLock",
        "CSL.SuperWatchdog.exe");

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    [DllImport("kernel32.dll")]
    private static extern uint GetLastError();

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            _instanceId = args.Length > 0 && int.TryParse(args[0], out int id) ? id : 0;
            SetCurrentProcessExplicitAppUserModelID($"CSL.SuperWatchdog.Instance{_instanceId}");

            EnablePrivileges();
            SetWatchdogProtection();

            Console.WriteLine($"SuperWatchdog instance {_instanceId} started from: {AppContext.BaseDirectory}");

            if (!IsRunningFromAppData())
            {
                Console.WriteLine("Not running from AppData, copying to AppData...");
                if (CopyToAppData())
                {
                    Console.WriteLine("Successfully copied to AppData, starting AppData version...");
                    StartAppDataVersion(args);
                    return;
                }
                else
                {
                    Console.WriteLine("Failed to copy to AppData, continuing from current location.");
                }
            }

            StartOrAttachToProcesses();
            MonitorProcesses();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SuperWatchdog error: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            Cleanup();
        }
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    private static bool IsRunningFromAppData()
    {
        var currentDir = AppContext.BaseDirectory;
        return currentDir.StartsWith(_appDataDir, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CopyToAppData()
    {
        try
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(currentExe))
            {
                Console.WriteLine("Could not get current executable path.");
                return false;
            }

            if (!Directory.Exists(_appDataDir))
            {
                Directory.CreateDirectory(_appDataDir);
                Console.WriteLine($"Created AppData directory: {_appDataDir}");
            }

            if (File.Exists(_superWatchdogExe))
            {
                var currentVersion = File.GetLastWriteTime(currentExe);
                var appDataVersion = File.GetLastWriteTime(_superWatchdogExe);

                if (currentVersion <= appDataVersion)
                {
                    Console.WriteLine("AppData version is up-to-date or newer, skipping copy.");
                    return true;
                }

                Console.WriteLine("AppData version is older, updating...");
            }

            File.Copy(currentExe, _superWatchdogExe, true);
            Console.WriteLine($"Copied to AppData: {_superWatchdogExe}");

            var watchdogExe = Path.Combine(AppContext.BaseDirectory, "CSL.Watchdog.exe");
            if (File.Exists(watchdogExe))
            {
                var appDataWatchdog = Path.Combine(_appDataDir, "CSL.Watchdog.exe");
                File.Copy(watchdogExe, appDataWatchdog, true);
                Console.WriteLine($"Copied watchdog to AppData: {appDataWatchdog}");
            }

            var monitorExe = Path.Combine(AppContext.BaseDirectory, "MonitorProcess.exe");
            if (File.Exists(monitorExe))
            {
                var appDataMonitor = Path.Combine(_appDataDir, "MonitorProcess.exe");
                File.Copy(monitorExe, appDataMonitor, true);
                Console.WriteLine($"Copied monitor to AppData: {appDataMonitor}");
            }

            var breakButtonExe = Path.Combine(AppContext.BaseDirectory, "BreakButtonProcess.exe");
            if (File.Exists(breakButtonExe))
            {
                var appDataBreakButton = Path.Combine(_appDataDir, "BreakButtonProcess.exe");
                File.Copy(breakButtonExe, appDataBreakButton, true);
                Console.WriteLine($"Copied break button to AppData: {appDataBreakButton}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error copying to AppData: {ex.Message}");
            return false;
        }
    }

    private static void StartAppDataVersion(string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _superWatchdogExe,
                Arguments = string.Join(" ", args),
                UseShellExecute = true,
                CreateNoWindow = false,
                WorkingDirectory = _appDataDir
            };

            Process.Start(startInfo);
            Console.WriteLine("Started AppData version of SuperWatchdog.");

            Thread.Sleep(500);
            _shouldExit = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting AppData version: {ex.Message}");
        }
    }

    private static void EnablePrivileges()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
            {
                Console.WriteLine($"Failed to open process token. Error: {GetLastError()}");
                return;
            }

            var tkp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 3,
                Privileges = new LUID_AND_ATTRIBUTES[3]
            };

            string[] privileges = { "SeDebugPrivilege", "SeIncreasePriorityPrivilege", "SeLockMemoryPrivilege" };

            for (int i = 0; i < privileges.Length; i++)
            {
                if (LookupPrivilegeValue(null, privileges[i], out tkp.Privileges[i].Luid))
                {
                    tkp.Privileges[i].Attributes = SE_PRIVILEGE_ENABLED;
                }
            }

            if (!AdjustTokenPrivileges(tokenHandle, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                Console.WriteLine($"Failed to adjust privileges. Error: {GetLastError()}");
            }

            Console.WriteLine("Privileges enabled successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enabling privileges: {ex.Message}");
        }
    }

    private static void SetWatchdogProtection()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            currentProcess.PriorityClass = ProcessPriorityClass.Normal;
            Console.WriteLine("SuperWatchdog protection enabled with normal priority.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting watchdog protection: {ex.Message}");
        }
    }

    private static void StartOrAttachToProcesses()
    {
        var baseDir = IsRunningFromAppData() ? _appDataDir : AppContext.BaseDirectory;

        string mainExe = Path.Combine(baseDir, "ClassScreenLock.exe");
        string watchdogExe = Path.Combine(baseDir, "CSL.Watchdog.exe");

        _mainProcess = GetOrStartProcessByPath("ClassScreenLock", mainExe);
        _watchdogProcess = GetOrStartProcessByPath("CSL.Watchdog", watchdogExe);

        Console.WriteLine($"Main process: {_mainProcess?.Id ?? -1}");
        Console.WriteLine($"Watchdog process: {_watchdogProcess?.Id ?? -1}");
    }

    private static Process? GetOrStartProcessByPath(string name, string path, string? args = null)
    {
        try
        {
            var existing = Process.GetProcessesByName(name);
            if (existing.Length > 0)
            {
                Console.WriteLine($"Found existing {name} process: {existing[0].Id}");
                return existing[0];
            }

            if (File.Exists(path))
            {
                var arguments = string.IsNullOrEmpty(args) ? "" : args;
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = Path.GetDirectoryName(path)
                };

                Process.Start(startInfo);
                Console.WriteLine($"Started new {name} process");

                Thread.Sleep(50);
                var newProcess = Process.GetProcessesByName(name);
                return newProcess.Length > 0 ? newProcess[0] : null;
            }
            else
            {
                Console.WriteLine($"Executable not found: {path}");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error with {name}: {ex.Message}");
            return null;
        }
    }

    private static void MonitorProcesses()
    {
        var restartDelay = TimeSpan.FromMilliseconds(50);
        var baseDir = IsRunningFromAppData() ? _appDataDir : AppContext.BaseDirectory;
        var mainProcessPath = Path.Combine(baseDir, "ClassScreenLock.exe");
        var watchdogProcessPath = Path.Combine(baseDir, "CSL.Watchdog.exe");
        var exitFlagFile = Path.Combine(baseDir, "exit.flag");

        var exitMonitorThread = new Thread(MonitorExitSignal);
        exitMonitorThread.IsBackground = true;
        exitMonitorThread.Start();

        Console.WriteLine($"SuperWatchdog instance {_instanceId}: Monitoring started");

        while (!_shouldExit)
        {
            try
            {
                bool hasException = false;

                var mainProcesses = Process.GetProcessesByName("ClassScreenLock");
                bool mainProcessExists = mainProcesses.Length > 0;

                if (!mainProcessExists)
                {
                    hasException = true;

                    if (File.Exists(exitFlagFile))
                    {
                        Console.WriteLine("Main process exited normally. SuperWatchdog exiting.");
                        _shouldExit = true;
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"SuperWatchdog instance {_instanceId}: Main process not found, restarting...");
                        _mainProcess = GetOrStartProcessByPath("ClassScreenLock", mainProcessPath);
                        Thread.Sleep((int)restartDelay.TotalMilliseconds);
                    }
                }
                else if (_mainProcess == null)
                {
                    _mainProcess = mainProcesses[0];
                }

                if (!CheckAndRestartProcess(ref _watchdogProcess, "CSL.Watchdog", watchdogProcessPath, restartDelay))
                {
                    hasException = true;
                }

                if (hasException)
                {
                    Thread.Sleep(200);
                }
                else
                {
                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Monitor error: {ex.Message}");
                Thread.Sleep(200);
            }
        }

        Console.WriteLine($"SuperWatchdog instance {_instanceId} exiting.");
    }

    private static bool CheckAndRestartProcess(ref Process? process, string name, string path, TimeSpan delay, string? args = null)
    {
        try
        {
            var existingProcesses = Process.GetProcessesByName(name);
            bool processExists = existingProcesses.Length > 0;

            if (!processExists || (process != null && process.HasExited))
            {
                Console.WriteLine($"{name} process not found or exited. Restarting immediately...");
                process = GetOrStartProcessByPath(name, path, args);
                Thread.Sleep((int)delay.TotalMilliseconds);
                return false;
            }
            else if (process == null && processExists)
            {
                process = existingProcesses[0];
                Console.WriteLine($"Found existing {name} process: {process.Id}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking {name}: {ex.Message}");
            return false;
        }
    }

    private static void MonitorExitSignal()
    {
        try
        {
            var baseDir = IsRunningFromAppData() ? _appDataDir : AppContext.BaseDirectory;
            var exitFlagFile = Path.Combine(baseDir, "exit.flag");

            while (!_shouldExit)
            {
                if (File.Exists(exitFlagFile))
                {
                    Console.WriteLine("Exit flag detected. SuperWatchdog exiting.");
                    _shouldExit = true;
                    break;
                }
                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exit signal monitor error: {ex.Message}");
        }
    }

    private static void Cleanup()
    {
        try
        {
            _mainProcess?.Dispose();
            _watchdogProcess?.Dispose();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during cleanup: {ex.Message}");
        }
    }
}

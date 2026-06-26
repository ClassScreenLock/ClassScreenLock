using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace CSL.Watchdog;

class Program
{
    private static Process? _mainProcess;
    private static Process? _monitorProcess;
    private static Process? _breakButtonProcess;
    private static int _instanceId = 0;
    private static bool _shouldExit = false;
    private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);
    private static string _restartLockFile = Path.Combine(AppContext.BaseDirectory, "restart.lock");
    private static string _exitFlagFile = Path.Combine(AppContext.BaseDirectory, "exit.dat");
    private static readonly string WatchdogName = "CSL.Watchdog";
    
    private static int _consecutiveExceptions = 0;
    private static int _consecutiveNormal = 0;
    private static bool _isAbnormalState = false;
    private static readonly object _stateLock = new object();
    private static TimeSpan _currentCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _normalInterval = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan _abnormalInterval = TimeSpan.FromMilliseconds(500);
    private const int REQUIRED_NORMAL_COUNT = 10;
    
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
            SetCurrentProcessExplicitAppUserModelID($"CSL.Watchdog.Instance{_instanceId}");
        }
        catch
        {
        }

        try
        {
            EnablePrivileges();
            SetWatchdogProtection();
            Console.WriteLine($"WatchdogProcess instance {_instanceId} started with elevated privileges and protection.");
            
            EnsureAutoStartEnabled();
            
            StartOrAttachToProcesses();
            
            MonitorProcesses();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Watchdog error: {ex.Message}");
            Environment.Exit(1);
        }
        finally
        {
            Cleanup();
        }
    }
    
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);
    
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
            Console.WriteLine("Watchdog protection enabled with normal priority.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting watchdog protection: {ex.Message}");
        }
    }
    
    private static void EnsureAutoStartEnabled()
    {
        try
        {
            var watchdogPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(watchdogPath))
            {
                watchdogPath = Process.GetCurrentProcess().MainModule?.FileName;
            }
            
            if (string.IsNullOrWhiteSpace(watchdogPath))
            {
                Console.WriteLine("Warning: Could not determine watchdog path");
                return;
            }
            
            var expectedValue = $"\"{watchdogPath}\"";
            bool needsRepair = false;
            
            using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
            {
                if (key == null)
                {
                    needsRepair = true;
                }
                else
                {
                    var value = key.GetValue(WatchdogName) as string;
                    if (value != expectedValue)
                    {
                        needsRepair = true;
                    }
                }
            }
            
            if (!needsRepair)
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupFolder, $"{WatchdogName}.lnk");
                if (!File.Exists(shortcutPath))
                {
                    needsRepair = true;
                }
            }
            
            if (needsRepair)
            {
                Console.WriteLine("Auto-start not properly configured, enabling...");
                EnableAutoStart(watchdogPath);
            }
            else
            {
                Console.WriteLine("Auto-start already configured correctly");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error ensuring auto-start: {ex.Message}");
        }
    }
    
    private static void EnableAutoStart(string watchdogPath)
    {
        try
        {
            var expectedValue = $"\"{watchdogPath}\"";
            
            using (var key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
            {
                key?.SetValue(WatchdogName, expectedValue);
            }
            
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            var shortcutPath = Path.Combine(startupFolder, $"{WatchdogName}.lnk");
            
            if (!File.Exists(shortcutPath))
            {
                CreateShortcut(watchdogPath, shortcutPath);
            }
            
            Console.WriteLine("Auto-start enabled successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error enabling auto-start: {ex.Message}");
        }
    }
    
    private static void CreateShortcut(string targetPath, string shortcutPath)
    {
        try
        {
            var script = $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{shortcutPath}');$s.TargetPath='{targetPath}';$s.Save()";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating shortcut: {ex.Message}");
        }
    }
    
    private static void StartOrAttachToProcesses()
    {
        var baseDir = AppContext.BaseDirectory;
        
        string mainExe = Path.Combine(baseDir, "ClassScreenLock.exe");
        string monitorExe = Path.Combine(baseDir, "MonitorProcess.exe");
        string breakButtonExe = Path.Combine(baseDir, "BreakButtonProcess.exe");
        
        _mainProcess = GetOrStartProcessByPath("ClassScreenLock", mainExe);
        _monitorProcess = GetOrStartProcessByPath("MonitorProcess", monitorExe);
        _breakButtonProcess = GetOrStartProcessByPath("BreakButtonProcess", breakButtonExe);
        
        Console.WriteLine($"Main process: {_mainProcess?.Id ?? -1}");
        Console.WriteLine($"Monitor process: {_monitorProcess?.Id ?? -1}");
        Console.WriteLine($"BreakButton process: {_breakButtonProcess?.Id ?? -1}");
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
    
    private static bool IsProcessAlive(Process? process, string processName)
    {
        if (process == null)
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        
        try
        {
            return !process.HasExited && Process.GetProcessById(process.Id) != null;
        }
        catch
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
    }
    
    private static void MonitorProcesses()
    {
        var restartDelay = TimeSpan.FromMilliseconds(50);
        var baseDir = AppContext.BaseDirectory;
        var mainProcessPath = Path.Combine(baseDir, "ClassScreenLock.exe");
        
        Console.WriteLine($"Watchdog instance {_instanceId}: Dynamic check interval enabled (Normal: 1.5s, Abnormal: 0.5s)");
        
        int autoStartCheckCounter = 0;
        const int AUTO_START_CHECK_INTERVAL = 20;
        int autoStartFailureCount = 0;
        const int MAX_AUTO_START_FAILURES = 1;
        
        while (!_shouldExit)
        {
            try
            {
                bool hasException = false;
                
                if (CheckExitFlag())
                {
                    Console.WriteLine("Valid exit flag detected. Watchdog exiting.");
                    _shouldExit = true;
                    break;
                }
                
                autoStartCheckCounter++;
                if (autoStartCheckCounter >= AUTO_START_CHECK_INTERVAL)
                {
                    autoStartCheckCounter = 0;
                    
                    var autoStartResult = CheckAutoStartStatus(baseDir);
                    if (!autoStartResult)
                    {
                        autoStartFailureCount++;
                        
                        if (autoStartFailureCount >= MAX_AUTO_START_FAILURES)
                        {
                            RepairAutoStart(baseDir);
                            autoStartFailureCount = 0;
                        }
                        
                        hasException = true;
                    }
                    else
                    {
                        autoStartFailureCount = 0;
                    }
                }
                
                if (!IsProcessAlive(_mainProcess, "ClassScreenLock"))
                {
                    hasException = true;
                    Console.WriteLine($"Watchdog instance {_instanceId}: Main process not found, restarting...");
                    
                    if (TryAcquireRestartLock())
                    {
                        try
                        {
                            _mainProcess = GetOrStartProcessByPath("ClassScreenLock", mainProcessPath);
                            Thread.Sleep((int)restartDelay.TotalMilliseconds);
                        }
                        finally
                        {
                            ReleaseRestartLock();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"Watchdog instance {_instanceId} could not acquire restart lock. Waiting...");
                        Thread.Sleep(200);
                    }
                }
                else
                {
                    if (_mainProcess == null || _mainProcess.HasExited)
                    {
                        var processes = Process.GetProcessesByName("ClassScreenLock");
                        if (processes.Length > 0)
                        {
                            _mainProcess = processes[0];
                        }
                    }
                }
                
                if (!CheckAndRestartProcessWithExceptionFlag(ref _monitorProcess, "MonitorProcess", 
                    Path.Combine(baseDir, "MonitorProcess.exe"), 
                    restartDelay))
                {
                    hasException = true;
                }
                
                if (!CheckAndRestartProcessWithExceptionFlag(ref _breakButtonProcess, "BreakButtonProcess", 
                    Path.Combine(baseDir, "BreakButtonProcess.exe"), 
                    restartDelay))
                {
                    hasException = true;
                }
                
                UpdateCheckInterval(hasException);
                
                Thread.Sleep(_currentCheckInterval);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Monitor error: {ex.Message}");
                lock (_stateLock)
                {
                    _isAbnormalState = true;
                    _consecutiveExceptions++;
                    _currentCheckInterval = _abnormalInterval;
                }
                Thread.Sleep(200);
            }
        }
        
        Console.WriteLine($"Watchdog instance {_instanceId} exiting.");
    }
    
    private static void UpdateCheckInterval(bool hasException)
    {
        lock (_stateLock)
        {
            if (hasException)
            {
                _isAbnormalState = true;
                _consecutiveExceptions++;
                _consecutiveNormal = 0;
                _currentCheckInterval = _abnormalInterval;
                Console.WriteLine($"[Watchdog {_instanceId}] Abnormal state detected! Switched to 0.5s check interval. Consecutive exceptions: {_consecutiveExceptions}");
            }
            else
            {
                if (_isAbnormalState)
                {
                    _consecutiveNormal++;
                    
                    if (_consecutiveNormal >= REQUIRED_NORMAL_COUNT)
                    {
                        _consecutiveExceptions = 0;
                        _consecutiveNormal = 0;
                        _isAbnormalState = false;
                        _currentCheckInterval = _normalInterval;
                        Console.WriteLine($"[Watchdog {_instanceId}] Returned to normal state after {_consecutiveNormal} consecutive normal checks. Switched to 1.5s check interval.");
                    }
                    else
                    {
                        _currentCheckInterval = _abnormalInterval;
                        Console.WriteLine($"[Watchdog {_instanceId}] Normal check {_consecutiveNormal}/{REQUIRED_NORMAL_COUNT}. Keeping 0.5s interval.");
                    }
                }
                else
                {
                    _currentCheckInterval = _normalInterval;
                }
            }
        }
    }
    
    private static bool CheckAndRestartProcessWithExceptionFlag(ref Process? process, string name, string path, TimeSpan delay, string? args = null)
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
    
    private static bool CheckExitFlag()
    {
        try
        {
            if (!File.Exists(_exitFlagFile))
            {
                return false;
            }
            
            var encryptedData = File.ReadAllBytes(_exitFlagFile);
            
            if (encryptedData.Length == 0)
            {
                return false;
            }
            
            var decryptedData = DecryptExitFlag(encryptedData);
            
            if (decryptedData == null)
            {
                Console.WriteLine("Exit flag decryption failed, file may be corrupted");
                return false;
            }
            
            var content = Encoding.UTF8.GetString(decryptedData);
            var parts = content.Split('|');
            
            if (parts.Length != 2)
            {
                Console.WriteLine("Exit flag format invalid");
                return false;
            }
            
            if (!int.TryParse(parts[0], out var pid))
            {
                Console.WriteLine($"Exit flag PID invalid: {parts[0]}");
                return false;
            }
            
            if (!long.TryParse(parts[1], out var timestamp))
            {
                Console.WriteLine($"Exit flag timestamp invalid: {parts[1]}");
                return false;
            }
            
            var mainProcesses = Process.GetProcessesByName("ClassScreenLock");
            bool pidMatches = false;
            foreach (var proc in mainProcesses)
            {
                if (proc.Id == pid)
                {
                    pidMatches = true;
                    break;
                }
            }
            
            if (!pidMatches && mainProcesses.Length > 0)
            {
                Console.WriteLine($"Exit flag PID {pid} does not match any running main process");
                return false;
            }
            
            var flagTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            var now = DateTime.Now;
            var age = now - flagTime;
            
            if (age.TotalSeconds > 30)
            {
                Console.WriteLine($"Exit flag is too old ({age.TotalSeconds:F1} seconds)");
                return false;
            }
            
            if (mainProcesses.Length == 0 && age.TotalSeconds > 5)
            {
                Console.WriteLine($"Main process not running and flag is {age.TotalSeconds:F1} seconds old");
                return false;
            }
            
            Console.WriteLine("Valid exit flag detected");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating exit flag: {ex.Message}");
            return false;
        }
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
        catch (Exception ex)
        {
            Console.WriteLine($"Decryption error: {ex.Message}");
            return null;
        }
    }
    
    private static bool CheckAutoStartStatus(string baseDir)
    {
        try
        {
            var appPath = Path.Combine(baseDir, "ClassScreenLock.exe");
            var expectedValue = $"\"{appPath}\" --minimized";
            bool allHealthy = true;
            
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    if (key != null)
                    {
                        var value = key.GetValue("ClassScreenLock") as string;
                        if (value != expectedValue)
                        {
                            allHealthy = false;
                            Console.WriteLine($"[Watchdog {_instanceId}] Auto-start registry entry missing or incorrect");
                        }
                    }
                    else
                    {
                        allHealthy = false;
                        Console.WriteLine($"[Watchdog {_instanceId}] Auto-start registry key not found");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] Error checking registry: {ex.Message}");
                allHealthy = false;
            }
            
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupFolder, "ClassScreenLock.lnk");
                if (!File.Exists(shortcutPath))
                {
                    allHealthy = false;
                    Console.WriteLine($"[Watchdog {_instanceId}] Auto-start shortcut missing");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] Error checking shortcut: {ex.Message}");
                allHealthy = false;
            }
            
            try
            {
                var taskName = "ClassScreenLockAutoStart";
                var script = $"Get-ScheduledTask -TaskName '{taskName}' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty State";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                
                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        process.WaitForExit(3000);
                        var output = process.StandardOutput.ReadToEnd().Trim();
                        if (!output.Equals("Ready", StringComparison.OrdinalIgnoreCase))
                        {
                            allHealthy = false;
                            Console.WriteLine($"[Watchdog {_instanceId}] Auto-start scheduled task not ready (State: {output})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] Error checking task scheduler: {ex.Message}");
                allHealthy = false;
            }
            
            return allHealthy;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog {_instanceId}] Error checking auto-start status: {ex.Message}");
            return false;
        }
    }
    
    private static void RepairAutoStart(string baseDir)
    {
        try
        {
            var appPath = Path.Combine(baseDir, "ClassScreenLock.exe");
            Console.WriteLine($"[Watchdog {_instanceId}] Repairing auto-start...");
            
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    key?.SetValue("ClassScreenLock", $"\"{appPath}\" --minimized");
                    Console.WriteLine($"[Watchdog {_instanceId}] Registry entry repaired");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] Error repairing registry: {ex.Message}");
            }
            
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                var shortcutPath = Path.Combine(startupFolder, "ClassScreenLock.lnk");
                
                var script = $"$s=(New-Object -COM WScript.Shell).CreateShortcut('{shortcutPath}');$s.TargetPath='{appPath}';$s.Arguments='--minimized';$s.Save()";
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(3000);
                    Console.WriteLine($"[Watchdog {_instanceId}] Shortcut repaired");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] Error repairing shortcut: {ex.Message}");
            }
            
            try
            {
                var taskName = "ClassScreenLockAutoStart";
                var script = $@"
$action = New-ScheduledTaskAction -Execute '{appPath}' -Argument '--minimized'
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Days 365)
Register-ScheduledTask -TaskName '{taskName}' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force";
                
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{script}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                
                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(5000);
                    Console.WriteLine($"[Watchdog {_instanceId}] Scheduled task repaired");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] Error repairing task scheduler: {ex.Message}");
            }
            
            Console.WriteLine($"[Watchdog {_instanceId}] Auto-start repair completed");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog {_instanceId}] Error repairing auto-start: {ex.Message}");
        }
    }
    
    private static bool TryAcquireRestartLock()
    {
        try
        {
            using (var fileStream = new FileStream(_restartLockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = new StreamWriter(fileStream))
                {
                    writer.WriteLine($"Instance: {_instanceId}");
                    writer.WriteLine($"Timestamp: {DateTime.UtcNow}");
                }
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error acquiring restart lock: {ex.Message}");
            return false;
        }
    }
    
    private static void ReleaseRestartLock()
    {
        try
        {
            if (File.Exists(_restartLockFile))
            {
                File.Delete(_restartLockFile);
                Console.WriteLine($"Watchdog instance {_instanceId} released restart lock.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error releasing restart lock: {ex.Message}");
        }
    }
    
    private static void CheckAndRestartProcess(ref Process? process, string name, string path, TimeSpan delay, string? args = null)
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
            }
            else if (process == null && processExists)
            {
                process = existingProcesses[0];
                Console.WriteLine($"Found existing {name} process: {process.Id}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking {name}: {ex.Message}");
        }
    }
    
    private static void Cleanup()
    {
        try
        {
            _mainProcess?.Dispose();
            _monitorProcess?.Dispose();
            _breakButtonProcess?.Dispose();
        }
        catch { }
    }
}

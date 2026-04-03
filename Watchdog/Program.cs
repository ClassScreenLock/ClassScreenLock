using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;

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
    
    // 动态检测频率控制
    private static int _consecutiveExceptions = 0; // 连续异常计数
    private static int _consecutiveNormal = 0; // 连续正常计数
    private static bool _isAbnormalState = false; // 是否处于异常状态
    private static readonly object _stateLock = new object(); // 状态锁
    private static TimeSpan _currentCheckInterval = TimeSpan.FromSeconds(2); // 当前检测间隔
    private static readonly TimeSpan _normalInterval = TimeSpan.FromSeconds(1.5); // 正常间隔：1.5 秒
    private static readonly TimeSpan _abnormalInterval = TimeSpan.FromMilliseconds(500); // 异常间隔：0.5 秒
    private const int REQUIRED_NORMAL_COUNT = 10; // 需要连续正常 10 次才恢复正常
    
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
        // 尽早设置独立的 AppUserModelID，避免和主程序分在同一组
        // 必须在任何窗口创建之前调用
        try
        {
            // 为每个实例设置不同的AppUserModelID
            _instanceId = args.Length > 0 && int.TryParse(args[0], out int id) ? id : 0;
            SetCurrentProcessExplicitAppUserModelID($"CSL.Watchdog.Instance{_instanceId}");
        }
        catch
        {
            // 忽略失败，不影响功能
        }

        try
        {
            EnablePrivileges();
            SetWatchdogProtection();
            Console.WriteLine($"WatchdogProcess instance {_instanceId} started with elevated privileges and protection.");
            
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
            // 看门狗使用正常优先级，避免占用过多系统资源
            var currentProcess = Process.GetCurrentProcess();
            currentProcess.PriorityClass = ProcessPriorityClass.Normal;
            Console.WriteLine("Watchdog protection enabled with normal priority.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error setting watchdog protection: {ex.Message}");
        }
    }
    
    private static void StartOrAttachToProcesses()
    {
        var baseDir = AppContext.BaseDirectory;
        
        // 所有进程都在同一目录下
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
                // 使用更直接的方式启动进程，不通过cmd.exe
                var arguments = string.IsNullOrEmpty(args) ? "" : args;
                var startInfo = new ProcessStartInfo
                {
                    FileName = path,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = false, // 直接显示进程窗口，加快启动速度
                    WorkingDirectory = Path.GetDirectoryName(path)
                };
                
                // 启动进程并立即返回，不等待
                Process.Start(startInfo);
                Console.WriteLine($"Started new {name} process");
                
                // 短暂延迟确保进程启动
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
    var baseDir = AppContext.BaseDirectory;
    var mainProcessPath = Path.Combine(baseDir, "ClassScreenLock.exe");
    var restartFlagFile = Path.Combine(baseDir, "restart.flag");
    var exitFlagFile = Path.Combine(baseDir, "exit.flag");
    
    // 启动退出信号监听线程
    var exitMonitorThread = new Thread(MonitorExitSignal);
    exitMonitorThread.IsBackground = true;
    exitMonitorThread.Start();
    
    Console.WriteLine($"Watchdog instance {_instanceId}: Dynamic check interval enabled (Normal: 1.5s, Abnormal: 0.5s)");
    
    // 自启动检查变量
    int autoStartCheckCounter = 0;
    const int AUTO_START_CHECK_INTERVAL = 20; // 每 20 次循环检查一次自启动（约 60-120 秒）
    int autoStartFailureCount = 0;
    const int MAX_AUTO_START_FAILURES = 1; // 连续失败 1 次立即触发修复
    
    while (!_shouldExit)
    {
        try
        {
            bool hasException = false;
            
            // 定期检查自启动状态（与进程监控共用检测循环）
            autoStartCheckCounter++;
            if (autoStartCheckCounter >= AUTO_START_CHECK_INTERVAL)
            {
                autoStartCheckCounter = 0;
                
                // 检查自启动状态
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
            
            // 检查主进程
            var mainProcesses = Process.GetProcessesByName("ClassScreenLock");
            bool mainProcessExists = mainProcesses.Length > 0;
            
            if (!mainProcessExists)
            {
                hasException = true;
                
                // 优先检查重启标记文件（UIAccess 重启时创建）
                if (File.Exists(restartFlagFile))
                {
                    // 主进程正在重启，等待它完成
                    Console.WriteLine($"Watchdog instance {_instanceId}: restart.flag detected, waiting for process to restart...");
                    Thread.Sleep(500);
                    continue;
                }
                
                // 检查是否有退出标记文件
                if (File.Exists(exitFlagFile))
                {
                    // 主进程正常退出，看门狗也退出
                    Console.WriteLine("Main process exited normally. Watchdog exiting.");
                    _shouldExit = true;
                    break;
                }
                else
                {
                    // 尝试获取重启锁，避免多个看门狗实例同时重启主进程
                    if (TryAcquireRestartLock())
                    {
                        try
                        {
                            // 主进程异常退出，重启它
                            Console.WriteLine($"Watchdog instance {_instanceId} acquired restart lock. Restarting main process...");
                            _mainProcess = GetOrStartProcessByPath("ClassScreenLock", mainProcessPath);
                            Thread.Sleep((int)restartDelay.TotalMilliseconds);
                        }
                        finally
                        {
                            // 释放重启锁
                            ReleaseRestartLock();
                        }
                    }
                    else
                    {
                        // 其他看门狗实例已经在重启主进程，等待一段时间后再检查
                        Console.WriteLine($"Watchdog instance {_instanceId} could not acquire restart lock. Waiting...");
                        Thread.Sleep(100);
                    }
                }
            }
            else if (_mainProcess == null)
            {
                // 更新主进程对象
                _mainProcess = mainProcesses[0];
                
                // 主进程已经成功启动，删除重启标记文件
                if (File.Exists(restartFlagFile))
                {
                    try
                    {
                        File.Delete(restartFlagFile);
                        Console.WriteLine($"Watchdog instance {_instanceId}: removed restart.flag after successful restart.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete restart.flag: {ex.Message}");
                    }
                }
            }
            
            // 检查其他辅助进程
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
            
            // 根据状态动态调整检测频率
            UpdateCheckInterval(hasException);
            
            // 使用动态调整后的检测间隔
            Thread.Sleep(_currentCheckInterval);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Monitor error: {ex.Message}");
            // 发生异常，切换到快速检测模式
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
                // 检测到异常，立即切换到快速检测模式
                _isAbnormalState = true;
                _consecutiveExceptions++;
                _consecutiveNormal = 0; // 重置正常计数
                _currentCheckInterval = _abnormalInterval;
                Console.WriteLine($"[Watchdog {_instanceId}] Abnormal state detected! Switched to 0.5s check interval. Consecutive exceptions: {_consecutiveExceptions}");
            }
            else
            {
                // 正常状态
                if (_isAbnormalState)
                {
                    // 处于异常状态，累计正常次数
                    _consecutiveNormal++;
                    
                    if (_consecutiveNormal >= REQUIRED_NORMAL_COUNT)
                    {
                        // 连续正常达到 10 次，恢复正常状态
                        _consecutiveExceptions = 0;
                        _consecutiveNormal = 0;
                        _isAbnormalState = false;
                        _currentCheckInterval = _normalInterval;
                        Console.WriteLine($"[Watchdog {_instanceId}] Returned to normal state after {_consecutiveNormal} consecutive normal checks. Switched to 1.5s check interval.");
                    }
                    else
                    {
                        // 继续快速检测，直到达到 10 次
                        _currentCheckInterval = _abnormalInterval;
                        Console.WriteLine($"[Watchdog {_instanceId}] Normal check {_consecutiveNormal}/{REQUIRED_NORMAL_COUNT}. Keeping 0.5s interval.");
                    }
                }
                else
                {
                    // 保持正常状态
                    _currentCheckInterval = _normalInterval;
                }
            }
        }
    }
    
    private static bool CheckAndRestartProcessWithExceptionFlag(ref Process? process, string name, string path, TimeSpan delay, string? args = null)
    {
        try
        {
            // 直接检查进程名称，不依赖于保存的进程对象，确保即使进程对象失效也能检测到
            var existingProcesses = Process.GetProcessesByName(name);
            bool processExists = existingProcesses.Length > 0;
            
            // 如果进程不存在或已退出，立即重启
            if (!processExists || (process != null && process.HasExited))
            {
                Console.WriteLine($"{name} process not found or exited. Restarting immediately...");
                
                // 不做任何等待，直接启动新进程
                process = GetOrStartProcessByPath(name, path, args);
                
                // 短暂延迟确保进程启动
                Thread.Sleep((int)delay.TotalMilliseconds);
                
                // 返回 false 表示检测到异常
                return false;
            }
            else if (process == null && processExists)
            {
                // 如果进程存在但进程对象为 null，更新进程对象
                process = existingProcesses[0];
                Console.WriteLine($"Found existing {name} process: {process.Id}");
            }
            
            // 返回 true 表示正常
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking {name}: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 检查自启动状态（注册表 + 启动文件夹 + 任务计划）
    /// </summary>
    private static bool CheckAutoStartStatus(string baseDir)
    {
        try
        {
            var appPath = Path.Combine(baseDir, "ClassScreenLock.exe");
            var expectedValue = $"\"{appPath}\" --minimized";
            bool allHealthy = true;
            
            // 检查注册表
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
            
            // 检查启动文件夹快捷方式
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
            
            // 检查任务计划程序
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
    
    /// <summary>
    /// 修复自启动（重新启用所有方式）
    /// </summary>
    private static void RepairAutoStart(string baseDir)
    {
        try
        {
            var appPath = Path.Combine(baseDir, "ClassScreenLock.exe");
            Console.WriteLine($"[Watchdog {_instanceId}] Repairing auto-start...");
            
            // 修复注册表
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
            
            // 修复启动文件夹快捷方式
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
            
            // 修复任务计划程序
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
    
    private static void MonitorExitSignal()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var exitFlagFile = Path.Combine(baseDir, "exit.flag");
            
            while (!_shouldExit)
            {
                if (File.Exists(exitFlagFile))
                {
                    if (ValidateExitFlag(exitFlagFile))
                    {
                        Console.WriteLine("Valid exit flag detected. Watchdog exiting.");
                        _shouldExit = true;
                        break;
                    }
                    else
                    {
                        Console.WriteLine("Invalid exit flag detected. Ignoring and deleting.");
                        try
                        {
                            File.Delete(exitFlagFile);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Failed to delete invalid exit flag: {ex.Message}");
                        }
                    }
                }
                Thread.Sleep(100);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exit signal monitor error: {ex.Message}");
        }
    }
    
    private static bool ValidateExitFlag(string exitFlagFile)
    {
        try
        {
            var content = File.ReadAllText(exitFlagFile).Trim();
            if (string.IsNullOrEmpty(content))
            {
                Console.WriteLine("Exit flag is empty.");
                return false;
            }
            
            var parts = content.Split('|');
            if (parts.Length != 2)
            {
                Console.WriteLine("Exit flag format invalid.");
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
                Console.WriteLine($"Exit flag PID {pid} does not match any running main process.");
                return false;
            }
            
            var flagTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            var now = DateTime.Now;
            var age = now - flagTime;
            
            if (age.TotalSeconds > 30)
            {
                Console.WriteLine($"Exit flag is too old ({age.TotalSeconds:F1} seconds).");
                return false;
            }
            
            if (mainProcesses.Length == 0 && age.TotalSeconds > 5)
            {
                Console.WriteLine($"Main process not running and flag is {age.TotalSeconds:F1} seconds old.");
                return false;
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error validating exit flag: {ex.Message}");
            return false;
        }
    }
    
    private static bool TryAcquireRestartLock()
    {
        try
        {
            // 尝试创建重启锁文件，如果文件已存在则获取失败
            using (var fileStream = new FileStream(_restartLockFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                // 写入当前实例ID和时间戳
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
            // 文件已存在，获取锁失败
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
            // 直接检查进程名称，不依赖于保存的进程对象，确保即使进程对象失效也能检测到
            var existingProcesses = Process.GetProcessesByName(name);
            bool processExists = existingProcesses.Length > 0;
            
            // 如果进程不存在或已退出，立即重启
            if (!processExists || (process != null && process.HasExited))
            {
                Console.WriteLine($"{name} process not found or exited. Restarting immediately...");
                
                // 不做任何等待，直接启动新进程
                process = GetOrStartProcessByPath(name, path, args);
                
                // 短暂延迟确保进程启动
                Thread.Sleep((int)delay.TotalMilliseconds);
            }
            else if (process == null && processExists)
            {
                // 如果进程存在但进程对象为null，更新进程对象
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

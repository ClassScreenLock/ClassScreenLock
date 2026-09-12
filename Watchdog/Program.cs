using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Management;

namespace CSL.Watchdog;

class Program
{
    private static Process? _mainProcess;
    private static Process? _monitorProcess;
    private static Process? _breakButtonProcess;
    private static int _instanceId = 0;
    private static bool _shouldExit = false;
    private static readonly ManualResetEvent _exitEvent = new ManualResetEvent(false);
    // 与主程序 AppPathHelper.AppDirectory 保持一致：基于 Environment.ProcessPath 解析
    // 真实发布目录。单文件发布下 AppContext.BaseDirectory 指向 SystemTemp 临时解压目录，
    // 若在此读写 exit.dat / restart.lock 会与主程序（写发布目录）路径不一致，
    // 导致主程序退出后看门狗收不到退出信号（不退出）或实例间锁丢失。
    private static string ProcessDirectory
    {
        get
        {
            try
            {
                var processPath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(processPath))
                {
                    return Path.GetDirectoryName(processPath) ?? AppContext.BaseDirectory;
                }
            }
            catch { }
            return AppContext.BaseDirectory;
        }
    }
    private static string _restartLockFile = Path.Combine(ProcessDirectory, "restart.lock");
    private static string _exitFlagFile = Path.Combine(ProcessDirectory, "exit.dat");

    private static int _consecutiveExceptions = 0;
    private static int _consecutiveNormal = 0;
    private static bool _isAbnormalState = false;
    private static readonly object _stateLock = new object();
    private static TimeSpan _currentCheckInterval = TimeSpan.FromSeconds(1);
    private static TimeSpan _normalInterval = TimeSpan.FromSeconds(1);
    private static TimeSpan _abnormalInterval = TimeSpan.FromMilliseconds(300);
    private const int REQUIRED_NORMAL_COUNT = 10;

    // 主程序 Data/settings.json 中挡位配置的监测间隔（与主程序 WatchdogTier 保持一致）
    private static readonly (int Tier, int NormalMs, int AbnormalMs)[] TierTable = new[]
    {
        (1, 100, 50),    // 极速响应
        (2, 250, 100),   // 快速响应
        (3, 500, 200),   // 标准平衡
        (4, 1000, 500),  // 稳定运行
        (5, 2000, 1000), // 节能模式
        (6, 5000, 2500), // 极低功耗
    };
    // 挡位配置缓存：每 60 次循环（约 1 分钟）检查一次 mtime，配置变更即时生效
    private const int TIER_REFRESH_LOOP_INTERVAL = 60;
    private static int _tierRefreshCounter = 0;
    private static DateTime _lastTierMtimeUtc = DateTime.MinValue;
    
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

            StartOrAttachToProcesses();

            // 学习主程序安全中心的"监测时长"挡位设置（强制首次刷新）
            RefreshTierFromConfig(true);

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
                var commandLine = string.IsNullOrEmpty(args)
                    ? $"\"{path}\""
                    : $"\"{path}\" {args}";
                // 与主程序拉起看门狗一致：改用 WMI Win32_Process.Create 启动目标进程。
                //   1) 目标进程的父进程变成 WMI 服务进程（WmiPrvSE），完全脱离本看门狗
                //      进程树 / Job，本进程退出不影响目标进程；
                //   2) 目标进程继承调用者（看门狗）的令牌与会话：看门狗以 SYSTEM 运行时
                //      拉起的主程序同为 SYSTEM 权限且保持在用户会话（UI 正常）；
                //   3) 不经过 cmd.exe，DisableCMD 组策略不会误伤。
                // 不能用 ShellExecute/Process.Start 直接启动——那会让目标进程变成
                // 本进程的直接子进程，被分到同一个进程树 / Job Object 下。
                using var processClass = new ManagementClass("Win32_Process");
                using var methodParams = processClass.GetMethodParameters("Create");
                methodParams["CommandLine"] = commandLine;
                using var result = processClass.InvokeMethod("Create", methodParams, null);
                var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
                if (returnValue != 0)
                {
                    Console.WriteLine($"WMI start failed for {name}, returnValue={returnValue} (0=success)");
                    return null;
                }
                Console.WriteLine($"Started new {name} process via WMI (detached from watchdog tree)");

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
    
    /// <summary>
    /// 读取主程序 Data/settings.json 的 watchdogMonitorTier，同步本进程的监测间隔。
    /// 使看门狗监测速度与主程序安全中心"监测时长"挡位设置保持一致。
    /// </summary>
    private static void RefreshTierFromConfig(bool force)
    {
        try
        {
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "Data", "settings.json");
            DateTime mtime = DateTime.MinValue;
            if (File.Exists(settingsPath))
            {
                mtime = File.GetLastWriteTimeUtc(settingsPath);
            }

            // 非强制刷新：mtime 未变化则跳过
            if (!force && mtime != DateTime.MinValue && mtime == _lastTierMtimeUtc)
            {
                return;
            }

            _lastTierMtimeUtc = mtime;

            if (!File.Exists(settingsPath))
            {
                return; // 无配置文件，保持默认挡位
            }

            int tier = 4; // 默认"稳定运行"
            var json = File.ReadAllText(settingsPath);
            // 轻量解析：查找 watchdogMonitorTier 字段
            int idx = json.IndexOf("\"watchdogMonitorTier\"", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                int colonIdx = json.IndexOf(':', idx);
                if (colonIdx >= 0)
                {
                    int start = colonIdx + 1;
                    while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\r' || json[start] == '\n')) start++;
                    int end = start;
                    while (end < json.Length && char.IsDigit(json[end])) end++;
                    if (end > start && int.TryParse(json.Substring(start, end - start), out int parsedTier))
                    {
                        tier = parsedTier;
                    }
                }
            }

            foreach (var t in TierTable)
            {
                if (t.Tier == tier)
                {
                    var newNormal = TimeSpan.FromMilliseconds(t.NormalMs);
                    var newAbnormal = TimeSpan.FromMilliseconds(t.AbnormalMs);
                    if (newNormal != _normalInterval || newAbnormal != _abnormalInterval)
                    {
                        Console.WriteLine($"[Watchdog {_instanceId}] 监测间隔已同步为挡位 {t.Tier}（正常 {t.NormalMs}ms，异常 {t.AbnormalMs}ms）");
                        _normalInterval = newNormal;
                        _abnormalInterval = newAbnormal;
                    }
                    return;
                }
            }
            // 越界挡位回退到"稳定运行"（1000ms/500ms）
            _normalInterval = TimeSpan.FromMilliseconds(1000);
            _abnormalInterval = TimeSpan.FromMilliseconds(500);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog {_instanceId}] 读取挡位配置失败: {ex.Message}");
        }
    }

    private static void MonitorProcesses()
    {
        var restartDelay = TimeSpan.FromMilliseconds(50);
        var baseDir = AppContext.BaseDirectory;
        var mainProcessPath = Path.Combine(baseDir, "ClassScreenLock.exe");
        
        Console.WriteLine($"Watchdog instance {_instanceId}: Dynamic check interval enabled (Normal: {_normalInterval.TotalMilliseconds}ms, Abnormal: {_abnormalInterval.TotalMilliseconds}ms)");

        // 服务状态检查间隔：30 次循环 ≈ 正常态 30 秒一次。
        // 看门狗服务进程被强制结束后，SCM 恢复策略（1 秒）会先尝试重启；
        // 三次恢复机会耗尽时，由本独立短周期检查快速拉起服务。
        // 多实例错峰：instance 0/1/2 从 0/10/20 起点计数，全局约每 10 秒一次 sc query。
        const int SERVICE_CHECK_INTERVAL = 30;
        int serviceCheckCounter = ((_instanceId % 3) + 3) % 3 * (SERVICE_CHECK_INTERVAL / 3);

        while (!_shouldExit)
        {
            try
            {
                bool hasException = false;

                // 定期检查主程序挡位配置是否变更（mtime 变化才重新解析）
                if (++_tierRefreshCounter >= TIER_REFRESH_LOOP_INTERVAL)
                {
                    _tierRefreshCounter = 0;
                    RefreshTierFromConfig(false);
                }

                if (CheckExitFlag())
                {
                    Console.WriteLine("Valid exit flag detected. Watchdog exiting.");
                    _shouldExit = true;
                    break;
                }

                // 独立短周期检查 Windows 看门狗服务状态（约 30 秒）：
                // 服务进程被强制结束且 SCM 三次恢复机会耗尽时，在此快速拉起。
                serviceCheckCounter++;
                if (serviceCheckCounter >= SERVICE_CHECK_INTERVAL)
                {
                    serviceCheckCounter = 0;
                    EnsureWatchdogServiceRunning();
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

                // 守护 IFEO 重定向程序常驻占坑实例（防学生删除弹窗 exe）：
                // 主程序 / 看门狗服务 / 看门狗进程三方冗余守护，任一发现丢失即拉起，
                // 周期跟随本进程挡位（最快 100ms，正常 1000ms）。
                EnsureRedirectorResident();
                
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
                Console.WriteLine($"[Watchdog {_instanceId}] Abnormal state detected! Switched to {_abnormalInterval.TotalMilliseconds}ms check interval. Consecutive exceptions: {_consecutiveExceptions}");
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
                        Console.WriteLine($"[Watchdog {_instanceId}] Returned to normal state after {_consecutiveNormal} consecutive normal checks. Switched to {_normalInterval.TotalMilliseconds}ms check interval.");
                    }
                    else
                    {
                        _currentCheckInterval = _abnormalInterval;
                        Console.WriteLine($"[Watchdog {_instanceId}] Normal check {_consecutiveNormal}/{REQUIRED_NORMAL_COUNT}. Keeping {_abnormalInterval.TotalMilliseconds}ms interval.");
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
    
    /// <summary>
    /// 双向保护：检查 Windows 看门狗服务 (CSL.WatchdogService) 是否正在运行，
    /// 如果被用户在服务管理器中停止，则重新启动它。
    /// 这样形成服务 ↔ 用户模式看门狗的互保闭环。
    /// </summary>
    private static void EnsureWatchdogServiceRunning()
    {
        try
        {
            const string serviceName = "CSL.WatchdogService";

            // 使用 sc.exe 查询服务状态
            var queryPsi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {serviceName}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using (var queryProcess = Process.Start(queryPsi))
            {
                if (queryProcess == null) return;
                queryProcess.WaitForExit(3000);
                var output = queryProcess.StandardOutput.ReadToEnd();

                // 服务未安装（退出码非0且输出为空）— 跳过
                if (queryProcess.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
                {
                    return;
                }

                // 检查服务状态是否为 RUNNING
                if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                {
                    return; // 服务正在运行，无需操作
                }

                // 服务已停止或已暂停，尝试启动
                Console.WriteLine($"[Watchdog {_instanceId}] {serviceName} is not running (state: {ExtractServiceState(output)}), restarting...");

                var startPsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start {serviceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                };

                using var startProcess = Process.Start(startPsi);
                startProcess?.WaitForExit(5000);

                Console.WriteLine($"[Watchdog {_instanceId}] {serviceName} restart command issued.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog {_instanceId}] Error ensuring WatchdogService running: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 sc query 输出中提取服务状态文本
    /// </summary>
    private static string ExtractServiceState(string queryOutput)
    {
        try
        {
            var lines = queryOutput.Split('\n', '\r');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("STATE", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }
            }
        }
        catch { }
        return "UNKNOWN";
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

    /// <summary>
    /// 确保 IFEO 重定向程序（CSL.IfeoRedirector.exe）常驻占坑实例在运行。
    ///
    /// 背景：IFEO 重定向程序每次触发才短暂运行，平时文件无人占用，学生可把
    /// 主程序目录（或固定部署目录）下的 CSL.IfeoRedirector.exe 删掉，导致弹窗失效。
    /// 方案：服务 / 主程序 / 看门狗三方共同守护一个常驻实例（--resident），
    /// 该实例持有 exe 文件句柄（共享读/写但不共享删除），使删除/重命名被系统拒绝；
    /// IFEO 每次触发的新实例仍能正常读取启动（多实例并存，互不干扰）。
    /// 全局互斥 Global\CSL.IfeoRedirector.Resident 保证任一时刻只有一个占坑实例。
    /// </summary>
    private static void EnsureRedirectorResident()
    {
        try
        {
            // 探测全局互斥：占坑实例存活则无需处理。
            // 看门狗为主程序/服务以提升令牌拉起，可访问 Global 命名空间。
            try
            {
                using var existing = Mutex.OpenExisting(@"Global\CSL.IfeoRedirector.Resident");
                return; // 占坑实例正在运行
            }
            catch
            {
                // 互斥不存在 → 占坑实例缺失，需要启动
            }

            var baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, "CSL.IfeoRedirector.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "ClassScreenLock", "CSL.IfeoRedirector.exe")
            };

            string? exePath = null;
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    exePath = candidate;
                    break;
                }
            }

            if (exePath == null)
            {
                Console.WriteLine($"[Watchdog {_instanceId}] 未找到 CSL.IfeoRedirector.exe，无法启动常驻占坑实例");
                return;
            }

            // 与主程序拉起看门狗一致：改用 WMI Win32_Process.Create 启动目标进程，
            // 脱离本进程树；占坑实例继承看门狗（SYSTEM/高完整性）令牌，学生无法结束它。
            using var processClass = new ManagementClass("Win32_Process");
            using var methodParams = processClass.GetMethodParameters("Create");
            methodParams["CommandLine"] = $"\"{exePath}\" --resident";
            using var result = processClass.InvokeMethod("Create", methodParams, null);
            var returnValue = Convert.ToUInt32(result?["ReturnValue"] ?? 1u);
            Console.WriteLine($"[Watchdog {_instanceId}] IFEO 占坑实例 {(returnValue == 0 ? "启动成功" : $"启动失败 code={returnValue}")}（{exePath} --resident）");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Watchdog {_instanceId}] 确保 IFEO 占坑实例时异常: {ex.Message}");
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

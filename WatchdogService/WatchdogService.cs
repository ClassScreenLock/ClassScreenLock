using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace CSL.WatchdogService;

/// <summary>
/// Windows 服务看门狗。以 LocalSystem 身份运行在 Session 0 中，
/// 使用 CreateProcessAsUser API 在活动用户的会话中启动并监控主程序。
///
/// 这提供了一层 SYSTEM 级别的保护：
///   - 即使用户模式的看门狗被终止，服务仍能重启主程序
///   - 服务本身受 Windows SCM 管理和恢复策略保护
/// </summary>
public class WatchdogService : ServiceBase
{
    public const string ServiceNameValue = "CSL.WatchdogService";

    private Timer? _monitorTimer;
    private Timer? _residentTimer;
    private TimeSpan _normalInterval = TimeSpan.FromSeconds(2);
    private TimeSpan _abnormalInterval = TimeSpan.FromSeconds(1);
    private TimeSpan _currentInterval;

    // IFEO 重定向程序占坑实例守护周期：固定 1 秒，
    // 不受"监测时长"挡位影响，配合主程序（300ms）与看门狗（挡位）三方冗余守护。
    private static readonly TimeSpan ResidentCheckInterval = TimeSpan.FromSeconds(1);

    private readonly string _baseDir;
    private readonly string _exitFlagFile;
    private readonly string _settingsJsonPath;

    // 挡位配置缓存（mtime 变化才重新解析）
    private DateTime _lastTierRefreshUtc;
    private static readonly TimeSpan TierRefreshMinInterval = TimeSpan.FromSeconds(30);

    // 看门狗目标实例数（与主程序一致：3 个）
    private const int WatchdogTargetCount = 3;

    // 退出标志有效期：超过此时间视为陈旧标志，恢复监控
    private static readonly TimeSpan ExitFlagMaxAge = TimeSpan.FromMinutes(5);

    // 启动后初次检查前的等待时间（给系统登录一些时间）
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(3);

    public WatchdogService()
    {
        ServiceName = ServiceNameValue;
        CanStop = true;
        CanPauseAndContinue = false;
        AutoLog = true;
        _currentInterval = _normalInterval;

        _baseDir = AppContext.BaseDirectory;
        _settingsJsonPath = Path.Combine(_baseDir, "Data", "settings.json");
        _exitFlagFile = Path.Combine(_baseDir, "exit.dat");
    }

    protected override void OnStart(string[] args)
    {
        ServiceLogger.Log("WatchdogService", "服务正在启动...");

        // 初始延迟后开始监控，等待用户会话就绪
        _monitorTimer = new Timer(MonitorCallback, null, InitialDelay, _normalInterval);

        // IFEO 重定向程序占坑实例独立守护定时器：固定 1 秒，与主监控互不影响。
        // 即使主监控挡位被调慢，占坑实例仍被快速守护。
        _residentTimer = new Timer(ResidentCallback, null, InitialDelay, ResidentCheckInterval);
    }

    protected override void OnStop()
    {
        ServiceLogger.Log("WatchdogService", "服务正在停止...");
        _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _monitorTimer?.Dispose();
        _monitorTimer = null;
        _residentTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _residentTimer?.Dispose();
        _residentTimer = null;
        ServiceLogger.Log("WatchdogService", "服务已停止");
    }

    /// <summary>
    /// 占坑实例守护回调（固定 1 秒周期）。主程序 / 看门狗 / 服务三方
    /// 共同守护常驻占坑实例，丢失即拉起。
    /// 主程序正常退出（退出标志有效）期间不拉起，使占坑实例随主程序一起退出、
    /// 不再驻留占用资源；退出标志过期后恢复守护（届时主程序也已被重新拉起）。
    /// </summary>
    private void ResidentCallback(object? state)
    {
        // 主程序正常退出期间暂停守护，避免占坑实例被反复拉起残留
        if (IsExitFlagValid()) return;
        EnsureRedirectorResident();
    }

    /// <summary>
    /// 主监控回调。每次检查所有受监控进程的状态，必要时通过 CreateProcessAsUser 重启。
    /// </summary>
    private void MonitorCallback(object? state)
    {
        // 动态调整定时器间隔
        try
        {
            _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
        catch { }

        bool hasAnomaly = false;

        try
        {
            // 学习主程序安全中心的"监测时长"挡位设置
            RefreshTierFromConfig();

            // 检查退出标志 — 如果主程序是正常退出的，不重启
            if (IsExitFlagValid())
            {
                // 退出标志有效时使用正常间隔轮询
                _currentInterval = _normalInterval;
                return;
            }

            // 检查并重启主程序 ClassScreenLock.exe
            if (!EnsureProcessRunning("ClassScreenLock", "ClassScreenLock.exe"))
            {
                hasAnomaly = true;
            }

            // 检查用户模式看门狗 CSL.Watchdog.exe（作为二级保护）：数量不足 3 个时一次性补满
            if (!EnsureWatchdogsFull())
            {
                hasAnomaly = true;
            }
            // 注：IFEO 重定向程序占坑实例由独立 1 秒定时器 _residentTimer 守护
            //（ResidentCallback → EnsureRedirectorResident），不占用主监控周期。

            _currentInterval = hasAnomaly ? _abnormalInterval : _normalInterval;
        }
        catch (Exception ex)
        {
            ServiceLogger.LogError("MonitorCallback 异常", ex);
            _currentInterval = _abnormalInterval;
        }
        finally
        {
            try
            {
                _monitorTimer?.Change(_currentInterval, _currentInterval);
            }
            catch { }
        }
    }

    /// <summary>
    /// 刷新监测间隔挡位：读取主程序 Data/settings.json 的 watchdogMonitorTier，
    /// 使服务监测速度与主程序安全中心"监测时长"设置保持一致。
    /// 为避免频繁读盘，仅在间隔超时或配置文件 mtime 变化时重新解析。
    /// </summary>
    private void RefreshTierFromConfig()
    {
        try
        {
            var nowUtc = DateTime.UtcNow;
            DateTime settingsMtime = DateTime.MinValue;

            if (File.Exists(_settingsJsonPath))
            {
                settingsMtime = File.GetLastWriteTimeUtc(_settingsJsonPath);
            }

            // 未到刷新周期且配置文件未变化：直接跳过
            if (_lastTierRefreshUtc != DateTime.MinValue &&
                (nowUtc - _lastTierRefreshUtc) < TierRefreshMinInterval &&
                settingsMtime <= _lastTierRefreshUtc)
            {
                return;
            }

            var tier = WatchdogTierConfig.LoadFromSettings(_settingsJsonPath);
            var newNormal = TimeSpan.FromMilliseconds(tier.NormalMs);
            var newAbnormal = TimeSpan.FromMilliseconds(tier.AbnormalMs);

            if (newNormal != _normalInterval || newAbnormal != _abnormalInterval)
            {
                _normalInterval = newNormal;
                _abnormalInterval = newAbnormal;
            }

            _lastTierRefreshUtc = nowUtc;
        }
        catch (Exception ex)
        {
            ServiceLogger.LogError("刷新监测挡位配置时异常", ex);
        }
    }

    /// <summary>
    /// 确保 3 个用户模式看门狗实例都在运行。数量不足时一次性补满（快速同时补充）。
    /// </summary>
    private bool EnsureWatchdogsFull()
    {
        try
        {
            var existing = Process.GetProcessesByName("CSL.Watchdog");
            int existingCount = existing.Length;
            foreach (var p in existing) p.Dispose();

            if (existingCount >= WatchdogTargetCount)
            {
                return true; // 数量已足
            }

            int needToStart = WatchdogTargetCount - existingCount;
            ServiceLogger.Log("WatchdogService", "辅助进程数量不足，正在补充");

            string exePath = Path.Combine(_baseDir, "CSL.Watchdog.exe");
            if (!File.Exists(exePath))
            {
                return false;
            }

            // 并行快速启动补齐（与主程序一致，实例 ID 从现有数量开始偏移）
            System.Threading.Tasks.Parallel.For(0, needToStart, i =>
            {
                int instanceId = existingCount + i;
                bool launched = CreateProcessAsUserHelper.LaunchProcessAsActiveUser(exePath, instanceId.ToString());
                if (!launched)
                {
                    ServiceLogger.Log("WatchdogService", "补充辅助进程失败（可能没有活动用户会话）");
                }
            });

            return false; // 刚启动，当前数量仍不足 → 触发异常快查
        }
        catch (Exception ex)
        {
            ServiceLogger.LogError("补充看门狗实例时异常", ex);
            return false;
        }
    }

    /// <summary>
    /// 确保 IFEO 重定向程序（CSL.IfeoRedirector.exe）的常驻占坑实例在运行。
    ///
    /// 背景：IFEO 重定向程序每次触发才短暂运行，平时文件无人占用，学生可把
    /// 主程序目录（或固定部署目录）下的 CSL.IfeoRedirector.exe 删掉，导致弹窗失效。
    /// 方案：由本服务以 SYSTEM 身份（Session 0）启动一个常驻实例（参数 --resident），
    /// 该实例持有 exe 文件句柄（共享读/写但不共享删除）——文件删除/重命名被系统拒绝，
    /// 而 IFEO 每次触发的新实例仍能正常读取启动（多实例并存，互不干扰）。
    /// 以 SYSTEM 身份运行还保证学生进程无法结束占坑实例；本服务按周期轮询互斥，
    /// 占坑实例丢失时立即重新拉起。
    /// </summary>
    private void EnsureRedirectorResident()
    {
        try
        {
            // 探测全局互斥：占坑实例存活则无需处理。
            // 服务运行在 Session 0，其命名对象天然位于 Global 命名空间，与占坑实例一致。
            try
            {
                using var existing = Mutex.OpenExisting(@"Global\CSL.IfeoRedirector.Resident");
                return; // 占坑实例正在运行
            }
            catch
            {
                // 互斥不存在 → 占坑实例缺失，需要启动
            }

            // 定位重定向程序：主程序目录优先，固定部署目录（%ProgramData%\ClassScreenLock\）兜底
            string[] candidates =
            {
                Path.Combine(_baseDir, "CSL.IfeoRedirector.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
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
                return;
            }

            // 直接以服务自身（LocalSystem）身份启动，进程进入 Session 0 常驻，
            // 学生无法结束它；不要用 CreateProcessAsUser（用户身份可被杀）。
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--resident",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
        }
        catch (Exception ex)
        {
            ServiceLogger.LogError("确保重定向程序占坑实例时异常", ex);
        }
    }

    /// <summary>
    /// 确保指定进程正在运行。如果未运行，使用 CreateProcessAsUser 在用户会话中启动它。
    /// </summary>
    /// <param name="processName">进程名（不带扩展名）</param>
    /// <param name="exeFileName">可执行文件名（相对于基础目录）</param>
    /// <returns>true 表示进程正在运行或成功启动；false 表示检测到异常（进程未运行）</returns>
    private bool EnsureProcessRunning(string processName, string exeFileName)
    {
        try
        {
            var existing = Process.GetProcessesByName(processName);
            if (existing.Length > 0)
            {
                foreach (var p in existing) p.Dispose();
                return true; // 进程正在运行
            }

            // 进程未运行，通过 CreateProcessAsUser 启动
            string exePath = Path.Combine(_baseDir, exeFileName);
            ServiceLogger.Log("WatchdogService", $"{processName} 未运行，正在恢复");

            bool launched = CreateProcessAsUserHelper.LaunchProcessAsActiveUser(exePath);
            if (!launched)
            {
                ServiceLogger.Log("WatchdogService", $"恢复 {processName} 失败（可能没有活动用户会话）");
            }

            // 无论是否启动成功，进程当前不在运行 → 返回 false 表示异常状态
            return false;
        }
        catch (Exception ex)
        {
            ServiceLogger.LogError($"检查 {processName} 时异常", ex);
            return false;
        }
    }

    /// <summary>
    /// 检查退出标志（exit.dat）是否有效。
    /// 主程序正常退出时会创建此文件，服务检测到后不会立即重启主程序。
    /// 退出标志在以下情况视为无效（将恢复重启逻辑）：
    ///   - 文件不存在
    ///   - 解密失败
    ///   - 超过有效期（ExitFlagMaxAge）
    /// </summary>
    private bool IsExitFlagValid()
    {
        try
        {
            if (!File.Exists(_exitFlagFile))
                return false;

            var encryptedData = File.ReadAllBytes(_exitFlagFile);
            if (encryptedData.Length < 32)
                return false;

            var decryptedData = DecryptExitFlag(encryptedData);
            if (decryptedData == null)
                return false;

            var content = Encoding.UTF8.GetString(decryptedData);
            var parts = content.Split('|');
            if (parts.Length != 2)
                return false;

            if (!long.TryParse(parts[1], out var timestamp))
                return false;

            var flagTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).LocalDateTime;
            var age = DateTime.Now - flagTime;

            if (age > ExitFlagMaxAge)
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            ServiceLogger.LogError("检查退出标志时异常", ex);
            return false;
        }
    }

    /// <summary>
    /// 解密退出标志。与主程序的加密格式兼容：
    /// [16字节Key][16字节IV][AES-CBC加密的 PID|timestamp]
    /// </summary>
    private static byte[]? DecryptExitFlag(byte[] encryptedData)
    {
        try
        {
            if (encryptedData.Length < 32)
                return null;

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
            return decryptor.TransformFinalBlock(payload, 0, payload.Length);
        }
        catch
        {
            return null;
        }
    }
}

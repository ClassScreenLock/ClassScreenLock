using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ClassScreenLock.Models;
using System.Windows.Automation;

namespace ClassScreenLock.Services;

public class NetworkBlockingService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint SC_CLOSE = 0xF060;

    [DllImport("oleacc.dll")]
    private static extern int AccessibleObjectFromWindow(IntPtr hWnd, uint dwObjectID, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? pacc);

    [DllImport("oleacc.dll")]
    private static extern int AccessibleChildren(object paccContainer, int iChildStart, int cChildren, [Out] object[]? rgvarChildren, out int pcObtained);

    private static readonly Guid IID_IAccessible = new(0x618736E0, 0x3C3D, 0x11CF, 0x81, 0x0C, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71);
    private const uint OBJID_WINDOW = 0x00000000;

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_W = 0x57;
    private const byte VK_RETURN = 0x0D;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly NetworkBlockingService _instance = new();
    public static NetworkBlockingService Instance => _instance;

    private const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";
    private const string MarkerStart = "# CLASS_SCREEN_LOCK_START";
    private const string MarkerEnd = "# CLASS_SCREEN_LOCK_END";
    private const string FirewallGroup = "ClassScreenLock";

    private Thread? _monitorThread;
    private bool _isMonitoring;
    private CancellationTokenSource? _cts;
    private int _cycleCount = 0; // 用于控制深度扫描频率

    private DateTime _lastRulesIntegrityCheckUtc = DateTime.MinValue;
    private int _integrityRepairRunning;
    private bool _integrityWarningShown;
    private readonly SemaphoreSlim _applyRulesLock = new(1, 1);

    private static bool _adminRestartAttempted;

    // 常见的浏览器进程名
    private readonly string[] _browserProcesses = 
    { 
        "chrome", "msedge", "firefox", "iexplore", "opera", "brave", "safari",
        "360chrome", "360se", "sogouexplorer", "qqbrowser", "ucbrowser", "liebao", "2345explorer", "maxthon",
        "vivaldi", "yandex", "browser", "centbrowser", "cent", "avastbrowser", "securebrowser", "avgbrowser",
        "ccleanerbrowser", "slimjet", "torch", "blisk", "epic", "hiddenbrowser"
    };

    private readonly string[] _dohHosts =
    {
        "dns.google",
        "cloudflare-dns.com",
        "mozilla.cloudflare-dns.com",
        "dns.quad9.net",
        "doh.pub",
        "dot.pub",
        "dns.alidns.com",
        "dns.nextdns.io",
        "dns.opendns.com"
    };

    private readonly string[] _sniIndicators =
    {
        "host-resolver-rules",
        "ignore-certificate-errors",
        "proxy-server",
        "cealing",
        "cealer",
        "map "
    };

    private NetworkBlockingService() 
    {
        StartMonitoring();
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        _isMonitoring = true;
        _cts = new CancellationTokenSource();
        
        // 使用普通优先级线程，降低对系统资源的抢占
        _monitorThread = new Thread(() => MonitorLoop(_cts.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.Normal,
            Name = "NetworkMonitor"
        };
        _monitorThread.Start();
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;
        _cts?.Cancel();
        _monitorThread?.Join(500);
    }

    private void MonitorLoop(CancellationToken token)
    {
        var stopwatch = new Stopwatch();

        // 启动时立即执行一次完整自检
        LogService.Observe(Task.Run(async () =>
        {
            try
            {
                var settings = SettingsService.Blockage;
                var lockService = LockScreenService.Instance;
                bool lockState = lockService.IsLocked || lockService.IsProtectionOnlyActive;
                if (settings != null && (settings.IsNetworkLockEnabled || lockState))
                {
                    await EnsureBlockingIntegrityAsync(lockState);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Initial Integrity Check Error: {ex.Message}");
            }
        }, token), "NetworkBlocking.InitialIntegrity");
        
        while (!token.IsCancellationRequested && _isMonitoring)
        {
            stopwatch.Restart();
            
            var settings = SettingsService.Blockage;
            var lockService = LockScreenService.Instance;
            bool lockState = lockService.IsLocked || lockService.IsProtectionOnlyActive;
            if (settings != null && (settings.IsNetworkLockEnabled || lockState || settings.IsBasicProtectionEnabled))
            {
                try
                {
                    ExecuteDetectionCycle();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Detection Cycle Error: {ex.Message}");
                }

                TryScheduleIntegrityCheck(lockState);
            }

            int elapsed = (int)stopwatch.ElapsedMilliseconds;
            int intervalMs = CalculateNetworkCheckInterval(lockService.IsLocked, lockService.IsProtectionOnlyActive, settings?.IsNetworkLockEnabled ?? false, settings?.IsBasicProtectionEnabled ?? false);
            int sleepTime = intervalMs - elapsed;
            if (sleepTime > 0)
            {
                try { Task.Delay(sleepTime, token).Wait(token); } catch { }
            }
        }
    }

    private int CalculateNetworkCheckInterval(bool isLocked, bool isProtectionOnly, bool isNetworkLockEnabled, bool isBasicProtectionEnabled)
    {
        if (isLocked)
        {
            return 400;
        }
        
        if (isProtectionOnly || isNetworkLockEnabled || isBasicProtectionEnabled)
        {
            return 500;
        }
        
        return 2000;
    }

    private void TryScheduleIntegrityCheck(bool lockState)
    {
        try
        {
            var settings = SettingsService.Blockage;
            if (settings == null) return;
            if (!(settings.IsNetworkLockEnabled || lockState)) return;

            var now = DateTime.UtcNow;
            var interval = lockState ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(60);
            
            if ((now - _lastRulesIntegrityCheckUtc) < interval) return;
            
            _lastRulesIntegrityCheckUtc = now;
            LogService.Instance.Log("Debug", "IntegrityCheck", "Monitor", $"Scheduling integrity check. LockState: {lockState}, Interval: {interval.TotalSeconds}s");

            LogService.Observe(Task.Run(async () =>
            {
                try
                {
                    await EnsureBlockingIntegrityAsync(lockState);
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Error", "IntegrityCheckTaskError", "Monitor", ex.Message);
                }
            }), "NetworkBlocking.IntegrityCheck");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "TryScheduleIntegrityCheckError", "Monitor", ex.Message);
        }
    }

    private async Task EnsureBlockingIntegrityAsync(bool lockState)
    {
        if (Interlocked.Exchange(ref _integrityRepairRunning, 1) == 1) return;
        
        if (!await _applyRulesLock.WaitAsync(0))
        {
            Interlocked.Exchange(ref _integrityRepairRunning, 0);
            return;
        }

        try
        {
            if (!CanPerformIntegrityCheck(lockState)) return;

            var rules = NetworkRuleService.LoadRules();
            if (!HasActiveDomainRules(rules)) return;

            var blockingStatus = CheckBlockingStatus();
            if (blockingStatus.IsAllOk) return;

            if (!IsAdministrator())
            {
                ShowAdministratorWarning();
                return;
            }

            await RepairBlockingRules(rules, blockingStatus);
        }
        finally
        {
            _applyRulesLock.Release();
            Interlocked.Exchange(ref _integrityRepairRunning, 0);
        }
    }

    private bool CanPerformIntegrityCheck(bool lockState)
    {
        var settings = SettingsService.Blockage;
        return settings != null && (settings.IsNetworkLockEnabled || lockState);
    }

    private bool HasActiveDomainRules(List<NetworkRule>? rules)
    {
        if (rules == null) return false;
        return rules.Any(r => r.IsEnabled && r.Type == "Domain" && !string.IsNullOrWhiteSpace(r.Domain));
    }

    private BlockingStatus CheckBlockingStatus()
    {
        return new BlockingStatus
        {
            HostsOk = HostsMarkersPresent(),
            FirewallOk = HasFirewallDomainRules()
        };
    }

    private void ShowAdministratorWarning()
    {
        if (!_integrityWarningShown)
        {
            _integrityWarningShown = true;
            NotificationService.Instance.ShowWarning("检测到网络拦截规则被更改，但当前无管理员权限，无法自动恢复。请以管理员身份运行应用。");
        }
    }

    private async Task RepairBlockingRules(List<NetworkRule> rules, BlockingStatus status)
    {
        if (!status.FirewallOk)
        {
            await RepairFirewallAndHosts(rules);
            return;
        }

        if (!status.HostsOk)
        {
            RepairHostsOnly(rules);
        }
    }

    private async Task RepairFirewallAndHosts(List<NetworkRule> rules)
    {
        LogService.Instance.Log("Info", "IntegrityRepair", "Firewall", "Firewall rules missing, re-applying all rules.");
        EnsureFirewallEnabled();
        ClearHostsRules();
        await UpdateDomainFirewallRules(rules);
        UpdateHostsFile(rules);
    }

    private void RepairHostsOnly(List<NetworkRule> rules)
    {
        LogService.Instance.Log("Info", "IntegrityRepair", "Hosts", "Hosts markers missing, re-applying hosts file.");
        UpdateHostsFile(rules);
    }

    private record BlockingStatus
    {
        public bool HostsOk { get; init; }
        public bool FirewallOk { get; init; }
        public bool IsAllOk => HostsOk && FirewallOk;
    }

    private bool HostsMarkersPresent()
    {
        try
        {
            if (!File.Exists(HostsPath)) return true;
            var lines = File.ReadAllLines(HostsPath);
            bool hasStart = lines.Any(l => string.Equals(l.Trim(), MarkerStart, StringComparison.Ordinal));
            bool hasEnd = lines.Any(l => string.Equals(l.Trim(), MarkerEnd, StringComparison.Ordinal));
            return hasStart && hasEnd;
        }
        catch
        {
            return true;
        }
    }

    private bool HasFirewallDomainRules()
    {
        // 先尝试通过 COM 快速检查
        if (HasFirewallDomainRulesCom()) return true;

        // 如果 COM 没找到，不要轻易下结论，因为可能 COM API 访问受限或规则属性不兼容
        // 我们不在这里用 netsh 检查，因为 netsh 解析输出太慢且容易出错。
        // 相反，我们在 EnsureBlockingIntegrityAsync 中如果发现 COM 检查失败，就执行一次重建。
        return false;
    }

    private bool HasFirewallDomainRulesCom()
    {
        try
        {
            var firewallPolicy = TryGetFirewallPolicy();
            if (firewallPolicy == null) return false;

            var rules = firewallPolicy.Rules;
            if (rules == null) return false;

            if (!HasValidRulesCount(rules)) return false;

            if (TryFindEnabledRuleByName(rules, "ClassScreenLock_DomainBlock_Out")) return true;

            return TryFindEnabledRuleByEnumeration(rules);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FirewallCheckException", "Firewall", ex.Message);
            return false;
        }
    }

    private dynamic? TryGetFirewallPolicy()
    {
        var policy2 = CreateComObject("HNetCfg.FwPolicy2");
        if (policy2 == null)
        {
            LogService.Instance.Log("Warning", "FirewallCheck", "COM", "Failed to create HNetCfg.FwPolicy2 object.");
            return null;
        }
        return policy2;
    }

    private bool HasValidRulesCount(dynamic rules)
    {
        int count = 0;
        try { count = rules.Count; }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "FirewallCheck", "COM", $"Failed to get rules count: {ex.Message}");
        }

        if (count == 0)
        {
            LogService.Instance.Log("Info", "FirewallCheck", "COM", "Firewall rules count is 0.");
            return false;
        }
        return true;
    }

    private bool TryFindEnabledRuleByName(dynamic rules, string ruleName)
    {
        try
        {
            dynamic? rule = null;
            try { rule = rules.Item(ruleName); } catch { }

            if (rule == null) return false;

            bool isEnabled = IsRuleEnabled(rule);
            if (isEnabled) return true;

            LogService.Instance.Log("Info", "FirewallCheck", "COM", $"Found rule {ruleName} but it is disabled.");
            return false;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Debug", "FirewallCheck", "COM", $"Direct lookup failed: {ex.Message}");
            return false;
        }
    }

    private bool TryFindEnabledRuleByEnumeration(dynamic rules)
    {
        int checkedCount = 0;
        foreach (dynamic rule in (IEnumerable)rules)
        {
            checkedCount++;
            if (checkedCount > 1000) break;

            if (IsClassScreenLockDomainBlockRule(rule)) return true;
        }

        LogService.Instance.Log("Info", "FirewallCheck", "COM", $"Finished traversing {checkedCount} rules, no active ClassScreenLock_DomainBlock_ rules found.");
        return false;
    }

    private bool IsClassScreenLockDomainBlockRule(dynamic rule)
    {
        try
        {
            if (rule == null) return false;
            string? name = rule.Name;
            if (string.IsNullOrWhiteSpace(name)) return false;

            if (name.StartsWith("ClassScreenLock_DomainBlock_", StringComparison.OrdinalIgnoreCase))
            {
                return IsRuleEnabled(rule);
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsRuleEnabled(dynamic rule)
    {
        try { return rule.Enabled; }
        catch { return false; }
    }

    private HashSet<uint> _cachedBrowserPids = new();
    private DateTime _lastPidUpdate = DateTime.MinValue;
    private readonly Dictionary<uint, DateTime> _lastInterceptionAt = new();
    private readonly Dictionary<uint, string> _lastInterceptionTitle = new();
    private readonly Dictionary<uint, string> _cachedBrowserUrls = new();
    private readonly Dictionary<uint, DateTime> _cachedBrowserUrlTimestamps = new();
    private DateTime _lastUrlCacheCleanup = DateTime.MinValue;
    private static readonly TimeSpan UrlCacheValidityDuration = TimeSpan.FromSeconds(0.5);

    private void ExecuteDetectionCycle()
    {
        var settings = SettingsService.Blockage;
        if (settings == null) return;

        var lockService = LockScreenService.Instance;
        bool lockState = lockService.IsLocked || lockService.IsProtectionOnlyActive;

        if (!ShouldRunDetection(lockState, settings)) return;

        IntPtr foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == IntPtr.Zero) return;

        _cycleCount++;
        bool isDeepScanCycle = _cycleCount % 10 == 0;

        var activeRules = GetActiveRules();
        if (activeRules == null || !activeRules.Any()) return;

        if (TryInterceptForegroundBrowser(foregroundHwnd, activeRules, settings, isDeepScanCycle)) return;

        UpdateBrowserPidsIfNeeded(isDeepScanCycle);
        if (!_cachedBrowserPids.Any()) return;

        ScanAllBrowserWindows(activeRules, settings, isDeepScanCycle);
        CleanupUrlCache();
    }

    private bool ShouldRunDetection(bool lockState, SoftwareBlockageModel settings)
    {
        return lockState || settings.IsNetworkLockEnabled || settings.IsBasicProtectionEnabled;
    }

    private List<NetworkRule>? GetActiveRules()
    {
        var rules = NetworkRuleService.LoadRules();
        if (rules == null) return null;
        return rules.Where(r => r.IsEnabled).ToList();
    }

    private bool TryInterceptForegroundBrowser(IntPtr foregroundHwnd, List<NetworkRule> activeRules, SoftwareBlockageModel settings, bool isDeepScanCycle)
    {
        try
        {
            GetWindowThreadProcessId(foregroundHwnd, out uint fgPid);
            using var fgProcess = Process.GetProcessById((int)fgPid);
            var processName = fgProcess.ProcessName;

            if (!IsBrowserProcess(processName)) return false;

            var windowTitle = GetWindowTitle(foregroundHwnd);
            var browserUrl = GetCachedBrowserUrl(foregroundHwnd, processName, isDeepScanCycle);
            var combinedText = CombineText(windowTitle, browserUrl);

            LogBrowserUrlIfNeeded(browserUrl, processName);

            if (TryAnalyzeAndIntercept(combinedText, activeRules, (uint)fgProcess.Id, foregroundHwnd)) return true;

            if (isDeepScanCycle && TryDetectAndBlockSniForgery(fgPid, processName, settings)) return true;
        }
        catch { }

        return false;
    }

    private bool IsBrowserProcess(string processName)
    {
        return _browserProcesses.Any(b => string.Equals(b, processName, StringComparison.OrdinalIgnoreCase));
    }

    private string GetWindowTitle(IntPtr hWnd)
    {
        var sb = new StringBuilder(1024);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private string CombineText(string title, string url)
    {
        return string.Join(" ", new[] { title, url }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private void LogBrowserUrlIfNeeded(string browserUrl, string processName)
    {
        if (!string.IsNullOrWhiteSpace(browserUrl))
        {
            LogService.Instance.Log("Debug", "BrowserScan", processName, $"URL: {browserUrl}");
        }
    }

    private bool TryAnalyzeAndIntercept(string combinedText, List<NetworkRule> activeRules, uint processId, IntPtr hWnd)
    {
        if (string.IsNullOrWhiteSpace(combinedText)) return false;

        var analysis = ContentAnalysisEngine.Instance.Analyze(combinedText, activeRules);
        if (analysis.IsViolation)
        {
            ExecuteInterception(processId, analysis, hWnd);
            return true;
        }
        return false;
    }

    private bool TryDetectAndBlockSniForgery(uint pid, string processName, SoftwareBlockageModel settings)
    {
        if (!settings.IsNetworkLockEnabled) return false;

        var cmd = GetProcessCommandLine(pid);
        var frontHosts = ExtractFrontingHosts(cmd);
        if (frontHosts.Count == 0) return false;

        LogService.Instance.Log("Debug", "SniForgeryDetected", processName, string.Join(",", frontHosts));
        LogService.Observe(BlockFrontingHostsAsync(frontHosts), "NetworkBlocking.BlockFrontHosts");
        NotificationService.Instance.ShowWarning("检测到SNI伪造，已临时屏蔽前置域网络访问。");
        return true;
    }

    private void UpdateBrowserPidsIfNeeded(bool isDeepScanCycle)
    {
        bool needsUpdate = isDeepScanCycle || 
                          _cachedBrowserPids.Count == 0 || 
                          (DateTime.Now - _lastPidUpdate).TotalSeconds > 10;

        if (needsUpdate)
        {
            UpdateBrowserPids();
        }
    }

    private void ScanAllBrowserWindows(List<NetworkRule> activeRules, SoftwareBlockageModel settings, bool isDeepScanCycle)
    {
        var interceptedPidsThisCycle = new HashSet<uint>();
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (interceptedPidsThisCycle.Contains(processId)) return true;

            if (_cachedBrowserPids.Contains(processId))
            {
                if (TryProcessBrowserWindow(hWnd, processId, activeRules, settings, isDeepScanCycle, interceptedPidsThisCycle)) return true;
            }
            return true;
        }, IntPtr.Zero);
    }

    private bool TryProcessBrowserWindow(IntPtr hWnd, uint processId, List<NetworkRule> activeRules, SoftwareBlockageModel settings, bool isDeepScanCycle, HashSet<uint> interceptedPids)
    {
        string procName = GetProcessNameSafe(processId);
        if (string.IsNullOrEmpty(procName)) return false;

        var windowTitle = GetWindowTitle(hWnd);
        var browserUrl = GetCachedBrowserUrl(hWnd, procName, isDeepScanCycle);
        var combinedText = CombineText(windowTitle, browserUrl);

        if (TryAnalyzeAndIntercept(combinedText, activeRules, processId, hWnd))
        {
            interceptedPids.Add(processId);
            return true;
        }

        if (isDeepScanCycle && TryDetectAndBlockSniForgeryForWindow(processId, settings, interceptedPids)) return true;

        return false;
    }

    private string GetProcessNameSafe(uint processId)
    {
        try
        {
            var p = Process.GetProcessById((int)processId);
            return p.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool TryDetectAndBlockSniForgeryForWindow(uint processId, SoftwareBlockageModel settings, HashSet<uint> interceptedPids)
    {
        if (!settings.IsNetworkLockEnabled) return false;

        var cmd = GetProcessCommandLine(processId);
        var frontHosts = ExtractFrontingHosts(cmd);
        if (frontHosts.Count == 0) return false;

        LogService.Instance.Log("Debug", "SniForgeryDetected", processId.ToString(), string.Join(",", frontHosts));
        LogService.Observe(BlockFrontingHostsAsync(frontHosts), "NetworkBlocking.BlockFrontHosts.WindowEnum");
        NotificationService.Instance.ShowWarning("检测到SNI伪造，已临时屏蔽前置域网络访问。");
        interceptedPids.Add(processId);
        return true;
    }

    private string GetCachedBrowserUrl(IntPtr hWnd, string processName, bool forceRefresh)
    {
        uint key = (uint)hWnd.ToInt64();
        var now = DateTime.Now;
        
        if (_cachedBrowserUrls.TryGetValue(key, out var cachedUrl))
        {
            if (!forceRefresh && _cachedBrowserUrlTimestamps.TryGetValue(key, out var timestamp))
            {
                if ((now - timestamp) <= UrlCacheValidityDuration)
                {
                    return cachedUrl;
                }
            }
        }
        
        var newUrl = GetBrowserUrlFromAccessibility(hWnd, processName);
        _cachedBrowserUrls[key] = newUrl;
        _cachedBrowserUrlTimestamps[key] = now;
        return newUrl;
    }

    private void CleanupUrlCache()
    {
        var now = DateTime.Now;
        if ((now - _lastUrlCacheCleanup).TotalSeconds > 5)
        {
            var expiredKeys = _cachedBrowserUrlTimestamps
                .Where(kvp => (now - kvp.Value) > UrlCacheValidityDuration)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var key in expiredKeys)
            {
                _cachedBrowserUrls.Remove(key);
                _cachedBrowserUrlTimestamps.Remove(key);
            }
            
            _lastUrlCacheCleanup = now;
        }
    }

    private void UpdateBrowserPids()
    {
        var browserPids = new HashSet<uint>();

        AddProcessesByName(browserPids, _browserProcesses);

        var settings = SettingsService.Blockage;
        AddCustomBrowserProcesses(browserPids, settings);

        TryDetectChromiumBrowsers(browserPids, settings);

        _cachedBrowserPids = browserPids;
        _lastPidUpdate = DateTime.Now;
    }

    private void AddProcessesByName(HashSet<uint> browserPids, IEnumerable<string> processNames)
    {
        foreach (var name in processNames)
        {
            AddProcessIdsByName(browserPids, name);
        }
    }

    private void AddProcessIdsByName(HashSet<uint> browserPids, string processName)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName(processName))
            {
                browserPids.Add((uint)p.Id);
            }
        }
        catch { }
    }

    private void AddCustomBrowserProcesses(HashSet<uint> browserPids, SoftwareBlockageModel? settings)
    {
        if (settings?.CustomBrowserProcesses == null) return;

        foreach (var name in settings.CustomBrowserProcesses)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            AddProcessIdsByName(browserPids, name);
        }
    }

    private void TryDetectChromiumBrowsers(HashSet<uint> browserPids, SoftwareBlockageModel? settings)
    {
        if (settings?.EnableChromiumAutoDetection != true) return;

        try
        {
            DetectChromiumBasedBrowsers(browserPids);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BrowserDetection] Chromium auto-detection error: {ex.Message}");
        }
    }

    private void DetectChromiumBasedBrowsers(HashSet<uint> browserPids)
    {
        var chromiumDlls = new[]
        {
            "chrome.dll",
            "chrome_child.dll",
            "libcef.dll",
            "libEGL.dll",
            "libGLESv2.dll",
            "v8.dll",
            "swiftshader.dll",
            "electron.dll"
        };

        var allProcesses = Process.GetProcesses();
        foreach (var proc in allProcesses)
        {
            try
            {
                if (browserPids.Contains((uint)proc.Id)) continue;
                
                if (proc.MainModule == null) continue;
                var processName = proc.ProcessName.ToLowerInvariant();
                
                if (processName.Contains("browser") || 
                    processName.Contains("chrome") || 
                    processName.Contains("edge") ||
                    processName.Contains("navigator") ||
                    processName.Contains("explorer") && processName != "explorer")
                {
                    if (HasChromiumModules(proc, chromiumDlls))
                    {
                        browserPids.Add((uint)proc.Id);
                        Debug.WriteLine($"[BrowserDetection] Detected Chromium browser: {proc.ProcessName} (PID: {proc.Id})");
                    }
                }
            }
            catch { }
        }
    }

    private bool HasChromiumModules(Process process, string[] chromiumDlls)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                try
                {
                    var moduleName = module.ModuleName?.ToLowerInvariant();
                    if (string.IsNullOrEmpty(moduleName)) continue;
                    
                    foreach (var dll in chromiumDlls)
                    {
                        if (moduleName.Equals(dll, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
        
        return false;
    }

    private void ExecuteInterception(uint processId, AnalysisResult analysis, IntPtr hWnd)
    {
        try
        {
            var p = Process.GetProcessById((int)processId);
            var sb = new StringBuilder(1024);
            GetWindowText(hWnd, sb, sb.Capacity);
            var title = sb.ToString();

            var now = DateTime.UtcNow;
            if (_lastInterceptionAt.TryGetValue(processId, out var last))
            {
                if ((now - last) < TimeSpan.FromMilliseconds(1200))
                {
                    if (_lastInterceptionTitle.TryGetValue(processId, out var lastTitle) && string.Equals(lastTitle, title, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }
            }

            _lastInterceptionAt[processId] = now;
            _lastInterceptionTitle[processId] = title;

            if (!string.IsNullOrWhiteSpace(analysis.MatchedDomain))
            {
                LogService.Observe(BlockSubdomainAsync(analysis.MatchedDomain), "NetworkBlocking.BlockSubdomain");
            }

            PerformInterception(p, analysis, hWnd);
        }
        catch { }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private void PerformInterception(Process p, AnalysisResult analysis, IntPtr hWnd)
    {
        // 1. 直接执行“防火墙式”阻断：关闭违规标签页并切断其网络连接
        try
        {
            if (hWnd != IntPtr.Zero && IsWindow(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
                Thread.Sleep(50);

                bool tabClosed = TryCloseTabByHotkey(hWnd);
                
                KillTcpConnectionsForProcess(p.Id);
                
                if (!tabClosed)
                {
                    TryTerminateBrowserProcess(p);
                    Debug.WriteLine($"[FIREWALL-BLOCK] Intercepted violation: {analysis.MatchedPattern} in {p.ProcessName} (PID: {p.Id}). Hotkey failed, process terminated.");
                }
                else
                {
                    Debug.WriteLine($"[FIREWALL-BLOCK] Intercepted violation: {analysis.MatchedPattern} in {p.ProcessName} (PID: {p.Id}). Tab closed by hotkey.");
                }
            }
            else
            {
                KillTcpConnectionsForProcess(p.Id);
                TryTerminateBrowserProcess(p);
                Debug.WriteLine($"[FIREWALL-BLOCK] Found violation in {p.ProcessName} (No window). Process terminated.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Firewall interception failed: {ex.Message}");
        }

        // 2. 异步记录到独立的 JSON 日志文件
        LogService.Observe(Task.Run(() =>
        {
            try
            {
                var entry = new InterceptedContent
                {
                    Timestamp = DateTime.Now,
                    Domain = analysis.MatchedPattern,
                    Title = "Blocked Content Detected",
                    ProcessName = p.ProcessName,
                    Reason = analysis.Reason,
                    Confidence = analysis.Confidence
                };
                InterceptionDatabase.Instance.Add(entry);
                LogService.Instance.Log("Network", "FirewallBlocked", entry.Domain, $"Reason: {analysis.Reason}, Confidence: {analysis.Confidence:P}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to record interception: {ex.Message}");
            }
        }), "NetworkBlocking.RecordInterception");

        // 3. 显示警告通知
        NotificationService.Instance.ShowError($"[安全警告] 检测到违禁访问: {analysis.MatchedPattern}。连接已被防火墙强制切断。", true);
    }

    private void KillTcpConnectionsForProcess(int processId)
    {
        try
        {
            RunCommand("ipconfig", "/flushdns");
        }
        catch { }
    }

    private void TryTerminateBrowserProcess(Process p)
    {
        try
        {
            var processName = p.ProcessName.ToLowerInvariant();
            
            if (_browserProcesses.Contains(processName) ||
                SettingsService.Blockage?.CustomBrowserProcesses?.Any(n => 
                    string.Equals(n, processName, StringComparison.OrdinalIgnoreCase)) == true)
            {
                LogService.Instance.Log("Warning", "BrowserTerminated", p.ProcessName, $"Process terminated due to violation. PID: {p.Id}");
                
                p.Kill();
                
                NotificationService.Instance.ShowWarning($"浏览器 {p.ProcessName} 因访问违禁内容已被强制关闭。");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TerminateProcess] Failed to terminate process: {ex.Message}");
        }
    }

    private bool TryCloseTabByHotkey(IntPtr hWnd)
    {
        try
        {
            keybd_event(VK_CONTROL, 0, 0, 0);
            keybd_event(VK_W, 0, 0, 0);
            keybd_event(VK_W, 0, KEYEVENTF_KEYUP, 0);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);
            Thread.Sleep(100);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool TryCloseTabByUIAutomation(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return false;

            if (TryCloseTabItems(element)) return true;
            if (TryCloseSelectedTab(element)) return true;

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[UIAutomation] Close tab failed: {ex.Message}");
            return false;
        }
    }

    private bool TryCloseTabItems(AutomationElement element)
    {
        var tabItemCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem);
        var tabItems = element.FindAll(TreeScope.Descendants, tabItemCondition);

        foreach (AutomationElement tabItem in tabItems)
        {
            if (TryInvokeCloseButtonForElement(tabItem)) return true;
        }
        return false;
    }

    private bool TryCloseSelectedTab(AutomationElement element)
    {
        var tabCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Tab);
        var tabs = element.FindAll(TreeScope.Descendants, tabCondition);

        foreach (AutomationElement tab in tabs)
        {
            if (TryCloseTabBySelectionPattern(tab)) return true;
        }
        return false;
    }

    private bool TryCloseTabBySelectionPattern(AutomationElement tab)
    {
        try
        {
            var selectionPattern = tab.GetCurrentPattern(SelectionPattern.Pattern) as SelectionPattern;
            if (selectionPattern == null) return false;

            var selection = selectionPattern.Current.GetSelection();
            if (selection == null || selection.Length == 0) return false;

            var selectedTab = selection[0];
            return TryInvokeCloseButtonForElement(selectedTab);
        }
        catch
        {
            return false;
        }
    }

    private bool TryInvokeCloseButtonForElement(AutomationElement element)
    {
        try
        {
            var closeButton = FindCloseButtonInTab(element);
            if (closeButton == null) return false;

            var invokePattern = closeButton.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;
            if (invokePattern == null) return false;

            invokePattern.Invoke();
            Thread.Sleep(50);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private AutomationElement? FindCloseButtonInTab(AutomationElement tabItem)
    {
        try
        {
            var buttonCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);
            var buttons = tabItem.FindAll(TreeScope.Children, buttonCondition);
            
            foreach (AutomationElement button in buttons)
            {
                try
                {
                    var name = button.Current.Name;
                    var automationId = button.Current.AutomationId;
                    
                    if (!string.IsNullOrEmpty(name) && 
                        (name.Contains("关闭", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("Close", StringComparison.OrdinalIgnoreCase) ||
                         name.Contains("×")))
                    {
                        return button;
                    }
                    
                    if (!string.IsNullOrEmpty(automationId) &&
                        (automationId.Contains("close", StringComparison.OrdinalIgnoreCase) ||
                         automationId.Contains("tabClose", StringComparison.OrdinalIgnoreCase)))
                    {
                        return button;
                    }
                }
                catch { }
            }
        }
        catch { }
        
        return null;
    }

    private bool TryNavigateToBlank(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return false;

            var editCondition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true)
            );
            
            var edits = element.FindAll(TreeScope.Descendants, editCondition);
            foreach (AutomationElement edit in edits)
            {
                try
                {
                    var name = edit.Current.Name;
                    var automationId = edit.Current.AutomationId;
                    
                    if (name?.Contains("地址", StringComparison.OrdinalIgnoreCase) == true ||
                        name?.Contains("Address", StringComparison.OrdinalIgnoreCase) == true ||
                        automationId?.Contains("address", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        var valuePattern = edit.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                        if (valuePattern != null)
                        {
                            valuePattern.SetValue("about:blank");
                            Thread.Sleep(50);
                            
                            keybd_event(VK_RETURN, 0, 0, 0);
                            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
                            return true;
                        }
                    }
                }
                catch { }
            }

            var addressBoxCondition = new PropertyCondition(AutomationElement.AutomationIdProperty, "addressEditBox", PropertyConditionFlags.IgnoreCase);
            var addressBox = element.FindFirst(TreeScope.Descendants, addressBoxCondition);
            if (addressBox != null)
            {
                try
                {
                    var valuePattern = addressBox.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    if (valuePattern != null)
                    {
                        valuePattern.SetValue("about:blank");
                        Thread.Sleep(50);
                        keybd_event(VK_RETURN, 0, 0, 0);
                        keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, 0);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[NavigateToBlank] Failed: {ex.Message}");
            return false;
        }
    }

    public async Task ApplyRulesAsync(string reason = "Unknown")
    {
        LogService.Instance.Log("Debug", "ApplyRulesAsync", "Network", $"ApplyRulesAsync called. Reason: {reason}");
        var settings = SettingsService.Blockage;
        var lockService = LockScreenService.Instance;
        bool lockState = lockService.IsLocked || lockService.IsProtectionOnlyActive;
        var rules = NetworkRuleService.LoadRules();
        if (settings == null || rules == null) return;

        // 使用信号量防止并发冲突
        if (!await _applyRulesLock.WaitAsync(0)) return;
        
        try
        {
            // 检查是否有活跃的域名规则
            var activeDomainRules = rules
                .Where(r => r.IsEnabled && r.Type == "Domain" && !string.IsNullOrWhiteSpace(r.Domain))
                .ToList();
            bool hasActiveRules = activeDomainRules.Any();

            // 如果既未开启总开关，且当前不在锁态/仅防护，则清空规则
            if (!(settings.IsNetworkLockEnabled || lockState))
            {
                ClearHostsRules();
                DeleteFirewallRulesByGroup(FirewallGroup);
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock");
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_Out");
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_In");
                DeleteFirewallRuleByName("BlockAllOutbound");
                LogService.Instance.Log("Info", "RulesCleared", "Network", "Network blocking disabled, all rules cleared.");
                return;
            }

            // 如果没有活跃规则，清理防火墙和 hosts
            if (!hasActiveRules)
            {
                ClearHostsRules();
                DeleteFirewallRulesByGroup(FirewallGroup);
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock");
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_Out");
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_In");
                DeleteFirewallRuleByName("BlockAllOutbound");
                LogService.Instance.Log("Info", "RulesCleared", "Network", "No active domain rules, cleared all blocking rules.");
                return;
            }

            if (!IsAdministrator())
            {
                TryRestartAsAdministrator();
                return;
            }

            // 确保旧的全拦截规则被删除，避免误杀所有网络
            DeleteFirewallRuleByName("BlockAllOutbound");
            EnsureFirewallEnabled();
            ClearHostsRules();
            await UpdateDomainFirewallRules(activeDomainRules);
            UpdateHostsFile(activeDomainRules);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NetworkBlockingService Error: {ex.Message}");
        }
        finally
        {
            _applyRulesLock.Release();
        }
    }

    private void TryRestartAsAdministrator()
    {
        if (_adminRestartAttempted) return;
        _adminRestartAttempted = true;

        try
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (!args.Any(a => string.Equals(a, "--restart", StringComparison.OrdinalIgnoreCase)))
            {
                args.Add("--restart");
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath)) return;

            NotificationService.Instance.ShowWarning("应用网络拦截需要管理员权限，正在请求授权…");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = string.Join(" ", args.Select(QuoteIfNeeded)),
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };

            Process.Start(psi);

            Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                {
                    lifetime.Shutdown();
                }
            });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            NotificationService.Instance.ShowWarning("已取消管理员授权：防火墙规则不会创建。请用管理员身份启动应用。");
        }
        catch
        {
            NotificationService.Instance.ShowWarning("无法请求管理员权限：防火墙规则不会创建。请用管理员身份启动应用。");
        }
    }

    private static string QuoteIfNeeded(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        if (arg.Contains(' ') || arg.Contains('"'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        return arg;
    }

    public void Cleanup()
    {
        try
        {
            _isMonitoring = false;
            _cts?.Cancel();

            // 将清理操作移到后台执行，不阻塞应用退出
            Task.Run(() =>
            {
                try
                {
                    bool isAdmin = IsAdministrator();
                    
                    if (isAdmin)
                    {
                        // 清理 Hosts 文件
                        ClearHostsRules();

                        // 删除防火墙规则
                        DeleteFirewallRulesByGroup(FirewallGroup);
                        DeleteFirewallRuleByName("ClassScreenLock_DomainBlock");
                        DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_Out");
                        DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_In");
                        DeleteFirewallRuleByName("BlockAllOutbound");
                        DeleteFirewallRuleByName("ClassScreenLock_FrontBlock_Out");
                        DeleteFirewallRuleByName("ClassScreenLock_FrontBlock_In");
                        for (int i = 2; i <= 50; i++)
                        {
                            DeleteFirewallRuleByName($"ClassScreenLock_FrontBlock_Out_{i}");
                            DeleteFirewallRuleByName($"ClassScreenLock_FrontBlock_In_{i}");
                        }
                    }
                    else
                    {
                        // 如果不是管理员，尝试以管理员身份运行清理命令
                        TryCleanupAsAdministrator();
                    }
                    
                    Debug.WriteLine("[CLEANUP] Network blocking rules removed on exit.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Cleanup Error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Cleanup Error: {ex.Message}");
        }
    }

    private void TryCleanupAsAdministrator()
    {
        try
        {
            // 使用 netsh 命令通过 UAC 提权清理防火墙规则
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"advfirewall firewall delete rule group=\"{FirewallGroup}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            
            try
            {
                Process.Start(psi)?.WaitForExit(5000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 用户取消了 UAC 提示，静默处理
                Debug.WriteLine("[CLEANUP] User cancelled UAC prompt for firewall cleanup.");
            }

            // 清理 hosts 文件 - 通过 PowerShell 提权
            var hostsCleanupScript = $@"
$hostsPath = '{HostsPath}'
$markerStart = '{MarkerStart}'
$markerEnd = '{MarkerEnd}'
if (Test-Path $hostsPath) {{
    $lines = Get-Content $hostsPath
    $startIndex = -1
    $endIndex = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {{
        if ($lines[$i].Trim() -eq $markerStart) {{ $startIndex = $i }}
        if ($lines[$i].Trim() -eq $markerEnd) {{ $endIndex = $i }}
    }}
    if ($startIndex -ge 0 -and $endIndex -ge 0 -and $endIndex -ge $startIndex) {{
        $newLines = $lines[0..($startIndex-1)] + $lines[($endIndex+1)..($lines.Count-1)]
        $newLines | Set-Content $hostsPath -Force
    }}
}}
ipconfig /flushdns
";
            
            var psipsi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"{hostsCleanupScript.Replace("\"", "\\\"")}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            };
            
            try
            {
                AppBlockingService.AllowPowerShellTemporarily(10);
                Process.Start(psipsi)?.WaitForExit(5000);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Debug.WriteLine("[CLEANUP] User cancelled UAC prompt for hosts cleanup.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TryCleanupAsAdministrator Error: {ex.Message}");
        }
    }

    private void ClearHostsRules()
    {
        try
        {
            if (!File.Exists(HostsPath)) return;
            if (!IsAdministrator()) return;

            var lines = File.ReadAllLines(HostsPath).ToList();
            int startIndex = lines.FindIndex(l => l.Trim() == MarkerStart);
            int endIndex = lines.FindIndex(l => l.Trim() == MarkerEnd);

            if (startIndex != -1 && endIndex != -1 && endIndex >= startIndex)
            {
                lines.RemoveRange(startIndex, endIndex - startIndex + 1);
                File.WriteAllLines(HostsPath, lines);
                RunCommand("ipconfig", "/flushdns");
            }
        }
        catch { }
    }

    /// <summary>
    /// 创建一个禁用的入站规则作为“标记”，确保 Windows 防火墙 GUI 能够识别并显示 ClassScreenLock 分组。
    /// 解决用户在图形界面中找不到规则的问题。
    /// </summary>
    private void EnsureFirewallGroupVisibility()
    {
        const string markerName = "ClassScreenLock_Visibility_Marker";
        string placeholderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "firewall_marker.dat");
        
        try
        {
            // 确保占位文件存在
            string directory = Path.GetDirectoryName(placeholderPath)!;
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            if (!File.Exists(placeholderPath)) File.WriteAllText(placeholderPath, "This is a placeholder file for Windows Firewall GUI visibility grouping.");

            // 1. 优先使用 COM API
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 != null)
            {
                dynamic rules = policy2.Rules;
                bool exists = false;
                try
                {
                    var rule = rules.Item(markerName);
                    if (rule != null) exists = true;
                }
                catch { }

                if (!exists)
                {
                    dynamic? rule = CreateComObject("HNetCfg.FWRule");
                    if (rule != null)
                    {
                        rule.Name = markerName;
                        rule.Grouping = FirewallGroup;
                        rule.Description = "这是一个禁用的标记规则，用于让 Windows 防火墙 GUI 能够识别并显示此分组。请勿删除。";
                        rule.Enabled = false; // 必须禁用，以免影响安全
                        rule.Action = NET_FW_ACTION_BLOCK;
                        rule.Direction = NET_FW_RULE_DIR_IN; // 入站方向
                        rule.InterfaceTypes = "All";
                        rule.ApplicationName = placeholderPath;
                        rule.Profiles = NET_FW_PROFILE2_ALL;
                        rules.Add(rule);
                        LogService.Instance.Log("Info", "FirewallVisibilityMarkerCreated", "Firewall", $"Created GUI visibility marker via COM with placeholder: {placeholderPath}");
                    }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Debug", "FirewallVisibilityMarkerError", "COM", ex.Message);
        }

        // 2. 回退到 netsh
        RunCommand("netsh", $"advfirewall firewall add rule name=\"{markerName}\" dir=in action=block program=\"{placeholderPath}\" group=\"{FirewallGroup}\" enable=no");
    }

    private async Task UpdateDomainFirewallRules(List<NetworkRule> rules)
    {
        if (!IsAdministrator())
        {
            Debug.WriteLine("[FIREWALL] Skip UpdateDomainFirewallRules: Not administrator.");
            return;
        }

        try
        {
            DeleteFirewallRulesByGroup(FirewallGroup);
            DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_Out");
            DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_In");

            EnsureFirewallGroupVisibility();

            var activeRules = rules
                .Where(r => r.IsEnabled && r.Type == "Domain" && !string.IsNullOrWhiteSpace(r.Domain))
                .ToList();

            if (!activeRules.Any())
            {
                Debug.WriteLine("[FIREWALL] No active domain rules to apply.");
                return;
            }

            var ipList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var domainsToResolve = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in activeRules)
            {
                string domain = rule.Domain.Trim().ToLower();
                if (string.IsNullOrWhiteSpace(domain)) continue;

                string baseDomain = domain;
                if (baseDomain.StartsWith("www."))
                {
                    baseDomain = baseDomain.Substring(4);
                }

                domainsToResolve.Add(baseDomain);
                domainsToResolve.Add("www." + baseDomain);
            }

            foreach (var host in _dohHosts)
            {
                domainsToResolve.Add(host);
            }

            var dnsTasks = domainsToResolve.Select(domain => ResolveDomainWithTimeoutAsync(domain, TimeSpan.FromSeconds(2)));
            var results = await Task.WhenAll(dnsTasks);

            foreach (var addresses in results)
            {
                if (addresses == null) continue;
                foreach (var addr in addresses)
                {
                    if (addr == null) continue;
                    if (IsLoopbackOrUnspecified(addr)) continue;
                    ipList.Add(NormalizeAddressString(addr));
                }
            }

            if (ipList.Any())
            {
                var chunks = ipList.Chunk(100).ToList();
                int chunkIndex = 1;

                foreach (var chunk in chunks)
                {
                    string chunkIps = string.Join(",", chunk);
                    string suffix = chunks.Count == 1 ? "" : (chunkIndex == 1 ? "" : $"_{chunkIndex}");

                    AddRemoteIpBlockRule($"ClassScreenLock_DomainBlock_Out{suffix}", FirewallGroup, NET_FW_RULE_DIR_OUT, chunkIps);
                    AddRemoteIpBlockRule($"ClassScreenLock_DomainBlock_In{suffix}", FirewallGroup, NET_FW_RULE_DIR_IN, chunkIps);

                    chunkIndex++;
                }

                Debug.WriteLine($"[FIREWALL] Applied {ipList.Count} IPs to firewall block rules in {chunks.Count} chunks.");
                LogService.Instance.Log("Info", "FirewallRulesApplied", "Firewall", $"Applied {ipList.Count} IPs in {chunks.Count} chunks.");

                LogService.Observe(Task.Run(() => {
                    if (!HasFirewallDomainRulesCom())
                    {
                        LogService.Instance.Log("Warning", "FirewallRuleValidationFailed", "Firewall", "Rules were applied but not found by COM API.");
                    }
                }), "NetworkBlocking.ValidateFirewallRules");
                
                await Task.Delay(50);
            }
            else
            {
                Debug.WriteLine("[FIREWALL] No IPs resolved from domains.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"UpdateDomainFirewallRules Error: {ex.Message}");
            LogService.Instance.Log("Error", "FirewallUpdateException", "Firewall", ex.Message);
        }
    }

    private void UpdateHostsFile(List<NetworkRule> rules)
    {
        try
        {
            if (!File.Exists(HostsPath)) return;

            if (!IsAdministrator())
            {
                NotificationService.Instance.ShowWarning("修改网络拦截规则需要管理员权限。");
                return;
            }

            int retryCount = 3;
            bool success = false;
            while (retryCount > 0 && !success)
            {
                try
                {
                    var lines = File.ReadAllLines(HostsPath).ToList();
                    int startIndex = lines.FindIndex(l => l.Trim() == MarkerStart);
                    int endIndex = lines.FindIndex(l => l.Trim() == MarkerEnd);

                    if (startIndex != -1 && endIndex != -1 && endIndex >= startIndex)
                    {
                        lines.RemoveRange(startIndex, endIndex - startIndex + 1);
                    }

                    var activeRules = rules
                        .Where(r => r.IsEnabled && r.Type == "Domain")
                        .ToList();

                    if (activeRules.Any())
                    {
                        lines.Add(MarkerStart);
                        foreach (var server in _dohHosts)
                        {
                            lines.Add($"127.0.0.1 {server}");
                            lines.Add($"::1 {server}");
                        }

                        foreach (var rule in activeRules)
                        {
                            string domain = rule.Domain.Trim().ToLower();
                            if (string.IsNullOrWhiteSpace(domain)) continue;

                            string baseDomain = domain;
                            if (baseDomain.StartsWith("www."))
                            {
                                baseDomain = baseDomain.Substring(4);
                            }

                            lines.Add($"127.0.0.1 {baseDomain}");
                            lines.Add($"::1 {baseDomain}");
                            lines.Add($"127.0.0.1 www.{baseDomain}");
                            lines.Add($"::1 www.{baseDomain}");
                        }
                        lines.Add(MarkerEnd);
                    }

                    File.WriteAllLines(HostsPath, lines);
                    success = true;
                }
                catch (IOException) when (retryCount > 1)
                {
                    retryCount--;
                    Thread.Sleep(500);
                }
                catch (Exception)
                {
                    throw;
                }
            }
            
            if (success)
            {
                RunCommand("ipconfig", "/flushdns");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "HostsUpdateFailed", "HostsFile", ex.Message);
        }
    }

    private void AddSubdomainToHosts(string subdomain)
    {
        if (string.IsNullOrWhiteSpace(subdomain)) return;
        if (!IsAdministrator()) return;

        try
        {
            var lines = File.ReadAllLines(HostsPath).ToList();
            string entry1 = $"127.0.0.1 {subdomain}";
            string entry2 = $"::1 {subdomain}";

            bool exists = lines.Any(l => l.Trim().Equals(entry1, StringComparison.OrdinalIgnoreCase) ||
                                         l.Trim().Equals(entry2, StringComparison.OrdinalIgnoreCase));

            if (exists) return;

            int endIndex = lines.FindIndex(l => l.Trim() == MarkerEnd);
            if (endIndex == -1) return;

            lines.Insert(endIndex, entry1);
            lines.Insert(endIndex + 1, entry2);
            File.WriteAllLines(HostsPath, lines);
            RunCommand("ipconfig", "/flushdns");
            
            Debug.WriteLine($"[HOSTS] Added subdomain: {subdomain}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HOSTS] Failed to add subdomain {subdomain}: {ex.Message}");
        }
    }

    private readonly HashSet<string> _addedSubdomainIps = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _subdomainLock = new();

    private async Task AddSubdomainToFirewallAsync(string subdomain)
    {
        if (string.IsNullOrWhiteSpace(subdomain)) return;
        if (!IsAdministrator()) return;

        try
        {
            var addresses = await ResolveDomainWithTimeoutAsync(subdomain, TimeSpan.FromSeconds(2));
            if (addresses == null || addresses.Length == 0) return;

            var newIps = new List<string>();
            lock (_subdomainLock)
            {
                foreach (var addr in addresses)
                {
                    if (addr == null) continue;
                    if (IsLoopbackOrUnspecified(addr)) continue;
                    var ipStr = NormalizeAddressString(addr);
                    if (!_addedSubdomainIps.Contains(ipStr))
                    {
                        _addedSubdomainIps.Add(ipStr);
                        newIps.Add(ipStr);
                    }
                }
            }

            if (newIps.Count == 0) return;

            foreach (var ip in newIps)
            {
                AddRemoteIpBlockRule($"ClassScreenLock_Subdomain_{ip.Replace(":", "_").Replace(".", "_")}", 
                    FirewallGroup, NET_FW_RULE_DIR_OUT, ip);
                AddRemoteIpBlockRule($"ClassScreenLock_Subdomain_In_{ip.Replace(":", "_").Replace(".", "_")}", 
                    FirewallGroup, NET_FW_RULE_DIR_IN, ip);
            }

            Debug.WriteLine($"[FIREWALL] Added {newIps.Count} IPs for subdomain: {subdomain}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[FIREWALL] Failed to add subdomain {subdomain}: {ex.Message}");
        }
    }

    public async Task BlockSubdomainAsync(string subdomain)
    {
        if (string.IsNullOrWhiteSpace(subdomain)) return;

        AddSubdomainToHosts(subdomain);
        await AddSubdomainToFirewallAsync(subdomain);
    }

    private bool IsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }


    private void EnsureFirewallEnabled()
    {
        if (!TryEnableFirewallWithCom())
        {
            RunCommand("netsh", "advfirewall set allprofiles state on");
        }
    }

    private void ApplyTemporaryProcessFirewallBlock(Process process, TimeSpan duration)
    {
        try
        {
            string? path = null;
            try { path = process.MainModule?.FileName; } catch { }
            if (string.IsNullOrWhiteSpace(path)) return;

            var ruleName = $"ClassScreenLock_TempBlock_{process.Id}";
            DeleteFirewallRuleByName(ruleName);
            AddProgramBlockRule(ruleName, FirewallGroup, path);

            LogService.Observe(Task.Run(async () =>
            {
                await Task.Delay(duration);
                DeleteFirewallRuleByName(ruleName);
            }), "NetworkBlocking.RemoveTempRule");
        }
        catch { }
    }

    private const int NET_FW_RULE_DIR_IN = 1;
    private const int NET_FW_RULE_DIR_OUT = 2;
    private const int NET_FW_ACTION_BLOCK = 0;
    private const int NET_FW_PROFILE2_ALL = unchecked((int)0x7FFFFFFF);

    private static object? CreateComObject(string progId)
    {
        try
        {
            var type = Type.GetTypeFromProgID(progId, throwOnError: false);
            if (type == null) return null;
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private bool TryEnableFirewallWithCom()
    {
        try
        {
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 == null) return false;

            foreach (var profile in new[] { 1, 2, 4 })
            {
                try { policy2.FirewallEnabled[profile] = true; } catch { }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DeleteFirewallRulesByGroup(string group)
    {
        if (DeleteFirewallRulesByGroupCom(group)) return;
        RunCommand("netsh", $"advfirewall firewall delete rule group=\"{group}\"");
    }

    private bool DeleteFirewallRulesByGroupCom(string group)
    {
        try
        {
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 == null) return false;
            dynamic rules = policy2.Rules;

            var namesToRemove = new List<string>();
            foreach (dynamic rule in (IEnumerable)rules)
            {
                try
                {
                    string? grouping = rule.Grouping;
                    if (!string.IsNullOrWhiteSpace(grouping) && string.Equals(grouping, group, StringComparison.OrdinalIgnoreCase))
                    {
                        string? name = rule.Name;
                        if (!string.IsNullOrWhiteSpace(name)) namesToRemove.Add(name);
                    }
                }
                catch { }
            }

            foreach (var name in namesToRemove)
            {
                try { rules.Remove(name); } catch { }
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void DeleteFirewallRuleByName(string name)
    {
        if (DeleteFirewallRuleByNameCom(name)) return;
        RunCommand("netsh", $"advfirewall firewall delete rule name=\"{name}\"");
    }

    private bool DeleteFirewallRuleByNameCom(string name)
    {
        try
        {
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 == null) return false;
            dynamic rules = policy2.Rules;
            try
            {
                rules.Remove(name);
                return true;
            }
            catch
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private void AddRemoteIpBlockRule(string name, string group, int direction, string remoteAddresses)
    {
        // 1. 优先使用 COM API，因为它更快速且支持完整的属性设置（如 Grouping）
        if (TryAddRemoteIpBlockRuleCom(name, group, direction, remoteAddresses))
        {
            return;
        }

        // 2. 如果 COM 失败，回退到 netsh
        // 注意：netsh add rule 指令并不支持 group 参数，group 参数仅用于 show/set/delete
        var dir = direction == NET_FW_RULE_DIR_IN ? "in" : "out";
        RunCommand("netsh", $"advfirewall firewall add rule name=\"{name}\" dir={dir} action=block remoteip={remoteAddresses} enable=yes profile=any");
    }

    private bool TryAddRemoteIpBlockRuleCom(string name, string group, int direction, string remoteAddresses)
    {
        try
        {
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 == null) return false;

            dynamic? rule = CreateComObject("HNetCfg.FWRule");
            if (rule == null) return false;

            rule.Name = name;
            rule.Grouping = group;
            rule.Description = "ClassScreenLock 自动生成的网络拦截规则，用于阻止受限域名的访问。";
            rule.Enabled = true;
            rule.Action = NET_FW_ACTION_BLOCK;
            rule.Direction = direction;
            rule.InterfaceTypes = "All";
            rule.Profiles = NET_FW_PROFILE2_ALL;
            rule.RemoteAddresses = remoteAddresses;

            policy2.Rules.Add(rule);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FirewallComAddFailed", "Firewall", $"Name: {name}, Error: {ex.Message}");
            return false;
        }
    }

    private void AddProgramBlockRule(string name, string group, string programPath)
    {
        // 1. 优先使用 COM API
        if (TryAddProgramBlockRuleCom(name, group, programPath))
        {
            return;
        }

        // 2. 回退到 netsh
        var dir = "out";
        RunCommand("netsh", $"advfirewall firewall add rule name=\"{name}\" dir={dir} action=block program=\"{programPath}\" enable=yes profile=any");
    }

    private bool TryAddProgramBlockRuleCom(string name, string group, string programPath)
    {
        try
        {
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 == null) return false;

            dynamic? rule = CreateComObject("HNetCfg.FWRule");
            if (rule == null) return false;

            rule.Name = name;
            rule.Grouping = group;
            rule.Description = "ClassScreenLock 自动生成的程序拦截规则。";
            rule.Enabled = true;
            rule.Action = NET_FW_ACTION_BLOCK;
            rule.Direction = NET_FW_RULE_DIR_OUT;
            rule.InterfaceTypes = "All";
            rule.ApplicationName = programPath;
            rule.Profiles = NET_FW_PROFILE2_ALL;

            policy2.Rules.Add(rule);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FirewallComAddProgramFailed", "Firewall", $"Name: {name}, Error: {ex.Message}");
            return false;
        }
    }

    private static async Task<IPAddress[]?> ResolveDomainWithTimeoutAsync(string domain, TimeSpan timeout)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            var task = Dns.GetHostAddressesAsync(domain, cts.Token);
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout, cts.Token));
            
            if (completedTask == task)
            {
                return await task;
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLoopbackOrUnspecified(IPAddress address)
    {
        try
        {
            if (IPAddress.IsLoopback(address)) return true;
            if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                if (bytes.Length == 4 && bytes[0] == 169 && bytes[1] == 254) return true;
                return false;
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal) return true;
                if (address.IsIPv6SiteLocal) return true;
                if (address.IsIPv6Multicast) return true;
                if (address.IsIPv6Teredo) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static string NormalizeAddressString(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            try { return address.MapToIPv4().ToString(); } catch { }
        }
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            try { return new IPAddress(address.GetAddressBytes()).ToString(); } catch { }
        }
        return address.ToString();
    }


    private void RunCommand(string fileName, string arguments, int timeoutMs = 5000)
    {
        try
        {
            bool isAdmin = IsAdministrator();
            
            if (!isAdmin)
            {
                Debug.WriteLine($"[RunCommand] Skip {fileName} {arguments} because not administrator.");
                return;
            }

            // 尝试获取全路径
            string fullPath = fileName;
            if (fileName == "netsh") fullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
            if (fileName == "ipconfig") fullPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ipconfig.exe");

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = fullPath,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                string output = string.Empty;
                string error = string.Empty;
                if (timeoutMs > 0)
                {
                    if (!process.WaitForExit(timeoutMs))
                    {
                        Debug.WriteLine($"[RunCommand] Timeout: {fileName} {arguments}");
                        try { process.Kill(); } catch { }
                    }
                }
                else
                {
                    process.WaitForExit();
                }

                try { output = process.StandardOutput.ReadToEnd(); } catch { }
                try { error = process.StandardError.ReadToEnd(); } catch { }

                if (process.ExitCode != 0)
                {
                    bool isIgnorableError = process.ExitCode == 1 && arguments.Contains("delete rule", StringComparison.OrdinalIgnoreCase);
                    
                    var combined = string.Join("\n", new[] { output, error }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                    if (!isIgnorableError)
                    {
                        Debug.WriteLine($"[RunCommand] Error Code {process.ExitCode}: {fileName} {arguments}");
                        if (!string.IsNullOrWhiteSpace(combined)) Debug.WriteLine($"[RunCommand] Error Detail: {combined}");
                        LogService.Instance.Log("Error", "FirewallCommandFailed", fileName, $"Args: {arguments}, ExitCode: {process.ExitCode}, Detail: {combined}");
                    }
                    else
                    {
                        Debug.WriteLine($"[RunCommand] Ignored Error Code 1 for delete: {fileName} {arguments}");
                    }
                }
                else
                {
                    Debug.WriteLine($"[RunCommand] Success: {fileName} {arguments}");
                    var combined = string.Join("\n", new[] { output, error }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
                    if (arguments.Contains("add rule"))
                    {
                        LogService.Instance.Log("Info", "FirewallRuleAdded", "Firewall", $"Rule added: {arguments}. Output: {combined}");
                    }

                    if (arguments.Contains("show rule") && !string.IsNullOrWhiteSpace(output))
                    {
                        string summary = output.Length > 800 ? output.Substring(0, 800) : output;
                        LogService.Instance.Log("Info", "FirewallShowRule", "Firewall", summary.Trim());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RunCommand] Exception: {ex.Message}");
            LogService.Instance.Log("Error", "FirewallCommandException", fileName, ex.Message);
        }
    }

    private string GetProcessCommandLine(uint pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementObject mo in searcher.Get())
            {
                var v = mo["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return string.Empty;
    }

    private string GetBrowserUrlFromAccessibility(IntPtr hWnd, string processName)
    {
        try
        {
            var urlFromUIA = GetUrlViaUIAutomation(hWnd);
            if (!string.IsNullOrEmpty(urlFromUIA))
            {
                return urlFromUIA;
            }
        }
        catch { }
        return string.Empty;
    }

    private string GetUrlViaUIAutomation(IntPtr hWnd)
    {
        try
        {
            var element = AutomationElement.FromHandle(hWnd);
            if (element == null) return string.Empty;

            var condition = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                new PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, true)
            );

            var edits = element.FindAll(TreeScope.Descendants, condition);
            foreach (AutomationElement edit in edits)
            {
                try
                {
                    var valuePattern = edit.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    if (valuePattern != null)
                    {
                        var value = valuePattern.Current.Value;
                        if (!string.IsNullOrEmpty(value) && IsUrl(value))
                        {
                            return value;
                        }
                    }
                }
                catch { }
            }

            var documentCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
            var documents = element.FindAll(TreeScope.Descendants, documentCondition);
            foreach (AutomationElement doc in documents)
            {
                try
                {
                    var valuePattern = doc.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    if (valuePattern != null)
                    {
                        var value = valuePattern.Current.Value;
                        if (!string.IsNullOrEmpty(value) && IsUrl(value))
                        {
                            return value;
                        }
                    }
                }
                catch { }
            }

            var nameCondition = new PropertyCondition(AutomationElement.AutomationIdProperty, "addressEditBox", PropertyConditionFlags.IgnoreCase);
            var addressBox = element.FindFirst(TreeScope.Descendants, nameCondition);
            if (addressBox != null)
            {
                try
                {
                    var valuePattern = addressBox.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    if (valuePattern != null)
                    {
                        var value = valuePattern.Current.Value;
                        if (!string.IsNullOrEmpty(value))
                        {
                            return value;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return string.Empty;
    }

    private string GetUrlViaAccessibility(IntPtr hWnd, string processName)
    {
        object? accObj = null;
        var iid = IID_IAccessible;
        int result = AccessibleObjectFromWindow(hWnd, OBJID_WINDOW, ref iid, out accObj);
        if (result != 0 || accObj == null) return string.Empty;

        return ExtractUrlFromAccessible(accObj, processName, 0);
    }

    private string GetUrlViaFindWindowEx(IntPtr hWnd, string processName)
    {
        IntPtr child = IntPtr.Zero;
        while (true)
        {
            child = FindWindowEx(hWnd, child, null, null);
            if (child == IntPtr.Zero) break;

            var className = new StringBuilder(256);
            GetClassName(child, className, className.Capacity);
            var cn = className.ToString();

            if (cn.Contains("Edit") || cn.Contains("Address") || cn.Contains("Toolbar") || cn.Contains("ReBar"))
            {
                var textLen = (int)SendMessage(child, WM_GETTEXTLENGTH, IntPtr.Zero, null!);
                if (textLen > 0)
                {
                    var text = new StringBuilder(textLen + 1);
                    SendMessage(child, WM_GETTEXT, (IntPtr)(textLen + 1), text);
                    var content = text.ToString();
                    if (IsUrl(content))
                    {
                        return content;
                    }
                }
            }

            var subResult = GetUrlViaFindWindowEx(child, processName);
            if (!string.IsNullOrEmpty(subResult)) return subResult;
        }

        return string.Empty;
    }

    private string ExtractUrlFromAccessible(object accObj, string processName, int depth)
    {
        if (depth > 10) return string.Empty;
        
        try
        {
            var accType = accObj.GetType();
            var accValueProp = accType.GetProperty("accValue");
            var accNameProp = accType.GetProperty("accName");
            var accRoleProp = accType.GetProperty("accRole");
            var accChildCountProp = accType.GetProperty("accChildCount");
            var accStateProp = accType.GetProperty("accState");

            if (accChildCountProp == null) return string.Empty;

            string? currentValue = accValueProp?.GetValue(accObj)?.ToString();
            string? currentName = accNameProp?.GetValue(accObj)?.ToString();
            
            if (!string.IsNullOrEmpty(currentValue) && IsUrl(currentValue))
            {
                return currentValue;
            }
            if (!string.IsNullOrEmpty(currentName) && IsUrl(currentName))
            {
                return currentName;
            }

            int childCount = (int)accChildCountProp.GetValue(accObj)!;
            if (childCount <= 0) return string.Empty;

            var children = new object[childCount];
            int obtained;
            AccessibleChildren(accObj, 0, childCount, children, out obtained);

            foreach (var child in children.Take(obtained))
            {
                if (child == null) continue;

                var childType = child.GetType();
                var childRoleProp = childType.GetProperty("accRole");
                var childNameProp = childType.GetProperty("accName");
                var childValueProp = childType.GetProperty("accValue");

                string? name = childNameProp?.GetValue(child)?.ToString();
                string? value = childValueProp?.GetValue(child)?.ToString();

                if (!string.IsNullOrEmpty(value) && IsUrl(value))
                {
                    return value;
                }
                if (!string.IsNullOrEmpty(name) && IsUrl(name))
                {
                    return name;
                }

                var subResult = ExtractUrlFromAccessible(child, processName, depth + 1);
                if (!string.IsNullOrEmpty(subResult)) return subResult;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[URL] ExtractUrlFromAccessible error at depth {depth}: {ex.Message}");
        }

        return string.Empty;
    }

    private static bool IsUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSniForgery(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        var s = commandLine.ToLowerInvariant();
        int score = 0;
        foreach (var k in _sniIndicators)
        {
            if (s.Contains(k)) score++;
        }
        if (s.Contains("host-resolver-rules")) score++;
        return score >= 3;
    }

    private List<string> ExtractFrontingHosts(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine)) return result;
        var s = commandLine;
        var m = Regex.Match(s, @"--?host-resolver-rules=""?(.*?)""?(?:\s|$)", RegexOptions.IgnoreCase);
        if (!m.Success) return result;
        var rules = m.Groups[1].Value;
        foreach (var part in rules.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var mp = Regex.Match(part, "(?i)map\\s+([a-z0-9_.-]+)(?::\\d+)?\\s+([a-z0-9_.-]+)");
            if (mp.Success)
            {
                var front = mp.Groups[2].Value.Trim();
                if (!string.IsNullOrWhiteSpace(front)) result.Add(front);
            }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private readonly Dictionary<string, DateTime> _frontBlockAppliedAt = new(StringComparer.OrdinalIgnoreCase);

    private async Task BlockFrontingHostsAsync(List<string> fronts)
    {
        try
        {
            var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tasks = new List<Task<IPAddress[]>>();
            foreach (var f in fronts)
            {
                if (_frontBlockAppliedAt.TryGetValue(f, out var at) && (DateTime.UtcNow - at) < TimeSpan.FromSeconds(5)) continue;
                tasks.Add(Dns.GetHostAddressesAsync(f));
                if (!f.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) tasks.Add(Dns.GetHostAddressesAsync("www." + f));
            }
            var results = await Task.WhenAll(tasks.Select(async t => { try { return await t; } catch { return Array.Empty<IPAddress>(); } }));
            foreach (var arr in results)
            {
                foreach (var a in arr)
                {
                    if (a == null) continue;
                    if (IsLoopbackOrUnspecified(a)) continue;
                    ips.Add(NormalizeAddressString(a));
                }
            }
            if (!ips.Any()) return;
            DeleteFirewallRuleByName("ClassScreenLock_FrontBlock_Out");
            DeleteFirewallRuleByName("ClassScreenLock_FrontBlock_In");
            for (int i = 2; i <= 50; i++)
            {
                DeleteFirewallRuleByName($"ClassScreenLock_FrontBlock_Out_{i}");
                DeleteFirewallRuleByName($"ClassScreenLock_FrontBlock_In_{i}");
            }
            var chunked = ips.Chunk(100).ToList();
            int idx = 1;
            foreach (var c in chunked)
            {
                var ipstr = string.Join(",", c);
                var suffix = chunked.Count == 1 ? "" : (idx == 1 ? "" : $"_{idx}");
                AddRemoteIpBlockRule($"ClassScreenLock_FrontBlock_Out{suffix}", FirewallGroup, NET_FW_RULE_DIR_OUT, ipstr);
                AddRemoteIpBlockRule($"ClassScreenLock_FrontBlock_In{suffix}", FirewallGroup, NET_FW_RULE_DIR_IN, ipstr);
                idx++;
            }
            foreach (var f in fronts) _frontBlockAppliedAt[f] = DateTime.UtcNow;
            LogService.Instance.Log("Info", "FrontingFirewallRulesApplied", "Firewall", $"Applied {ips.Count} IPs for fronting hosts.");
        }
        catch { }
    }
}

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

namespace ClassScreenLock.Services;

public class NetworkBlockingService
{
    // Win32 API for "Soft" interception
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

    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_W = 0x57;
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
        "360chrome", "360se", "sogouexplorer", "qqbrowser", "ucbrowser", "liebao", "2345explorer", "maxthon"
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
            int intervalMs = (lockService.IsLocked || lockService.IsProtectionOnlyActive || (settings?.IsNetworkLockEnabled ?? false) || (settings?.IsBasicProtectionEnabled ?? false)) ? 100 : 2000;
            int sleepTime = intervalMs - elapsed;
            if (sleepTime > 0)
            {
                try { Task.Delay(sleepTime, token).Wait(token); } catch { }
            }
        }
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
        
        // 同样使用 ApplyRulesLock 确保不与手动应用冲突
        if (!await _applyRulesLock.WaitAsync(0))
        {
            Interlocked.Exchange(ref _integrityRepairRunning, 0);
            return;
        }

        try
        {
            var settings = SettingsService.Blockage;
            if (settings == null) return;
            if (!(settings.IsNetworkLockEnabled || lockState)) return;

            var rules = NetworkRuleService.LoadRules();
            if (rules == null) return;
            bool hasActiveDomainRules = rules.Any(r => r.IsEnabled && r.Type == "Domain" && !string.IsNullOrWhiteSpace(r.Domain));
            if (!hasActiveDomainRules) return;

            bool hostsOk = HostsMarkersPresent();
            bool firewallOk = HasFirewallDomainRules();
            
            if (hostsOk && firewallOk) return;

            if (!IsAdministrator())
            {
                if (!_integrityWarningShown)
                {
                    _integrityWarningShown = true;
                    NotificationService.Instance.ShowWarning("检测到网络拦截规则被更改，但当前无管理员权限，无法自动恢复。请以管理员身份运行应用。");
                }
                return;
            }

            // 如果防火墙规则缺失，重新应用所有规则（包含 Hosts）
            if (!firewallOk)
            {
                LogService.Instance.Log("Info", "IntegrityRepair", "Firewall", "Firewall rules missing, re-applying all rules.");
                EnsureFirewallEnabled();
                ClearHostsRules();
                await UpdateDomainFirewallRules(rules);
                UpdateHostsFile(rules);
                return;
            }

            // 如果仅 Hosts 缺失
            if (!hostsOk)
            {
                LogService.Instance.Log("Info", "IntegrityRepair", "Hosts", "Hosts markers missing, re-applying hosts file.");
                UpdateHostsFile(rules);
            }
        }
        finally
        {
            _applyRulesLock.Release();
            Interlocked.Exchange(ref _integrityRepairRunning, 0);
        }
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
            dynamic? policy2 = CreateComObject("HNetCfg.FwPolicy2");
            if (policy2 == null)
            {
                LogService.Instance.Log("Warning", "FirewallCheck", "COM", "Failed to create HNetCfg.FwPolicy2 object.");
                return false;
            }

            dynamic rules = policy2.Rules;
            if (rules == null)
            {
                LogService.Instance.Log("Warning", "FirewallCheck", "COM", "policy2.Rules is null.");
                return false;
            }

            int count = 0;
            try { count = rules.Count; } catch (Exception ex) { 
                LogService.Instance.Log("Warning", "FirewallCheck", "COM", $"Failed to get rules count: {ex.Message}");
            }
            
            if (count == 0)
            {
                LogService.Instance.Log("Info", "FirewallCheck", "COM", "Firewall rules count is 0.");
                return false;
            }

            // 尝试直接获取
            try
            {
                dynamic? rule = null;
                try { rule = rules.Item("ClassScreenLock_DomainBlock_Out"); } catch { }
                
                if (rule != null)
                {
                    bool isEnabled = false;
                    try { isEnabled = rule.Enabled; } catch { }
                    if (isEnabled) return true;
                    else LogService.Instance.Log("Info", "FirewallCheck", "COM", "Found rule ClassScreenLock_DomainBlock_Out but it is disabled.");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Debug", "FirewallCheck", "COM", $"Direct lookup failed: {ex.Message}");
            }

            // 如果直接获取失败，尝试遍历
            int checkedCount = 0;
            foreach (dynamic rule in (IEnumerable)rules)
            {
                checkedCount++;
                try
                {
                    if (rule == null) continue;
                    string? name = rule.Name;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    if (name.StartsWith("ClassScreenLock_DomainBlock_", StringComparison.OrdinalIgnoreCase))
                    {
                        bool enabled = false;
                        try { enabled = rule.Enabled; } catch { }
                        if (enabled) return true;
                    }
                }
                catch { }
                if (checkedCount > 1000) break; // 防止规则太多导致遍历太慢
            }
            
            LogService.Instance.Log("Info", "FirewallCheck", "COM", $"Finished traversing {checkedCount} rules, no active ClassScreenLock_DomainBlock_ rules found.");
            return false;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "FirewallCheckException", "Firewall", ex.Message);
            return false;
        }
    }

    private HashSet<uint> _cachedBrowserPids = new();
    private DateTime _lastPidUpdate = DateTime.MinValue;
    private readonly Dictionary<uint, DateTime> _lastInterceptionAt = new();
    private readonly Dictionary<uint, string> _lastInterceptionTitle = new();

    private void ExecuteDetectionCycle()
    {
        var settings = SettingsService.Blockage;
        if (settings == null) return;

        var lockService = LockScreenService.Instance;
        bool lockState = lockService.IsLocked || lockService.IsProtectionOnlyActive;

        if (!lockState && !settings.IsNetworkLockEnabled && !settings.IsBasicProtectionEnabled) return;

        // 获取当前活动窗口
        IntPtr foregroundHwnd = GetForegroundWindow();
        if (foregroundHwnd == IntPtr.Zero) return;

        _cycleCount++;
        bool isDeepScanCycle = _cycleCount % 30 == 0; // 每 30 次循环（约 6 秒）执行一次全局深度扫描

        // 从独立的 Networkblockage.json 获取规则
        var rules = NetworkRuleService.LoadRules();
        if (rules == null) return;

        // 仅获取需要本应用拦截的规则
        var activeRules = rules
            .Where(r => r.IsEnabled)
            .ToList();
        
        if (!activeRules.Any()) return;

        try
        {
            GetWindowThreadProcessId(foregroundHwnd, out uint fgPid);
            using var fgProcess = Process.GetProcessById((int)fgPid);
            var name = fgProcess.ProcessName;
            if (_browserProcesses.Any(b => string.Equals(b, name, StringComparison.OrdinalIgnoreCase)))
            {
                var sb = new StringBuilder(1024);
                GetWindowText(foregroundHwnd, sb, sb.Capacity);
                string title = sb.ToString();
                if (!string.IsNullOrEmpty(title))
                {
                    var analysis = ContentAnalysisEngine.Instance.Analyze(title, activeRules);
                    if (analysis.IsViolation)
                    {
                        ExecuteInterception((uint)fgProcess.Id, analysis, foregroundHwnd);
                        return;
                    }
                }

                var cmd = GetProcessCommandLine(fgPid);
                var frontHosts = ExtractFrontingHosts(cmd);
                if ((settings?.IsNetworkLockEnabled ?? false) && frontHosts.Count > 0)
                {
                    LogService.Instance.Log("Debug", "SniForgeryDetected", name, string.Join(",", frontHosts));
                    LogService.Observe(BlockFrontingHostsAsync(frontHosts), "NetworkBlocking.BlockFrontHosts");
                    NotificationService.Instance.ShowWarning("检测到SNI伪造，已临时屏蔽前置域网络访问。");
                    return;
                }
            }
        }
        catch { }

        // 每秒更新一次浏览器进程 PID 列表
        if (isDeepScanCycle || _cachedBrowserPids.Count == 0 || (DateTime.Now - _lastPidUpdate).TotalSeconds > 5)
        {
            UpdateBrowserPids();
        }

        if (!_cachedBrowserPids.Any()) return;

        var interceptedPidsThisCycle = new HashSet<uint>();
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint processId);
            if (interceptedPidsThisCycle.Contains(processId)) return true;
            if (_cachedBrowserPids.Contains(processId))
            {
                // 1. 快速扫描：获取当前窗口标题
                var sb = new StringBuilder(1024);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                if (!string.IsNullOrEmpty(title))
                {
                    var analysis = ContentAnalysisEngine.Instance.Analyze(title, activeRules);
                    if (analysis.IsViolation)
                    {
                        ExecuteInterception(processId, analysis, hWnd);
                        interceptedPidsThisCycle.Add(processId);
                        return true;
                    }
                }

                var cmd = GetProcessCommandLine(processId);
                var frontHosts = ExtractFrontingHosts(cmd);
                if ((settings?.IsNetworkLockEnabled ?? false) && frontHosts.Count > 0)
                {
                    LogService.Instance.Log("Debug", "SniForgeryDetected", processId.ToString(), string.Join(",", frontHosts));
                    LogService.Observe(BlockFrontingHostsAsync(frontHosts), "NetworkBlocking.BlockFrontHosts.WindowEnum");
                    NotificationService.Instance.ShowWarning("检测到SNI伪造，已临时屏蔽前置域网络访问。");
                    interceptedPidsThisCycle.Add(processId);
                    return true;
                }
            }
            return true;
        }, IntPtr.Zero);
    }

    private void UpdateBrowserPids()
    {
        var browserPids = new HashSet<uint>();
        foreach (var name in _browserProcesses)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    browserPids.Add((uint)p.Id);
                }
            }
            catch { }
        }
        _cachedBrowserPids = browserPids;
        _lastPidUpdate = DateTime.Now;
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
            if (hWnd != IntPtr.Zero)
            {
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
                
                // 发送 Ctrl+W 关闭当前违规标签页
                keybd_event(VK_CONTROL, 0, 0, 0);
                keybd_event(VK_W, 0, 0, 0);
                keybd_event(VK_W, 0, KEYEVENTF_KEYUP, 0);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, 0);

                
                
                // 立即切断该进程的所有活动 TCP 连接（防火墙式行为）
                KillTcpConnectionsForProcess(p.Id);
                
                Debug.WriteLine($"[FIREWALL-BLOCK] Intercepted violation: {analysis.MatchedPattern} in {p.ProcessName} (PID: {p.Id}). Tab closed and connections reset.");
            }
            else
            {
                // 如果没有窗口句柄，直接切断网络连接
                KillTcpConnectionsForProcess(p.Id);
                Debug.WriteLine($"[FIREWALL-BLOCK] Found violation in {p.ProcessName} (No window). Connections reset.");
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
            await Task.Run(async () =>
            {
                try
                {
                    if ((settings.IsNetworkLockEnabled || lockState) && !IsAdministrator())
                    {
                        TryRestartAsAdministrator();
                        return;
                    }

                    // 如果既未开启总开关，且当前不在锁态/仅防护，则清空规则
                    if (!(settings.IsNetworkLockEnabled || lockState))
                    {
                        ClearHostsRules();
                        DeleteFirewallRulesByGroup(FirewallGroup);
                        DeleteFirewallRuleByName("ClassScreenLock_DomainBlock");
                        DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_Out");
                        DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_In");
                        DeleteFirewallRuleByName("BlockAllOutbound");
                        return;
                    }

                    // 确保旧的全拦截规则被删除，避免误杀所有网络
                    DeleteFirewallRuleByName("BlockAllOutbound");
                    EnsureFirewallEnabled();
                    ClearHostsRules();
                    await UpdateDomainFirewallRules(rules);
                    UpdateHostsFile(rules);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"NetworkBlockingService Error: {ex.Message}");
                }
            });
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

            // 清理 Hosts 文件
            ClearHostsRules();

            // 删除防火墙规则
            if (IsAdministrator())
            {
                DeleteFirewallRulesByGroup(FirewallGroup);
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock");
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_Out");
                DeleteFirewallRuleByName("ClassScreenLock_DomainBlock_In");
                DeleteFirewallRuleByName("BlockAllOutbound");
            }
            
            Debug.WriteLine("[CLEANUP] Network blocking rules removed on exit.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Cleanup Error: {ex.Message}");
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
            var resolutionTasks = new List<Task<IPAddress[]>>();

            foreach (var rule in activeRules)
            {
                string domain = rule.Domain.Trim().ToLower();
                if (string.IsNullOrWhiteSpace(domain)) continue;

                resolutionTasks.Add(Dns.GetHostAddressesAsync(domain));
                if (!domain.StartsWith("www."))
                {
                    resolutionTasks.Add(Dns.GetHostAddressesAsync("www." + domain));
                }
            }

            foreach (var host in _dohHosts)
            {
                resolutionTasks.Add(Dns.GetHostAddressesAsync(host));
            }

            var results = await Task.WhenAll(resolutionTasks.Select(async t =>
            {
                try { return await t; } catch { return Array.Empty<IPAddress>(); }
            }));

            foreach (var addresses in results)
            {
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
                    string suffix;
                    if (chunks.Count == 1)
                    {
                        suffix = "";
                    }
                    else
                    {
                        suffix = chunkIndex == 1 ? "" : $"_{chunkIndex}";
                    }

                    AddRemoteIpBlockRule($"ClassScreenLock_DomainBlock_Out{suffix}", FirewallGroup, NET_FW_RULE_DIR_OUT, chunkIps);
                    AddRemoteIpBlockRule($"ClassScreenLock_DomainBlock_In{suffix}", FirewallGroup, NET_FW_RULE_DIR_IN, chunkIps);

                    chunkIndex++;
                }

                Debug.WriteLine($"[FIREWALL] Applied {ipList.Count} IPs to firewall block rules in {chunks.Count} chunks.");
                LogService.Instance.Log("Info", "FirewallRulesApplied", "Firewall", $"Applied {ipList.Count} IPs in {chunks.Count} chunks.");

                // 立即验证规则是否真正出现在系统中
                LogService.Observe(Task.Run(() => {
                    Thread.Sleep(1000); // 等待系统同步
                    if (!HasFirewallDomainRulesCom())
                    {
                        LogService.Instance.Log("Warning", "FirewallRuleValidationFailed", "Firewall", "Rules were applied but not found by COM API.");
                    }
                }), "NetworkBlocking.ValidateFirewallRules");
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

            // 带有重试机制的 Hosts 文件写入，解决文件占用问题
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

                    // 所有启用的域名规则都写入 Hosts，实现纯网络层阻断
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

                            lines.Add($"127.0.0.1 {domain}");
                            lines.Add($"::1 {domain}");
                            if (!domain.StartsWith("www."))
                            {
                                lines.Add($"127.0.0.1 www.{domain}");
                                lines.Add($"::1 www.{domain}");
                            }
                        }
                        lines.Add(MarkerEnd);
                    }

                    File.WriteAllLines(HostsPath, lines);
                    success = true;
                }
                catch (IOException) when (retryCount > 1)
                {
                    retryCount--;
                    Thread.Sleep(500); // 等待 0.5 秒后重试
                }
                catch (Exception)
                {
                    throw; // 其他异常直接抛出
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

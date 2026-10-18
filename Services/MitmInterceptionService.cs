using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

/// <summary>
/// MITM 深度流量检查服务（基于 Titanium.Web.Proxy）。
///
/// 原理：
/// 1. 首次运行时动态生成一个本地根证书（Root CA）并持久化到 Data\mitm\root_ca.pfx，
///    安装到当前用户“受信任的根证书颁发机构”存储中（无需管理员权限）。
/// 2. 启动本地显式代理（127.0.0.1:端口），并把系统 IE/WinINET 代理指向该地址，
///    使浏览器等使用系统代理的应用程序流量经过本代理。
/// 3. 对 HTTPS 流量使用动态签发的服务器证书（按域名即时生成，受信任的根证书签名）解密，
///    在 CONNECT / 请求 / 响应三个阶段检查域名、URL 与页面内容，命中黑名单规则
///    或启发式特征即阻断，并写入拦截日志。
/// 4. 局域网/私有 IP 与集控服务器地址不解密（直接隧道透传），避免破坏集控连接。
/// </summary>
public class MitmInterceptionService : IDisposable
{
    private static readonly MitmInterceptionService _instance = new();
    public static MitmInterceptionService Instance => _instance;

    private const string CaPassword = "ClassScreenLock_Mitm_Ca_2026";
    private const string CaCommonName = "ClassScreenLock Root CA";
    private const string CaIssuerName = "ClassScreenLock MITM";
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const int MaxBodyInspectSize = 512 * 1024;
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DbLogCooldown = TimeSpan.FromSeconds(5);

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    private readonly object _lifecycleLock = new();
    private ProxyServer? _proxyServer;
    private ExplicitProxyEndPoint? _endPoint;
    private bool _isRunning;
    private int _proxyPort;

    private readonly Dictionary<string, object?> _originalProxyValues = new(StringComparer.OrdinalIgnoreCase);
    private bool _originalProxyCaptured;

    /// <summary>
    /// 额外的代理排除地址列表（运行时动态添加，如集控服务器地址）。
    /// SetSystemProxy 会将这些地址一并写入 ProxyOverride。
    /// </summary>
    private readonly HashSet<string> _extraBypassHosts = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTime> _lastNotifyAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastDbLogAt = new(StringComparer.OrdinalIgnoreCase);

    // 证书完整性监测：代理运行期间定时检查根证书是否仍受信任，缺失则自动重新安装
    private Timer? _integrityTimer;
    private static readonly TimeSpan IntegrityCheckInterval = TimeSpan.FromSeconds(60);
    private DateTime _lastCertReinstallAt = DateTime.MinValue;
    private static readonly TimeSpan CertReinstallCooldown = TimeSpan.FromMinutes(5);

    private MitmInterceptionService() { }

    public bool IsRunning => _isRunning;

    public int ProxyPort => _proxyPort;

    /// <summary>
    /// 添加额外的代理排除地址（如集控服务器地址）。
    /// 如果代理正在运行，会立即更新系统代理的 ProxyOverride。
    /// </summary>
    public void AddBypassHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        host = host.Trim().ToLowerInvariant();

        lock (_extraBypassHosts)
        {
            if (_extraBypassHosts.Add(host))
            {
                LogService.Instance.Log("Info", "MitmBypassAdd", "MITM", $"已添加代理排除地址：{host}");
            }
        }

        // 如果代理正在运行，立即更新 ProxyOverride
        if (_isRunning && _proxyPort > 0)
        {
            SetSystemProxy(_proxyPort);
        }
    }

    /// <summary>
    /// 临时停止代理并记录是否需要恢复。
    /// 返回一个 Token，调用 RestoreFromToken(token) 可在原代理处于运行状态时恢复。
    /// </summary>
    public MitmPauseToken PauseIfRunning()
    {
        bool wasRunning = _isRunning;
        int port = _proxyPort;

        if (wasRunning)
        {
            LogService.Instance.Log("Info", "MitmPaused", "MITM", $"加入组织流程需要，临时停止 MITM 代理（端口 {port}）");
            Stop();
        }

        return new MitmPauseToken { WasRunning = wasRunning, Port = port };
    }

    /// <summary>
    /// 根据之前 PauseIfRunning 返回的 Token 恢复代理状态。
    /// 仅在代理之前处于运行状态时才恢复。
    /// </summary>
    public void RestoreFromToken(MitmPauseToken token)
    {
        if (token?.WasRunning != true) return;

        if (!_isRunning)
        {
            LogService.Instance.Log("Info", "MitmRestored", "MITM", $"加入组织流程完成，恢复 MITM 代理（端口 {token.Port}）");
            Start(token.Port);
        }
    }

    /// <summary>
    /// 代理暂停/恢复的上下文标记。
    /// </summary>
    public class MitmPauseToken
    {
        public bool WasRunning { get; init; }
        public int Port { get; init; }
    }

    /// <summary>
    /// 根据当前设置与锁屏状态，决定代理的启停。由 NetworkBlockingService.ApplyRulesAsync 调用。
    /// </summary>
    public void SyncState()
    {
        try
        {
            var settings = SettingsService.Blockage;
            if (settings == null) return;

            var lockService = LockScreenService.Instance;
            bool active = settings.IsNetworkLockEnabled ||
                          lockService.IsLocked ||
                          lockService.IsProtectionOnlyActive;
            bool shouldRun = active && settings.IsMitmInterceptionEnabled;

            if (shouldRun) Start(settings.MitmProxyPort);
            else Stop();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmSyncError", "MITM", ex.Message);
        }
    }

    public void Start(int port)
    {
        lock (_lifecycleLock)
        {
            if (_isRunning && _proxyPort == port) return;
            if (_isRunning) StopInternal();

            try
            {
                LogService.Instance.Log("Info", "MitmStartBegin", "MITM", $"开始启动 MITM 代理，端口 {port}，根证书路径 {GetRootCaPath()}");

                _proxyServer = new ProxyServer(
                    userTrustRootCertificate: false,
                    machineTrustRootCertificate: false,
                    trustRootCertificateAsAdmin: false);

                _proxyServer.CertificateManager.PfxFilePath = GetRootCaPath();
                _proxyServer.CertificateManager.PfxPassword = CaPassword;
                _proxyServer.CertificateManager.RootCertificateName = CaCommonName;
                _proxyServer.CertificateManager.RootCertificateIssuerName = CaIssuerName;
                _proxyServer.CertificateManager.CertificateValidDays = 3650;
                _proxyServer.CertificateManager.DisableWildCardCertificates = false;
                _proxyServer.CertificateManager.SaveFakeCertificates = false;
                _proxyServer.ExceptionFunc = OnProxyException;

                LogService.Instance.Log("Info", "MitmStartStep", "MITM", "ProxyServer 已创建，开始检查根证书");

                var pfxPath = GetRootCaPath();
                bool pfxExists = File.Exists(pfxPath);
                bool certInStore = IsCertificateInStore(pfxPath);

                if (pfxExists && !certInStore)
                {
                    // pfx 文件存在但不在受信任根存储中——直接安装，不重新生成
                    LogService.Instance.Log("Info", "MitmStartStep", "MITM", "pfx 已存在但不在受信任根存储中，直接安装");
                    InstallRootCertToStore();
                    LogService.Instance.Log("Info", "MitmCertInstalled", "MITM", "根证书已从 pfx 安装到受信任根存储");
                }
                else if (!pfxExists)
                {
                    // pfx 文件不存在——需要从头生成
                    var mitmDir = Path.GetDirectoryName(pfxPath);
                    if (!string.IsNullOrEmpty(mitmDir) && !Directory.Exists(mitmDir))
                    {
                        Directory.CreateDirectory(mitmDir);
                    }

                    LogService.Instance.Log("Info", "MitmStartStep", "MITM", "pfx 不存在，开始生成根证书");

                    _proxyServer.CertificateManager.EnsureRootCertificate(
                        userTrustRootCertificate: false,
                        machineTrustRootCertificate: false,
                        trustRootCertificateAsAdmin: false);

                    if (File.Exists(pfxPath))
                    {
                        InstallRootCertToStore();
                        LogService.Instance.Log("Info", "MitmCertInstalled", "MITM", "根证书已生成并安装到受信任根存储");
                    }
                    else
                    {
                        LogService.Instance.Log("Error", "MitmCertGenFailed", "MITM", $"EnsureRootCertificate 未生成 pfx 文件，路径：{pfxPath}");
                        return;
                    }
                }
                else
                {
                    // pfx 存在且在受信任根存储中——直接复用
                    LogService.Instance.Log("Info", "MitmStartStep", "MITM", "根证书已就绪（pfx + 受信任根存储），直接复用");
                }

                LogService.Instance.Log("Info", "MitmStartStep", "MITM", "创建代理端点并注册事件");

                _endPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, port, decryptSsl: true);
                _endPoint.BeforeTunnelConnectRequest += OnTunnelConnectRequest;
                _proxyServer.AddEndPoint(_endPoint);
                _proxyServer.BeforeRequest += OnBeforeRequest;
                _proxyServer.BeforeResponse += OnBeforeResponse;

                // 先启动代理监听，再接管系统代理，避免浏览器在代理尚未就绪时读到死代理
                _proxyServer.Start(changeSystemProxySettings: false);

                _proxyPort = port;
                _isRunning = true;

                SetSystemProxy(port);

                LogService.Instance.Log("Info", "MitmStarted", "MITM", $"深度流量检查代理已启动：127.0.0.1:{port}");
                LogProxyStateToRegistry();

                // 启动证书完整性监测定时器，代理运行期间每 60 秒检查一次根证书是否仍受信任
                _integrityTimer?.Dispose();
                _integrityTimer = new Timer(OnIntegrityCheck, null, IntegrityCheckInterval, IntegrityCheckInterval);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "MitmStartFailed", "MITM", $"端口 {port}，异常：{ex}");
                try { _proxyServer?.Stop(); } catch { }
                try { RestoreSystemProxy(); } catch { }
                _proxyServer = null;
                _endPoint = null;
                _isRunning = false;
                _proxyPort = 0;
            }
        }
    }

    /// <summary>
    /// 停止 MITM 代理。
    /// 仅在用户主动关闭 MITM 功能开关时移除根证书；
    /// 锁屏解除、加入组织临时暂停等自动停止场景保留证书，下次启动直接复用。
    /// </summary>
    public void Stop()
    {
        lock (_lifecycleLock)
        {
            if (!_isRunning) return;
            StopInternal();

            // 判断是否为用户主动关闭：MITM 功能开关已关闭
            bool userDisabled = SettingsService.Blockage is { IsMitmInterceptionEnabled: false };

            if (userDisabled)
            {
                // 用户主动关闭时移除根证书，保持系统干净；下次开启时会重新安装
                RemoveRootCertFromStore();
                LogService.Instance.Log("Info", "MitmStopped", "MITM", "深度流量检查代理已停止，系统代理已还原，根证书已移除");
            }
            else
            {
                // 自动停止（锁屏解除、加入组织临时暂停等）：保留根证书，下次启动直接复用
                LogService.Instance.Log("Info", "MitmStopped", "MITM", "深度流量检查代理已停止，系统代理已还原（自动停止，根证书保留）");
            }
        }
    }

    /// <summary>
    /// 应用退出时调用：停止代理并还原系统代理。
    /// 注意：根证书保留在受信任根存储中（证书文件已持久化到 Data\mitm\root_ca.pfx），
    /// 下次启动开启 MITM 时直接复用，无需重新生成或重新安装。
    /// </summary>
    public void Cleanup()
    {
        lock (_lifecycleLock)
        {
            if (_isRunning) StopInternal();
            LogService.Instance.Log("Info", "MitmCleanup", "MITM", "MITM 资源已清理（代理已停止，系统代理已还原，根证书保留）");
        }
    }

    public void Dispose()
    {
        Cleanup();
    }

    private void StopInternal()
    {
        try
        {
            try { _integrityTimer?.Dispose(); } catch { }
            _integrityTimer = null;

            if (_proxyServer != null)
            {
                try { _proxyServer.Stop(); } catch { }
                try { _proxyServer.Dispose(); } catch { }
            }
            RestoreSystemProxy();
        }
        finally
        {
            _proxyServer = null;
            _endPoint = null;
            _proxyPort = 0;
            _isRunning = false;
        }
    }

    // ---------- Titanium.Web.Proxy 事件处理 ----------

    private void OnProxyException(Exception exception)
    {
        try
        {
            LogService.Instance.Log("Warning", "MitmProxyException", "MITM", exception.Message);
        }
        catch { }
    }

    /// <summary>
    /// 流量经过验证：记录代理实际捕获到的隧道/请求（限流），用于确认代理是否生效。
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTime> _lastTrafficLogAt = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan TrafficLogCooldown = TimeSpan.FromSeconds(30);

    private void LogTrafficSeen(string host, string url)
    {
        try
        {
            var now = DateTime.Now;
            string key = host.ToLowerInvariant();
            if (_lastTrafficLogAt.TryGetValue(key, out var last) && (now - last) < TrafficLogCooldown) return;
            _lastTrafficLogAt[key] = now;
            LogService.Instance.Log("Debug", "MitmTrafficSeen", host, Truncate(url, 200));
        }
        catch { }
    }

    /// <summary>
    /// CONNECT（HTTPS）阶段：检查目标主机，命中规则直接拒绝建立隧道；
    /// 局域网/私有 IP 与集控服务器地址不进行 TLS 解密，直接透传。
    /// </summary>
    private Task OnTunnelConnectRequest(object sender, TunnelConnectSessionEventArgs e)
    {
        try
        {
            string? host = null;
            try { host = e.HttpClient.Request.RequestUri?.Host; } catch { }
            if (string.IsNullOrWhiteSpace(host))
            {
                host = ExtractHostFromConnectUrl(e.HttpClient.Request.Url);
            }
            if (string.IsNullOrWhiteSpace(host)) return Task.CompletedTask;

            LogTrafficSeen(host, $"CONNECT {host}");

            if (ShouldBypassMitm(host))
            {
                e.DecryptSsl = false;
                return Task.CompletedTask;
            }

            if (TryMatchBlocked(host, $"https://{host}/", out var matched))
            {
                e.DenyConnect = true;
                LogInterception(matched, host, "Tunnel", $"https://{host}/");
                NotifyBlocked(matched);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmTunnelError", "MITM", ex.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 请求阶段（HTTP，或 HTTPS 解密后）：检查域名与 URL，命中即返回 403 阻断页。
    /// </summary>
    private Task OnBeforeRequest(object sender, SessionEventArgs e)
    {
        try
        {
            var req = e.HttpClient.Request;
            if (req == null) return Task.CompletedTask;

            string host = req.RequestUri?.Host ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host)) host = req.Host ?? string.Empty;
            if (string.IsNullOrWhiteSpace(host)) return Task.CompletedTask;

            string url = req.Url ?? string.Empty;
            LogTrafficSeen(host, url);

            if (ShouldBypassMitm(host)) return Task.CompletedTask;

            if (TryMatchBlocked(host, url, out var matched))
            {
                e.GenericResponse(BuildBlockPage(matched), HttpStatusCode.Forbidden, (IDictionary<string, HttpHeader>)null!, true);
                LogInterception(matched, host, "Request", url);
                NotifyBlocked(matched);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmRequestError", "MITM", ex.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 响应阶段：对解密后的 text/html 响应体进行内容检查（体积受限），
    /// 命中启发式特征则将页面替换为阻断页。
    /// </summary>
    private async Task OnBeforeResponse(object sender, SessionEventArgs e)
    {
        try
        {
            var resp = e.HttpClient.Response;
            if (resp == null || !resp.HasBody) return;

            string contentType = resp.ContentType ?? string.Empty;
            if (!contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
                !contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 大体积响应不做全文扫描，避免性能问题
            if (resp.ContentLength > MaxBodyInspectSize) return;

            string host = e.HttpClient.Request?.RequestUri?.Host ?? string.Empty;
            string url = e.HttpClient.Request?.Url ?? string.Empty;
            if (ShouldBypassMitm(host)) return;

            string body = await e.GetResponseBodyAsString(System.Threading.CancellationToken.None);
            if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyInspectSize) return;

            if (TryMatchBlockedContent(host, url, body, out var matched))
            {
                // 替换响应体前移除压缩编码头，避免浏览器按原编码解压导致乱码
                try { e.HttpClient.Response.Headers.RemoveHeader("Content-Encoding"); } catch { }
                e.SetResponseBodyString(BuildBlockPage(matched));
                try { e.HttpClient.Response.StatusCode = 403; } catch { }
                try { e.HttpClient.Response.StatusDescription = "Forbidden"; } catch { }
                LogInterception(matched, host, "Content", url);
                NotifyBlocked(matched);
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmResponseError", "MITM", ex.Message);
        }
    }

    // ---------- 规则检查 ----------

    private static bool TryMatchBlocked(string host, string url, out string matched)
    {
        matched = string.Empty;
        try
        {
            var rules = NetworkRuleService.LoadRules();
            if (rules == null || rules.Count == 0) return false;

            var result = ContentAnalysisEngine.Instance.Analyze(host + " " + url, rules);
            if (result.IsViolation)
            {
                matched = string.IsNullOrWhiteSpace(result.MatchedPattern) ? host : result.MatchedPattern;
                return true;
            }
        }
        catch { }
        return false;
    }

    private static bool TryMatchBlockedContent(string host, string url, string body, out string matched)
    {
        matched = string.Empty;
        try
        {
            var rules = NetworkRuleService.LoadRules();
            if (rules == null) return false;

            var result = ContentAnalysisEngine.Instance.Analyze(host + " " + url + " " + body, rules);
            if (result.IsViolation)
            {
                matched = string.IsNullOrWhiteSpace(result.MatchedPattern) ? host : result.MatchedPattern;
                return true;
            }
        }
        catch { }
        return false;
    }

    // ---------- 辅助 ----------

    private bool ShouldBypassMitm(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        host = host.Trim().TrimEnd('.').ToLowerInvariant();

        // IP 直连（局域网或公网 IP）不进行解密，避免生成 IP 证书导致浏览器报错
        if (IPAddress.TryParse(host, out _)) return true;

        // 集控服务器地址直接透传，避免破坏与集控端的加密连接
        var orgHost = GetOrganizationServerHost();
        if (!string.IsNullOrEmpty(orgHost) &&
            (string.Equals(host, orgHost, StringComparison.OrdinalIgnoreCase) ||
             host.EndsWith("." + orgHost, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // 额外排除列表（加入组织时动态添加的集控服务器地址）
        lock (_extraBypassHosts)
        {
            if (_extraBypassHosts.Contains(host))
                return true;
        }

        return false;
    }

    private static string? GetOrganizationServerHost()
    {
        try
        {
            var org = OrganizationService.Instance.CurrentOrganization;
            if (org != null && Uri.TryCreate(org.ServerUrl, UriKind.Absolute, out var uri))
            {
                return uri.Host.ToLowerInvariant();
            }
        }
        catch { }
        return null;
    }

    private static string? ExtractHostFromConnectUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim();
        var colon = s.IndexOf(':');
        if (colon > 0) s = s.Substring(0, colon);
        return s.Trim().ToLowerInvariant();
    }

    private void LogInterception(string matched, string host, string stage, string url)
    {
        try
        {
            var now = DateTime.Now;
            string key = host.ToLowerInvariant();
            if (_lastDbLogAt.TryGetValue(key, out var last) && (now - last) < DbLogCooldown) return;
            _lastDbLogAt[key] = now;

            InterceptionDatabase.Instance.Add(new InterceptedContent
            {
                Timestamp = now,
                Domain = host,
                Title = matched,
                ProcessName = stage,
                Reason = $"MITM {stage} Block",
                Confidence = 1.0f
            });
            LogService.Instance.Log("Network", $"Mitm{stage}Blocked", host,
                $"URL: {Truncate(url, 300)}, Matched: {matched}");
        }
        catch { }
    }

    private void NotifyBlocked(string domain)
    {
        try
        {
            var now = DateTime.Now;
            string key = domain.ToLowerInvariant();
            if (_lastNotifyAt.TryGetValue(key, out var last) && (now - last) < NotifyCooldown) return;
            _lastNotifyAt[key] = now;
            NotificationService.Instance.ShowWarning($"已拦截违禁网络访问：{domain}");
        }
        catch { }
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length <= maxLength ? text : text.Substring(0, maxLength);
    }

    private static string BuildBlockPage(string domain)
    {
        string title = "访问已被阻止";
        string body = $"您访问的网站（{domain}）已被课堂锁屏安全策略拦截。";
        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>" + title +
               "</title></head><body style=\"font-family:'Microsoft YaHei',sans-serif;background:#f4f4f4;" +
               "display:flex;align-items:center;justify-content:center;height:100vh;margin:0\">" +
               "<div style=\"background:#fff;padding:48px;border-radius:12px;box-shadow:0 8px 24px rgba(0,0,0,.12);" +
               "text-align:center;max-width:480px\">" +
               "<h1 style=\"color:#c0392b;margin:0 0 16px\">" + title + "</h1>" +
               "<p style=\"color:#555;font-size:16px;line-height:1.8\">" + body + "</p>" +
               "<p style=\"color:#999;font-size:13px;margin-top:24px\">ClassScreenLock 安全防护</p>" +
               "</div></body></html>";
    }

    // ---------- 系统代理（HKCU Internet Settings + WinINet 刷新） ----------

    private void SetSystemProxy(int port)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey);
            if (key == null) return;

            if (!_originalProxyCaptured)
            {
                CaptureOriginalProxyValues();
            }

            // 代理排除列表：本地地址 + 局域网 IP 段 + 集控服务器地址 + 额外排除地址
            // 加入局域网段确保 MITM 运行时不影响局域网内的集控通信和其他内网服务
            string overrideValue = "<local>;localhost;127.0.0.1;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*";
            var orgHost = GetOrganizationServerHost();
            if (!string.IsNullOrEmpty(orgHost)) overrideValue += ";" + orgHost;

            // 额外排除地址（如加入组织时动态添加的集控服务器地址）
            lock (_extraBypassHosts)
            {
                foreach (var h in _extraBypassHosts)
                {
                    if (!overrideValue.Contains(h, StringComparison.OrdinalIgnoreCase))
                    {
                        overrideValue += ";" + h;
                    }
                }
            }

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", $"127.0.0.1:{port}", RegistryValueKind.String);
            key.SetValue("ProxyOverride", overrideValue, RegistryValueKind.String);
            RefreshWinInet();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmSetProxyFailed", "MITM", ex.Message);
        }
    }

    private void CaptureOriginalProxyValues()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey);
            foreach (var name in new[] { "ProxyEnable", "ProxyServer", "ProxyOverride" })
            {
                _originalProxyValues[name] = key?.GetValue(name);
            }
            _originalProxyCaptured = true;
        }
        catch { }
    }

    /// <summary>
    /// 记录当前系统代理注册表状态，便于确认代理接管是否真正写入成功。
    /// </summary>
    private static void LogProxyStateToRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey);
            LogService.Instance.Log("Info", "MitmProxyVerification", "MITM",
                $"ProxyEnable={key?.GetValue("ProxyEnable")}, ProxyServer={key?.GetValue("ProxyServer")}, ProxyOverride={key?.GetValue("ProxyOverride")}");
        }
        catch { }
    }

    private void RestoreSystemProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(InternetSettingsKey);
            if (key == null) return;

            if (_originalProxyCaptured)
            {
                foreach (var name in new[] { "ProxyEnable", "ProxyServer", "ProxyOverride" })
                {
                    if (_originalProxyValues.TryGetValue(name, out var value))
                    {
                        if (value == null) key.DeleteValue(name, false);
                        else key.SetValue(name, value);
                    }
                }
            }
            else
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }

            _originalProxyCaptured = false;
            _originalProxyValues.Clear();
            RefreshWinInet();
        }
        catch { }
    }

    private static void RefreshWinInet()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }

    // ---------- 根证书 ----------

    private static string GetRootCaPath()
    {
        return Path.Combine(Helpers.AppPathHelper.AppDirectory, "Data", "mitm", "root_ca.pfx");
    }

    /// <summary>
    /// 自检根证书是否已就绪：pfx 文件存在 + 受信任根存储中有同指纹证书。
    /// </summary>
    private bool IsRootCertificateReady()
    {
        try
        {
            var pfxPath = GetRootCaPath();
            if (!File.Exists(pfxPath)) return false;
            return IsCertificateInStore(pfxPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 检查 pfx 文件对应的证书是否已在受信任根存储中。
    /// </summary>
    private static bool IsCertificateInStore(string pfxPath)
    {
        try
        {
            if (!File.Exists(pfxPath)) return false;

            string thumbprint;
            using (var pfxCert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, CaPassword))
            {
                thumbprint = pfxCert.Thumbprint;
            }

            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                if (string.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从受信任根存储中移除本应用的根证书。
    /// </summary>
    private void RemoveRootCertFromStore()
    {
        try
        {
            var pfxPath = GetRootCaPath();
            if (!File.Exists(pfxPath))
            {
                LogService.Instance.Log("Info", "MitmCertRemove", "MITM", "pfx 文件不存在，跳过证书移除");
                return;
            }

            string thumbprint;
            using (var pfxCert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, CaPassword))
            {
                thumbprint = pfxCert.Thumbprint;
            }

            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            var toRemove = new List<X509Certificate2>();
            foreach (var cert in store.Certificates)
            {
                if (string.Equals(cert.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(cert.Subject, CaCommonName, StringComparison.OrdinalIgnoreCase))
                {
                    toRemove.Add(cert);
                }
            }

            if (toRemove.Count == 0)
            {
                LogService.Instance.Log("Info", "MitmCertRemove", "MITM", "受信任根存储中未找到本应用证书，无需移除");
                return;
            }

            foreach (var cert in toRemove)
            {
                try { store.Remove(cert); } catch { }
            }

            LogService.Instance.Log("Info", "MitmCertRemoved", "MITM", $"已从受信任根存储移除 {toRemove.Count} 个根证书");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmCertRemoveFailed", "MITM", $"移除根证书失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 从 pfx 文件静默安装根证书到 CurrentUser 受信任根存储（不弹 Windows 证书安装对话框）。
    /// </summary>
    private void InstallRootCertToStore()
    {
        var pfxPath = GetRootCaPath();

        // 加载前再次确认文件存在（避免 VMware 共享文件夹等环境下 File.Exists 与实际读取之间的竞态）
        if (!File.Exists(pfxPath))
        {
            LogService.Instance.Log("Error", "MitmCertInstallFailed", "MITM", $"pfx 文件不存在：{pfxPath}");
            return;
        }

        try
        {
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, CaPassword);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            // 移除同主题的旧证书，避免重复
            var toRemove = new List<X509Certificate2>();
            foreach (var c in store.Certificates)
            {
                if (string.Equals(c.Subject, cert.Subject, StringComparison.OrdinalIgnoreCase))
                    toRemove.Add(c);
            }
            foreach (var c in toRemove)
            {
                try { store.Remove(c); } catch { }
            }

            store.Add(cert);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmCertInstallFailed", "MITM", $"安装根证书失败：{ex.Message}");
            throw;
        }
    }

    // ---------- 证书完整性监测 ----------

    private void OnIntegrityCheck(object? state)
    {
        try
        {
            if (!_isRunning) return;
            EnsureCertificateIntegrity();
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmIntegrityCheckError", "MITM", ex.Message);
        }
    }

    /// <summary>
    /// 检查 MITM 根证书安装是否完整：
    /// 1. 证书文件 Data\mitm\root_ca.pfx 缺失或损坏 → 重新生成并安装；
    /// 2. 受信任根存储中缺少与 pfx 指纹一致的证书 → 从 pfx 恢复安装。
    /// 代理运行期间每 60 秒自动执行；也可由外部（如完整性巡检）主动调用。
    /// </summary>
    public void EnsureCertificateIntegrity()
    {
        try
        {
            var pfxPath = GetRootCaPath();
            if (!File.Exists(pfxPath))
            {
                // 证书文件丢失：重新生成并安装
                TryReinstallCertificate(reason: "根证书文件缺失");
                return;
            }

            string expectedThumbprint;
            try
            {
                using var pfxCert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, CaPassword);
                expectedThumbprint = pfxCert.Thumbprint;
            }
            catch
            {
                // pfx 损坏：删除后重新生成
                try { File.Delete(pfxPath); } catch { }
                TryReinstallCertificate(reason: "根证书文件损坏");
                return;
            }

            using (var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                foreach (var cert in store.Certificates)
                {
                    if (string.Equals(cert.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        return; // 已受信任，完整性正常
                    }
                }
            }

            // 受信任根存储中缺少该证书：从 pfx 恢复安装
            TryReinstallCertificate(expectedThumbprint, "受信任根存储中缺少根证书");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmCertIntegrityError", "MITM", ex.Message);
        }
    }

    private void TryReinstallCertificate(string reason)
    {
        TryReinstallCertificate(null, reason);
    }

    private void TryReinstallCertificate(string? expectedThumbprint, string reason)
    {
        try
        {
            // 防抖：短时间内不重复重装，避免反复生成新证书导致浏览器信任失效
            if ((DateTime.Now - _lastCertReinstallAt) < CertReinstallCooldown)
            {
                LogService.Instance.Log("Warning", "MitmCertIntegrity", "MITM",
                    $"{reason}，但 5 分钟内已重装过证书，本次跳过（等待下次检查）");
                return;
            }
            _lastCertReinstallAt = DateTime.Now;

            var pfxPath = GetRootCaPath();
            if (!File.Exists(pfxPath))
            {
                // 重新生成根证书（会再次安装到受信任根存储）
                _proxyServer?.CertificateManager.EnsureRootCertificate(
                    userTrustRootCertificate: true,
                    machineTrustRootCertificate: false,
                    trustRootCertificateAsAdmin: false);
                LogService.Instance.Log("Warning", "MitmCertReinstalled", "MITM", $"{reason}，已重新生成并安装根证书");
                return;
            }

            // pfx 有效：直接从 pfx 恢复到受信任根存储
            using var cert = X509CertificateLoader.LoadPkcs12FromFile(pfxPath, CaPassword);
            using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);

            // 先移除同名/同指纹的旧证书，避免重复冲突
            var toRemove = new List<X509Certificate2>();
            foreach (var c in store.Certificates)
            {
                if (string.Equals(c.Subject, cert.Subject, StringComparison.OrdinalIgnoreCase) ||
                    (expectedThumbprint != null &&
                     string.Equals(c.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase)))
                {
                    toRemove.Add(c);
                }
            }
            foreach (var c in toRemove)
            {
                try { store.Remove(c); } catch { }
            }

            store.Add(cert);
            LogService.Instance.Log("Warning", "MitmCertReinstalled", "MITM", $"{reason}，已从 pfx 恢复到受信任根存储（{cert.Subject}）");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "MitmCertReinstallFailed", "MITM", $"{reason}，重装失败：{ex.Message}");
        }
    }
}

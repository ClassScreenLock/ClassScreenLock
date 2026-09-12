using System;
using Microsoft.Win32;

namespace ClassScreenLock.Services;

/// <summary>
/// "安全中心登录账户满足最低允许权限 → 放行被拦截进程"服务。
///
/// 当登录安全中心的账户权限等级（AccountType 数值）不高于设置的最低放行权限等级
/// （Settings.Blockage.MinimumAllowedAccountType）时，直接放行所有被拦截的进程
/// （CMD / PowerShell / ISE / 任务管理器 / 注册表编辑器等）：
///   - 主程序自身拦截链路（WMI 实时 / 轮询兜底）通过 <see cref="IsBypassActive"/> 直接跳过；
///   - IFEO 劫持链路由 IfeoRedirector（独立短命进程）读取本服务维护的机器级心跳标记自行放行。
///
/// 跨进程共享状态：HKLM\SOFTWARE\ClassScreenLock\AllowBlockedProcesses\Until
/// （QWORD Unix 毫秒时间戳）。主程序（SYSTEM）在监控循环中持续刷新心跳
/// （Until = now + 60s）；IfeoRedirector 判断 now &lt; Until 即放行。
///
/// 心跳机制保证无残留放行窗口：老师登出（清值）或主程序崩溃（停止刷新）后，
/// 最迟 60 秒自动恢复拦截。普通学生用户对 HKLM\SOFTWARE\ClassScreenLock 键无写权限，
/// 无法伪造放行标记。
/// </summary>
public static class ProcessBypassService
{
    private const string BypassKeyPath = @"SOFTWARE\ClassScreenLock\AllowBlockedProcesses";
    private const string UntilValueName = "Until";

    /// <summary>心跳窗口：Until = now + 60s；读侧在 60s 内刷新过即视为放行有效。</summary>
    private static readonly TimeSpan HeartbeatWindow = TimeSpan.FromSeconds(60);

    // 节流：MonitorLoop 每 300-2000ms 调用一次 RefreshHeartbeat，
    // 而 HKLM 注册表写入代价不菲。15 秒刷新一次仍有 4 倍余量（窗口 60s），
    // 状态切换（放行↔拦截）时立即执行不受节流限制。
    private static readonly TimeSpan HeartbeatRefreshInterval = TimeSpan.FromSeconds(15);
    private static DateTime _lastHeartbeatUtc = DateTime.MinValue;
    private static bool _lastActive;

    /// <summary>
    /// 当前安全中心登录账户是否满足最低放行权限（主程序进程内判断）。
    /// 锁屏（上课）期间强制拦截：即使老师账户仍处于登录状态也不放行，
    /// 防止"老师登录后未登出即锁屏"导致学生钻空子；
    /// 心跳会随之停止刷新并清除标记，IfeoRedirector 侧同样恢复拦截。
    /// </summary>
    public static bool IsBypassActive()
    {
        try
        {
            if (LockScreenService.Instance.IsLocked) return false;

            var current = AccountService.Instance.CurrentAccount;
            if (current == null) return false;
            var minLevel = SettingsService.Blockage?.MinimumAllowedAccountType ?? 1;
            return (int)current.AccountType <= minLevel;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 刷新机器级心跳标记：放行激活时写入 Until = now + 60s，否则删除标记。
    /// 应在监控循环每周期调用（跟随锁屏 1000ms / 防护 1500ms / 普通 2000ms）。
    /// 内部按 15 秒节流（状态切换立即执行），避免每周期写 HKLM 注册表。
    /// </summary>
    public static void RefreshHeartbeat()
    {
        try
        {
            var active = IsBypassActive();
            var stateChanged = active != _lastActive;
            _lastActive = active;

            if (!stateChanged && (DateTime.UtcNow - _lastHeartbeatUtc) < HeartbeatRefreshInterval)
            {
                return;
            }
            _lastHeartbeatUtc = DateTime.UtcNow;

            if (active)
            {
                using var key = Registry.LocalMachine.CreateSubKey(BypassKeyPath);
                var untilMs = DateTimeOffset.UtcNow.Add(HeartbeatWindow).ToUnixTimeMilliseconds();
                key?.SetValue(UntilValueName, untilMs, RegistryValueKind.QWord);
            }
            else
            {
                using var key = Registry.LocalMachine.OpenSubKey(BypassKeyPath, writable: true);
                key?.DeleteValue(UntilValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 心跳写入失败不影响主功能；读侧将因标记陈旧而保持拦截
        }
    }

    /// <summary>
    /// 立即清除心跳标记（账户登出时调用，立即恢复拦截，无需等待下一个监控周期）。
    /// </summary>
    public static void ClearHeartbeat()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BypassKeyPath, writable: true);
            key?.DeleteValue(UntilValueName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }
}

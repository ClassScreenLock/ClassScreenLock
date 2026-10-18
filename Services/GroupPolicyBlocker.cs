using System;
using Microsoft.Win32;

namespace ClassScreenLock.Services;

/// <summary>
/// 原生组策略级拦截（对应组策略"计算机配置 > 管理模板 > 系统 > 阻止访问命令提示符"）。
///
/// 实现：写入注册表策略键 HKLM\SOFTWARE\Policies\Microsoft\Windows\System\DisableCMD
///   1 = 阻止 cmd.exe 与 .bat/.cmd 批处理
///   2 = 仅阻止批处理
/// 该策略由系统在执行层强制生效，进程直接无法启动，没有任何轮询空窗期。
///
/// 重要约束：仅在锁屏（Full / ProtectionOnly）期间启用。
/// 本软件自身在正常操作中依赖 cmd.exe / 批处理（看门狗启动、自举脚本等），
/// 若防护模式下全局启用会把软件自身功能一并拦截，因此必须由调用方
/// （AppBlockingService.RefreshDynamicState）按锁屏状态动态启停。
/// </summary>
public static class GroupPolicyBlocker
{
    private const string SystemPolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string DisableCmdValue = "DisableCMD";

    private static volatile bool _disableCmdActive;

    /// <summary>"阻止访问命令提示符"策略当前是否处于启用状态（本进程内记忆）。</summary>
    public static bool IsDisableCmdActive => _disableCmdActive;

    /// <summary>
    /// 启动时同步注册表现状：若策略键残留为 1（上次进程异常退出未来得及清理），
    /// 标记为 active，由调用方按当前锁屏状态决定保留或清除，避免策略残留。
    /// </summary>
    public static void SyncFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SystemPolicyPath, false);
            var value = key?.GetValue(DisableCmdValue);
            _disableCmdActive = value is int i && i == 1;
            if (_disableCmdActive)
            {
                LogService.Instance.Log("Warning", "Gpo", "DisableCMD",
                    "检测到 DisableCMD 策略残留（值为 1），已标记为启用状态，等待按锁屏状态刷新");
            }
        }
        catch
        {
            _disableCmdActive = false;
        }
    }

    /// <summary>启用"阻止访问命令提示符"（cmd.exe + 批处理）。</summary>
    public static bool EnableDisableCmd()
    {
        if (_disableCmdActive) return true;
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(SystemPolicyPath, true);
            key?.SetValue(DisableCmdValue, 1, RegistryValueKind.DWord);
            _disableCmdActive = true;
            LogService.Instance.Log("Info", "Gpo", "DisableCMD",
                "已启用组策略：阻止访问命令提示符 (DisableCMD=1)");
            return true;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Gpo", "DisableCMD", $"启用 DisableCMD 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>停用"阻止访问命令提示符"，恢复系统默认。</summary>
    public static void DisableDisableCmd()
    {
        if (!_disableCmdActive) return;
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SystemPolicyPath, true);
            key?.DeleteValue(DisableCmdValue, false);
            _disableCmdActive = false;
            LogService.Instance.Log("Info", "Gpo", "DisableCMD",
                "已停用组策略：阻止访问命令提示符");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "Gpo", "DisableCMD", $"停用 DisableCMD 失败: {ex.Message}");
        }
    }
}

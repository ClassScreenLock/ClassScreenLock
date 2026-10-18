using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ClassScreenLock.Services;

/// <summary>
/// 全局粘贴拦截器：以 Win32 原始剪贴板 API 替代 Avalonia 默认的 OLE 剪贴板读取路径。
/// Avalonia 在 Windows 上默认走 OleGetClipboard，OLE 封送 IDataObject 依赖进程外 COM 服务器，
/// 当主程序以 SYSTEM 账户运行时，与用户账户进程之间的 COM 封送会被安全上下文拦截，
/// 表现为 GetTextAsync 静默返回空字符串（"粘贴中文直接消失"）。
/// GetClipboardData(CF_UNICODETEXT) 直接读剪贴板内存块，不经过 COM，SYSTEM 下同样稳定可靠。
/// 由于 Avalonia 11.3 的 IClipboard 接口包含私有默认成员、禁止用户代码实现，无法整体替换平台
/// 剪贴板，因此在窗口根部以隧道方式拦截 Ctrl+V，手动读取并插入焦点文本框，绕过 OLE。
/// </summary>
public static class SystemPasteInterceptor
{
    private static readonly ConditionalWeakTable<Window, object> _attached = new();

    /// <summary>
    /// 挂载到窗口：以隧道方式在窗口根部拦截 Ctrl+V。同一窗口实例只会挂载一次。
    /// </summary>
    public static void Attach(Window window)
    {
        if (window == null) return;
        if (_attached.TryGetValue(window, out _)) return;

        _attached.Add(window, null!);
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
    }

    private static void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V) return;
        if ((e.KeyModifiers & KeyModifiers.Control) == 0) return;
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0) return; // 保留 Ctrl+Alt+V 等特殊组合

        // 用 Win32 原生 API 读取剪贴板 Unicode 文本，绕过 Avalonia OLE 封送
        var text = SystemClipboard.GetUnicodeText();
        if (string.IsNullOrEmpty(text)) return;

        if (sender is not Window window) return;

        var focused = window.FocusManager?.GetFocusedElement();
        // PasswordBox 继承自 TextBox，密码输入框同样适用
        if (focused is not TextBox textBox) return;

        // 替换当前选区并插入文本，光标定位到插入内容末尾
        var start = Math.Min(textBox.SelectionStart, textBox.SelectionEnd);
        var end = Math.Max(textBox.SelectionStart, textBox.SelectionEnd);
        var current = textBox.Text ?? string.Empty;
        if (start < 0) start = 0;
        if (end > current.Length) end = current.Length;

        textBox.Text = current.Remove(start, end - start).Insert(start, text);
        textBox.CaretIndex = start + text.Length;

        // 拦截默认粘贴（默认走 OLE 读取会失败），避免重复触发
        e.Handled = true;
    }
}

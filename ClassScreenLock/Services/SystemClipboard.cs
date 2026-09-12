using System;
using System.Runtime.InteropServices;

namespace ClassScreenLock.Services;

/// <summary>
/// 绕过 Avalonia 的 OLE 剪贴板实现，直接用 Win32 原始 API 读取剪贴板 Unicode 文本。
/// Avalonia 在 Windows 上默认使用 OLE 剪贴板（OleGetClipboard），其依赖 STA 线程模型与
/// 跨会话封送。当主程序以 SYSTEM 账户运行时，OLE 读取可能静默失败返回空字符串，
/// 导致"粘贴中文直接消失"。Win32 GetClipboardData 无此限制，同一会话内可直接读取。
/// </summary>
public static class SystemClipboard
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    /// <summary>
    /// 读取剪贴板中的 Unicode 文本（兼容中文）。失败或为空时返回 null。
    /// </summary>
    public static string? GetUnicodeText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            IntPtr handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;

            IntPtr pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) return null;

            try
            {
                // 以 Unicode 方式读取（截取到终止符为止）
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// 清空剪贴板。返回是否成功。
    /// </summary>
    public static bool Clear()
    {
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            return EmptyClipboard();
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// 将 Unicode 文本写入剪贴板（兼容中文）。返回是否成功。
    /// </summary>
    public static bool SetUnicodeText(string? text)
    {
        if (text == null) text = string.Empty;

        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            // 清空旧内容（保持剪贴板查看器链通知完整）
            if (!EmptyClipboard()) return false;

            // Unicode 文本以 NUL 结尾：字节数 = (len + 1) * 2
            int byteCount = (text.Length + 1) * sizeof(char);
            IntPtr hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)byteCount);
            if (hGlobal == IntPtr.Zero) return false;

            try
            {
                IntPtr pointer = GlobalLock(hGlobal);
                if (pointer == IntPtr.Zero) return false;

                try
                {
                    byte[] bytes = System.Text.Encoding.Unicode.GetBytes(text + "\0");
                    Marshal.Copy(bytes, 0, pointer, bytes.Length);
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
                {
                    return false;
                }
                // 成功后 hGlobal 归系统所有，不再释放
                return true;
            }
            catch
            {
                GlobalFree(hGlobal);
                return false;
            }
        }
        finally
        {
            CloseClipboard();
        }
    }
}

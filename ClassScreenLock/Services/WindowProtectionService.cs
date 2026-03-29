using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ClassScreenLock.Services;

public class WindowProtectionService
{
    private const uint WDA_NONE = 0x00000000;
    private const uint WDA_MONITOR = 0x00000001;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const int ERROR_INVALID_PARAMETER = 87;

    private static readonly Version MinimumVersionForExcludeFromCapture = new(10, 0, 19041, 0);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

    [DllImport("kernel32.dll")]
    private static extern bool GetVersionEx(ref OSVERSIONINFOEX lpVersionInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct OSVERSIONINFOEX
    {
        public int dwOSVersionInfoSize;
        public int dwMajorVersion;
        public int dwMinorVersion;
        public int dwBuildNumber;
        public int dwPlatformId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szCSDVersion;
        public short wServicePackMajor;
        public short wServicePackMinor;
        public short wSuiteMask;
        public byte wProductType;
        public byte wReserved;
    }

    private static readonly Lazy<WindowProtectionService> _instance = new(() => new WindowProtectionService());
    public static WindowProtectionService Instance => _instance.Value;

    private readonly HashSet<IntPtr> _protectedWindows = new();
    private readonly object _lock = new();
    private bool _isEnabled = true;
    private bool? _supportsExcludeFromCapture;

    public bool IsEnabled
    {
        get
        {
            lock (_lock)
            {
                return _isEnabled;
            }
        }
        set
        {
            bool changed = false;
            lock (_lock)
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    changed = true;
                }
            }
            
            if (changed)
            {
                UpdateAllWindowsProtection();
            }
        }
    }

    private WindowProtectionService()
    {
        _isEnabled = SecurityService.Instance.Settings.EnableSoftwareSecurity;
    }

    private bool SupportsExcludeFromCapture
    {
        get
        {
            if (_supportsExcludeFromCapture.HasValue)
                return _supportsExcludeFromCapture.Value;

            try
            {
                var osInfo = new OSVERSIONINFOEX();
                osInfo.dwOSVersionInfoSize = Marshal.SizeOf(typeof(OSVERSIONINFOEX));
                
                if (GetVersionEx(ref osInfo))
                {
                    var currentVersion = new Version(osInfo.dwMajorVersion, osInfo.dwMinorVersion, osInfo.dwBuildNumber, 0);
                    _supportsExcludeFromCapture = currentVersion >= MinimumVersionForExcludeFromCapture;
                    
                    LogService.Instance.Log("Info", "WindowProtection", "VersionCheck", 
                        $"Windows 版本: {currentVersion}, 支持 WDA_EXCLUDEFROMCAPTURE: {_supportsExcludeFromCapture}");
                }
                else
                {
                    _supportsExcludeFromCapture = true;
                }
            }
            catch
            {
                _supportsExcludeFromCapture = true;
            }

            return _supportsExcludeFromCapture.Value;
        }
    }

    public void InitializeFromSettings()
    {
        var settings = SecurityService.Instance.Settings;
        _isEnabled = settings.EnableSoftwareSecurity;
    }

    public void ApplyProtectionAsync(Window window)
    {
        if (window == null) return;

        Dispatcher.UIThread.Post(async () =>
        {
            await System.Threading.Tasks.Task.Delay(100);
            
            try
            {
                var platformHandle = window.TryGetPlatformHandle();
                if (platformHandle == null)
                {
                    LogService.Instance.Log("Warning", "WindowProtection", "ApplyFailed", "TryGetPlatformHandle 返回 null");
                    return;
                }

                var handle = platformHandle.Handle;
                if (handle == IntPtr.Zero)
                {
                    LogService.Instance.Log("Warning", "WindowProtection", "ApplyFailed", "窗口句柄为 Zero");
                    return;
                }

                ApplyProtection(handle);
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WindowProtection", "ApplyException", ex.Message);
            }
        }, DispatcherPriority.Loaded);
    }

    public bool ApplyProtection(Window window)
    {
        if (window == null) return false;

        try
        {
            var handle = window.TryGetPlatformHandle()?.Handle;
            if (handle == null || handle.Value == IntPtr.Zero)
            {
                LogService.Instance.Log("Warning", "WindowProtection", "ApplyFailed", "无法获取窗口句柄");
                return false;
            }

            return ApplyProtection(handle.Value);
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WindowProtection", "ApplyException", ex.Message);
            return false;
        }
    }

    public bool ApplyProtection(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        lock (_lock)
        {
            try
            {
                if (!_isEnabled)
                {
                    bool clearResult = SetWindowDisplayAffinity(hWnd, WDA_NONE);
                    if (clearResult)
                    {
                        _protectedWindows.Remove(hWnd);
                        LogService.Instance.Log("Info", "WindowProtection", "Disabled", $"窗口保护已禁用, hWnd={hWnd}");
                    }
                    return clearResult;
                }

                uint affinity = SupportsExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_MONITOR;
                bool result = SetWindowDisplayAffinity(hWnd, affinity);

                if (result)
                {
                    _protectedWindows.Add(hWnd);
                    string mode = affinity == WDA_EXCLUDEFROMCAPTURE ? "完全排除捕获" : "仅显示器显示";
                    LogService.Instance.Log("Info", "WindowProtection", "Applied", 
                        $"窗口保护已启用, hWnd={hWnd}, 模式: {mode}");
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    
                    if (error == ERROR_INVALID_PARAMETER && affinity == WDA_EXCLUDEFROMCAPTURE)
                    {
                        LogService.Instance.Log("Warning", "WindowProtection", "Fallback", 
                            $"WDA_EXCLUDEFROMCAPTURE 不支持，回退到 WDA_MONITOR");
                        
                        _supportsExcludeFromCapture = false;
                        affinity = WDA_MONITOR;
                        result = SetWindowDisplayAffinity(hWnd, affinity);
                        
                        if (result)
                        {
                            _protectedWindows.Add(hWnd);
                            LogService.Instance.Log("Info", "WindowProtection", "Applied", 
                                $"窗口保护已启用(回退模式), hWnd={hWnd}, 模式: 仅显示器显示");
                            return true;
                        }
                    }
                    
                    LogService.Instance.Log("Warning", "WindowProtection", "ApplyFailed", 
                        $"SetWindowDisplayAffinity 失败, 错误码: {error}, hWnd={hWnd}");
                }

                return result;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WindowProtection", "ApplyException", ex.Message);
                return false;
            }
        }
    }

    public bool RemoveProtection(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        lock (_lock)
        {
            try
            {
                bool result = SetWindowDisplayAffinity(hWnd, WDA_NONE);
                if (result)
                {
                    _protectedWindows.Remove(hWnd);
                    LogService.Instance.Log("Info", "WindowProtection", "Removed", $"窗口保护已移除, hWnd={hWnd}");
                }
                return result;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WindowProtection", "RemoveException", ex.Message);
                return false;
            }
        }
    }

    public bool IsWindowProtected(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return false;

        try
        {
            if (GetWindowDisplayAffinity(hWnd, out uint affinity))
            {
                return (affinity & WDA_EXCLUDEFROMCAPTURE) != 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateAllWindowsProtection()
    {
        List<IntPtr> windowsToUpdate;
        lock (_lock)
        {
            windowsToUpdate = _protectedWindows.ToList();
        }
        
        foreach (var hWnd in windowsToUpdate)
        {
            try
            {
                if (_isEnabled)
                {
                    uint affinity = SupportsExcludeFromCapture ? WDA_EXCLUDEFROMCAPTURE : WDA_MONITOR;
                    bool result = SetWindowDisplayAffinity(hWnd, affinity);
                    LogService.Instance.Log("Debug", "WindowProtection", "UpdateProtection", 
                        $"更新窗口保护: hWnd={hWnd}, 模式: {(affinity == WDA_EXCLUDEFROMCAPTURE ? "完全排除捕获" : "仅显示器显示")}, 结果: {result}");
                }
                else
                {
                    bool result = SetWindowDisplayAffinity(hWnd, WDA_NONE);
                    LogService.Instance.Log("Debug", "WindowProtection", "UpdateProtection", 
                        $"清除窗口保护: hWnd={hWnd}, 结果: {result}");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WindowProtection", "UpdateException", 
                    $"更新窗口保护失败: hWnd={hWnd}, 错误: {ex.Message}");
            }
        }
    }

    public void SetSoftwareSecurityEnabled(bool enabled)
    {
        var settings = SecurityService.Instance.Settings;
        settings.EnableSoftwareSecurity = enabled;
        SecurityService.Instance.SaveSettings(settings);
        
        IsEnabled = enabled;
        
        LogService.Instance.Log("Security", "SoftwareSecurityChanged", "System", 
            $"软件安全保护已{(enabled ? "启用" : "禁用")}");
    }
}

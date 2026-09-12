using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using ClassScreenLock.Extensions;
using ClassScreenLock.Helpers;
using Avalonia.Styling;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
#if WINDOWS
using Microsoft.Win32;
#endif
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ClassScreenLock.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private bool _suppressSettingSideEffects;

    [ObservableProperty]
    private SettingsModel _settings = null!;
    
    [ObservableProperty]
    private bool _darkMode = false;
    
    [ObservableProperty]
    private string _accentColor = "#0067C0";
    
    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private double _notificationDurationSeconds = 3;

    [ObservableProperty]
    private bool _notificationSound = true;

    [ObservableProperty]
    private int _weeklyCycleCount = 4;

    [ObservableProperty]
    private DateTime? _termStartDate;
    
    [ObservableProperty]
    private string _language = "zh-CN";
    
    [ObservableProperty]
    private bool _useSystemAccentColor = false;
    
    [ObservableProperty]
    private string _customAccentColor = "#0067C0";
    
    public List<string> AvailableLanguages { get; private set; } = new List<string>();
    public List<string> AvailableAccentColors { get; private set; } = new List<string>();

    [ObservableProperty]
    private ObservableCollection<AccentColorOption> _accentColorOptions = new();
    
    public SettingsViewModel()
    {
        LoadSettings();
        
        // 订阅语言变化事件
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        
        // 订阅系统颜色变化事件
        if (Application.Current?.PlatformSettings != null)
        {
            Application.Current.PlatformSettings.ColorValuesChanged += OnSystemColorValuesChanged;
        }
    }

    [ObservableProperty]
    private ObservableCollection<string> _automationSchemes = new();

    [ObservableProperty]
    private string _currentAutomationScheme = "Default";

    [ObservableProperty]
    private string _newAutomationSchemeName = string.Empty;

    private void OnSystemColorValuesChanged(object? sender, Avalonia.Platform.PlatformColorValues e)
    {
        if (UseSystemAccentColor)
        {
            // 在UI线程上更新
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                AccentColor = GetSystemAccentColor();
            });
        }
    }
    
    private void LoadSettings()
    {
        Settings = SettingsService.General;
        
        // 初始化属性
        DarkMode = Settings.DarkMode;
        AccentColor = Settings.AccentColor;
        ShowNotifications = Settings.ShowNotifications;
        NotificationDurationSeconds = Settings.NotificationDurationMs > 0 ? Settings.NotificationDurationMs / 1000.0 : 3;
        NotificationSound = Settings.NotificationSound;
        Language = Settings.Language;
        UseSystemAccentColor = Settings.UseSystemAccentColor;
        CustomAccentColor = Settings.AccentColor; // 初始化为当前强调色
        WeeklyCycleCount = Settings.WeeklyCycleCount;
        TermStartDate = Settings.TermStartDate;

        // 通知位置固定为屏幕中央
        if (Settings.NotificationPosition != NotificationPosition.Center)
        {
            UpdateSetting(s => s.NotificationPosition = NotificationPosition.Center);
        }

        AutomationSchemes = new ObservableCollection<string>(Settings.AutomationSchemes ?? new System.Collections.Generic.List<string> { "Default" });
        CurrentAutomationScheme = string.IsNullOrWhiteSpace(Settings.CurrentAutomationScheme) ? "Default" : Settings.CurrentAutomationScheme;
        
        // 如果使用系统强调色，则获取系统强调色
        if (UseSystemAccentColor)
        {
            AccentColor = GetSystemAccentColor();
        }
        
        // 初始化可用选项
        AvailableLanguages = new List<string>
        {
            "zh-CN",
            "en-US"
        };
        
        AvailableAccentColors = new List<string>
        {
            "#0067C0", // 默认蓝色
            "#FF6B00", // 橙色
            "#107C10", // 绿色
            "#E81123", // 红色
            "#5C2D91", // 紫色
            "#00B294", // 青色
            "#E74856", // 亮红色
            "#0078D4", // 亮蓝色
            "#FFB900", // 亮黄色
            "#E3008C"  // 亮粉色
        };

        // 构建预设色板
        AccentColorOptions = new ObservableCollection<AccentColorOption>(
            AvailableAccentColors.Select(c => new AccentColorOption { Hex = c }));
        RefreshAccentColorSelection();
    }

    /// <summary>
    /// 根据当前强调色刷新预设色板的选中状态。
    /// </summary>
    private void RefreshAccentColorSelection()
    {
        foreach (var option in AccentColorOptions)
        {
            option.IsSelected = string.Equals(option.Hex, AccentColor, StringComparison.OrdinalIgnoreCase);
        }
    }

    [RelayCommand]
    private void AddAutomationScheme()
    {
        var name = (NewAutomationSchemeName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!AutomationSchemes.Contains(name))
        {
            AutomationSchemes.Add(name);
            UpdateSetting(s =>
            {
                if (s.AutomationSchemes == null) s.AutomationSchemes = new System.Collections.Generic.List<string>();
                if (!s.AutomationSchemes.Contains(name)) s.AutomationSchemes.Add(name);
            });
            SettingsService.EnsureAutomationSchemeFile(name);
        }
        NewAutomationSchemeName = string.Empty;
    }

    [RelayCommand]
    private void RemoveAutomationScheme(string? name)
    {
        var target = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        if (string.Equals(target, CurrentAutomationScheme, StringComparison.OrdinalIgnoreCase)) return;
        if (AutomationSchemes.Contains(target))
        {
            AutomationSchemes.Remove(target);
            UpdateSetting(s =>
            {
                s.AutomationSchemes = (s.AutomationSchemes ?? new System.Collections.Generic.List<string>()).Where(x => !string.Equals(x, target, StringComparison.OrdinalIgnoreCase)).ToList();
            });
        }
    }

    [RelayCommand]
    private void SetCurrentAutomationScheme(string? name)
    {
        var target = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!AutomationSchemes.Contains(target)) return;
        CurrentAutomationScheme = target;
        UpdateSetting(s => s.CurrentAutomationScheme = target);
        SettingsService.EnsureAutomationSchemeFile(target);
        // 切换方案后清空缓存以便重新加载自动化配置
        ClassScreenLock.Services.AutomationService.Instance.ForceCheck();
    }
    
    partial void OnDarkModeChanged(bool value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.DarkMode = value);
        // 立即应用主题更改
        ApplyThemeChange(value);
    }
    
    partial void OnAccentColorChanged(string value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.AccentColor = value);
        // 立即应用强调色更改
        ApplyAccentColorChange(value);
        // 刷新预设色板选中状态
        RefreshAccentColorSelection();
    }
    
    partial void OnUseSystemAccentColorChanged(bool value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.UseSystemAccentColor = value);
        
        // 如果使用系统强调色，则获取系统强调色
        if (value)
        {
            AccentColor = GetSystemAccentColor();
        }
        else
        {
            // 否则使用自定义强调色
            AccentColor = CustomAccentColor;
        }
    }
    
    partial void OnCustomAccentColorChanged(string value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.AccentColor = value);
        
        // 如果不使用系统强调色，则应用自定义强调色
        if (!UseSystemAccentColor)
        {
            AccentColor = value;
        }
    }
    
    /// <summary>
    /// 获取系统强调色
    /// </summary>
    /// <returns>系统强调色的十六进制字符串</returns>
    private string GetSystemAccentColor()
    {
        try
        {
            if (Application.Current?.PlatformSettings != null)
            {
                var colorValues = Application.Current.PlatformSettings.GetColorValues();
                var accentColor = colorValues.AccentColor1;
                return $"#{accentColor.R:X2}{accentColor.G:X2}{accentColor.B:X2}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"获取系统强调色失败: {ex.Message}");
        }
        
        // 如果获取失败，返回默认颜色
        return "#0067C0";
    }
    
    partial void OnShowNotificationsChanged(bool value)
    {
        UpdateSetting(s => s.ShowNotifications = value);
        NotificationService.Instance.UpdateNotificationSettings(value);
    }

    partial void OnNotificationDurationSecondsChanged(double value)
    {
        if (_suppressSettingSideEffects) return;
        if (value < 1) value = 1;
        if (value > 8) value = 8;
        // 取整到 0.5 秒粒度，避免滑块产生抖动值
        value = Math.Round(value * 2) / 2;
        UpdateSetting(s => s.NotificationDurationMs = (int)(value * 1000));
    }

    partial void OnNotificationSoundChanged(bool value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.NotificationSound = value);
    }

    partial void OnWeeklyCycleCountChanged(int value)
    {
        if (_suppressSettingSideEffects) return;
        if (value < 1) value = 1;
        if (value > 6) value = 6;
        UpdateSetting(s => s.WeeklyCycleCount = value);
    }

    partial void OnTermStartDateChanged(DateTime? value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.TermStartDate = value);
    }

    partial void OnLanguageChanged(string value)
    {
        if (_suppressSettingSideEffects) return;
        // 当 UI 绑定更改 Language 属性时，同步更新本地化服务
        if (LocalizationService.Instance.CurrentLanguage != value)
        {
            LocalizationService.Instance.CurrentLanguage = value;
        }
    }
    
    private void OnLanguageChanged(object? sender, string e)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.Language = e);
        // 立即应用语言更改
        ApplyLanguageChange(e);
    }
    
    private void ApplyThemeChange(bool isDarkMode)
    {
        var theme = isDarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        Application.Current!.RequestedThemeVariant = theme;
        
        // 更新所有窗口的 dark 类
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (isDarkMode)
                {
                    if (!window.Classes.Contains("dark"))
                    {
                        window.Classes.Add("dark");
                    }
                }
                else
                {
                    window.Classes.Remove("dark");
                }
            }
        }
    }
    
    private void ApplyAccentColorChange(string accentColor)
    {
        ThemeHelper.ApplyAccentColor(accentColor);
    }
    
    private void RefreshAccentBrushes(Control parent, HashSet<Control>? visitedControls = null)
    {
        // 现在使用 ThemeHelper.RefreshAccentBrushes，此处的私有方法可以保留空实现或直接移除
    }

    

    
    private void ApplyLanguageChange(string language, bool showNotification = true)
    {
        try
        {
            // 1. 更新设置模型
            UpdateSetting(s => s.Language = language);
            
            // 2. 使用本地化服务切换语言
            LocalizationService.Instance.CurrentLanguage = language;
            
            // 3. 根据语言代码设置文化信息
            var cultureInfo = new System.Globalization.CultureInfo(language);
            System.Globalization.CultureInfo.CurrentCulture = cultureInfo;
            System.Globalization.CultureInfo.CurrentUICulture = cultureInfo;
            
            // 4. 通知 Language 属性变更
            OnPropertyChanged(nameof(Language));
            
            // 5. 递归刷新所有 UI 控件（重载方案：通过重新加载资源或强制刷新属性）
            // 在 Avalonia 中，如果使用了 {DynamicResource}，语言切换通常会自动触发更新
            // 但如果部分文本是绑定的，可能需要手动通知
            RefreshAllLocalizableProperties();
            
            // 6. 显示语言更改成功通知
            if (showNotification)
            {
                _ = NotificationService.Instance.ShowSuccessAsync("Notify_LanguageChanged", 2000);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"应用语言更改失败: {ex.Message}");
        }
    }

    private void RefreshAllLocalizableProperties()
    {
        // 触发所有可能受语言影响的属性变更通知
        OnPropertyChanged(string.Empty); // 通知所有属性已更改
    }
    
    private void UpdateSetting(Action<SettingsModel> updateAction)
    {
        SettingsService.UpdateGeneral(updateAction);
        Settings = SettingsService.General;
    }
    
    [RelayCommand]
    private void SetAccentColor(string color)
    {
        CustomAccentColor = color;
        // 如果不使用系统强调色，则更新当前强调色
        if (!UseSystemAccentColor)
        {
            AccentColor = color;
        }
    }
    
    [RelayCommand]
    private void ResetToDefaults()
    {
        var defaultSettings = new SettingsModel();

        _suppressSettingSideEffects = true;
        try
        {
            SettingsService.SaveGeneral(defaultSettings);
            Settings = SettingsService.General;

            DarkMode = defaultSettings.DarkMode;
            UseSystemAccentColor = defaultSettings.UseSystemAccentColor;
            CustomAccentColor = defaultSettings.AccentColor;
            AccentColor = defaultSettings.AccentColor;
            ShowNotifications = defaultSettings.ShowNotifications;
            NotificationDurationSeconds = defaultSettings.NotificationDurationMs / 1000.0;
            NotificationSound = defaultSettings.NotificationSound;
            Language = defaultSettings.Language;
        }
        finally
        {
            _suppressSettingSideEffects = false;
        }

        ApplyThemeChange(DarkMode);

        var appliedAccent = UseSystemAccentColor ? GetSystemAccentColor() : CustomAccentColor;
        if (AccentColor != appliedAccent)
        {
            AccentColor = appliedAccent;
        }
        ApplyAccentColorChange(AccentColor);
        RefreshAccentColorSelection();

        NotificationService.Instance.UpdateNotificationSettings(ShowNotifications);
        ApplyLanguageChange(Language, false);
        
        // 异步显示重置成功通知
        _ = NotificationService.Instance.ShowSuccessAsync("设置已重置", "所有设置已恢复为默认值");
    }

    [RelayCommand]
    private async Task RestartApp()
    {
        try
        {
            // 重启前尽量备份最新设置，但最多等 1 秒，避免后台数据验证/备份未完成时长时间卡顿。
            // 备份失败或超时都不影响重启（新实例启动时会再次验证恢复数据）。
            try
            {
                var syncTask = DataProtectionService.Instance.SyncToAppDataAsync();
                var completed = await Task.WhenAny(syncTask, Task.Delay(1000));
                if (completed != syncTask)
                {
                    // 超时：放弃等待，继续重启
                }
            }
            catch
            {
                // 忽略备份失败，继续重启
            }

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                var arg0 = Environment.GetCommandLineArgs().FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(arg0))
                {
                    exePath = Path.GetFullPath(arg0);
                }
            }

            if (string.IsNullOrWhiteSpace(exePath))
            {
                NotificationService.Instance.ShowError("无法重启：未找到程序路径", true);
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
            };

            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
            {
                if (string.Equals(arg, "--restart", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                psi.ArgumentList.Add(arg);
            }

            psi.ArgumentList.Add("--restart");

            System.Diagnostics.Process.Start(psi);

            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
            else
            {
                Environment.Exit(0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"重启失败: {ex.Message}");
            NotificationService.Instance.ShowError($"重启失败: {ex.Message}", true);
        }
    }
    
    private bool _disposed = false;
    
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 清理事件订阅
                if (LocalizationService.Instance != null)
                {
                    LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
                }

                if (Application.Current?.PlatformSettings != null)
                {
                    Application.Current.PlatformSettings.ColorValuesChanged -= OnSystemColorValuesChanged;
                }
            }
            
            _disposed = true;
        }
        
        // 调用基类的Dispose方法
        base.Dispose(disposing);
    }
}

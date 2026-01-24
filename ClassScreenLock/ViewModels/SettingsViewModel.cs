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
    private double _fontSize = 14.0;
    
    [ObservableProperty]
    private string _fontFamily = "Microsoft YaHei UI";
    
    [ObservableProperty]
    private bool _autoStart = false;
    
    [ObservableProperty]
    private bool _darkMode = false;
    
    [ObservableProperty]
    private string _accentColor = "#0078D4";
    
    [ObservableProperty]
    private bool _showNotifications = true;

    [ObservableProperty]
    private int _notificationPositionIndex = 0;

    [ObservableProperty]
    private int _weeklyCycleCount = 4;

    [ObservableProperty]
    private DateTime? _termStartDate;
    
    [ObservableProperty]
    private string _language = "zh-CN";
    
    [ObservableProperty]
    private bool _useSystemAccentColor = false;
    
    [ObservableProperty]
    private string _customAccentColor = "#0078D4";
    
    [ObservableProperty]
    private ObservableCollection<string> _availableFontFamilies = new();
    
    public List<string> AvailableLanguages { get; private set; } = new List<string>();
    public List<string> AvailableAccentColors { get; private set; } = new List<string>();
    
    public SettingsViewModel()
    {
        LoadAvailableFontFamilies();
        LoadSettings();
        
        // 订阅语言变化事件
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
        
        // 订阅系统颜色变化事件
        if (Application.Current?.PlatformSettings != null)
        {
            Application.Current.PlatformSettings.ColorValuesChanged += OnSystemColorValuesChanged;
        }
    }

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
    
    private void LoadAvailableFontFamilies()
    {
        // 获取系统字体
        var systemFonts = new List<string>();
        
        try
        {
            // 使用 Avalonia 的 FontManager 获取系统字体，这是最可靠且跨平台的方式
            var installedFonts = Avalonia.Media.FontManager.Current.SystemFonts;
            foreach (var font in installedFonts)
            {
                if (!string.IsNullOrEmpty(font.Name))
                {
                    systemFonts.Add(font.Name);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"使用 FontManager 获取系统字体失败: {ex.Message}");
            
            // 回退到常用字体列表
            var commonFonts = new List<string>
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimSun",
                "SimHei",
                "Arial",
                "Segoe UI"
            };
            systemFonts.AddRange(commonFonts);
        }
        
        // 去重并过滤掉不支持的字体（华文、隶书）
        var unsupportedKeywords = new[] { "华文", "隶书", "STHupo", "STXingkai", "STXinwei", "STLiti", "STXihei", "STKaiti", "STSong", "STFangsong", "STCaiyun", "LiSu" };
        
        var fonts = systemFonts
            .Distinct()
            .Where(f => !unsupportedKeywords.Any(k => f.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f)
            .ToList();
            
        AvailableFontFamilies.Clear();
        foreach (var font in fonts)
        {
            AvailableFontFamilies.Add(font);
        }
    }
    
    private void LoadSettings()
    {
        Settings = SettingsService.General;
        
        // 初始化属性
        FontSize = Settings.FontSize;
        
        // 检查加载的字体是否在禁用列表中
        var unsupportedKeywords = new[] { "华文", "隶书", "STHupo", "STXingkai", "STXinwei", "STLiti", "STXihei", "STKaiti", "STSong", "STFangsong", "STCaiyun", "LiSu" };
        if (unsupportedKeywords.Any(k => Settings.FontFamily.Contains(k, StringComparison.OrdinalIgnoreCase)))
        {
            FontFamily = "Microsoft YaHei UI"; // 回退到安全默认字体
            UpdateSetting(s => s.FontFamily = FontFamily);
        }
        else
        {
            FontFamily = Settings.FontFamily;
        }
        AutoStart = Settings.AutoStart;
        DarkMode = Settings.DarkMode;
        AccentColor = Settings.AccentColor;
        ShowNotifications = Settings.ShowNotifications;
        Language = Settings.Language;
        UseSystemAccentColor = Settings.UseSystemAccentColor;
        CustomAccentColor = Settings.AccentColor; // 初始化为当前强调色
        NotificationPositionIndex = (int)Settings.NotificationPosition;
        WeeklyCycleCount = Settings.WeeklyCycleCount;
        TermStartDate = Settings.TermStartDate;
        
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
            "#0078D4", // 默认蓝色
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
    }
    
    partial void OnFontSizeChanged(double value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.FontSize = value);
        // 立即应用字体大小更改
        ApplyFontSizeChange(value);
    }
    
    partial void OnFontFamilyChanged(string value)
    {
        if (_suppressSettingSideEffects) return;
        ApplyFontFamilyChange(value);
    }
    
    partial void OnAutoStartChanged(bool value)
    {
        if (_suppressSettingSideEffects) return;
        UpdateSetting(s => s.AutoStart = value);
        // 立即应用自启动更改
        SetAutoStart(value);
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
        return "#0078D4";
    }
    
    partial void OnShowNotificationsChanged(bool value)
    {
        UpdateSetting(s => s.ShowNotifications = value);
        NotificationService.Instance.UpdateNotificationSettings(value);
    }

    partial void OnNotificationPositionIndexChanged(int value)
    {
        UpdateSetting(s => s.NotificationPosition = (NotificationPosition)value);
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
    }
    
    private void ApplyFontSizeChange(double fontSize)
    {
        // 应用字体大小到应用程序
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = desktop.MainWindow;
            if (mainWindow != null)
            {
                // 应用到主窗口
                mainWindow.FontSize = fontSize;
                
                // 递归应用到所有子控件
                ApplyFontSizeToChildren(mainWindow, fontSize);
            }
        }
    }
    
    private void ApplyFontSizeToChildren(Control parent, double fontSize, HashSet<Control>? visitedControls = null)
    {
        if (parent == null) return;
        
        // 初始化已访问控件集合
        visitedControls ??= new HashSet<Control>();
        
        // 如果已访问过此控件，跳过以防止循环引用
        if (visitedControls.Contains(parent))
            return;
            
        // 将当前控件添加到已访问集合
        visitedControls.Add(parent);
        
        try
        {
            // 应用到当前控件
            if (parent is TextBlock textBlock)
            {
                textBlock.FontSize = fontSize;
            }
            else if (parent is Button button)
            {
                button.FontSize = fontSize;
            }
            else if (parent is TextBox textBox)
            {
                textBox.FontSize = fontSize;
            }
            else if (parent is ComboBox comboBox)
            {
                comboBox.FontSize = fontSize;
            }
            else if (parent is CheckBox checkBox)
            {
                checkBox.FontSize = fontSize;
            }
            else if (parent is RadioButton radioButton)
            {
                radioButton.FontSize = fontSize;
            }
            else if (parent is ToggleSwitch toggleSwitch)
            {
                toggleSwitch.FontSize = fontSize;
            }
            else if (parent is Slider slider)
            {
                slider.FontSize = fontSize;
            }
            else if (parent is HeaderedContentControl headeredContent)
            {
                headeredContent.FontSize = fontSize;
            }
            
            // 递归应用到子控件
            if (parent is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Control childControl)
                    {
                        ApplyFontSizeToChildren(childControl, fontSize, visitedControls);
                    }
                }
            }
            else if (parent is ContentControl contentControl && contentControl.Content is Control content)
            {
                ApplyFontSizeToChildren(content, fontSize, visitedControls);
            }
            else if (parent is ItemsControl itemsControl)
            {
                foreach (var item in itemsControl.Items)
                {
                    if (item is Control itemControl)
                    {
                        ApplyFontSizeToChildren(itemControl, fontSize, visitedControls);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"应用字体大小到控件时出错: {ex.Message}");
        }
    }
    
    private void ApplyFontFamilyChange(string fontFamily)
    {
        UpdateSetting(s => s.FontFamily = fontFamily);
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

            FontSize = defaultSettings.FontSize;
            FontFamily = defaultSettings.FontFamily;
            AutoStart = defaultSettings.AutoStart;
            DarkMode = defaultSettings.DarkMode;
            UseSystemAccentColor = defaultSettings.UseSystemAccentColor;
            CustomAccentColor = defaultSettings.AccentColor;
            AccentColor = defaultSettings.AccentColor;
            ShowNotifications = defaultSettings.ShowNotifications;
            Language = defaultSettings.Language;
            NotificationPositionIndex = (int)defaultSettings.NotificationPosition;
        }
        finally
        {
            _suppressSettingSideEffects = false;
        }

        ApplyFontSizeChange(FontSize);
        ApplyFontFamilyChange(FontFamily);
        ApplyThemeChange(DarkMode);

        var appliedAccent = UseSystemAccentColor ? GetSystemAccentColor() : CustomAccentColor;
        if (AccentColor != appliedAccent)
        {
            AccentColor = appliedAccent;
        }
        ApplyAccentColorChange(AccentColor);

        SetAutoStart(AutoStart);
        NotificationService.Instance.UpdateNotificationSettings(ShowNotifications);
        ApplyLanguageChange(Language, false);
        
        // 异步显示重置成功通知
        _ = NotificationService.Instance.ShowSuccessAsync("设置已重置", "所有设置已恢复为默认值");
    }

    [RelayCommand]
    private void RestartApp()
    {
        try
        {
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
    
    private void SetAutoStart(bool enable)
    {
        try
        {
            // 仅在Windows平台上设置开机自启动
            if (OperatingSystem.IsWindows())
            {
#if WINDOWS
                // 获取应用程序路径
                var appPath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(appPath))
                {
                    appPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
                var appName = "ClassScreenLock";
                
                // 获取注册表路径
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
                
                if (enable)
                {
                    // 添加到启动项
                    key?.SetValue(appName, $"\"{appPath}\"");
                }
                else
                {
                    // 从启动项移除
                    key?.DeleteValue(appName, false);
                }
#endif
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置开机自启动失败: {ex.Message}");
        }
    }
    
    private bool _disposed = false;
    
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 释放托管资源
                AvailableFontFamilies?.Clear();
                
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

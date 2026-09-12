using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

/// <summary>
/// USB 密钥登录内容（作为 ContentDialog 的 Content 使用）。
/// 遮罩、弹出动画、阴影均由 ContentDialog 提供，与删除确认对话框一致。
/// </summary>
public partial class UsbKeyLoginDialog : UserControl
{
    private readonly TaskCompletionSource<string?> _tcs = new();
    public Task<string?> Result => _tcs.Task;

    /// <summary>设备被选中时触发（参数为盘符，null 表示取消）</summary>
    public event Action<string?>? DriveSelected;

    private readonly List<UsbDriveItem> _drives = new();

    public UsbKeyLoginDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ScanDrives();
    }

    private void ScanDrives()
    {
        _drives.Clear();
        var messageText = this.FindControl<TextBlock>("MessageText");

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Removable || !drive.IsReady) continue;

                var letter = drive.Name.TrimEnd('\\', ':');
                var label = drive.VolumeLabel ?? LocalizationService.Instance.GetString("UsbKeyLogin_RemovableDisk");
                _drives.Add(new UsbDriveItem
                {
                    DriveLetter = letter,
                    Display = $"{letter}: ({label})"
                });
            }

            if (_drives.Count == 0 && messageText != null)
            {
                messageText.Text = LocalizationService.Instance.GetString("UsbKeyLogin_NoDriveFound");
                messageText.IsVisible = true;
            }
        }
        catch
        {
            if (messageText != null)
            {
                messageText.Text = LocalizationService.Instance.GetString("UsbKeyLogin_ScanError");
                messageText.IsVisible = true;
            }
        }

        var listControl = this.FindControl<ItemsControl>("UsbDriveList");
        if (listControl != null)
            listControl.ItemsSource = _drives;
    }

    private void OnDriveSelected(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control) return;
        if (control.DataContext is not UsbDriveItem item) return;

        _tcs.TrySetResult(item.DriveLetter);
        DriveSelected?.Invoke(item.DriveLetter);
    }

    /// <summary>取消登录（由 ContentDialog 关闭时调用）</summary>
    public void Cancel()
    {
        _tcs.TrySetResult(null);
        DriveSelected?.Invoke(null);
    }
}

public class UsbDriveItem
{
    public string DriveLetter { get; set; } = "";
    public string Display { get; set; } = "";
}

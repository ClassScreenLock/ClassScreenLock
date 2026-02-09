using System;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class SplashWindow : Window
{
    private TextBlock? _versionText;
    private ProgressBar? _loadingProgressBar;
    private TextBlock? _statusText;

    public SplashWindow()
    {
        InitializeComponent();
        Opened += (_, _) => Start();
        Closed += (_, _) => Stop();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Start()
    {
        _versionText ??= this.FindControl<TextBlock>("VersionText");
        _loadingProgressBar ??= this.FindControl<ProgressBar>("LoadingProgressBar");
        _statusText ??= this.FindControl<TextBlock>("StatusText");

        SetProgress(null, "正在启动…");

        if (_versionText != null)
        {
            var aboutVersion = new AboutViewModel().AppVersion;
            _versionText.Text = string.IsNullOrWhiteSpace(aboutVersion)
                ? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty
                : aboutVersion;
        }
    }

    private void Stop()
    {
        _versionText = null;
        _loadingProgressBar = null;
        _statusText = null;
    }

    public void SetProgress(double? percent, string? statusText)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetProgress(percent, statusText));
            return;
        }

        if (_statusText != null && !string.IsNullOrWhiteSpace(statusText))
        {
            _statusText.Text = statusText;
        }

        if (_loadingProgressBar == null) return;

        if (percent.HasValue)
        {
            _loadingProgressBar.IsIndeterminate = false;
            _loadingProgressBar.Value = Math.Clamp(percent.Value, 0, 100);
        }
        else
        {
            _loadingProgressBar.IsIndeterminate = true;
        }
    }
}

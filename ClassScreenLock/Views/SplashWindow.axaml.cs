using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace ClassScreenLock.Views;

public partial class SplashWindow : Window
{
    private TextBlock? _versionText;
    private TextBlock? _statusText;
    private TextBlock? _percentText;
    private Border? _progressIndicator;
    private double _currentProgress;
    private double _targetProgress;
    private double _windowWidth;
    private bool _isIndeterminate;
    private DispatcherTimer? _indeterminateTimer;
    private double _indeterminatePosition;
    private bool _indeterminateDirection = true;

    public SplashWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _windowWidth = Bounds.Width;
        Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Stop();
    }

    private void Start()
    {
        _versionText ??= this.FindControl<TextBlock>("VersionText");
        _statusText ??= this.FindControl<TextBlock>("StatusText");
        _percentText ??= this.FindControl<TextBlock>("PercentText");
        _progressIndicator ??= this.FindControl<Border>("ProgressIndicator");

        SetProgress(null, "正在启动…");

        if (_versionText != null)
        {
            var aboutVersion = new ViewModels.AboutViewModel().AppVersion;
            _versionText.Text = string.IsNullOrWhiteSpace(aboutVersion)
                ? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty
                : aboutVersion;
        }
    }

    private void Stop()
    {
        _versionText = null;
        _statusText = null;
        _percentText = null;
        _progressIndicator = null;
        
        if (_indeterminateTimer != null)
        {
            _indeterminateTimer.Stop();
            _indeterminateTimer = null;
        }
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

        if (_progressIndicator == null) return;

        _windowWidth = Bounds.Width > 0 ? Bounds.Width : 560;

        if (percent.HasValue)
        {
            _isIndeterminate = false;
            StopIndeterminateAnimation();
            
            _targetProgress = Math.Clamp(percent.Value, 0, 100);
            
            if (_percentText != null)
            {
                _percentText.Text = $"{(int)_targetProgress}%";
            }
            
            AnimateToProgress();
        }
        else
        {
            if (_percentText != null)
            {
                _percentText.Text = "";
            }
            StartIndeterminateAnimation();
        }
    }

    private void AnimateToProgress()
    {
        if (_progressIndicator == null) return;
        
        var targetWidth = (_targetProgress / 100.0) * _windowWidth;
        _progressIndicator.Width = targetWidth;
        _progressIndicator.Margin = new Thickness(0, 0, 0, 0);
        _currentProgress = _targetProgress;
    }

    private void StartIndeterminateAnimation()
    {
        if (_isIndeterminate) return;
        
        _isIndeterminate = true;
        _indeterminatePosition = 0;
        _indeterminateDirection = true;
        
        _indeterminateTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnIndeterminateTick);
        _indeterminateTimer.Start();
    }

    private void StopIndeterminateAnimation()
    {
        _indeterminateTimer?.Stop();
        _isIndeterminate = false;
    }

    private void OnIndeterminateTick(object? sender, EventArgs e)
    {
        if (_progressIndicator == null || !_isIndeterminate)
        {
            StopIndeterminateAnimation();
            return;
        }

        var indicatorWidth = _windowWidth * 0.3;
        var speed = _windowWidth * 0.015;

        if (_indeterminateDirection)
        {
            _indeterminatePosition += speed;
            if (_indeterminatePosition + indicatorWidth >= _windowWidth)
            {
                _indeterminatePosition = _windowWidth - indicatorWidth;
                _indeterminateDirection = false;
            }
        }
        else
        {
            _indeterminatePosition -= speed;
            if (_indeterminatePosition <= 0)
            {
                _indeterminatePosition = 0;
                _indeterminateDirection = true;
            }
        }

        _progressIndicator.Width = indicatorWidth;
        _progressIndicator.Margin = new Thickness(_indeterminatePosition, 0, 0, 0);
    }
}

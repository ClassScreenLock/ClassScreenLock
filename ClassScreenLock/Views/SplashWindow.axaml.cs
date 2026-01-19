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
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private RotateTransform? _rotate;
    private Canvas? _dotRingCanvas;
    private TextBlock? _versionText;

    public SplashWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => Tick();

        Opened += (_, _) => Start();
        Closed += (_, _) => Stop();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void Start()
    {
        _dotRingCanvas ??= this.FindControl<Canvas>("DotRingCanvas");
        _versionText ??= this.FindControl<TextBlock>("VersionText");

        if (_dotRingCanvas?.RenderTransform is RotateTransform rotate)
        {
            _rotate = rotate;
        }

        _stopwatch.Restart();
        _timer.Start();

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
        _timer.Stop();
        _rotate = null;
        _dotRingCanvas = null;
        _versionText = null;
    }

    private void Tick()
    {
        var rotate = _rotate;
        if (rotate == null)
        {
            return;
        }

        var elapsed = _stopwatch.Elapsed.TotalMilliseconds;
        var t = (elapsed % 1200.0) / 1200.0;
        rotate.Angle = t * 360.0;
    }
}

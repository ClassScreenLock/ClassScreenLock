using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;

namespace ClassScreenLock.Services;

public class RippleEffectService
{
    private static RippleEffectService? _instance;
    public static RippleEffectService Instance => _instance ??= new RippleEffectService();

    private readonly object _gate = new();
    private readonly List<Ellipse> _pool = new();
    private readonly List<bool> _busy = new();
    private Canvas? _overlay;
    private Window? _mainWindow;
    private bool _attached;
    private double _avgFrameMs = 16.0;
    private const int MaxPoolSize = 24;

    private RippleEffectService() { }

    public void Attach(Window window)
    {
        if (_attached) return;
        _mainWindow = window;
        _overlay = window.FindControl<Canvas>("RippleOverlay");
        if (_overlay == null)
        {
            var root = window.Content as Control;
            _overlay = new Canvas
            {
                IsHitTestVisible = false,
                Background = Brushes.Transparent
            };
            if (root is Panel panel)
            {
                panel.Children.Add(_overlay);
            }
        }

        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        _attached = true;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_overlay == null || _mainWindow == null) return;
        var p = e.GetPosition(_overlay);
        _ = PlayRippleAsync(p);
        // 不标记为已处理，避免干扰点击
    }

    private async Task PlayRippleAsync(Point p)
    {
        if (_overlay == null) return;

        var accentObj = Application.Current?.Resources?["AccentColor"];
        var accent = accentObj is Color c ? c : Colors.DodgerBlue;
        var startRadius = 10;
        var targetRadius = IsLowFps() ? 150 : 200;
        var durationMs = IsLowFps() ? 240 : 330;
        var steps = Math.Max(12, (int)(durationMs / 16));
        var gradient = new RadialGradientBrush
        {
            GradientStops = new GradientStops
            {
                new GradientStop(new Color(100, accent.R, accent.G, accent.B), 0),
                new GradientStop(new Color(0, accent.R, accent.G, accent.B), 1)
            }
        };

        var ellipse = Acquire();
        ellipse.Fill = gradient;
        ellipse.Stroke = null;
        ellipse.IsVisible = true;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var eased = 1 - Math.Pow(1 - t, 3); // ease-out cubic
            var r = startRadius + (targetRadius - startRadius) * eased;
            var size = r * 2;
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                ellipse.Width = size;
                ellipse.Height = size;
                Canvas.SetLeft(ellipse, p.X - r);
                Canvas.SetTop(ellipse, p.Y - r);
                ellipse.Opacity = 0.25 * (1 - t);
            });
            var delayMs = 16;
            await Task.Delay(delayMs).ConfigureAwait(false);
        }
        sw.Stop();
        UpdateAvgFrame(sw.ElapsedMilliseconds / steps);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            ellipse.IsVisible = false;
        });
        Release(ellipse);
    }

    private Ellipse Acquire()
    {
        lock (_gate)
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (!_busy[i])
                {
                    _busy[i] = true;
                    return _pool[i];
                }
            }
            var ell = new Ellipse { IsVisible = false };
            _overlay?.Children.Add(ell);
            _pool.Add(ell);
            _busy.Add(true);
            // 限制池大小
            if (_pool.Count > MaxPoolSize)
            {
                // 复用最早一个（清理其属性）
                _busy[0] = true;
                return _pool[0];
            }
            return ell;
        }
    }

    private void Release(Ellipse ell)
    {
        lock (_gate)
        {
            var idx = _pool.IndexOf(ell);
            if (idx >= 0) _busy[idx] = false;
        }
    }

    private void UpdateAvgFrame(double frameMs)
    {
        _avgFrameMs = (_avgFrameMs * 0.85) + (frameMs * 0.15);
    }

    private bool IsLowFps()
    {
        return _avgFrameMs > 16.7; // 约 <60fps
    }
}

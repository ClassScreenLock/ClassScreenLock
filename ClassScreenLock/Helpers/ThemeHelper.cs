using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Styling;
using ClassScreenLock.Extensions;
using System.Collections.Generic;

namespace ClassScreenLock.Helpers
{
    public static class ThemeHelper
    {
        public static void ApplyAccentColor(string accentColorHex)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (Color.TryParse(accentColorHex, out var color))
                    {
                    var accentBrush = new SolidColorBrush(color);
                    
                    var light1 = color.LightenPercent(15);
                    var light2 = color.LightenPercent(30);
                    var light3 = color.LightenPercent(45);
                    var dark1 = color.LightenPercent(-15);
                    var dark2 = color.LightenPercent(-30);
                    var dark3 = color.LightenPercent(-45);
                    
                    var light1Brush = new SolidColorBrush(light1);
                    var light2Brush = new SolidColorBrush(light2);
                    var light3Brush = new SolidColorBrush(light3);
                    var dark1Brush = new SolidColorBrush(dark1);
                    var dark2Brush = new SolidColorBrush(dark2);
                    var dark3Brush = new SolidColorBrush(dark3);
                    
                    if (Application.Current?.Resources != null)
                    {
                        // 1. 更新基础颜色资源
                        Application.Current.Resources["SystemAccentColor"] = color;
                        Application.Current.Resources["SystemAccentColorLight1"] = light1;
                        Application.Current.Resources["SystemAccentColorLight2"] = light2;
                        Application.Current.Resources["SystemAccentColorLight3"] = light3;
                        Application.Current.Resources["SystemAccentColorDark1"] = dark1;
                        Application.Current.Resources["SystemAccentColorDark2"] = dark2;
                        Application.Current.Resources["SystemAccentColorDark3"] = dark3;

                        // 2. 更新 SystemControl 相关的画刷资源
                        Application.Current.Resources["SystemControlBackgroundAccentBrush"] = accentBrush;
                        Application.Current.Resources["SystemControlForegroundAccentBrush"] = accentBrush;
                        Application.Current.Resources["SystemControlHighlightAccentBrush"] = accentBrush;
                        Application.Current.Resources["SystemControlHighlightAltAccentBrush"] = accentBrush;
                        Application.Current.Resources["SystemControlHighlightBaseMediumLowAccentBrush"] = accentBrush;
                        Application.Current.Resources["SystemControlHighlightListAccentLowBrush"] = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B));
                        Application.Current.Resources["SystemControlHighlightListAccentMediumBrush"] = new SolidColorBrush(Color.FromArgb(80, color.R, color.G, color.B));
                        Application.Current.Resources["SystemControlHighlightListAccentHighBrush"] = new SolidColorBrush(Color.FromArgb(120, color.R, color.G, color.B));
                        
                        Application.Current.Resources["SystemControlBackgroundAccentDarkBrush"] = dark1Brush;
                        Application.Current.Resources["SystemControlForegroundAccentDarkBrush"] = dark1Brush;
                        Application.Current.Resources["SystemControlBackgroundAccentDarkerBrush"] = dark2Brush;
                        Application.Current.Resources["SystemControlForegroundAccentDarkerBrush"] = dark2Brush;
                        
                        // 补充常用系统画刷
                        Application.Current.Resources["ContentControlBorderBrushFocused"] = accentBrush;
                        Application.Current.Resources["ProgressBarFillBrush"] = accentBrush;
                        Application.Current.Resources["SliderThumbBackgroundBrush"] = accentBrush;
                        Application.Current.Resources["SliderTrackValueFill"] = accentBrush;
                        Application.Current.Resources["ToggleSwitchFillOn"] = accentBrush;
                        Application.Current.Resources["ToggleSwitchFillOnPointerOver"] = light1Brush;
                        Application.Current.Resources["ToggleSwitchFillOnPressed"] = dark1Brush;
                        Application.Current.Resources["ToggleSwitchKnobFillOn"] = new SolidColorBrush(Colors.White);

                        // 3. 更新自定义资源
                        Application.Current.Resources["AccentColor"] = color;
                        Application.Current.Resources["AccentBrush"] = accentBrush;
                        Application.Current.Resources["DarkAccentBrush"] = dark1Brush;
                        Application.Current.Resources["DarkerAccentBrush"] = dark2Brush;
                    }
                    
                    // 强制更新UI
                    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var mainWindow = desktop.MainWindow;
                        if (mainWindow != null)
                        {
                            mainWindow.InvalidateMeasure();
                            mainWindow.InvalidateVisual();
                            RefreshAccentBrushes(mainWindow);
                        }
                    }
                }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ApplyAccentColor Error: {ex.Message}");
                }
            });
        }

        public static async System.Threading.Tasks.Task ApplyThemeCircularReveal(bool isDark, Point? center = null, double durationMs = 350)
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null) return;

                var accentObj = Application.Current.Resources?["AccentColor"];
                var accentColor = accentObj is Color c ? c : (isDark ? Colors.White : Colors.Black);
                var overlayColor = Color.FromArgb(64, accentColor.R, accentColor.G, accentColor.B); // ~25% 透明度
                var newBrush = (IBrush)new SolidColorBrush(overlayColor);

                var bounds = mainWindow.Bounds;
                var w = bounds.Width;
                var h = bounds.Height;
                var cx = center?.X ?? w / 2.0;
                var cy = center?.Y ?? h / 2.0;
                var maxRadius = Math.Sqrt(w * w + h * h);

                var overlay = new Window
                {
                    Background = Brushes.Transparent,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Width = w,
                    Height = h,
                };
                try { overlay.SystemDecorations = SystemDecorations.None; } catch { }

                // 对齐到主窗体位置
                try { overlay.Position = mainWindow.Position; } catch { }

                var canvas = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
                var ellipse = new Ellipse { Fill = newBrush, Width = 0, Height = 0, Opacity = 1.0 };
                Canvas.SetLeft(ellipse, cx);
                Canvas.SetTop(ellipse, cy);
                canvas.Children.Add(ellipse);
                overlay.Content = canvas;
                overlay.Show();

                var steps = Math.Max(20, (int)(durationMs / 16));
                bool themeApplied = false;
                for (int i = 0; i <= steps; i++)
                {
                    var t = i / (double)steps; // 0..1
                    // ease out quad
                    var eased = 1 - (1 - t) * (1 - t);
                    var r = eased * maxRadius;
                    var size = r * 2;
                    ellipse.Width = size;
                    ellipse.Height = size;
                    Canvas.SetLeft(ellipse, cx - r);
                    Canvas.SetTop(ellipse, cy - r);
                    // 当圆基本覆盖全屏后再切换主题，避免看到瞬时切换
                    if (!themeApplied && r >= maxRadius * 0.85)
                    {
                        Application.Current.RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
                        themeApplied = true;
                    }
                    // 覆盖后再轻微淡出
                    if (themeApplied)
                    {
                        var fadeStart = 0.9; // 在动画末尾阶段淡出
                        var fadeT = Math.Max(0, (t - fadeStart) / (1 - fadeStart));
                        ellipse.Opacity = Math.Max(0, 1.0 - fadeT);
                    }
                    await System.Threading.Tasks.Task.Delay(16);
                }

                overlay.Close();
            }
            catch { }
        }

        public static async System.Threading.Tasks.Task PlayViewCircularReveal(Point? center = null, double durationMs = 320)
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null) return;

                var overlay = mainWindow.FindControl<Canvas>("RippleOverlay");
                if (overlay == null)
                {
                    if (mainWindow.Content is Control root && root is Panel panel)
                    {
                        overlay = new Canvas { Background = Brushes.Transparent, IsHitTestVisible = false };
                        panel.Children.Add(overlay);
                    }
                    else
                    {
                        return;
                    }
                }

                var accentObj = Application.Current.Resources?[(object)"AccentColor"];
                var accentColor = accentObj is Color c ? c : Colors.Black;
                var overlayColor = Color.FromArgb(96, accentColor.R, accentColor.G, accentColor.B);
                var brush = (IBrush)new SolidColorBrush(overlayColor);

                var bounds = mainWindow.Bounds;
                var w = bounds.Width;
                var h = bounds.Height;
                var cx = center?.X ?? w / 2.0;
                var cy = center?.Y ?? h / 2.0;
                var maxRadius = Math.Sqrt(w * w + h * h);

                var ellipse = new Ellipse { Fill = brush, Width = 0, Height = 0, Opacity = 1.0, IsVisible = true };
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Children.Add(ellipse);
                });

                var steps = Math.Max(20, (int)(durationMs / 16));
                for (int i = 0; i <= steps; i++)
                {
                    var t = i / (double)steps;
                    var eased = 1 - (1 - t) * (1 - t);
                    var r = eased * maxRadius;
                    var size = r * 2;
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ellipse.Width = size;
                        ellipse.Height = size;
                        Canvas.SetLeft(ellipse, cx - r);
                        Canvas.SetTop(ellipse, cy - r);
                        var fadeStart = 0.88;
                        var fadeT = Math.Max(0, (t - fadeStart) / (1 - fadeStart));
                        ellipse.Opacity = Math.Max(0, 1.0 - fadeT);
                    });
                    await System.Threading.Tasks.Task.Delay(16).ConfigureAwait(false);
                }

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    overlay.Children.Remove(ellipse);
                });
            }
            catch { }
        }

        public static async System.Threading.Tasks.Task PlayStepTransitionFade(double peakOpacity = 0.08, double durationMs = 220)
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;
                var mainWindow = desktop.MainWindow;
                if (mainWindow == null) return;

                var w = mainWindow.Bounds.Width;
                var h = mainWindow.Bounds.Height;
                var accent = Application.Current.Resources?["AccentBrush"] as IBrush ?? new SolidColorBrush(Colors.Gray);

                var overlay = new Window
                {
                    Background = Brushes.Transparent,
                    CanResize = false,
                    ShowInTaskbar = false,
                    Topmost = true,
                    TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                    Width = w,
                    Height = h,
                };
                try { overlay.SystemDecorations = SystemDecorations.None; } catch { }
                try { overlay.Position = mainWindow.Position; } catch { }

                var border = new Border
                {
                    Background = accent,
                    Opacity = 0,
                };
                overlay.Content = border;
                overlay.Show();

                var steps = Math.Max(16, (int)(durationMs / 16));
                for (int i = 0; i <= steps; i++)
                {
                    var t = i / (double)steps;
                    border.Opacity = t <= 0.5 ? peakOpacity * (t / 0.5) : peakOpacity * (1 - (t - 0.5) / 0.5);
                    await System.Threading.Tasks.Task.Delay(16);
                }

                overlay.Close();
            }
            catch { }
        }

        public static async System.Threading.Tasks.Task SlideControlHorizontal(Control target, double from, double to, double durationMs = 280)
        {
            try
            {
                var steps = Math.Max(16, (int)(durationMs / 16));
                var dx = to - from;
                for (int i = 0; i <= steps; i++)
                {
                    var t = i / (double)steps;
                    // ease out cubic
                    var eased = 1 - Math.Pow(1 - t, 3);
                    var x = from + dx * eased;
                    target.RenderTransform = new TranslateTransform(x, 0);
                    await System.Threading.Tasks.Task.Delay(16);
                }
                target.RenderTransform = new TranslateTransform(to, 0);
            }
            catch { }
        }

        private static void RefreshAccentBrushes(Control parent, HashSet<Control>? visitedControls = null)
        {
            if (parent == null) return;
            visitedControls ??= new HashSet<Control>();
            if (visitedControls.Contains(parent)) return;
            visitedControls.Add(parent);

            try
            {
                if (parent is Panel panel)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is Control childControl) RefreshAccentBrushes(childControl, visitedControls);
                    }
                }
                else if (parent is ContentControl contentControl && contentControl.Content is Control content)
                {
                    RefreshAccentBrushes(content, visitedControls);
                }
                else if (parent is ItemsControl itemsControl)
                {
                    foreach (var item in itemsControl.Items)
                    {
                        if (item is Control itemControl) RefreshAccentBrushes(itemControl, visitedControls);
                    }
                }
                
                parent.InvalidateVisual();
            }
            catch { }
        }
    }
}

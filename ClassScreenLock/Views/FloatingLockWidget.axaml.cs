using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassScreenLock.Services;

namespace ClassScreenLock.Views;

public partial class FloatingLockWidget : Window
{
    public FloatingLockWidget()
    {
        InitializeComponent();

        Opened += OnOpened;

        var screens = Screens;
        if (screens != null)
        {
            var primaryScreen = screens.Primary;
            if (primaryScreen != null)
            {
                var workingArea = primaryScreen.WorkingArea;
                int x = workingArea.X + 20;
                int y = workingArea.Y + workingArea.Height - 220;
                Position = new PixelPoint(x, y);
            }
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            LogService.Instance.Log("UI", "FloatingWidgetOpened", "FloatingLockWidget", "Floating lock widget opened");
        }
        catch
        {
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            LogService.Instance.Log("UI", "FloatingWidgetClosed", "FloatingLockWidget", "Floating lock widget closed");
        }
        catch
        {
        }
        base.OnClosed(e);
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using ClassScreenLock.ViewModels;
using System;
using System.ComponentModel;

namespace ClassScreenLock.Views;

public partial class ScreenshotHistoryView : UserControl
{
    private ScreenshotHistoryViewModel? _vm;

    public ScreenshotHistoryView()
    {
        InitializeComponent();
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ScreenshotHistoryViewModel vm) return;

        // Image Viewer keys are now handled by the ImageViewerWindow
        
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.A)
        {
            if (!vm.IsSelectionMode)
            {
                vm.ToggleSelectionModeCommand.Execute(null);
            }

            vm.SelectAllOnPageCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && vm.IsSelectionMode)
        {
            vm.ToggleSelectionModeCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete && vm.IsSelectionMode)
        {
            if (vm.DeleteSelectedCommand.CanExecute(null))
            {
                vm.DeleteSelectedCommand.Execute(null);
            }

            e.Handled = true;
        }
    }

    private void OnScreenshotItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ScreenshotHistoryViewModel vm) return;
        if (!vm.IsSelectionMode) return;
        if (sender is not Control control) return;

        var properties = e.GetCurrentPoint(control).Properties;
        if (!properties.IsLeftButtonPressed) return;

        if (control.DataContext is not ScreenshotItem item) return;

        var isShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        vm.HandleSelectionClick(item, isShiftPressed);
        e.Handled = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DataContextChanged += OnDataContextChanged;
        AttachViewModel(DataContext as ScreenshotHistoryViewModel);

        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        AttachViewModel(null);

        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel(DataContext as ScreenshotHistoryViewModel);
    }

    private void AttachViewModel(ScreenshotHistoryViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = vm;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScreenshotHistoryViewModel.IsImageViewerOpen))
        {
            if (_vm != null && _vm.IsImageViewerOpen)
            {
                var viewerWindow = new ImageViewerWindow
                {
                    DataContext = _vm
                };
                
                // 根据设置应用 dark 类
                if (SettingsService.General.DarkMode)
                {
                    viewerWindow.Classes.Add("dark");
                }
                
                viewerWindow.Show();
            }
        }
    }
}
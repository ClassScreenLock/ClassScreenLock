using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassScreenLock.ViewModels;
using System;
using System.ComponentModel;

namespace ClassScreenLock.Views;

public partial class ImageViewerWindow : Window
{
    private ScrollViewer? _imageScrollViewer;
    private IImageViewerViewModel? _vm;
    
    private bool _isDragging;
    private int? _activeDragPointerId;
    private Point _startPoint;
    private Vector _startPan;
    private bool _dragUsesPanTransform;
    
    private double _lastPinchScale = 1.0;
    private DateTime _lastPinchAtUtc = DateTime.MinValue;
    private DateTime _suppressDragUntilUtc = DateTime.MinValue;

    public ImageViewerWindow()
    {
        InitializeComponent();
#if DEBUG
        // DevTools is automatically attached by Avalonia in DEBUG mode when Avalonia.Diagnostics package is referenced
#endif
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        if (_imageScrollViewer != null)
        {
            _imageScrollViewer.AddHandler(Gestures.PinchEvent, OnPinch);
        }
        
        DataContextChanged += OnDataContextChanged;
        AttachViewModel(DataContext as IImageViewerViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_imageScrollViewer != null)
        {
            _imageScrollViewer.RemoveHandler(Gestures.PinchEvent, OnPinch);
        }
        
        DataContextChanged -= OnDataContextChanged;
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        // Try to get VM from DataContext if _vm is null (e.g. if detached already)
        var vm = _vm ?? DataContext as IImageViewerViewModel;
        if (vm != null && vm.IsImageViewerOpen)
        {
            vm.CloseImageViewerCommand.Execute(null);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        AttachViewModel(DataContext as IImageViewerViewModel);
    }

    private void AttachViewModel(IImageViewerViewModel? vm)
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
        
        ApplyFullscreenState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IImageViewerViewModel.IsMaximized))
        {
            ApplyFullscreenState();
        }
        else if (e.PropertyName == nameof(IImageViewerViewModel.IsImageViewerOpen))
        {
            if (_vm != null && !_vm.IsImageViewerOpen)
            {
                Close();
            }
        }
    }

    private void ApplyFullscreenState()
    {
        if (_vm == null) return;
        
        if (_vm.IsMaximized)
        {
            if (WindowState != WindowState.Maximized)
                WindowState = WindowState.Maximized;
        }
        else
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not IImageViewerViewModel vm) return;

        if (e.Key == Key.Escape)
        {
            vm.CloseImageViewerCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            vm.PreviousImageCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            vm.NextImageCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            vm.ToggleMaximizeCommand.Execute(null);
            e.Handled = true;
        }
    }
    
    // Reuse the pan/zoom logic from previous view
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_imageScrollViewer == null) return;

        var vm = DataContext as IImageViewerViewModel;
        var pointer = e.Pointer;
        var properties = e.GetCurrentPoint(_imageScrollViewer).Properties;
        var isMouseDrag = properties.IsLeftButtonPressed;
        var isTouchDrag = pointer.Type == PointerType.Touch || pointer.Type == PointerType.Pen;

        if (DateTime.UtcNow < _suppressDragUntilUtc) return;

        if (isMouseDrag || isTouchDrag)
        {
            if (vm == null) return;
            _isDragging = true;
            _dragUsesPanTransform = true;
            _activeDragPointerId = pointer.Id;
            _startPoint = e.GetPosition(_imageScrollViewer);
            _startPan = new Vector(vm.PanX, vm.PanY);
            e.Pointer.Capture(_imageScrollViewer);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _imageScrollViewer == null) return;
        if (_activeDragPointerId != e.Pointer.Id) return;
        if (DateTime.UtcNow < _suppressDragUntilUtc) return;

        var currentPoint = e.GetPosition(_imageScrollViewer);
        if (_dragUsesPanTransform)
        {
            if (DataContext is IImageViewerViewModel vm)
            {
                var delta = currentPoint - _startPoint;
                vm.PanX = _startPan.X + delta.X;
                vm.PanY = _startPan.Y + delta.Y;
            }
        }
        // 使用平移变换模式
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && _imageScrollViewer != null)
        {
            _isDragging = false;
            _activeDragPointerId = null;
            _dragUsesPanTransform = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (DataContext is IImageViewerViewModel vm && _imageScrollViewer != null)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastPinchAtUtc) > TimeSpan.FromMilliseconds(250))
            {
                _lastPinchScale = 1.0;
            }

            var scaleDelta = e.Scale <= 0 ? 1.0 : e.Scale / _lastPinchScale;
            _lastPinchScale = e.Scale;
            _lastPinchAtUtc = now;

            _suppressDragUntilUtc = now.AddMilliseconds(150);

            var origin = e.ScaleOrigin;
            ApplyZoomFactor(vm, scaleDelta, origin);
            e.Handled = true;
        }
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is IImageViewerViewModel vm && _imageScrollViewer != null)
        {
            var delta = e.Delta.Y;
            if (delta == 0) return;

            var factorPerNotch = 1.12;
            var zoomFactor = Math.Pow(factorPerNotch, delta);
            var origin = e.GetPosition(_imageScrollViewer);
            ApplyZoomFactor(vm, zoomFactor, origin);
            e.Handled = true;
        }
    }

    private void ApplyZoomFactor(IImageViewerViewModel vm, double zoomFactor, Point origin)
    {
        if (_imageScrollViewer == null) return;
        if (zoomFactor <= 0) return;

        var oldZoom = vm.ZoomLevel;
        var newZoom = Math.Clamp(oldZoom * zoomFactor, vm.ZoomMin, vm.ZoomMax);
        if (Math.Abs(newZoom - oldZoom) < 0.000001) return;

        var ratio = newZoom / oldZoom;
        var originVector = new Vector(origin.X, origin.Y);

        var oldPan = new Vector(vm.PanX, vm.PanY);
        var newPan = oldPan * ratio + originVector * (1 - ratio);

        vm.ZoomLevel = newZoom;
        vm.PanX = newPan.X;
        vm.PanY = newPan.Y;
    }
}

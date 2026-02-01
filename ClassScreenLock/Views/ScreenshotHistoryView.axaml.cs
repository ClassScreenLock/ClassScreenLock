using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ClassScreenLock.Models;
using ClassScreenLock.ViewModels;
using System;

namespace ClassScreenLock.Views;

public partial class ScreenshotHistoryView : UserControl
{
    private ScrollViewer? _imageScrollViewer;

    private bool _isDragging;
    private int? _activeDragPointerId;
    private Point _startPoint;
    private Vector _startOffset;

    private double _lastPinchScale = 1.0;
    private DateTime _lastPinchAtUtc = DateTime.MinValue;
    private DateTime _suppressDragUntilUtc = DateTime.MinValue;

    public ScreenshotHistoryView()
    {
        InitializeComponent();
    }

    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ScreenshotHistoryViewModel vm) return;

        if (vm.IsImageViewerOpen)
        {
            if (e.Key == Key.Escape)
            {
                vm.CloseImageViewerCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Left)
            {
                vm.PreviousImageCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.Right)
            {
                vm.NextImageCommand.Execute(null);
                e.Handled = true;
                return;
            }
            if (e.Key == Key.F11)
            {
                vm.ToggleMaximizeCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

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

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_imageScrollViewer == null) return;

        var pointer = e.Pointer;
        var properties = e.GetCurrentPoint(_imageScrollViewer).Properties;
        var isMouseDrag = properties.IsLeftButtonPressed;
        var isTouchDrag = pointer.Type == PointerType.Touch || pointer.Type == PointerType.Pen;

        if (DateTime.UtcNow < _suppressDragUntilUtc)
        {
            return;
        }

        if (isMouseDrag || isTouchDrag)
        {
            _isDragging = true;
            _activeDragPointerId = pointer.Id;
            _startPoint = e.GetPosition(_imageScrollViewer);
            _startOffset = _imageScrollViewer.Offset;
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
        var delta = _startPoint - currentPoint;
        
        _imageScrollViewer.Offset = _startOffset + delta;
        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDragging && _imageScrollViewer != null)
        {
            _isDragging = false;
            _activeDragPointerId = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _imageScrollViewer = this.FindControl<ScrollViewer>("ImageScrollViewer");
        if (_imageScrollViewer != null)
        {
            _imageScrollViewer.AddHandler(Gestures.PinchEvent, OnPinch);
        }

        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_imageScrollViewer != null)
        {
            _imageScrollViewer.RemoveHandler(Gestures.PinchEvent, OnPinch);
        }
        base.OnDetachedFromVisualTree(e);
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        if (DataContext is ScreenshotHistoryViewModel vm && _imageScrollViewer != null)
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
        if (DataContext is ScreenshotHistoryViewModel vm && _imageScrollViewer != null)
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

    private void ApplyZoomFactor(ScreenshotHistoryViewModel vm, double zoomFactor, Point origin)
    {
        if (_imageScrollViewer == null) return;
        if (zoomFactor <= 0) return;

        var oldZoom = vm.ZoomLevel;
        var newZoom = Math.Clamp(oldZoom * zoomFactor, ScreenshotHistoryViewModel.MinZoom, ScreenshotHistoryViewModel.MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 0.000001) return;

        var ratio = newZoom / oldZoom;
        var oldOffset = _imageScrollViewer.Offset;
        var originVector = new Vector(origin.X, origin.Y);
        var newOffset = (oldOffset + originVector) * ratio - originVector;

        vm.ZoomLevel = newZoom;
        _imageScrollViewer.Offset = newOffset;
    }
}

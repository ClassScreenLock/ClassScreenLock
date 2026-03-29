using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Services;
using System;

namespace ClassScreenLock.Views;

public partial class MainWindow : Window
{
    private bool _isClosing = false;
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        
        DataContextChanged += OnDataContextChanged;
        PropertyChanged += OnPropertyChanged;
        
        var titleBar = this.FindControl<Border>("TitleBar");
        if (titleBar != null)
        {
            titleBar.PointerPressed += OnTitleBarPointerPressed;
            titleBar.DoubleTapped += OnTitleBarDoubleTapped;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        WindowProtectionService.Instance.ApplyProtectionAsync(this);
    }
    
    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
        {
            _viewModel?.UpdateMaximizedState(WindowState == WindowState.Maximized);
        }
    }
    
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _viewModel = vm;
            vm.SetMainWindow(this);
        }
    }
    
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
    
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized 
            ? WindowState.Normal 
            : WindowState.Maximized;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        ClassScreenLock.Services.AccountService.Instance.Logout();
        
        if (!_isClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    public void RealClose()
    {
        _isClosing = true;
        Close();
    }
}
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class QuickActionEditorView : Window
{
    public QuickActionEditorView()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is QuickActionEditorViewModel vm)
        {
            vm.SaveCommand.Execute(null);
        }
        Close();
    }

    /// <summary>
    /// 标题栏拖动支持（无窗口装饰时）
    /// </summary>
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}

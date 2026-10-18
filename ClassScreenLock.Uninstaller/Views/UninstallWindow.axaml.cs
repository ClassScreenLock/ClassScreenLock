using Avalonia.Controls;
using Avalonia.Interactivity;
using ClassScreenLock.Uninstaller.ViewModels;

namespace ClassScreenLock.Uninstaller.Views;

public partial class UninstallWindow : Window
{
    public UninstallWindow()
    {
        InitializeComponent();

        // 订阅窗口关闭事件，卸载进行中时阻止关闭
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is UninstallViewModel vm)
        {
            if (!vm.TryCloseWindow())
            {
                // 卸载进行中，取消关闭操作
                e.Cancel = true;
            }
        }
    }
}

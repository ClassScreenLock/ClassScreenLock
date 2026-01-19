using System.ComponentModel;
using Avalonia.Controls;

namespace ClassScreenLock.Views;

public partial class MainWindow : Window
{
    private bool _isClosing = false;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 每次关闭主窗口时，登录令牌失效（退出登录）
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
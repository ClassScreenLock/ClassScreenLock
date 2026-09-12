using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClassScreenLock.Views;

/// <summary>
/// 管理员身份验证内容（作为 ContentDialog 的 Content 使用）。
/// 遮罩、弹出动画、阴影均由 ContentDialog 提供，与删除确认对话框一致。
/// </summary>
public partial class VerifyWindow : UserControl
{
    public VerifyWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

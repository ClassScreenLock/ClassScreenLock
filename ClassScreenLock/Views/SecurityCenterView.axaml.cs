using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClassScreenLock.Views;

public partial class SecurityCenterView : UserControl
{
    public SecurityCenterView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}


using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClassScreenLock.Views;

public partial class NetworkManagementView : UserControl
{
    public NetworkManagementView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ClassScreenLock.Views;

public partial class LockSettingsView : UserControl
{
    public LockSettingsView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

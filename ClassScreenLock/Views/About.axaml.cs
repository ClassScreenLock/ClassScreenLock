using Avalonia.Controls;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class About : UserControl
{
    public About()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }
}
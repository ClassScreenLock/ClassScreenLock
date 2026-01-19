using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ClassScreenLock.ViewModels;
using System.Linq;

namespace ClassScreenLock.Views;

public partial class AppManagementView : UserControl
{
    public AppManagementView()
    {
        InitializeComponent();
    }

    private async void PickFileToBlock_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要阻止的可执行文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("可执行文件")
                {
                    Patterns = new[] { "*.exe" }
                }
            }
        });

        if (files.Count >= 1)
        {
            var path = files[0].Path.LocalPath;
            if (DataContext is AppManagementViewModel vm)
            {
                vm.AddPathToBlockedCommand.Execute(path);
            }
        }
    }
}

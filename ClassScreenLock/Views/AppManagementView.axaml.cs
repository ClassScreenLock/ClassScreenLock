using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClassScreenLock.ViewModels;
using System.IO;
using System.Linq;

namespace ClassScreenLock.Views;

public partial class AppManagementView : UserControl
{
    private IBrush? _originalDropZoneBackground;

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
            Title = "选择要阻止的文件",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*.*" }
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

    private void DropZone_DragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            _originalDropZoneBackground ??= border.Background;
            border.Background = Brush.Parse("#1A0078D4");
            border.BorderThickness = new Thickness(3);
        }
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void DropZone_DragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = _originalDropZoneBackground ?? Brush.Parse("#1A0078D4");
            border.BorderThickness = new Thickness(2);
        }
    }

    private void DropZone_Drop(object? sender, DragEventArgs e)
    {
        if (sender is Border border)
        {
            border.Background = _originalDropZoneBackground ?? Brush.Parse("#1A0078D4");
            border.BorderThickness = new Thickness(2);
        }

        if (e.DataTransfer.TryGetFiles() is { } files)
        {
            if (DataContext is AppManagementViewModel vm)
            {
                foreach (var file in files)
                {
                    var path = file.Path.LocalPath;
                    if (File.Exists(path))
                    {
                        vm.AddPathToBlockedCommand.Execute(path);
                    }
                    else if (Directory.Exists(path))
                    {
                        var dirFiles = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                        foreach (var dirFile in dirFiles)
                        {
                            vm.AddPathToBlockedCommand.Execute(dirFile);
                        }
                    }
                }
            }
        }
    }
}

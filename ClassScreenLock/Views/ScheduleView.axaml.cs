using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ClassScreenLock.ViewModels;
using System;

namespace ClassScreenLock.Views;

public partial class ScheduleView : UserControl
{
    public ScheduleView()
    {
        InitializeComponent();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (DataContext is ScheduleViewModel vm)
        {
            vm.IsMobileView = e.NewSize.Width < 900;
            vm.IsLargeView = e.NewSize.Width > 1300;
        }
    }

    private async void ImportSchedule_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入时间表",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("JSON 文件")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        });

        if (files.Count >= 1)
        {
            var path = files[0].Path.LocalPath;
            if (DataContext is ScheduleViewModel vm)
            {
                vm.ImportScheduleCommand.Execute(path);
            }
        }
    }

    private async void ExportSchedule_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        
        if (DataContext is not ScheduleViewModel vm || vm.SelectedSchedule == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出时间表",
            SuggestedFileName = vm.SelectedSchedule.Name + ".json",
            DefaultExtension = "json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("JSON 文件")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        });

        if (file != null)
        {
            var path = file.Path.LocalPath;
            vm.ExportScheduleCommand.Execute(path);
        }
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

public partial class ScheduleView : UserControl
{
    private bool _firstLoad = true;

    public ScheduleView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_firstLoad && DataContext is ScheduleViewModel vm)
        {
            _firstLoad = false;
            // 第一次显示时重新从磁盘加载课表（解决启动时集控同步时序问题）
            vm.LoadSchedulesCommand.Execute(null);
        }
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
                },
                new FilePickerFileType("YAML 文件")
                {
                    Patterns = new[] { "*.yml", "*.yaml" }
                }
            }
        });

        if (files.Count >= 1)
        {
            var path = files[0].Path.LocalPath;
            if (DataContext is ScheduleViewModel vm)
            {
                vm.ImportScheduleCommand.Execute(path);
                vm.LoadSchedulesCommand.Execute(null);
            }
        }
    }

    private async void ExportSchedule_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        
        if (DataContext is not ScheduleViewModel vm) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出周课表",
            SuggestedFileName = (vm.SelectedWeekly?.Name ?? "周课表") + ".json",
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
            if (vm.SelectedWeekly != null)
            {
                var weekNum = vm.SelectedWeekly.WeekNumber;
                var weekly = ClassScreenLock.Services.WeeklyScheduleService.Instance.GetWeekly(weekNum);
                if (weekly != null)
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(weekly, ClassScreenLock.Services.WeeklyScheduleService.JsonOptions);
                    System.IO.File.WriteAllText(path, json);
                }
            }
        }
    }
}

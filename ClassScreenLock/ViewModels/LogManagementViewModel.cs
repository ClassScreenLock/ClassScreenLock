using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

public partial class LogManagementViewModel : ViewModelBase
{
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logs = new();

    [ObservableProperty]
    private ObservableCollection<LogEntry> _displayLogs = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableTypes = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedType = "全部";

    [ObservableProperty]
    private ObservableCollection<string> _availableSources = new();

    [ObservableProperty]
    private string _selectedSource = "全部";

    public LogManagementViewModel()
    {
        RefreshLogs();
    }

    [RelayCommand]
    private void RefreshLogs()
    {
        var logList = LogService.Instance.LoadLogs();
        Logs = new ObservableCollection<LogEntry>(logList);
        UpdateAvailableTypes();
        UpdateAvailableSources();
        ApplyFilter();
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogService.Instance.ClearLogs();
        RefreshLogs();
    }

    [RelayCommand]
    private async Task ExportJsonAskPathAsync()
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = win?.StorageProvider;
        if (provider == null) return;
        var options = new FilePickerSaveOptions
        {
            Title = "导出 JSON",
            SuggestedFileName = $"logs-export-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" }, MimeTypes = new[] { "application/json" } }
            }
        };
        var file = await provider.SaveFilePickerAsync(options);
        if (file == null) return;
        var list = DisplayLogs?.ToList() ?? new();
        var json = System.Text.Json.JsonSerializer.Serialize(list, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(json);
        await writer.FlushAsync();
        NotificationService.Instance.ShowSuccess("已导出 JSON");
    }

    [RelayCommand]
    private async Task ExportCsvAskPathAsync()
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = win?.StorageProvider;
        if (provider == null) return;
        var options = new FilePickerSaveOptions
        {
            Title = "导出 CSV",
            SuggestedFileName = $"logs-export-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" }, MimeTypes = new[] { "text/csv" } }
            }
        };
        var file = await provider.SaveFilePickerAsync(options);
        if (file == null) return;
        var sb = new StringBuilder();
        sb.AppendLine("时间,类型,操作,目标,详情");
        foreach (var e in DisplayLogs ?? new())
        {
            var t = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
            var type = EscapeCsv(e.Type);
            var action = EscapeCsv(e.Action);
            var target = EscapeCsv(e.Target);
            var details = EscapeCsv(e.Details);
            sb.AppendLine($"{t},{type},{action},{target},{details}");
        }
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        await writer.WriteAsync(sb.ToString());
        await writer.FlushAsync();
        NotificationService.Instance.ShowSuccess("已导出 CSV");
    }

    [RelayCommand]
    private async Task ExportJsonBySourceAsync()
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = win?.StorageProvider;
        if (provider == null) return;
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择导出目录", AllowMultiple = false });
        var rootFolder = folders?.FirstOrDefault();
        if (rootFolder == null) return;
        
        var subFolderName = $"logs-export-{DateTime.Now:yyyyMMdd-HHmmss}";
        var folder = await rootFolder.CreateFolderAsync(subFolderName);
        if (folder == null) return;

        var groups = (DisplayLogs ?? new()).GroupBy(e => e.Target);
        foreach (var g in groups)
        {
            var safe = MakeSafeFileName(string.IsNullOrWhiteSpace(g.Key) ? "未知来源" : g.Key);
            var json = System.Text.Json.JsonSerializer.Serialize(g.ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            var file = await folder!.CreateFileAsync($"logs-source-{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            if (file == null) continue;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(json);
            await writer.FlushAsync();
        }
        NotificationService.Instance.ShowSuccess("已按来源分包导出 JSON");
    }

    [RelayCommand]
    private async Task ExportCsvBySourceAsync()
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = win?.StorageProvider;
        if (provider == null) return;
        var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择导出目录", AllowMultiple = false });
        var rootFolder = folders?.FirstOrDefault();
        if (rootFolder == null) return;

        var subFolderName = $"logs-export-{DateTime.Now:yyyyMMdd-HHmmss}";
        var folder = await rootFolder.CreateFolderAsync(subFolderName);
        if (folder == null) return;

        var groups = (DisplayLogs ?? new()).GroupBy(e => e.Target);
        foreach (var g in groups)
        {
            var safe = MakeSafeFileName(string.IsNullOrWhiteSpace(g.Key) ? "未知来源" : g.Key);
            var sb = new StringBuilder();
            sb.AppendLine("时间,类型,操作,目标,详情");
            foreach (var e in g)
            {
                var t = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                var type = EscapeCsv(e.Type);
                var action = EscapeCsv(e.Action);
                var target = EscapeCsv(e.Target);
                var details = EscapeCsv(e.Details);
                sb.AppendLine($"{t},{type},{action},{target},{details}");
            }
            var file = await folder!.CreateFileAsync($"logs-source-{safe}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            if (file == null) continue;
            await using var stream = await file.OpenWriteAsync();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(sb.ToString());
            await writer.FlushAsync();
        }
        NotificationService.Instance.ShowSuccess("已按来源分包导出 CSV");
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedTypeChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedSourceChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var q = (SearchText ?? string.Empty).Trim().ToLowerInvariant();
        var type = SelectedType ?? "全部";
        var source = SelectedSource ?? "全部";
        var src = Logs?.ToList() ?? new();
        var filtered = src.Where(e =>
            (type == "全部" || string.Equals(e.Type, type, StringComparison.OrdinalIgnoreCase)) &&
            (source == "全部" || string.Equals(e.Target, source, StringComparison.OrdinalIgnoreCase)) &&
            (q.Length == 0 ||
             (e.Type ?? string.Empty).ToLowerInvariant().Contains(q) ||
             (e.Action ?? string.Empty).ToLowerInvariant().Contains(q) ||
             (e.Target ?? string.Empty).ToLowerInvariant().Contains(q) ||
             (e.Details ?? string.Empty).ToLowerInvariant().Contains(q))
        ).ToList();
        DisplayLogs = new ObservableCollection<LogEntry>(filtered);
    }

    private void UpdateAvailableTypes()
    {
        var types = Logs.Select(l => l.Type).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList();
        var list = new ObservableCollection<string>();
        list.Add("全部");
        foreach (var t in types) list.Add(t);
        AvailableTypes = list;
        if (!AvailableTypes.Contains(SelectedType)) SelectedType = "全部";
    }

    private void UpdateAvailableSources()
    {
        var targets = Logs.Select(l => l.Target).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().OrderBy(t => t).ToList();
        var list = new ObservableCollection<string>();
        list.Add("全部");
        foreach (var t in targets) list.Add(t);
        AvailableSources = list;
        if (!AvailableSources.Contains(SelectedSource)) SelectedSource = "全部";
    }

    private static string EscapeCsv(string? value)
    {
        var s = value ?? string.Empty;
        var needQuote = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
        if (needQuote) s = '"' + s.Replace("\"", "\"\"") + '"';
        return s;
    }

    private static string MakeSafeFileName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            s = s.Replace(c, '_');
        }
        return s;
    }
}

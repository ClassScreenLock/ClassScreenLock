using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ClassScreenLock.ViewModels;
using System;
using System.Collections.Generic;
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
            var path = NormalizePath(files[0].Path.LocalPath);
            if (string.IsNullOrEmpty(path)) return;
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

        if (e.DataTransfer.TryGetFiles() is not { } files) return;
        if (DataContext is not AppManagementViewModel vm) return;

        // 关键修复 K：1) 规范化所有路径；2) 按规范化结果去重，避免父目录遍历 + 子文件拖入造成的重复添加。
        var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var raw = file.Path.LocalPath;
            if (string.IsNullOrWhiteSpace(raw)) continue;

            if (File.Exists(raw))
            {
                var normalized = NormalizePath(raw);
                if (!string.IsNullOrEmpty(normalized)) collected.Add(normalized);
            }
            else if (Directory.Exists(raw))
            {
                try
                {
                    foreach (var dirFile in Directory.GetFiles(raw, "*", SearchOption.TopDirectoryOnly))
                    {
                        var normalized = NormalizePath(dirFile);
                        if (!string.IsNullOrEmpty(normalized)) collected.Add(normalized);
                    }
                }
                catch
                {
                    // 跳过无法读取的目录
                }
            }
        }

        foreach (var path in collected)
        {
            vm.AddPathToBlockedCommand.Execute(path);
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var trimmed = path.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmed)) return null;
            return Path.GetFullPath(trimmed);
        }
        catch
        {
            return null;
        }
    }
}

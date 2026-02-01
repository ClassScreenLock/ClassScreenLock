using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public partial class ScreenshotItem : ObservableObject
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool IsClassTime { get; set; }
    public string FileName => System.IO.Path.GetFileName(FilePath);

    [ObservableProperty]
    private bool _isSelected;
}

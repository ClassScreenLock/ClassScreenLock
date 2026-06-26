using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media.Imaging;

namespace ClassScreenLock.Models;

public partial class AppInfo : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _processName = string.Empty;

    [ObservableProperty]
    private string _executablePath = string.Empty;

    [ObservableProperty]
    private bool _isAllowed = true;

    [ObservableProperty]
    private bool _isRunning = false;

    [ObservableProperty]
    private string _iconName = "fas fa-window-maximize";

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private long _memoryUsage;

    [ObservableProperty]
    private string _memoryUsageString = string.Empty;

    // 进程磁盘 I/O（read+write）累计值；与"网络"无关，避免命名误导。
    [ObservableProperty]
    private string _totalIoUsage = "0 B";

    // 进程磁盘 I/O 速率（read+write / 时间差）。
    [ObservableProperty]
    private string _ioRate = "0 B/s";

    [ObservableProperty]
    private double _cpuUsage;

    [ObservableProperty]
    private string _cpuUsageString = "0%";

    [ObservableProperty]
    private int _threadCount;

    [ObservableProperty]
    private int _handleCount;

    [ObservableProperty]
    private string _category = "后台进程"; // "应用" 或 "后台进程"

    [ObservableProperty]
    private int _categoryOrder = 1; // 0 为应用，1 为后台进程

    public int ProcessId { get; set; }
}

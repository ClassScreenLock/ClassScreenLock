using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using ClassScreenLock.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClassScreenLock.ViewModels;

public partial class ScreenshotHistoryViewModel : ViewModelBase
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 12.0;

    private ObservableCollection<ScreenshotItem>? _hookedAllScreenshots;
    private ScreenshotItem? _selectionAnchor;

    [ObservableProperty]
    private ObservableCollection<ScreenshotItem> _allScreenshots = new();

    [ObservableProperty]
    private ObservableCollection<ScreenshotItem> _pagedScreenshots = new();

    [ObservableProperty]
    private bool _isSelectionMode;

    [ObservableProperty]
    private int _pageSize = 24;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _bulkRotation;

    [ObservableProperty]
    private bool _bulkFlipHorizontal;

    [ObservableProperty]
    private bool _bulkFlipVertical;
    
    [ObservableProperty]
    private ScreenshotItem? _selectedScreenshot;

    [ObservableProperty]
    private ScreenshotSettingsModel _settings = new();

    // Filters
    [ObservableProperty]
    private DateTime? _filterDate;

    [ObservableProperty]
    private int _filterTypeIndex = 0; // 0: All, 1: Class, 2: Break

    // Image Viewer
    [ObservableProperty]
    private bool _isImageViewerOpen;

    [ObservableProperty]
    private bool _isMaximized;

    [ObservableProperty]
    private ScreenshotItem? _currentViewingImage;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private double _rotation = 0;

    [ObservableProperty]
    private bool _flipHorizontal;

    [ObservableProperty]
    private bool _flipVertical;

    [ObservableProperty]
    private double _renderScaleX = 1.0;

    [ObservableProperty]
    private double _renderScaleY = 1.0;

    public List<ImageFormatOption> AvailableFormats { get; } = new()
    {
        new("PNG", "PNG", "高质量，无损，文件较大"),
        new("JPEG", "JPEG", "画质好，压缩，文件较小"),
        new("BMP", "BMP", "无压缩，文件非常大"),
        new("GIF", "GIF", "256色，适合简单图形")
    };

    public ScreenshotHistoryViewModel()
    {
        LoadScreenshots();
        LoadSettings();
        UpdateRenderScales();
    }

    public List<int> PageSizes { get; } = new() { 12, 24, 48, 96 };
    public List<int> ClassIntervalOptions { get; } = Enumerable.Range(1, 120).ToList();
    public List<int> BreakIntervalOptions { get; } = Enumerable.Range(1, 20).ToList();
    public List<int> RetentionDaysOptions { get; } = new() { 7, 15, 30, 60, 90, 180, 365 };
    public List<int> MaxStorageMBOptions { get; } = Enumerable.Range(2, 39).Select(x => x * 512).ToList(); // 1GB (1024MB) to 20GB (20480MB) in 0.5GB (512MB) steps

    public int TotalItems => AllScreenshots.Count;
    public int TotalPages => TotalItems == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);
    public int SelectedCount => AllScreenshots.Count(x => x.IsSelected);
    public bool IsEmpty => TotalItems == 0;
    public bool HasScreenshots => TotalItems > 0;
    public int PageStartItemIndex => TotalItems == 0 || CurrentPage <= 0 ? 0 : ((CurrentPage - 1) * PageSize + 1);
    public int PageEndItemIndex => TotalItems == 0 || CurrentPage <= 0 ? 0 : Math.Min(TotalItems, CurrentPage * PageSize);
    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => TotalPages > 0 && CurrentPage < TotalPages;

    partial void OnAllScreenshotsChanged(ObservableCollection<ScreenshotItem> value)
    {
        if (_hookedAllScreenshots != null)
        {
            _hookedAllScreenshots.CollectionChanged -= OnAllScreenshotsCollectionChanged;
            foreach (var item in _hookedAllScreenshots)
            {
                item.PropertyChanged -= OnScreenshotItemPropertyChanged;
            }
        }

        _hookedAllScreenshots = value;

        _hookedAllScreenshots.CollectionChanged += OnAllScreenshotsCollectionChanged;
        foreach (var item in _hookedAllScreenshots)
        {
            item.PropertyChanged += OnScreenshotItemPropertyChanged;
        }

        UpdatePagedScreenshots();
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    private void OnAllScreenshotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems.OfType<ScreenshotItem>())
            {
                oldItem.PropertyChanged -= OnScreenshotItemPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems.OfType<ScreenshotItem>())
            {
                newItem.PropertyChanged += OnScreenshotItemPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
    }

    private void OnScreenshotItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScreenshotItem.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
        }
    }

    private void LoadSettings()
    {
        Settings = SettingsService.Screenshot;

        Settings.ClassScreenshotInterval = Math.Clamp(Settings.ClassScreenshotInterval, 1, 120);
        Settings.BreakScreenshotInterval = Math.Clamp(Settings.BreakScreenshotInterval, 1, 20);
        Settings.RetentionDays = Math.Clamp(Settings.RetentionDays, 7, 365);
        Settings.MaxStorageMB = Math.Clamp(Settings.MaxStorageMB, 1024, 20480);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SettingsService.SaveScreenshot(Settings);
        NotificationService.Instance.ShowSuccess("截图设置已保存");
    }

    [RelayCommand]
    private void LoadScreenshots()
    {
        var items = ScreenshotService.Instance.GetScreenshots();
        
        // Apply Filters
        if (FilterDate.HasValue)
        {
            var date = FilterDate.Value.Date;
            items = items.Where(x => x.Timestamp.Date == date).ToList();
        }

        if (FilterTypeIndex == 1) // Class
        {
            items = items.Where(x => x.IsClassTime).ToList();
        }
        else if (FilterTypeIndex == 2) // Break
        {
            items = items.Where(x => !x.IsClassTime).ToList();
        }

        AllScreenshots = new ObservableCollection<ScreenshotItem>(items);
        CurrentPage = AllScreenshots.Count == 0 ? 0 : 1;
        UpdatePagedScreenshots();
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
        OnPropertyChanged(nameof(SelectedCount));
    }
    
    [RelayCommand]
    private void DeleteScreenshot(ScreenshotItem? item)
    {
        if (item == null) return;
        try 
        {
            if (System.IO.File.Exists(item.FilePath))
            {
                System.IO.File.Delete(item.FilePath);
            }
            AllScreenshots.Remove(item);
            UpdatePagedScreenshots();
            OnPropertyChanged(nameof(TotalItems));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageStartItemIndex));
            OnPropertyChanged(nameof(PageEndItemIndex));
            OnPropertyChanged(nameof(SelectedCount));
            
            // If deleting currently viewed image
            if (CurrentViewingImage == item)
            {
                CloseImageViewer();
            }
        }
        catch (Exception ex)
        {
             NotificationService.Instance.ShowError($"删除失败: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private void OpenScreenshot(ScreenshotItem? item)
    {
        if (item == null) return;
        
        // Instead of opening externally, open internal viewer
        CurrentViewingImage = item;
        ResetTransform();
        IsImageViewerOpen = true;
    }

    [RelayCommand]
    private void OpenOrToggleSelect(ScreenshotItem? item)
    {
        if (item == null) return;
        if (IsSelectionMode)
        {
            ToggleSingleSelection(item);
            return;
        }

        OpenScreenshot(item);
    }

    public void HandleSelectionClick(ScreenshotItem item, bool isShiftPressed)
    {
        if (!IsSelectionMode) return;

        if (isShiftPressed && _selectionAnchor != null)
        {
            var list = PagedScreenshots.ToList();
            var start = list.IndexOf(_selectionAnchor);
            var end = list.IndexOf(item);

            if (start >= 0 && end >= 0)
            {
                if (start > end) (start, end) = (end, start);
                for (var i = start; i <= end; i++)
                {
                    list[i].IsSelected = true;
                }

                _selectionAnchor = item;
                OnPropertyChanged(nameof(SelectedCount));
                return;
            }
        }

        ToggleSingleSelection(item);
    }

    private void ToggleSingleSelection(ScreenshotItem item)
    {
        item.IsSelected = !item.IsSelected;
        _selectionAnchor = item;
        OnPropertyChanged(nameof(SelectedCount));
    }
    
    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Screenshots");
            if (!System.IO.Directory.Exists(folder)) System.IO.Directory.CreateDirectory(folder);
            
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"打开文件夹失败: {ex.Message}");
        }
    }

    // Image Viewer Commands
    [RelayCommand]
    private void CloseImageViewer()
    {
        IsImageViewerOpen = false;
        IsMaximized = false; // Reset maximization when closing
        CurrentViewingImage = null;
        // Optional: clear large image cache to free memory immediately
        StringToBitmapConverter.ClearCache();
    }

    [RelayCommand]
    private void ToggleMaximize()
    {
        IsMaximized = !IsMaximized;
    }

    [RelayCommand]
    private void PreviousImage()
    {
        if (CurrentViewingImage == null) return;
        var index = PagedScreenshots.IndexOf(CurrentViewingImage);
        if (index > 0)
        {
            CurrentViewingImage = PagedScreenshots[index - 1];
            ResetTransform();
        }
        else if (PagedScreenshots.Count > 0)
        {
            // Optional: Loop to last
            CurrentViewingImage = PagedScreenshots[^1];
            ResetTransform();
        }
    }

    [RelayCommand]
    private void NextImage()
    {
        if (CurrentViewingImage == null) return;
        var index = PagedScreenshots.IndexOf(CurrentViewingImage);
        if (index >= 0 && index < PagedScreenshots.Count - 1)
        {
            CurrentViewingImage = PagedScreenshots[index + 1];
            ResetTransform();
        }
        else if (PagedScreenshots.Count > 0)
        {
            // Optional: Loop to first
            CurrentViewingImage = PagedScreenshots[0];
            ResetTransform();
        }
    }

    [RelayCommand]
    private void ZoomIn()
    {
        SetZoom(ZoomLevel * 1.1);
    }

    [RelayCommand]
    private void ZoomOut()
    {
        SetZoom(ZoomLevel / 1.1);
    }

    [RelayCommand]
    private void ResetView()
    {
        ResetTransform();
    }

    [RelayCommand]
    private void RotateLeft()
    {
        Rotation -= 90;
    }

    [RelayCommand]
    private void RotateRight()
    {
        Rotation += 90;
    }

    [RelayCommand]
    private void ToggleFlipHorizontal()
    {
        FlipHorizontal = !FlipHorizontal;
    }

    [RelayCommand]
    private void ToggleFlipVertical()
    {
        FlipVertical = !FlipVertical;
    }

    [RelayCommand]
    private async Task ApplyEditsToCurrentAsync()
    {
        var item = CurrentViewingImage;
        if (item == null) return;

        var confirmed = await NotificationService.Instance.ShowConfirmAsync(
            "将覆盖原图且无法撤销，确定应用当前编辑到此图片？",
            "应用编辑");
        if (!confirmed) return;

        if (!TryApplyEditsToFile(item.FilePath, Rotation, FlipHorizontal, FlipVertical, out var error))
        {
            NotificationService.Instance.ShowError($"应用失败: {error}");
            return;
        }

        Rotation = 0;
        FlipHorizontal = false;
        FlipVertical = false;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentViewingImage = null;
            CurrentViewingImage = item;
        });

        NotificationService.Instance.ShowSuccess("已应用到当前图片");
    }

    [RelayCommand]
    private async Task ApplyEditsToAllAsync()
    {
        if (AllScreenshots.Count == 0) return;

        var confirmed = await NotificationService.Instance.ShowConfirmAsync(
            $"将覆盖当前筛选结果中的 {AllScreenshots.Count} 张图片且无法撤销，确定批量应用当前编辑？",
            "批量应用编辑");
        if (!confirmed) return;

        var rotation = Rotation;
        var flipH = FlipHorizontal;
        var flipV = FlipVertical;

        var failed = new List<string>();

        await Task.Run(() =>
        {
            foreach (var item in AllScreenshots)
            {
                if (!TryApplyEditsToFile(item.FilePath, rotation, flipH, flipV, out var error))
                {
                    failed.Add($"{Path.GetFileName(item.FilePath)}: {error}");
                }
            }
        });

        Rotation = 0;
        FlipHorizontal = false;
        FlipVertical = false;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            LoadScreenshots();
            if (CurrentViewingImage != null)
            {
                var current = CurrentViewingImage;
                CurrentViewingImage = null;
                CurrentViewingImage = current;
            }
        });

        if (failed.Count > 0)
        {
            NotificationService.Instance.ShowWarning($"批量完成，但有 {failed.Count} 张失败");
            Debug.WriteLine(string.Join(Environment.NewLine, failed));
            return;
        }

        NotificationService.Instance.ShowSuccess("已批量应用编辑");
    }

    [RelayCommand]
    private void ToggleSelectionMode()
    {
        IsSelectionMode = !IsSelectionMode;
        if (!IsSelectionMode)
        {
            ClearSelection();
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var item in AllScreenshots)
        {
            item.IsSelected = false;
        }

        _selectionAnchor = null;
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void SelectAllOnPage()
    {
        foreach (var item in PagedScreenshots)
        {
            item.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedCount));
    }

    [RelayCommand]
    private void ResetBulkEdits()
    {
        BulkRotation = 0;
        BulkFlipHorizontal = false;
        BulkFlipVertical = false;
    }

    [RelayCommand]
    private void BulkRotateLeft()
    {
        BulkRotation = ((BulkRotation - 90) % 360 + 360) % 360;
    }

    [RelayCommand]
    private void BulkRotateRight()
    {
        BulkRotation = (BulkRotation + 90) % 360;
    }

    [RelayCommand]
    private void BulkToggleFlipHorizontal()
    {
        BulkFlipHorizontal = !BulkFlipHorizontal;
    }

    [RelayCommand]
    private void BulkToggleFlipVertical()
    {
        BulkFlipVertical = !BulkFlipVertical;
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = AllScreenshots.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirmed = await NotificationService.Instance.ShowConfirmAsync(
            $"将删除 {selected.Count} 张图片且无法撤销，确定继续？",
            "批量删除");
        if (!confirmed) return;

        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var item in selected)
            {
                try
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(item.FilePath)}: {ex.Message}");
                }
            }
        });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var item in selected)
            {
                AllScreenshots.Remove(item);
            }
            UpdatePagedScreenshots();
            OnPropertyChanged(nameof(TotalItems));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageStartItemIndex));
            OnPropertyChanged(nameof(PageEndItemIndex));
            OnPropertyChanged(nameof(SelectedCount));
        });

        if (failed.Count > 0)
        {
            NotificationService.Instance.ShowWarning($"批量删除完成，但有 {failed.Count} 张失败");
            Debug.WriteLine(string.Join(Environment.NewLine, failed));
            return;
        }

        NotificationService.Instance.ShowSuccess("已批量删除");
    }

    [RelayCommand]
    private async Task ApplyBulkEditsToSelectedAsync()
    {
        var selected = AllScreenshots.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var confirmed = await NotificationService.Instance.ShowConfirmAsync(
            $"将覆盖 {selected.Count} 张图片且无法撤销，确定应用统一编辑？",
            "批量应用编辑");
        if (!confirmed) return;

        var rotation = BulkRotation;
        var flipH = BulkFlipHorizontal;
        var flipV = BulkFlipVertical;

        var failed = new List<string>();
        await Task.Run(() =>
        {
            foreach (var item in selected)
            {
                if (!TryApplyEditsToFile(item.FilePath, rotation, flipH, flipV, out var error))
                {
                    failed.Add($"{Path.GetFileName(item.FilePath)}: {error}");
                }
            }
        });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdatePagedScreenshots();
            if (CurrentViewingImage != null)
            {
                var current = CurrentViewingImage;
                CurrentViewingImage = null;
                CurrentViewingImage = current;
            }
        });

        if (failed.Count > 0)
        {
            NotificationService.Instance.ShowWarning($"批量完成，但有 {failed.Count} 张失败");
            Debug.WriteLine(string.Join(Environment.NewLine, failed));
            return;
        }

        NotificationService.Instance.ShowSuccess("已批量应用编辑");
    }

    [RelayCommand]
    private void FirstPage()
    {
        CurrentPage = 1;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage <= 1) return;
        CurrentPage -= 1;
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage >= TotalPages) return;
        CurrentPage += 1;
    }

    [RelayCommand]
    private void LastPage()
    {
        CurrentPage = TotalPages;
    }

    private void ResetTransform()
    {
        ZoomLevel = 1.0;
        Rotation = 0;
        FlipHorizontal = false;
        FlipVertical = false;
    }

    public void SetZoom(double zoom)
    {
        ZoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    partial void OnZoomLevelChanged(double value)
    {
        if (value < MinZoom)
        {
            ZoomLevel = MinZoom;
            return;
        }

        if (value > MaxZoom)
        {
            ZoomLevel = MaxZoom;
            return;
        }

        UpdateRenderScales();
    }

    partial void OnFlipHorizontalChanged(bool value)
    {
        UpdateRenderScales();
    }

    partial void OnFlipVerticalChanged(bool value)
    {
        UpdateRenderScales();
    }

    private void UpdateRenderScales()
    {
        RenderScaleX = ZoomLevel * (FlipHorizontal ? -1.0 : 1.0);
        RenderScaleY = ZoomLevel * (FlipVertical ? -1.0 : 1.0);
    }

    private static bool TryApplyEditsToFile(string filePath, double rotationDegrees, bool flipHorizontal, bool flipVertical, out string? error)
    {
        error = null;

        if (!File.Exists(filePath))
        {
            error = "文件不存在";
            return false;
        }

        try
        {
            var rotation = NormalizeRotation(rotationDegrees);
            var rotateFlip = GetRotateFlipType(rotation, flipHorizontal, flipVertical);

            var bytes = File.ReadAllBytes(filePath);
            using var ms = new MemoryStream(bytes);
            using var source = Image.FromStream(ms);
            using var bitmap = new Bitmap(source);

            bitmap.RotateFlip(rotateFlip);

            var imageFormat = GetImageFormatByExtension(Path.GetExtension(filePath));
            var tempPath = filePath + ".tmp";
            bitmap.Save(tempPath, imageFormat);
            File.Move(tempPath, filePath, true);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static int NormalizeRotation(double rotationDegrees)
    {
        var rounded = (int)Math.Round(rotationDegrees / 90.0) * 90;
        var normalized = rounded % 360;
        if (normalized < 0) normalized += 360;
        return normalized;
    }

    private static RotateFlipType GetRotateFlipType(int rotationDegrees, bool flipHorizontal, bool flipVertical)
    {
        rotationDegrees = rotationDegrees % 360;
        if (rotationDegrees < 0) rotationDegrees += 360;

        return (rotationDegrees, flipHorizontal, flipVertical) switch
        {
            (0, false, false) => RotateFlipType.RotateNoneFlipNone,
            (0, true, false) => RotateFlipType.RotateNoneFlipX,
            (0, false, true) => RotateFlipType.RotateNoneFlipY,
            (0, true, true) => RotateFlipType.RotateNoneFlipXY,

            (90, false, false) => RotateFlipType.Rotate90FlipNone,
            (90, true, false) => RotateFlipType.Rotate90FlipX,
            (90, false, true) => RotateFlipType.Rotate90FlipY,
            (90, true, true) => RotateFlipType.Rotate90FlipXY,

            (180, false, false) => RotateFlipType.Rotate180FlipNone,
            (180, true, false) => RotateFlipType.Rotate180FlipX,
            (180, false, true) => RotateFlipType.Rotate180FlipY,
            (180, true, true) => RotateFlipType.Rotate180FlipXY,

            (270, false, false) => RotateFlipType.Rotate270FlipNone,
            (270, true, false) => RotateFlipType.Rotate270FlipX,
            (270, false, true) => RotateFlipType.Rotate270FlipY,
            (270, true, true) => RotateFlipType.Rotate270FlipXY,

            _ => RotateFlipType.RotateNoneFlipNone
        };
    }

    private static ImageFormat GetImageFormatByExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return ImageFormat.Png;
        extension = extension.Trim().ToLowerInvariant();

        return extension switch
        {
            ".jpg" => ImageFormat.Jpeg,
            ".jpeg" => ImageFormat.Jpeg,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            _ => ImageFormat.Png
        };
    }
    
    partial void OnFilterDateChanged(DateTime? value)
    {
        LoadScreenshots();
    }

    partial void OnFilterTypeIndexChanged(int value)
    {
        LoadScreenshots();
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value <= 0) PageSize = 24;
        CurrentPage = TotalItems == 0 ? 0 : 1;
        UpdatePagedScreenshots();
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnCurrentPageChanged(int value)
    {
        var pages = TotalPages;
        if (pages == 0)
        {
            if (CurrentPage != 0) CurrentPage = 0;
            return;
        }

        if (value < 1)
        {
            CurrentPage = 1;
            return;
        }

        if (value > pages)
        {
            CurrentPage = pages;
            return;
        }

        UpdatePagedScreenshots();
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        if (!value)
        {
            ClearSelection();
        }
    }

    private void UpdatePagedScreenshots()
    {
        _selectionAnchor = null;

        var totalPages = TotalPages;
        if (totalPages == 0)
        {
            if (CurrentPage != 0) CurrentPage = 0;
            PagedScreenshots = new ObservableCollection<ScreenshotItem>();
            OnPropertyChanged(nameof(PageStartItemIndex));
            OnPropertyChanged(nameof(PageEndItemIndex));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(CanGoNext));
            return;
        }

        if (CurrentPage < 1) CurrentPage = 1;
        if (CurrentPage > totalPages) CurrentPage = totalPages;

        var skip = (CurrentPage - 1) * PageSize;
        var pageItems = AllScreenshots.Skip(skip).Take(PageSize).ToList();
        PagedScreenshots = new ObservableCollection<ScreenshotItem>(pageItems);
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
    }
}

public record ImageFormatOption(string Name, string Value, string Description);

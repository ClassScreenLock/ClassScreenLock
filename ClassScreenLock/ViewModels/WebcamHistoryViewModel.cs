using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AForge.Video;
using AForge.Video.DirectShow;
using Avalonia.Threading;
using ClassScreenLock.Models;
using ClassScreenLock.Services;
using ClassScreenLock.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using SystemDrawingBitmap = System.Drawing.Bitmap;
using SystemDrawingImageFormat = System.Drawing.Imaging.ImageFormat;

namespace ClassScreenLock.ViewModels;

public partial class WebcamHistoryViewModel : ViewModelBase, IImageViewerViewModel
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 12.0;

    private ObservableCollection<ScreenshotItem>? _hookedAllScreenshots;
    private ScreenshotItem? _selectionAnchor;
    private VideoCaptureDevice? _previewDevice;
    private DateTime _lastPreviewAtUtc = DateTime.MinValue;

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
    private ScreenshotItem? _selectedScreenshot;

    [ObservableProperty]
    private ScreenshotSettingsModel _settings = new();

    public double WebcamBrightness
    {
        get => Settings.WebcamBrightness;
        set
        {
            if (Math.Abs(Settings.WebcamBrightness - value) < 0.0001) return;
            Settings.WebcamBrightness = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private AvaloniaBitmap? _previewImage;

    [ObservableProperty]
    private bool _isPreviewRunning;

    [ObservableProperty]
    private string _previewStatus = "未启动";

    [ObservableProperty]
    private List<CameraItem> _cameraOptions = new();

    [ObservableProperty]
    private string? _selectedDate;

    [ObservableProperty]
    private ObservableCollection<string> _availableDates = new() { "全部日期" };

    [ObservableProperty]
    private int _filterTypeIndex = 0;

    [ObservableProperty]
    private bool _isImageViewerOpen;

    [ObservableProperty]
    private bool _isMaximized;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    [ObservableProperty]
    private ScreenshotItem? _currentViewingImage;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private double _rotation = 0;

    public List<ImageFormatOption> AvailableFormats { get; } = new()
    {
        new("PNG", "PNG", "高质量，无损，文件较大"),
        new("JPEG", "JPEG", "画质好，压缩，文件较小"),
        new("BMP", "BMP", "无压缩，文件非常大"),
        new("GIF", "GIF", "256色，适合简单图形")
    };

    public WebcamHistoryViewModel()
    {
        LoadSettings();
        RefreshCameraOptionsAsync();
    }

    public List<int> PageSizes { get; } = new() { 12, 24, 48, 96 };
    public List<int> ClassIntervalOptions { get; } = Enumerable.Range(1, 120).ToList();
    public List<int> BreakIntervalOptions { get; } = Enumerable.Range(1, 20).ToList();
    public List<int> RetentionDaysOptions { get; } = new() { 7, 15, 30, 60, 90, 180, 365 };
    public List<int> MaxStorageMBOptions { get; } = Enumerable.Range(2, 39).Select(x => x * 512).ToList();

    public int TotalItems => AllScreenshots.Count;
    public int TotalPages => TotalItems == 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);
    public int SelectedCount => AllScreenshots.Count(x => x.IsSelected);
    public bool IsEmpty => TotalItems == 0;
    public bool HasScreenshots => TotalItems > 0;
    public int PageStartItemIndex => TotalItems == 0 || CurrentPage <= 0 ? 0 : ((CurrentPage - 1) * PageSize + 1);
    public int PageEndItemIndex => TotalItems == 0 || CurrentPage <= 0 ? 0 : Math.Min(TotalItems, CurrentPage * PageSize);
    public bool CanGoPrevious => CurrentPage > 1;
    public bool CanGoNext => TotalPages > 0 && CurrentPage < TotalPages;

    public double ZoomMin => MinZoom;
    public double ZoomMax => MaxZoom;

    IRelayCommand IImageViewerViewModel.DeleteScreenshotCommand => DeleteScreenshotCommand;

    public string SelectedCameraMoniker
    {
        get => Settings.SelectedCameraMoniker;
        set
        {
            var next = value ?? string.Empty;
            if (Settings.SelectedCameraMoniker == next) return;
            Settings.SelectedCameraMoniker = next;
            if (IsPreviewRunning)
            {
                StopPreview();
            }
            OnPropertyChanged();
        }
    }

    public bool IsDebugEnabled
    {
        get => Settings.EnableWebcamDebug;
        set
        {
            if (Settings.EnableWebcamDebug == value) return;
            Settings.EnableWebcamDebug = value;
            if (!value)
            {
                StopPreview();
            }
            OnPropertyChanged();
        }
    }

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

        Settings.ClassWebcamInterval = Math.Clamp(Settings.ClassWebcamInterval, 1, 120);
        Settings.BreakWebcamInterval = Math.Clamp(Settings.BreakWebcamInterval, 1, 20);
        Settings.RetentionDays = Math.Clamp(Settings.RetentionDays, 7, 365);
        Settings.MaxStorageMB = Math.Clamp(Settings.MaxStorageMB, 1024, 20480);
        Settings.WebcamBrightness = Math.Clamp(Settings.WebcamBrightness, 0.1, 1.0);
        OnPropertyChanged(nameof(IsDebugEnabled));
        OnPropertyChanged(nameof(SelectedCameraMoniker));
    }

    private void RefreshCameraOptionsAsync()
    {
        Task.Run(() =>
        {
            var cameras = WebcamService.Instance.GetAvailableCamerasWithNames();
            var cameraItems = cameras.Select(kvp => new CameraItem(kvp.Value, kvp.Key)).ToList();

            Dispatcher.UIThread.Post(() =>
            {
                CameraOptions = cameraItems;

                if (string.IsNullOrEmpty(Settings.SelectedCameraMoniker) && CameraOptions.Any())
                {
                    Settings.SelectedCameraMoniker = CameraOptions.First().Moniker;
                    OnPropertyChanged(nameof(SelectedCameraMoniker));
                }
            });
        });
    }

    [RelayCommand]
    private void SaveSettings()
    {
        SettingsService.SaveScreenshot(Settings);
        NotificationService.Instance.ShowSuccess("拍照设置已保存");
    }

    [RelayCommand]
    private void DebugCapture()
    {
        if (!IsDebugEnabled)
        {
            NotificationService.Instance.ShowError("请先开启调试模式");
            return;
        }
        WebcamService.Instance.CaptureOnce(Settings.SelectedCameraMoniker);
        NotificationService.Instance.ShowSuccess("已触发拍照");
    }

    [RelayCommand]
    private void StartPreview()
    {
        if (!IsDebugEnabled)
        {
            NotificationService.Instance.ShowError("请先开启调试模式");
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedCameraMoniker))
        {
            PreviewStatus = "未选择摄像头";
            return;
        }

        if (IsPreviewRunning) return;

        try
        {
            _previewDevice = new VideoCaptureDevice(SelectedCameraMoniker);
            _previewDevice.NewFrame += OnPreviewNewFrame;
            _previewDevice.Start();
            IsPreviewRunning = true;
            PreviewStatus = "预览中";
        }
        catch (Exception ex)
        {
            PreviewStatus = "预览启动失败";
            NotificationService.Instance.ShowError($"预览启动失败: {ex.Message}");
            StopPreview();
        }
    }

    [RelayCommand]
    private void StopPreview()
    {
        try
        {
            if (_previewDevice != null)
            {
                _previewDevice.NewFrame -= OnPreviewNewFrame;
                if (_previewDevice.IsRunning)
                {
                    _previewDevice.SignalToStop();
                    for (int i = 0; i < 30; i++)
                    {
                        if (!_previewDevice.IsRunning) break;
                        Thread.Sleep(100);
                    }
                    if (_previewDevice.IsRunning)
                    {
                        System.Diagnostics.Debug.WriteLine("Preview stop timed out, forcing stop");
                        _previewDevice.Stop();
                    }
                }
                _previewDevice = null;
            }
        }
        catch
        {
        }

        IsPreviewRunning = false;
        PreviewStatus = "已停止";
        var old = PreviewImage;
        PreviewImage = null;
        old?.Dispose();
    }

    private void OnPreviewNewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        if (!IsPreviewRunning) return;
        var nowUtc = DateTime.UtcNow;
        if ((nowUtc - _lastPreviewAtUtc).TotalMilliseconds < 150) return;
        _lastPreviewAtUtc = nowUtc;

        try
        {
            using var frame = (SystemDrawingBitmap)eventArgs.Frame.Clone();
            var brightness = Math.Clamp(Settings.WebcamBrightness, 0.1, 1.0);
            using var adjusted = ApplyBrightness(frame, brightness);
            using var stream = new MemoryStream();
            adjusted.Save(stream, SystemDrawingImageFormat.Bmp);
            stream.Position = 0;
            var bitmap = new AvaloniaBitmap(stream);
            Dispatcher.UIThread.Post(() =>
            {
                var old = PreviewImage;
                PreviewImage = bitmap;
                old?.Dispose();
                if (IsPreviewRunning)
                {
                    PreviewStatus = "预览中";
                }
            });
        }
        catch
        {
        }
    }

    private static SystemDrawingBitmap ApplyBrightness(SystemDrawingBitmap source, double brightness)
    {
        if (Math.Abs(brightness - 1.0) < 0.001)
        {
            return (SystemDrawingBitmap)source.Clone();
        }

        var adjusted = new SystemDrawingBitmap(source.Width, source.Height);
        using var g = System.Drawing.Graphics.FromImage(adjusted);
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        var matrix = new System.Drawing.Imaging.ColorMatrix(new[]
        {
            new[] { (float)brightness, 0f, 0f, 0f, 0f },
            new[] { 0f, (float)brightness, 0f, 0f, 0f },
            new[] { 0f, 0f, (float)brightness, 0f, 0f },
            new[] { 0f, 0f, 0f, 1f, 0f },
            new[] { 0f, 0f, 0f, 0f, 1f }
        });
        attributes.SetColorMatrix(matrix);
        g.DrawImage(source, new System.Drawing.Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, System.Drawing.GraphicsUnit.Pixel, attributes);
        return adjusted;
    }

    private bool _isLoadingPhotos;

    [RelayCommand]
    private async Task LoadScreenshots()
    {
        if (_isLoadingPhotos) return;
        _isLoadingPhotos = true;
        try
        {
            var allItems = await Task.Run(() => WebcamService.Instance.GetPhotos());

            // Update Available Dates only if changed
            var dates = allItems.Select(x => x.Timestamp.ToString("yyyy-MM-dd")).Distinct().OrderByDescending(x => x).ToList();
            var currentDates = AvailableDates.Skip(1).ToList(); // Skip "全部日期"

            if (!dates.SequenceEqual(currentDates))
            {
                var currentSelected = SelectedDate;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AvailableDates.Clear();
                    AvailableDates.Add("全部日期");
                    foreach (var d in dates) AvailableDates.Add(d);
                });

                if (string.IsNullOrEmpty(currentSelected) || !AvailableDates.Contains(currentSelected))
                {
                    SelectedDate = "全部日期";
                }
                else
                {
                    SelectedDate = currentSelected;
                }
            }

            var items = allItems.AsEnumerable();

            if (!string.IsNullOrEmpty(SelectedDate) && SelectedDate != "全部日期")
            {
                if (DateTime.TryParse(SelectedDate, out var date))
                {
                    var targetDate = date.Date;
                    items = items.Where(x => x.Timestamp.Date == targetDate);
                }
            }

            if (FilterTypeIndex == 1)
            {
                items = items.Where(x => x.IsClassTime);
            }
            else if (FilterTypeIndex == 2)
            {
                items = items.Where(x => !x.IsClassTime);
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AllScreenshots = new ObservableCollection<ScreenshotItem>(items);
                CurrentPage = AllScreenshots.Count == 0 ? 0 : 1;
                UpdatePagedScreenshots();
                OnPropertyChanged(nameof(TotalItems));
                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageStartItemIndex));
                OnPropertyChanged(nameof(PageEndItemIndex));
                OnPropertyChanged(nameof(SelectedCount));
            });
        }
        finally
        {
            _isLoadingPhotos = false;
        }
    }

    [RelayCommand]
    private void DeleteScreenshot(ScreenshotItem? item)
    {
        if (item == null) return;
        try
        {
            if (File.Exists(item.FilePath))
            {
                File.Delete(item.FilePath);
            }
            AllScreenshots.Remove(item);
            UpdatePagedScreenshots();
            OnPropertyChanged(nameof(TotalItems));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageStartItemIndex));
            OnPropertyChanged(nameof(PageEndItemIndex));
            OnPropertyChanged(nameof(SelectedCount));

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
            var folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "WebcamPhotos");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

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

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedCount == 0) return;

        var selectedItems = AllScreenshots.Where(x => x.IsSelected).ToList();
        var errors = new List<string>();

        await Task.Run(() =>
        {
            foreach (var item in selectedItems)
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
                    errors.Add($"{item.FileName}: {ex.Message}");
                }
            }
        });

        foreach (var item in selectedItems)
        {
            AllScreenshots.Remove(item);
        }

        UpdatePagedScreenshots();
        OnPropertyChanged(nameof(TotalItems));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(PageStartItemIndex));
        OnPropertyChanged(nameof(PageEndItemIndex));
        OnPropertyChanged(nameof(SelectedCount));

        if (errors.Count > 0)
        {
            NotificationService.Instance.ShowError($"部分删除失败:\n{string.Join("\n", errors)}");
        }
    }

    [RelayCommand]
    private void SelectAllOnPage()
    {
        if (!IsSelectionMode) return;
        foreach (var item in PagedScreenshots)
        {
            item.IsSelected = true;
        }
        OnPropertyChanged(nameof(SelectedCount));
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
    private void CloseImageViewer()
    {
        IsImageViewerOpen = false;
        IsMaximized = false;
        CurrentViewingImage = null;
        ResetTransform();
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
    private void ResetTransform()
    {
        ZoomLevel = 1.0;
        Rotation = 0;
        PanX = 0;
        PanY = 0;
    }

    public void SetZoom(double zoom)
    {
        ZoomLevel = Math.Clamp(zoom, MinZoom, MaxZoom);
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
    }

    partial void OnSelectedDateChanged(string? value)
    {
        LoadScreenshotsCommand.Execute(null);
    }

    partial void OnFilterTypeIndexChanged(int value)
    {
        LoadScreenshotsCommand.Execute(null);
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
        if (value < 0) CurrentPage = 0;
        if (value > TotalPages) CurrentPage = TotalPages;
        UpdatePagedScreenshots();
    }

    partial void OnIsSelectionModeChanged(bool value)
    {
        if (!value)
        {
            ClearSelection();
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
        }
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
        }
    }

    [RelayCommand]
    private void FirstPage()
    {
        if (TotalPages > 0)
        {
            CurrentPage = 1;
        }
    }

    [RelayCommand]
    private void LastPage()
    {
        if (TotalPages > 0)
        {
            CurrentPage = TotalPages;
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

    public record CameraItem(string Name, string Moniker)
    {
        public override string ToString() => Name;
    }
}

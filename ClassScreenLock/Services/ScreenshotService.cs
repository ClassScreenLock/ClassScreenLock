using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class ScreenshotService
{
    private static ScreenshotService? _instance;
    public static ScreenshotService Instance => _instance ??= new ScreenshotService();

    private readonly string _screenshotDirectory;
    private Timer? _timer;
    private DateTime _lastScreenshotTime = DateTime.MinValue;
    private readonly SemaphoreSlim _captureSemaphore = new(1, 1);
    
    private ScreenshotService()
    {
        _screenshotDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Screenshots");
        if (!Directory.Exists(_screenshotDirectory))
        {
            Directory.CreateDirectory(_screenshotDirectory);
        }
    }

    public void Start()
    {
        // Check every minute
        _timer = new Timer(OnTimerTick, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    public void Stop()
    {
        _timer?.Change(Timeout.Infinite, 0);
        _timer?.Dispose();
        _timer = null;
    }

    public void CaptureOnce()
    {
        var isClass = ResolveIsClassTime();
        TakeScreenshot(isClass);
        _lastScreenshotTime = DateTime.Now;
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            CheckAndTakeScreenshot();
            CleanUpOldScreenshots();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Screenshot service error: {ex.Message}");
        }
    }

    private void CheckAndTakeScreenshot()
    {
        var settings = SettingsService.Screenshot;
        var now = DateTime.Now;
        var timeOfDay = now.TimeOfDay;

        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(timeOfDay);

        bool shouldTake = false;
        int interval = 0;
        bool isClass = false;

        if (current != null)
        {
            if (current.Type == TimePointType.Class)
            {
                if (settings.EnableClassScreenshot)
                {
                    interval = settings.ClassScreenshotInterval;
                    isClass = true;
                    shouldTake = true;
                }
            }
            else if (current.Type == TimePointType.Break)
            {
                if (settings.EnableBreakScreenshot)
                {
                    interval = settings.BreakScreenshotInterval;
                    isClass = false;
                    shouldTake = true;
                }
            }
        }

        if (shouldTake)
        {
            interval = Math.Max(1, interval);
        }

        if (shouldTake && interval > 0)
        {
            if ((now - _lastScreenshotTime).TotalMinutes >= interval)
            {
                TakeScreenshot(isClass);
                _lastScreenshotTime = now;
            }
        }
    }

    private void TakeScreenshot(bool isClass)
    {
        Task.Run(async () =>
        {
            if (!await _captureSemaphore.WaitAsync(0))
            {
                System.Diagnostics.Debug.WriteLine("Screenshot skipped: another capture is already in progress");
                return;
            }

            try
            {
                var screens = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop 
                    ? desktop.MainWindow?.Screens 
                    : null;

                int width = 1920;
                int height = 1080;
                int x = 0;
                int y = 0;

                if (screens?.Primary != null)
                {
                    var screen = screens.Primary;
                    width = (int)screen.Bounds.Width;
                    height = (int)screen.Bounds.Height;
                    x = (int)screen.Bounds.X;
                    y = (int)screen.Bounds.Y;
                }

                using var bitmap = new Bitmap(width, height);
                using var g = Graphics.FromImage(bitmap);
                g.CopyFromScreen(x, y, 0, 0, bitmap.Size);

                var imageFormat = ImageFormat.Jpeg;
                var extension = ".jpg";
                var settings = SettingsService.Screenshot;

                if (!string.IsNullOrEmpty(settings.ImageFormat))
                {
                    switch (settings.ImageFormat.ToUpper())
                    {
                        case "PNG":
                            imageFormat = ImageFormat.Png;
                            extension = ".png";
                            break;
                        case "BMP":
                            imageFormat = ImageFormat.Bmp;
                            extension = ".bmp";
                            break;
                        case "GIF":
                            imageFormat = ImageFormat.Gif;
                            extension = ".gif";
                            break;
                    }
                }

                var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{(isClass ? "Class" : "Break")}{extension}";
                var dayFolder = Path.Combine(_screenshotDirectory, DateTime.Now.ToString("yyyy-MM-dd"));
                if (!Directory.Exists(dayFolder))
                {
                    Directory.CreateDirectory(dayFolder);
                }
                var filePath = Path.Combine(dayFolder, fileName);

                bitmap.Save(filePath, imageFormat);
                LogService.Instance.Log("自动化", "截屏成功", "屏幕", $"文件保存至: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to take screenshot: {ex.Message}");
                LogService.Instance.Log("自动化", "截屏失败", "错误", ex.Message);
            }
            finally
            {
                _captureSemaphore.Release();
            }
        });
    }

    private bool ResolveIsClassTime()
    {
        var now = DateTime.Now.TimeOfDay;
        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        return current != null && current.Type == TimePointType.Class;
    }


    private void CleanUpOldScreenshots()
    {
        try
        {
            if (!Directory.Exists(_screenshotDirectory)) return;

            var settings = SettingsService.Screenshot;

            var retentionDays = settings.RetentionDays;
            var maxStorageMb = settings.MaxStorageMB;
            if (maxStorageMb < 0) maxStorageMb = 0;
            var maxStorageBytes = maxStorageMb <= 0 ? 0 : maxStorageMb * 1024L * 1024L;

            var files = Directory.EnumerateFiles(_screenshotDirectory, "*.*", SearchOption.AllDirectories)
                .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                {
                    try { return new FileInfo(path); }
                    catch { return null; }
                })
                .Where(fi => fi != null)
                .Cast<FileInfo>()
                .ToList();

            if (retentionDays > 0)
            {
                var cutoffDate = DateTime.Now.AddDays(-retentionDays);
                foreach (var fi in files.Where(f => f.Exists && f.CreationTime < cutoffDate).ToList())
                {
                    try
                    {
                        fi.Delete();
                        files.Remove(fi);
                    }
                    catch
                    {
                    }
                }
            }

            if (maxStorageBytes > 0)
            {
                long totalBytes = 0;
                foreach (var fi in files)
                {
                    if (fi.Exists) totalBytes += fi.Length;
                }

                if (totalBytes > maxStorageBytes)
                {
                    foreach (var fi in files.Where(f => f.Exists).OrderBy(f => f.CreationTime))
                    {
                        if (totalBytes <= maxStorageBytes) break;
                        try
                        {
                            var len = fi.Length;
                            fi.Delete();
                            totalBytes -= len;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            foreach (var dir in Directory.EnumerateDirectories(_screenshotDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch
                {
                }
            }
        }
        catch { }
    }

    public List<ScreenshotItem> GetScreenshots()
    {
        var list = new List<ScreenshotItem>();
        if (!Directory.Exists(_screenshotDirectory)) return list;

        var files = Directory.GetFiles(_screenshotDirectory, "*.*", SearchOption.AllDirectories)
            .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                        s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || 
                        s.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) || 
                        s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));
        
        foreach (var file in files)
        {
            var fi = new FileInfo(file);
            // Parse filename to determine type if possible, or trust filename convention
            var isClass = fi.Name.Contains("-Class");
            list.Add(new ScreenshotItem
            {
                FilePath = file,
                Timestamp = fi.CreationTime,
                IsClassTime = isClass
            });
        }

        return list.OrderByDescending(x => x.Timestamp).ToList();
    }
}

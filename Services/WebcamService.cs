using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using ClassScreenLock.Models;
using AForge.Video;
using AForge.Video.DirectShow;

namespace ClassScreenLock.Services;

public class WebcamService
{
    private static WebcamService? _instance;
    public static WebcamService Instance => _instance ??= new WebcamService();

    private readonly string _webcamDirectory;
    private Timer? _timer;
    private DateTime _lastPhotoTime = DateTime.MinValue;

    private WebcamService()
    {
        _webcamDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "WebcamPhotos");
        if (!Directory.Exists(_webcamDirectory))
        {
            Directory.CreateDirectory(_webcamDirectory);
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

    public void CaptureOnce(string? cameraMoniker = null, bool? isClassOverride = null)
    {
        var settings = SettingsService.Screenshot;
        var moniker = string.IsNullOrEmpty(cameraMoniker) ? settings.SelectedCameraMoniker : cameraMoniker;
        if (string.IsNullOrEmpty(moniker))
        {
            try
            {
                var list = GetAvailableCameras();
                moniker = list?.FirstOrDefault() ?? string.Empty;
            }
            catch { moniker = string.Empty; }
        }
        var isClass = isClassOverride ?? ResolveIsClassTime();
        TakePhoto(isClass, moniker);
        _lastPhotoTime = DateTime.Now;
    }

    public List<string> GetAvailableCameras()
    {
        var cameras = new List<string>();
        try
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in videoDevices)
            {
                cameras.Add(device.MonikerString);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting cameras: {ex.Message}");
        }
        return cameras;
    }

    public Dictionary<string, string> GetAvailableCamerasWithNames()
    {
        var cameras = new Dictionary<string, string>();
        try
        {
            var videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            foreach (FilterInfo device in videoDevices)
            {
                cameras[device.MonikerString] = device.Name;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error getting cameras: {ex.Message}");
        }
        return cameras;
    }

    private void OnTimerTick(object? state)
    {
        try
        {
            CheckAndTakePhoto();
            CleanUpOldPhotos();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Webcam service error: {ex.Message}");
        }
    }

    private void CheckAndTakePhoto()
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
                if (settings.EnableClassWebcam)
                {
                    interval = settings.ClassWebcamInterval;
                    isClass = true;
                    shouldTake = true;
                }
            }
            else if (current.Type == TimePointType.Break)
            {
                if (settings.EnableBreakWebcam)
                {
                    interval = settings.BreakWebcamInterval;
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
            if ((now - _lastPhotoTime).TotalMinutes >= interval)
            {
                TakePhoto(isClass, settings.SelectedCameraMoniker);
                _lastPhotoTime = now;
            }
        }
    }

    private bool ResolveIsClassTime()
    {
        var now = DateTime.Now.TimeOfDay;
        var (current, _) = ScheduleService.Instance.GetCurrentAndNextTimePoint(now);
        return current != null && current.Type == TimePointType.Class;
    }

    private void TakePhoto(bool isClass, string cameraMoniker)
    {
        if (string.IsNullOrEmpty(cameraMoniker)) return;

        // Run in a separate thread to avoid blocking UI or Timer
        ThreadPool.QueueUserWorkItem(_ =>
        {
            VideoCaptureDevice? videoSource = null;
            try
            {
                videoSource = new VideoCaptureDevice(cameraMoniker);
                
                // Wait for a frame
                var frameReceived = new ManualResetEvent(false);
                Bitmap? capturedBitmap = null;

                void NewFrameHandler(object sender, NewFrameEventArgs eventArgs)
                {
                    try
                    {
                        if (capturedBitmap == null)
                        {
                            capturedBitmap = (Bitmap)eventArgs.Frame.Clone();
                            frameReceived.Set();
                            if (videoSource.IsRunning)
                            {
                                videoSource.SignalToStop();
                            }
                        }
                    }
                    catch
                    {
                        // Ignore frame error
                    }
                }

                videoSource.NewFrame += NewFrameHandler;
                videoSource.Start();

                // Wait up to 5 seconds for a frame
                if (frameReceived.WaitOne(5000))
                {
                    // Give it a small delay to ensure stop signal is processed if needed, or just proceed
                    if (capturedBitmap != null)
                    {
                        SaveBitmap(capturedBitmap, isClass);
                        capturedBitmap.Dispose();
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Webcam capture timeout");
                    videoSource.SignalToStop();
                }
                
                videoSource.NewFrame -= NewFrameHandler;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to take photo: {ex.Message}");
            }
            finally
            {
                 if (videoSource != null)
                 {
                     if (videoSource.IsRunning)
                     {
                         videoSource.SignalToStop();
                         videoSource.WaitForStop();
                     }
                     videoSource = null;
                 }
            }
        });
    }

    private void SaveBitmap(Bitmap bitmap, bool isClass)
    {
        try
        {
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

            var fileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-{(isClass ? "Class" : "Break")}-Cam{extension}";
            var dayFolder = Path.Combine(_webcamDirectory, DateTime.Now.ToString("yyyy-MM-dd"));
            if (!Directory.Exists(dayFolder))
            {
                Directory.CreateDirectory(dayFolder);
            }
            var filePath = Path.Combine(dayFolder, fileName);

            var brightness = Math.Clamp(settings.WebcamBrightness, 0.1, 1.0);
            if (Math.Abs(brightness - 1.0) < 0.001)
            {
                bitmap.Save(filePath, imageFormat);
            }
            else
            {
                using var adjusted = new Bitmap(bitmap.Width, bitmap.Height);
                using var g = Graphics.FromImage(adjusted);
                using var attributes = new ImageAttributes();
                var matrix = new ColorMatrix(new[]
                {
                    new[] { (float)brightness, 0f, 0f, 0f, 0f },
                    new[] { 0f, (float)brightness, 0f, 0f, 0f },
                    new[] { 0f, 0f, (float)brightness, 0f, 0f },
                    new[] { 0f, 0f, 0f, 1f, 0f },
                    new[] { 0f, 0f, 0f, 0f, 1f }
                });
                attributes.SetColorMatrix(matrix);
                g.DrawImage(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, attributes);
                adjusted.Save(filePath, imageFormat);
            }

            LogService.Instance.Log("自动化", "拍照成功", "摄像头", $"文件保存至: {filePath}");
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Failed to save photo: {ex.Message}");
             LogService.Instance.Log("自动化", "拍照失败", "错误", ex.Message);
        }
    }
    
    private void CleanUpOldPhotos()
    {
        try
        {
            if (!Directory.Exists(_webcamDirectory)) return;

            var settings = SettingsService.Screenshot;

            var retentionDays = settings.RetentionDays;
            if (retentionDays < 1) retentionDays = 7;
            var cutoffDate = DateTime.Now.AddDays(-retentionDays);

            var maxStorageMb = settings.MaxStorageMB;
            if (maxStorageMb < 0) maxStorageMb = 0;
            var maxStorageBytes = maxStorageMb <= 0 ? 0 : maxStorageMb * 1024L * 1024L;

            var files = Directory.EnumerateFiles(_webcamDirectory, "*.*", SearchOption.AllDirectories)
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

             foreach (var fi in files.Where(f => f.Exists && f.CreationTime < cutoffDate).ToList())
            {
                try
                {
                    fi.Delete();
                    files.Remove(fi);
                }
                catch { }
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
            
            // Cleanup empty directories
            foreach (var dir in Directory.EnumerateDirectories(_webcamDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public List<ScreenshotItem> GetPhotos()
    {
        var list = new List<ScreenshotItem>();
        if (!Directory.Exists(_webcamDirectory)) return list;

        var files = Directory.GetFiles(_webcamDirectory, "*.*", SearchOption.AllDirectories)
            .Where(s => s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase) ||
                        s.EndsWith(".gif", StringComparison.OrdinalIgnoreCase));

        foreach (var file in files)
        {
            var fi = new FileInfo(file);
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

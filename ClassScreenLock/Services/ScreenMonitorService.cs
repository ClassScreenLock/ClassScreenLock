using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using ClassScreenLock.Models;
using Device = SharpDX.Direct3D11.Device;

namespace ClassScreenLock.Services;

/// <summary>
/// 被控端屏幕监控服务（SharpDX DXGI Desktop Duplication 实现，GPU 硬件加速）。
///
/// 工作流程：
///   1. 创建 D3D11 设备 → 枚举 DXGI 输出 → Desktop Duplication。
///   2. 按 fps 定时 AcquireNextFrame，获取 GPU 纹理。
///   3. 复制到 CPU 可读 staging texture，构造 Bitmap，JPEG 编码后推送。
/// </summary>
public class ScreenMonitorService : IDisposable
{
    private static ScreenMonitorService? _instance;
    public static ScreenMonitorService Instance => _instance ??= new ScreenMonitorService();

    private readonly object _lock = new();
    private Device? _d3dDevice;
    private OutputDuplication? _duplication;
    private Texture2D? _stagingTexture;
    private Texture2D? _desktopTexture;
    private int _captureWidth;
    private int _captureHeight;
    private Timer? _frameTimer;
    private CancellationTokenSource? _cts;
    private long _frameSeq;

    private int _activeFps = 10;
    private int _activeJpegQuality = 60;
    private int _activeMaxWidth = 1280;
    private int _activeMonitorIndex;

    private bool _isStreaming;
    private DateTime _streamStartTime;

    public bool IsStreaming => _isStreaming;
    public DateTime? StreamStartTime => _isStreaming ? _streamStartTime : null;
    public long FramesSent { get; private set; }
    public string? LastError { get; private set; }

    public event Action<bool, string?>? StatusChanged;

    private ScreenMonitorService() { }

    public void Initialize()
    {
        try
        {
            var ws = WebSocketService.Instance;
            ws.OnScreenMonitorStart += OnCentralStart;
            ws.OnScreenMonitorStop += OnCentralStop;
            ws.OnScreenMonitorSettings += OnCentralSettings;
            LogService.Instance.Log("Info", "ScreenMonitor", "ScreenMonitorService",
                "屏幕监控服务已初始化 (DXGI Desktop Duplication)");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "ScreenMonitor", "ScreenMonitorService", $"初始化失败: {ex.Message}");
        }
    }

    public bool StartLocal()
    {
        var s = SettingsService.ScreenMonitor;
        return Start(s.Fps, s.JpegQuality, s.MaxWidth, s.MonitorIndex);
    }

    private void OnCentralStart(int fps, int jpegQuality, int maxWidth, int monitorIndex)
    {
        var s = SettingsService.ScreenMonitor;
        if (!s.Enabled)
        {
            _ = WebSocketService.Instance.SendScreenMonitorStatusAsync(false, "disabled");
            return;
        }
        if (!s.AllowCentralControlStart)
        {
            _ = WebSocketService.Instance.SendScreenMonitorStatusAsync(false, "not_allowed");
            return;
        }
        Start(
            fps > 0 ? fps : s.Fps,
            jpegQuality > 0 ? jpegQuality : s.JpegQuality,
            maxWidth > 0 ? maxWidth : s.MaxWidth,
            monitorIndex >= 0 ? monitorIndex : s.MonitorIndex);
    }

    private void OnCentralStop() => Stop();

    private void OnCentralSettings(int fps, int jpegQuality, int maxWidth)
    {
        lock (_lock)
        {
            if (fps > 0) _activeFps = Math.Clamp(fps, 1, 30);
            if (jpegQuality > 0) _activeJpegQuality = Math.Clamp(jpegQuality, 1, 100);
            if (maxWidth >= 0) _activeMaxWidth = maxWidth;
        }
        ResetTimer();
        LogService.Instance.Log("Info", "ScreenMonitor", "ScreenMonitorService",
            $"已应用新参数: fps={_activeFps}, quality={_activeJpegQuality}, maxWidth={_activeMaxWidth}");
        // 通知集控端参数已实际生效
        _ = WebSocketService.Instance.SendScreenMonitorSettingsAppliedAsync(_activeFps, _activeJpegQuality, _activeMaxWidth);
    }

    public bool Start(int fps, int jpegQuality, int maxWidth, int monitorIndex)
    {
        lock (_lock)
        {
            if (_isStreaming) return true;

            _activeFps = Math.Clamp(fps, 1, 30);
            _activeJpegQuality = Math.Clamp(jpegQuality, 1, 100);
            _activeMaxWidth = Math.Max(0, maxWidth);
            _activeMonitorIndex = Math.Max(0, monitorIndex);
            FramesSent = 0;
            LastError = null;
            _frameSeq = 0;

            try
            {
                BuildDuplication();
                _cts = new CancellationTokenSource();
                _isStreaming = true;
                _streamStartTime = DateTime.Now;

                var periodMs = Math.Max(33, 1000 / _activeFps);
                _frameTimer = new Timer(OnTimerTick, _cts.Token, periodMs, periodMs);

                LogService.Instance.Log("Info", "ScreenMonitor", "ScreenMonitorService",
                    $"屏幕监控已启动 (DXGI): fps={_activeFps}, quality={_activeJpegQuality}, maxWidth={_activeMaxWidth}, monitor={_activeMonitorIndex}, {_captureWidth}x{_captureHeight}");

                _ = WebSocketService.Instance.SendScreenMonitorStatusAsync(true, null);
                StatusChanged?.Invoke(true, null);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "ScreenMonitor", "ScreenMonitorService", $"启动失败: {ex.Message}");
                LastError = ex.Message;
                _isStreaming = false;
                Cleanup();
                _ = WebSocketService.Instance.SendScreenMonitorStatusAsync(false, ex.Message);
                StatusChanged?.Invoke(false, ex.Message);
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isStreaming) return;
            _isStreaming = false;
            LogService.Instance.Log("Info", "ScreenMonitor", "ScreenMonitorService", "屏幕监控已停止");
            Cleanup();
            _ = WebSocketService.Instance.SendScreenMonitorStatusAsync(false, null);
            StatusChanged?.Invoke(false, null);
        }
    }

    private void BuildDuplication()
    {
        // 1. 创建 D3D11 设备
        _d3dDevice = new Device(SharpDX.Direct3D.DriverType.Hardware, DeviceCreationFlags.BgraSupport);

        using var factory = new Factory1();
        var adapter = factory.GetAdapter1(0);

        // 2. 枚举输出，选择目标显示器
        var output = adapter.GetOutput(0);
        int monitorCount = adapter.GetOutputCount();
        int idx = Math.Min(_activeMonitorIndex, monitorCount - 1);
        if (idx != 0)
        {
            output.Dispose();
            output = adapter.GetOutput(idx);
        }

        var desc = output.Description;
        _captureWidth = desc.DesktopBounds.Right - desc.DesktopBounds.Left;
        _captureHeight = desc.DesktopBounds.Bottom - desc.DesktopBounds.Top;

        // 3. 创建 DXGI Output Duplication
        var output1 = output.QueryInterface<Output1>();
        _duplication = output1.DuplicateOutput(_d3dDevice);

        // 4. 创建 CPU 可读的 staging texture（格式匹配 desktop texture: B8G8R8A8_UNORM）
        _stagingTexture = new Texture2D(_d3dDevice, new Texture2DDescription
        {
            CpuAccessFlags = CpuAccessFlags.Read,
            BindFlags = BindFlags.None,
            Format = Format.B8G8R8A8_UNorm,
            Width = _captureWidth,
            Height = _captureHeight,
            OptionFlags = ResourceOptionFlags.None,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging
        });

        output1.Dispose();
        output.Dispose();
        adapter.Dispose();
    }

    private void OnTimerTick(object? state)
    {
        if (state is CancellationToken token && token.IsCancellationRequested) return;
        if (!_isStreaming) return;

        _ = Task.Run(() =>
        {
            try
            {
                if (!_isStreaming || _duplication == null || _d3dDevice == null) return;

                // 尝试获取下一帧（超时 50ms，避免阻塞）
                var result = _duplication.TryAcquireNextFrame(50, out var frameInfo, out var desktopResource);
                if (result != Result.Ok) return;

                using (desktopResource)
                {
                    // 先拷贝数据，再 ReleaseFrame（释放后纹理内容失效）
                    using var desktopTexture = desktopResource.QueryInterface<Texture2D>();
                    _d3dDevice.ImmediateContext.CopyResource(desktopTexture, _stagingTexture);
                    _duplication.ReleaseFrame();

                    // Read pixels from staging texture
                    var dataBox = _d3dDevice.ImmediateContext.MapSubresource(
                        _stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);

                    try
                    {
                        using var bitmap = new Bitmap(_captureWidth, _captureHeight, PixelFormat.Format32bppArgb);
                        var bmpData = bitmap.LockBits(
                            new Rectangle(0, 0, _captureWidth, _captureHeight),
                            ImageLockMode.WriteOnly,
                            PixelFormat.Format32bppArgb);

                        // DXGI B8G8R8A8_UNorm 和 GDI+ Format32bppArgb 在内存中都是 [B,G,R,A]
                        int srcRow = dataBox.RowPitch;
                        int dstRow = bmpData.Stride;
                        int rowBytes = _captureWidth * 4;
                        byte[] buf = new byte[rowBytes];
                        for (int y = 0; y < _captureHeight; y++)
                        {
                            Marshal.Copy(IntPtr.Add(dataBox.DataPointer, y * srcRow), buf, 0, rowBytes);
                            Marshal.Copy(buf, 0, IntPtr.Add(bmpData.Scan0, y * dstRow), rowBytes);
                        }

                        bitmap.UnlockBits(bmpData);

                        // PNG 无损保存（避免 JPEG→JPEG 双重压缩损失画质）
                        using var pngStream = new MemoryStream();
                        bitmap.Save(pngStream, ImageFormat.Png);
                        var pngBytes = pngStream.ToArray();

                        // 一次编码：缩放 + JPEG 压缩
                        var compressed = CompressFrame(pngBytes, _activeMaxWidth, _activeJpegQuality, out int w, out int h);
                        if (compressed == null || compressed.Length == 0) return;

                        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        _ = WebSocketService.Instance.SendScreenMonitorFrameAsync(ts, "jpeg", w, h, compressed);
                        FramesSent++;
                        _frameSeq++;
                    }
                    finally
                    {
                        _d3dDevice.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                    }
                }
            }
            catch (SharpDXException sx) when (sx.ResultCode == SharpDX.DXGI.ResultCode.AccessLost)
            {
                // Display mode changed or UAC prompt → recreate
                LogService.Instance.Log("Warning", "ScreenMonitor", "ScreenMonitorService", "DXGI Access Lost, will restart on next tick");
                // Don't clean up here, let the next tick fail and the caller will see _isStreaming still true
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "ScreenMonitor", "ScreenMonitorService", $"帧处理失败: {ex.Message}");
            }
        }, _cts?.Token ?? CancellationToken.None);
    }

    private void ResetTimer()
    {
        lock (_lock)
        {
            if (!_isStreaming || _frameTimer == null) return;
            var periodMs = Math.Max(33, 1000 / _activeFps);
            _frameTimer.Change(periodMs, periodMs);
        }
    }

    private static byte[]? CompressFrame(byte[] sourceImage, int maxWidth, int quality, out int width, out int height)
    {
        width = 0;
        height = 0;
        try
        {
            using var srcStream = new MemoryStream(sourceImage);
            using var src = Image.FromStream(srcStream, false, true);
            width = src.Width;
            height = src.Height;

            Image? resized = src;
            try
            {
                if (maxWidth > 0 && src.Width > maxWidth)
                {
                    double ratio = (double)maxWidth / src.Width;
                    int newW = maxWidth;
                    int newH = Math.Max(1, (int)Math.Round(src.Height * ratio));
                    var newImg = new Bitmap(newW, newH, PixelFormat.Format24bppRgb);
                    using (var g = Graphics.FromImage(newImg))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(src, 0, 0, newW, newH);
                    }
                    resized = newImg;
                    width = newW;
                    height = newH;
                }

                var jpegCodec = ImageCodecInfo.GetImageEncoders()
                    .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
                if (jpegCodec == null)
                {
                    using var outStream = new MemoryStream();
                    resized.Save(outStream, ImageFormat.Jpeg);
                    return outStream.ToArray();
                }

                var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(
                    System.Drawing.Imaging.Encoder.Quality,
                    (long)Math.Clamp(quality, 1, 100));

                using var outStream2 = new MemoryStream();
                resized.Save(outStream2, jpegCodec, encoderParams);
                return outStream2.ToArray();
            }
            finally
            {
                if (!ReferenceEquals(resized, src)) resized.Dispose();
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "ScreenMonitor", "ScreenMonitorService", $"压缩失败，返回原图: {ex.Message}");
            return sourceImage;
        }
    }

    private void Cleanup()
    {
        try { _cts?.Cancel(); _cts = null; } catch { }
        try { _frameTimer?.Dispose(); _frameTimer = null; } catch { }

        try
        {
            if (_duplication != null)
            {
                try { _duplication.Dispose(); } catch { }
                _duplication = null;
            }
        }
        catch { }

        try
        {
            if (_stagingTexture != null)
            {
                try { _stagingTexture.Dispose(); } catch { }
                _stagingTexture = null;
            }
        }
        catch { }

        try
        {
            if (_desktopTexture != null)
            {
                try { _desktopTexture.Dispose(); } catch { }
                _desktopTexture = null;
            }
        }
        catch { }

        try
        {
            if (_d3dDevice != null)
            {
                try { _d3dDevice.Dispose(); } catch { }
                _d3dDevice = null;
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try { Stop(); } catch { }
    }
}

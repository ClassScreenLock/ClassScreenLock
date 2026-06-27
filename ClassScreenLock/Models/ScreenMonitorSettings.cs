using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

/// <summary>
/// 集控端屏幕监控设置。
/// 控制被控端是否允许被远程查看屏幕、上报帧率、画质、分辨率等。
/// </summary>
public partial class ScreenMonitorSettingsModel : ObservableObject
{
    private bool _enabled = true;
    /// <summary>
    /// 是否允许集控端查看本机屏幕。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    private int _fps = 10;
    /// <summary>
    /// 帧率（每秒发送多少张截图），范围 1~30。
    /// </summary>
    [JsonPropertyName("fps")]
    public int Fps
    {
        get => _fps;
        set => SetProperty(ref _fps, value);
    }

    private int _jpegQuality = 60;
    /// <summary>
    /// JPEG 压缩质量（1~100），数值越低越省带宽，画质越差。
    /// </summary>
    [JsonPropertyName("jpegQuality")]
    public int JpegQuality
    {
        get => _jpegQuality;
        set => SetProperty(ref _jpegQuality, value);
    }

    private int _maxWidth = 1280;
    /// <summary>
    /// 缩放后的最大宽度（像素），高度按比例计算。0 表示保持原分辨率。
    /// </summary>
    [JsonPropertyName("maxWidth")]
    public int MaxWidth
    {
        get => _maxWidth;
        set => SetProperty(ref _maxWidth, value);
    }

    private int _monitorIndex = 0;
    /// <summary>
    /// 显示器索引（0 表示主显示器），仅在多屏环境下生效。
    /// </summary>
    [JsonPropertyName("monitorIndex")]
    public int MonitorIndex
    {
        get => _monitorIndex;
        set => SetProperty(ref _monitorIndex, value);
    }

    private bool _showCursor = true;
    /// <summary>
    /// 是否在截图中包含鼠标光标。
    /// </summary>
    [JsonPropertyName("showCursor")]
    public bool ShowCursor
    {
        get => _showCursor;
        set => SetProperty(ref _showCursor, value);
    }

    private bool _allowCentralControlStart = true;
    /// <summary>
    /// 是否允许集控端主动发起屏幕监控。
    /// 关闭后，集控端只能响应本地主动发起的共享。
    /// </summary>
    [JsonPropertyName("allowCentralControlStart")]
    public bool AllowCentralControlStart
    {
        get => _allowCentralControlStart;
        set => SetProperty(ref _allowCentralControlStart, value);
    }
}

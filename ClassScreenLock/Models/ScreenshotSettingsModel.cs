using System.Text.Json.Serialization;

namespace ClassScreenLock.Models;

public class ScreenshotSettingsModel
{
    [JsonPropertyName("enableClassScreenshot")]
    public bool EnableClassScreenshot { get; set; } = true;

    [JsonPropertyName("classScreenshotInterval")]
    public int ClassScreenshotInterval { get; set; } = 5; // Minutes

    [JsonPropertyName("enableBreakScreenshot")]
    public bool EnableBreakScreenshot { get; set; } = true;

    [JsonPropertyName("breakScreenshotInterval")]
    public int BreakScreenshotInterval { get; set; } = 5; // Minutes

    [JsonPropertyName("retentionDays")]
    public int RetentionDays { get; set; } = 15;

    [JsonPropertyName("maxStorageMB")]
    public int MaxStorageMB { get; set; } = 2048;

    [JsonPropertyName("imageFormat")]
    public string ImageFormat { get; set; } = "PNG"; // PNG, JPEG, BMP, GIF
}

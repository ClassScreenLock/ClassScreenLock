using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.Models;

public partial class ScreenshotSettingsModel : ObservableObject
{
    private bool _enableClassScreenshot = true;
    [JsonPropertyName("enableClassScreenshot")]
    public bool EnableClassScreenshot 
    { 
        get => _enableClassScreenshot; 
        set => SetProperty(ref _enableClassScreenshot, value); 
    }

    private int _classScreenshotInterval = 5;
    [JsonPropertyName("classScreenshotInterval")]
    public int ClassScreenshotInterval 
    { 
        get => _classScreenshotInterval; 
        set => SetProperty(ref _classScreenshotInterval, value); 
    }

    private bool _enableBreakScreenshot = true;
    [JsonPropertyName("enableBreakScreenshot")]
    public bool EnableBreakScreenshot 
    { 
        get => _enableBreakScreenshot; 
        set => SetProperty(ref _enableBreakScreenshot, value); 
    }

    private int _breakScreenshotInterval = 5;
    [JsonPropertyName("breakScreenshotInterval")]
    public int BreakScreenshotInterval 
    { 
        get => _breakScreenshotInterval; 
        set => SetProperty(ref _breakScreenshotInterval, value); 
    }

    private int _retentionDays = 15;
    [JsonPropertyName("retentionDays")]
    public int RetentionDays 
    { 
        get => _retentionDays; 
        set => SetProperty(ref _retentionDays, value); 
    }

    private int _maxStorageMB = 2048;
    [JsonPropertyName("maxStorageMB")]
    public int MaxStorageMB 
    { 
        get => _maxStorageMB; 
        set => SetProperty(ref _maxStorageMB, value); 
    }

    private string _imageFormat = "PNG";
    [JsonPropertyName("imageFormat")]
    public string ImageFormat 
    { 
        get => _imageFormat; 
        set => SetProperty(ref _imageFormat, value); 
    }

    private bool _enableCaptureDebug = false;
    [JsonPropertyName("enableCaptureDebug")]
    public bool EnableCaptureDebug 
    { 
        get => _enableCaptureDebug; 
        set => SetProperty(ref _enableCaptureDebug, value); 
    }

    private bool _enableClassWebcam = false;
    [JsonPropertyName("enableClassWebcam")]
    public bool EnableClassWebcam 
    { 
        get => _enableClassWebcam; 
        set => SetProperty(ref _enableClassWebcam, value); 
    }

    private int _classWebcamInterval = 5;
    [JsonPropertyName("classWebcamInterval")]
    public int ClassWebcamInterval 
    { 
        get => _classWebcamInterval; 
        set => SetProperty(ref _classWebcamInterval, value); 
    }

    private bool _enableBreakWebcam = false;
    [JsonPropertyName("enableBreakWebcam")]
    public bool EnableBreakWebcam 
    { 
        get => _enableBreakWebcam; 
        set => SetProperty(ref _enableBreakWebcam, value); 
    }

    private int _breakWebcamInterval = 5;
    [JsonPropertyName("breakWebcamInterval")]
    public int BreakWebcamInterval 
    { 
        get => _breakWebcamInterval; 
        set => SetProperty(ref _breakWebcamInterval, value); 
    }

    private string _selectedCameraMoniker = string.Empty;
    [JsonPropertyName("selectedCameraMoniker")]
    public string SelectedCameraMoniker 
    { 
        get => _selectedCameraMoniker; 
        set => SetProperty(ref _selectedCameraMoniker, value); 
    }

    private double _webcamBrightness = 0.85;
    [JsonPropertyName("webcamBrightness")]
    public double WebcamBrightness 
    { 
        get => _webcamBrightness; 
        set => SetProperty(ref _webcamBrightness, value); 
    }
}

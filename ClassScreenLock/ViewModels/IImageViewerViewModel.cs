using System.ComponentModel;
using ClassScreenLock.Models;
using CommunityToolkit.Mvvm.Input;

namespace ClassScreenLock.ViewModels;

public interface IImageViewerViewModel : INotifyPropertyChanged
{
    bool IsMaximized { get; set; }
    bool IsImageViewerOpen { get; set; }
    double PanX { get; set; }
    double PanY { get; set; }
    double ZoomLevel { get; set; }
    double Rotation { get; set; }
    double ZoomMin { get; }
    double ZoomMax { get; }
    ScreenshotItem? CurrentViewingImage { get; set; }
    IRelayCommand CloseImageViewerCommand { get; }
    IRelayCommand PreviousImageCommand { get; }
    IRelayCommand NextImageCommand { get; }
    IRelayCommand ToggleMaximizeCommand { get; }
    IRelayCommand ZoomInCommand { get; }
    IRelayCommand ZoomOutCommand { get; }
    IRelayCommand ResetViewCommand { get; }
    IRelayCommand RotateLeftCommand { get; }
    IRelayCommand RotateRightCommand { get; }
    IRelayCommand DeleteScreenshotCommand { get; }
}

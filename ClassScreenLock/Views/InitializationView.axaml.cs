using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using System.ComponentModel;
using ClassScreenLock.ViewModels;
using ClassScreenLock.Helpers;

namespace ClassScreenLock.Views;

public partial class InitializationView : UserControl
{
    private int _lastIndex = 0;
    private Carousel? _carousel;

    public InitializationView()
    {
        InitializeComponent();
        _carousel = this.FindControl<Carousel>("InitCarousel");
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is InitializationViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            _lastIndex = vm.StepIndex;
        }
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InitializationViewModel.StepIndex))
        {
            if (sender is InitializationViewModel vm && _carousel != null)
            {
                var newIndex = vm.StepIndex;
                var w = _carousel.Bounds.Width;
                if (w <= 0) w = Bounds.Width;
                if (w <= 0) w = 900;
                var from = newIndex > _lastIndex ? w : -w;
                _carousel.RenderTransform = new Avalonia.Media.TranslateTransform(from, 0);
                await ThemeHelper.SlideControlHorizontal(_carousel, from, 0, 280);
                _lastIndex = newIndex;
            }
        }
    }
}


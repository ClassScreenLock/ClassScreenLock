using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia;
using Avalonia.Media;
using System.ComponentModel;
using ClassScreenLock.ViewModels;
using System.Threading.Tasks;

namespace ClassScreenLock.Views;

public partial class InitializationView : UserControl
{
    private Carousel? _carousel;
    private bool _isTransitioning = false;
    private StackPanel? _welcomeContent;
    private InitializationViewModel? _vm;

    public InitializationView()
    {
        InitializeComponent();
        _carousel = this.FindControl<Carousel>("InitCarousel");
        _welcomeContent = this.FindControl<StackPanel>("WelcomeContent");
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is InitializationViewModel vm)
        {
            _vm = vm;
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnLoaded(object? sender, System.EventArgs e)
    {
        if (_welcomeContent == null) return;

        // 内容初始状态：上方 200px 偏移 + 完全透明
        _welcomeContent.Opacity = 0;
        _welcomeContent.RenderTransform = new TranslateTransform(0, -200);

        _ = AnimateWelcomeEntranceAsync();
    }

    /// <summary>
    /// 欢迎内容从屏幕上方滑入（-200px → 0，透明度 0→1），ease-out-cubic 缓动。
    /// 动画完成后停留 2 秒，再触发 ViewModel 淡出。
    /// </summary>
    private async Task AnimateWelcomeEntranceAsync()
    {
        if (_welcomeContent?.RenderTransform is not TranslateTransform translate || _vm == null) return;

        // 等窗口渲染稳定
        await Task.Delay(150);

        var duration = 700;
        var interval = 16;
        var steps = duration / interval;

        // 滑入阶段
        for (int i = 1; i <= steps; i++)
        {
            var t = (double)i / steps;
            var eased = 1 - System.Math.Pow(1 - t, 3); // ease-out-cubic

            _welcomeContent!.Opacity = eased;
            translate.Y = -200 * (1 - eased);

            await Task.Delay(interval);
        }

        _welcomeContent!.Opacity = 1;
        translate.Y = 0;

        // 停留阶段：让用户看清内容
        await Task.Delay(2000);

        // 触发 ViewModel 淡出（Border 的 Opacity 绑定到 WelcomeOpacity）
        _vm.WelcomeOpacity = 0;
        await Task.Delay(500); // 等淡出动画完成
        _vm.IsWelcomeVisible = false;
    }

    /// <summary>
    /// 步骤切换时使用简洁的交叉淡入。
    /// </summary>
    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InitializationViewModel.StepIndex))
            return;

        if (_carousel == null || _isTransitioning)
            return;

        _isTransitioning = true;

        _carousel.Opacity = 0;
        await Task.Delay(300);

        _carousel.Opacity = 1;

        _isTransitioning = false;
    }
}

using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using ClassScreenLock.ViewModels;

namespace ClassScreenLock.Views;

/// <summary>
/// 加入组织向导内容（作为 ContentDialog 的 Content 使用）。
/// 遮罩、弹出动画、阴影均由 ContentDialog 提供，与删除确认对话框一致。
/// </summary>
public partial class JoinOrganizationDialog : UserControl
{
    private OrganizationViewModel? _vm;

    public JoinOrganizationDialog()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DataContextChanged += OnDataContextChanged;
        AttachViewModel(DataContext as OrganizationViewModel);
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        DataContextChanged -= OnDataContextChanged;
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        AttachViewModel(DataContext as OrganizationViewModel);
    }

    private void AttachViewModel(OrganizationViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _vm = vm;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OrganizationViewModel.CurrentJoinStep))
        {
            AnimateStepTransition();
        }
    }

    /// <summary>
    /// 步骤切换时淡入新内容：先将透明度设为 0，下一帧恢复为 1，触发 DoubleTransition 动画
    /// </summary>
    private void AnimateStepTransition()
    {
        var content = this.FindControl<Panel>("StepContentPanel");
        if (content == null) return;

        content.Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            content.Opacity = 1;
        }, DispatcherPriority.Render);
    }
}

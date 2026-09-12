using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ClassScreenLock.ViewModels;
using FluentAvalonia.UI.Controls;

namespace ClassScreenLock.Views;

public partial class OrganizationView : UserControl
{
    private OrganizationViewModel? _vm;
    private ContentDialog? _joinDialog;
    private bool _joinDialogShowing;

    public OrganizationView()
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

    private void OnDataContextChanged(object? sender, EventArgs e)
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
        if (e.PropertyName == nameof(OrganizationViewModel.IsJoinDialogOpen))
        {
            if (_vm!.IsJoinDialogOpen)
            {
                _ = ShowJoinDialogAsync();
            }
            else
            {
                HideJoinDialog();
            }
        }
    }

    /// <summary>
    /// 以 ContentDialog 形式弹出加入组织向导：
    /// 遮罩、弹出动画、阴影均由 ContentDialog 提供，与删除确认对话框一致，并覆盖整个主窗口。
    /// </summary>
    private async Task ShowJoinDialogAsync()
    {
        if (_joinDialogShowing) return;
        _joinDialogShowing = true;

        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        var content = new JoinOrganizationDialog { DataContext = _vm };
        var dialog = new ContentDialog
        {
            // 标题、步骤指示器、导航按钮均由内容区自绘
            Content = content
        };
        dialog.Closed += OnJoinDialogClosed;
        _joinDialog = dialog;

        if (owner != null)
        {
            await dialog.ShowAsync(owner);
        }
        else
        {
            await dialog.ShowAsync();
        }
    }

    private void HideJoinDialog()
    {
        if (_joinDialog is { } dialog)
        {
            dialog.Hide();
        }
    }

    /// <summary>
    /// 弹窗关闭（点击 X、Esc 或加入成功）时清理状态；
    /// 若用户通过 Esc 等系统方式关闭，同步回 ViewModel。
    /// </summary>
    private void OnJoinDialogClosed(object? sender, EventArgs e)
    {
        if (_joinDialog != null)
        {
            _joinDialog.Closed -= OnJoinDialogClosed;
            _joinDialog = null;
        }
        _joinDialogShowing = false;

        if (_vm is { IsJoinDialogOpen: true, IsLoading: false })
        {
            _vm.CloseJoinDialogCommand.Execute(null);
        }
    }
}

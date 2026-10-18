using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Uninstaller.Services;

namespace ClassScreenLock.Uninstaller.ViewModels;

/// <summary>
/// 卸载主窗口 ViewModel
/// </summary>
public partial class UninstallViewModel : ViewModelBase
{
    private readonly UninstallService _uninstallService = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private bool _isUninstalling;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _currentStepText = "准备卸载";

    [ObservableProperty]
    private string _detailText = string.Empty;

    [ObservableProperty]
    private string _resultTitle = string.Empty;

    [ObservableProperty]
    private string _resultMessage = string.Empty;

    /// <summary>
    /// 是否在确认页（卸载前）
    /// </summary>
    public bool IsConfirmPage => !IsUninstalling && !IsCompleted && !IsFailed;

    /// <summary>
    /// 是否显示进度页
    /// </summary>
    public bool IsProgressPage => IsUninstalling;

    /// <summary>
    /// 是否显示结果页
    /// </summary>
    public bool IsResultPage => IsCompleted || IsFailed;

    /// <summary>
    /// 窗口是否可以关闭
    /// 卸载进行中时禁止关闭，防止中断卸载流程
    /// </summary>
    public bool CanCloseWindow => !IsUninstalling;

    /// <summary>
    /// 警告列表
    /// </summary>
    public ObservableCollection<string> Warnings { get; } = new();

    /// <summary>
    /// 卸载完成事件（View 订阅以关闭窗口）
    /// </summary>
    public event EventHandler? UninstallFinished;

    /// <summary>
    /// 请求关闭窗口事件（View 订阅，用于窗口关闭拦截）
    /// </summary>
    public event EventHandler<WindowCloseRequestEventArgs>? CloseRequested;

    partial void OnIsUninstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConfirmPage));
        OnPropertyChanged(nameof(IsProgressPage));
        OnPropertyChanged(nameof(IsResultPage));
        OnPropertyChanged(nameof(CanCloseWindow));
    }

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConfirmPage));
        OnPropertyChanged(nameof(IsProgressPage));
        OnPropertyChanged(nameof(IsResultPage));
        OnPropertyChanged(nameof(CanCloseWindow));
    }

    partial void OnIsFailedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConfirmPage));
        OnPropertyChanged(nameof(IsProgressPage));
        OnPropertyChanged(nameof(IsResultPage));
        OnPropertyChanged(nameof(CanCloseWindow));
    }

    [RelayCommand]
    private async Task StartUninstall()
    {
        if (IsUninstalling) return;

        IsUninstalling = true;
        IsCompleted = false;
        IsFailed = false;
        Warnings.Clear();
        Progress = 0;

        // 注意：卸载过程中不再支持取消，CancellationTokenSource 仅作为服务接口参数传入，
        // 不会通过 UI 触发取消。卸载一旦开始必须执行到底。
        _cts = new CancellationTokenSource();

        var progress = new Progress<UninstallProgressReport>(report =>
        {
            Progress = report.Progress;
            CurrentStepText = report.StepDescription;
            if (!string.IsNullOrEmpty(report.Detail))
            {
                DetailText = report.Detail;
            }
        });

        try
        {
            var result = await _uninstallService.RunUninstallAsync(progress, _cts.Token);

            if (result.Success)
            {
                IsCompleted = true;
                ResultTitle = "卸载成功";
                ResultMessage = "ClassScreenLock 已成功从您的电脑中卸载。";
            }
            else
            {
                IsFailed = true;
                ResultTitle = "卸载失败";
                ResultMessage = result.Message;
            }

            // 显示警告
            foreach (var warning in result.Warnings)
            {
                Warnings.Add(warning);
            }
        }
        catch (Exception ex)
        {
            IsFailed = true;
            ResultTitle = "卸载失败";
            ResultMessage = ex.Message;
        }
        finally
        {
            IsUninstalling = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 确认页的取消按钮：直接退出（卸载尚未开始，可以取消）
    /// </summary>
    [RelayCommand]
    private void CancelConfirm()
    {
        if (IsConfirmPage)
        {
            Environment.Exit(0);
        }
    }

    [RelayCommand]
    private void Finish()
    {
        // 如果卸载成功，安排自删除
        if (IsCompleted)
        {
            UninstallService.ScheduleSelfDestruct();
        }

        UninstallFinished?.Invoke(this, EventArgs.Empty);
        Environment.Exit(0);
    }

    /// <summary>
    /// 尝试关闭窗口（由 View 在 Closing 事件中调用）
    /// </summary>
    /// <returns>true=允许关闭，false=阻止关闭</returns>
    public bool TryCloseWindow()
    {
        if (!CanCloseWindow)
        {
            // 卸载进行中，不允许关闭
            CloseRequested?.Invoke(this, new WindowCloseRequestEventArgs(false, "卸载正在进行中，无法关闭窗口"));
            return false;
        }

        return true;
    }
}

/// <summary>
/// 窗口关闭请求事件参数
/// </summary>
public class WindowCloseRequestEventArgs : EventArgs
{
    public bool CanClose { get; }
    public string Message { get; }

    public WindowCloseRequestEventArgs(bool canClose, string message)
    {
        CanClose = canClose;
        Message = message;
    }
}

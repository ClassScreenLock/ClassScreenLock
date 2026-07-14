using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Diagnostics;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appName = "ClassScreenLock";
    
    [ObservableProperty]
    private string _appVersion = "V1.15.37.3639 - Creeper";
    
    [ObservableProperty]
    private string _appDescription = "一款专业的课堂屏幕锁定工具，帮助教师管理课堂环境，提高教学效率。";
    
    [ObservableProperty]
    private string _developer = "JiuGuLiXiaoNiu（旧故里小牛）";
    
    [ObservableProperty]
    private string _copyright = "© 2025-2026 JiuGuLiXiaoNiu（旧故里小牛）";
    
    [ObservableProperty]
    private string _license = "本软件基于GNU GPL3.0开源";
    
    [ObservableProperty]
    private string _workspaceInfo = " ";

    [ObservableProperty]
    private string _repositoryUrl = "https://github.com/jiugulixiaoniu/ClassScreenLock";

    [ObservableProperty]
    private string _userAgreementUrl = "https://classscreenlock.github.io/eula";

    [ObservableProperty]
    private string _privacyPolicyUrl = "https://classscreenlock.github.io/eula";

    public AboutViewModel()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        if (version != null)
        {
            AppVersion = $"V{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }

    [RelayCommand]
    private void OpenRepository()
    {
        OpenUrlWithPowerShell(RepositoryUrl);
    }

    [RelayCommand]
    private void OpenUserAgreement()
    {
        OpenUrlWithPowerShell(UserAgreementUrl);
    }

    private void OpenUrlWithPowerShell(string url)
    {
        try
        {
            // 使用 explorer.exe 打开链接 - 以用户权限运行，解决管理员权限问题
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = url,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            NotificationService.Instance.ShowError($"无法打开链接: {ex.Message}");
        }
    }
}

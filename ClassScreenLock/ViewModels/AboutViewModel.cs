using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;

namespace ClassScreenLock.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appName = "ClassScreenLock";
    
    [ObservableProperty]
    private string _appVersion = "V1.15.32.3176 - Creeper";
    
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
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private void OpenUserAgreement()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UserAgreementUrl) { UseShellExecute = true });
        }
        catch { }
    }
}

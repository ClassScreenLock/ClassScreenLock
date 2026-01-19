using CommunityToolkit.Mvvm.ComponentModel;

namespace ClassScreenLock.ViewModels;

public partial class AboutViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _appName = "ClassScreenLock";
    
    [ObservableProperty]
    private string _appVersion = "V1.1.5.1005";
    
    [ObservableProperty]
    private string _appDescription = "一款专业的课堂屏幕锁定工具，帮助教师管理课堂环境，提高教学效率。";
    
    [ObservableProperty]
    private string _developer = "JiuGuLiXiaoNiu（旧故里小牛）";
    
    [ObservableProperty]
    private string _copyright = "© 2025-2026 JiuGuLiXiaoNiu（旧故里小牛）";
    
    [ObservableProperty]
    private string _license = "本软件基于GNU GPL3.0开源";
    
    [ObservableProperty]
    private string _workspaceInfo = "工作空间：D:/46517/Documents/Rider/ClassScreenLock";
}
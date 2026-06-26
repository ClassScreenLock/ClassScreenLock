using System.Collections.Generic;
using System.Linq;

namespace ClassScreenLock.Models;

/// <summary>
/// 快速操作目录：预定义 30+ 个可用功能
/// </summary>
public static class QuickActionCatalog
{
    /// <summary>
    /// 内置的 4 个默认快速操作（向后兼容）
    /// </summary>
    public static readonly string[] DefaultActionIds = new[]
    {
        "navigate.appManagement",
        "navigate.network",
        "navigate.securityCenter",
        "navigate.settings"
    };

    /// <summary>
    /// 完整目录
    /// </summary>
    public static readonly IReadOnlyList<QuickActionDefinition> All = new List<QuickActionDefinition>
    {
        // ========== 导航类（页面跳转）==========
        new() { Id = "navigate.appManagement", LabelKey = "Home_QuickAppManagement", DescriptionKey = "QA_Desc_AppManagement", Category = "QA_Cat_Navigation", IconName = "Apps", AccentColor = "#0078D4", TargetId = "appManagement" },
        new() { Id = "navigate.network", LabelKey = "Home_QuickNetwork", DescriptionKey = "QA_Desc_Network", Category = "QA_Cat_Navigation", IconName = "Globe", AccentColor = "#0078D4", TargetId = "network" },
        new() { Id = "navigate.securityCenter", LabelKey = "Home_QuickSecurityCenter", DescriptionKey = "QA_Desc_SecurityCenter", Category = "QA_Cat_Navigation", IconName = "Shield", AccentColor = "#107C10", TargetId = "securityCenter" },
        new() { Id = "navigate.settings", LabelKey = "Home_QuickSettings", DescriptionKey = "QA_Desc_Settings", Category = "QA_Cat_Navigation", IconName = "Settings", AccentColor = "#5C5C5C", TargetId = "settings" },
        new() { Id = "navigate.schedule", LabelKey = "Home_QuickSchedule", DescriptionKey = "QA_Desc_Schedule", Category = "QA_Cat_Navigation", IconName = "Calendar", AccentColor = "#0078D4", TargetId = "schedule" },
        new() { Id = "navigate.securityLogs", LabelKey = "Home_QuickSecurityLogs", DescriptionKey = "QA_Desc_SecurityLogs", Category = "QA_Cat_Navigation", IconName = "History", AccentColor = "#5C5C5C", TargetId = "securityLogs" },
        new() { Id = "navigate.automation", LabelKey = "Home_QuickAutomation", DescriptionKey = "QA_Desc_Automation", Category = "QA_Cat_Navigation", IconName = "Flash", AccentColor = "#FF8C00", TargetId = "automation" },
        new() { Id = "navigate.organization", LabelKey = "Home_QuickOrganization", DescriptionKey = "QA_Desc_Organization", Category = "QA_Cat_Navigation", IconName = "People", AccentColor = "#5C5C5C", TargetId = "organization" },
        new() { Id = "navigate.screenshotHistory", LabelKey = "Home_QuickScreenshotHistory", DescriptionKey = "QA_Desc_ScreenshotHistory", Category = "QA_Cat_Navigation", IconName = "Image", AccentColor = "#5C5C5C", TargetId = "screenshotHistory" },
        new() { Id = "navigate.webcamHistory", LabelKey = "Home_QuickWebcamHistory", DescriptionKey = "QA_Desc_WebcamHistory", Category = "QA_Cat_Navigation", IconName = "Camera", AccentColor = "#5C5C5C", TargetId = "webcamHistory" },
        new() { Id = "navigate.about", LabelKey = "Home_QuickAbout", DescriptionKey = "QA_Desc_About", Category = "QA_Cat_Navigation", IconName = "Info", AccentColor = "#5C5C5C", TargetId = "about" },

        // ========== 锁屏控制类（命令）==========
        new() { Id = "command.startLock", LabelKey = "Home_QuickStartLock", DescriptionKey = "QA_Desc_StartLock", Category = "QA_Cat_LockControl", IconName = "LockClosed", AccentColor = "#E81123", IsCommand = true, TargetId = "startLock" },
        new() { Id = "command.unlock", LabelKey = "Home_QuickUnlock", DescriptionKey = "QA_Desc_Unlock", Category = "QA_Cat_LockControl", IconName = "LockOpen", AccentColor = "#107C10", IsCommand = true, TargetId = "unlock" },
        new() { Id = "command.protectionMode", LabelKey = "Home_QuickProtectionMode", DescriptionKey = "QA_Desc_ProtectionMode", Category = "QA_Cat_LockControl", IconName = "Shield", AccentColor = "#FF8C00", IsCommand = true, TargetId = "protectionMode" },
        new() { Id = "command.refreshStatus", LabelKey = "Home_QuickRefreshStatus", DescriptionKey = "QA_Desc_RefreshStatus", Category = "QA_Cat_LockControl", IconName = "ArrowSync", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "refreshStatus" },

        // ========== 视图与主题类 ==========
        new() { Id = "command.toggleDarkMode", LabelKey = "Home_QuickToggleTheme", DescriptionKey = "QA_Desc_ToggleTheme", Category = "QA_Cat_Appearance", IconName = "WeatherMoon", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "toggleDarkMode" },
        new() { Id = "command.minimizeWindow", LabelKey = "Home_QuickMinimize", DescriptionKey = "QA_Desc_Minimize", Category = "QA_Cat_Appearance", IconName = "Subtract", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "minimize" },
        new() { Id = "command.toggleSidebar", LabelKey = "Home_QuickToggleSidebar", DescriptionKey = "QA_Desc_ToggleSidebar", Category = "QA_Cat_Appearance", IconName = "Navigation", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "toggleSidebar" },

        // ========== 应用与拦截管理类 ==========
        new() { Id = "command.refreshAppList", LabelKey = "Home_QuickRefreshApps", DescriptionKey = "QA_Desc_RefreshApps", Category = "QA_Cat_Management", IconName = "ArrowSync", AccentColor = "#0078D4", IsCommand = true, TargetId = "refreshAppList" },
        new() { Id = "command.openLockSettings", LabelKey = "Home_QuickLockSettings", DescriptionKey = "QA_Desc_LockSettings", Category = "QA_Cat_Management", IconName = "LockClosed", AccentColor = "#E81123", IsCommand = true, TargetId = "openLockSettings" },
        new() { Id = "command.openBreakSettings", LabelKey = "Home_QuickBreakSettings", DescriptionKey = "QA_Desc_BreakSettings", Category = "QA_Cat_Management", IconName = "DrinkToGo", AccentColor = "#FF8C00", IsCommand = true, TargetId = "openBreakSettings" },

        // ========== 数据与日志类 ==========
        new() { Id = "command.openScreenshot", LabelKey = "Home_QuickOpenScreenshot", DescriptionKey = "QA_Desc_OpenScreenshot", Category = "QA_Cat_Data", IconName = "Screenshot", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "openScreenshot" },
        new() { Id = "command.openWebcam", LabelKey = "Home_QuickOpenWebcam", DescriptionKey = "QA_Desc_OpenWebcam", Category = "QA_Cat_Data", IconName = "Camera", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "openWebcam" },
        new() { Id = "command.exportLogs", LabelKey = "Home_QuickExportLogs", DescriptionKey = "QA_Desc_ExportLogs", Category = "QA_Cat_Data", IconName = "ArrowExport", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "exportLogs" },
        new() { Id = "command.clearLogs", LabelKey = "Home_QuickClearLogs", DescriptionKey = "QA_Desc_ClearLogs", Category = "QA_Cat_Data", IconName = "Delete", AccentColor = "#E81123", IsCommand = true, TargetId = "clearLogs" },
        new() { Id = "command.openDataFolder", LabelKey = "Home_QuickOpenDataFolder", DescriptionKey = "QA_Desc_OpenDataFolder", Category = "QA_Cat_Data", IconName = "Folder", AccentColor = "#FFA500", IsCommand = true, TargetId = "openDataFolder" },

        // ========== 备份与恢复类 ==========
        new() { Id = "command.backupConfig", LabelKey = "Home_QuickBackupConfig", DescriptionKey = "QA_Desc_BackupConfig", Category = "QA_Cat_Backup", IconName = "Save", AccentColor = "#107C10", IsCommand = true, TargetId = "backupConfig" },
        new() { Id = "command.restoreConfig", LabelKey = "Home_QuickRestoreConfig", DescriptionKey = "QA_Desc_RestoreConfig", Category = "QA_Cat_Backup", IconName = "ArrowImport", AccentColor = "#FF8C00", IsCommand = true, TargetId = "restoreConfig" },
        new() { Id = "command.exportSchedules", LabelKey = "Home_QuickExportSchedules", DescriptionKey = "QA_Desc_ExportSchedules", Category = "QA_Cat_Backup", IconName = "ArrowExport", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "exportSchedules" },
        new() { Id = "command.importSchedules", LabelKey = "Home_QuickImportSchedules", DescriptionKey = "QA_Desc_ImportSchedules", Category = "QA_Cat_Backup", IconName = "ArrowImport", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "importSchedules" },

        // ========== 系统工具类 ==========
        new() { Id = "command.openSystemInfo", LabelKey = "Home_QuickSystemInfo", DescriptionKey = "QA_Desc_SystemInfo", Category = "QA_Cat_System", IconName = "Info", AccentColor = "#0078D4", IsCommand = true, TargetId = "openSystemInfo" },
        new() { Id = "command.openServices", LabelKey = "Home_QuickServices", DescriptionKey = "QA_Desc_Services", Category = "QA_Cat_System", IconName = "Server", AccentColor = "#5C5C5C", IsCommand = true, TargetId = "openServices" },
        new() { Id = "command.checkUpdate", LabelKey = "Home_QuickCheckUpdate", DescriptionKey = "QA_Desc_CheckUpdate", Category = "QA_Cat_System", IconName = "ArrowSync", AccentColor = "#107C10", IsCommand = true, TargetId = "checkUpdate" },
        new() { Id = "command.openHelp", LabelKey = "Home_QuickHelp", DescriptionKey = "QA_Desc_Help", Category = "QA_Cat_System", IconName = "QuestionCircle", AccentColor = "#0078D4", IsCommand = true, TargetId = "openHelp" }
    };

    public static QuickActionDefinition? FindById(string id)
        => All.FirstOrDefault(d => d.Id == id);

    public static IEnumerable<IGrouping<string, QuickActionDefinition>> GroupByCategory()
        => All.GroupBy(d => d.Category);
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ClassScreenLock.Services;

namespace ClassScreenLock.Helpers;

/// <summary>
/// 注册表备份导出的共享逻辑。主界面「应用管理」与初始化引导都通过它导出注册表，
/// 避免逻辑重复：弹出保存对话框由用户自选保存位置（可取消），
/// 用带不确定态进度条的模态框包裹 reg.exe 导出过程，完成后自动关闭并提示结果。
/// </summary>
public static class RegistryBackupHelper
{
    /// <summary>
    /// 导出完整 HKLM 注册表到用户选择的 .reg 文件。用户取消保存对话框则静默返回。
    /// </summary>
    public static async Task ExportAsync()
    {
        var win = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var provider = win?.StorageProvider;
        if (provider == null)
        {
            NotificationService.Instance.ShowError("无法打开保存对话框");
            return;
        }

        var options = new FilePickerSaveOptions
        {
            Title = "导出注册表备份",
            SuggestedFileName = $"registry-backup-{DateTime.Now:yyyyMMdd-HHmmss}.reg",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new FilePickerFileType("注册表文件") { Patterns = new[] { "*.reg" } }
            }
        };

        var file = await provider.SaveFilePickerAsync(options);
        if (file == null) return; // 用户取消保存

        var targetPath = file.TryGetLocalPath();
        if (string.IsNullOrEmpty(targetPath))
        {
            NotificationService.Instance.ShowError("无法获取保存路径");
            return;
        }

        // 用带不确定态进度条的模态对话框包裹导出过程：reg.exe 导出 HKLM 可能耗时数秒~数十秒，
        // 期间禁止用户误操作，进度框在导出结束后自动关闭。
        var ok = false;
        await NotificationService.Instance.RunWithProgressAsync(
            "正在导出注册表",
            "正在导出注册表备份，请稍候，期间请勿关闭程序...",
            async () =>
            {
                ok = await Task.Run(() =>
                {
                    try
                    {
                        // reg.exe export HKLM <file> /y —— 导出整个 HKEY_LOCAL_MACHINE，覆盖已存在文件。
                        if (File.Exists(targetPath))
                            File.Delete(targetPath);

                        var psi = new ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = $"export HKLM \"{targetPath}\" /y",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardError = true,
                            RedirectStandardOutput = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc == null) return false;
                        proc.WaitForExit();
                        return proc.ExitCode == 0 && File.Exists(targetPath);
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Error", "AppManagement", "ExportRegistry", $"导出注册表失败: {ex.Message}");
                        return false;
                    }
                });
            });

        if (ok)
            NotificationService.Instance.ShowSuccess("注册表已导出备份");
        else
            NotificationService.Instance.ShowError("注册表导出失败，请检查权限或磁盘空间");
    }
}

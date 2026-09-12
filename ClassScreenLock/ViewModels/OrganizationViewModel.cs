using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClassScreenLock.Services;

namespace ClassScreenLock.ViewModels;

public partial class OrganizationViewModel : ViewModelBase
{
    private readonly Services.OrganizationService _organizationService;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _organizationId = string.Empty;

    [ObservableProperty]
    private string _contactPhone = string.Empty;

    [ObservableProperty]
    private string _className = string.Empty;

    [ObservableProperty]
    private string _personInCharge = string.Empty;

    [ObservableProperty]
    private bool _hasJoinedOrganization;

    [ObservableProperty]
    private string _organizationName = string.Empty;

    [ObservableProperty]
    private string _organizationDescription = string.Empty;

    [ObservableProperty]
    private DateTime? _joinedAt;

    [ObservableProperty]
    private DateTime? _lastSyncTime;

    [ObservableProperty]
    private string _joinedAtText = string.Empty;

    [ObservableProperty]
    private string _lastSyncTimeText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    // ========== 向导弹窗状态 ==========
    [ObservableProperty]
    private bool _isJoinDialogOpen;

    [ObservableProperty]
    private int _currentJoinStep;

    /// <summary>导入配置后的状态提示（仅在步骤 0 显示）</summary>
    [ObservableProperty]
    private string _importStatusMessage = string.Empty;

    /// <summary>
    /// 主页面是否显示加载状态（向导打开时不显示，避免与向导内进度条重复）
    /// </summary>
    public bool IsPageLoading => IsLoading && !IsJoinDialogOpen;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool HasSuccess => !string.IsNullOrEmpty(SuccessMessage);
    public bool HasImportStatus => !string.IsNullOrEmpty(ImportStatusMessage);

    // ========== 步骤指示器 ==========
    public int StepNumber => CurrentJoinStep + 1;
    public bool IsStep0 => CurrentJoinStep == 0;
    public bool IsStep1 => CurrentJoinStep == 1;
    public bool IsStep2 => CurrentJoinStep == 2;
    public bool IsStep3 => CurrentJoinStep == 3;

    public double Step0Opacity => 1;
    public double Step1Opacity => CurrentJoinStep >= 1 ? 1 : 0.2;
    public double Step2Opacity => CurrentJoinStep >= 2 ? 1 : 0.2;
    public double Step3Opacity => CurrentJoinStep >= 3 ? 1 : 0.2;

    // ========== 步骤验证 ==========
    public bool CanNextFromStep0 => !string.IsNullOrWhiteSpace(ServerUrl);
    public bool CanNextFromStep1 => !string.IsNullOrWhiteSpace(OrganizationId);
    public bool CanNextFromStep2 =>
        !string.IsNullOrWhiteSpace(ContactPhone) &&
        !string.IsNullOrWhiteSpace(ClassName) &&
        !string.IsNullOrWhiteSpace(PersonInCharge);

    /// <summary>"下一步"按钮是否可用</summary>
    public bool CanProceed => CurrentJoinStep switch
    {
        0 => CanNextFromStep0,
        1 => CanNextFromStep1,
        2 => CanNextFromStep2,
        _ => true
    };

    public OrganizationViewModel()
    {
        _organizationService = new Services.OrganizationService();
        // 延迟加载组织信息，确保服务初始化完成
        Task.Run(async () =>
        {
            await _organizationService.LoadOrganizationAsync();
            await Task.Delay(100); // 短暂延迟确保状态更新
            await Task.Run(() => LoadCurrentOrganization());
        });
    }

    // ========== 属性变更通知 ==========

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPageLoading));
    }

    partial void OnIsJoinDialogOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(IsPageLoading));
    }

    partial void OnCurrentJoinStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(StepNumber));
        OnPropertyChanged(nameof(Step0Opacity));
        OnPropertyChanged(nameof(Step1Opacity));
        OnPropertyChanged(nameof(Step2Opacity));
        OnPropertyChanged(nameof(Step3Opacity));
        OnPropertyChanged(nameof(CanProceed));
        // 步骤切换时清除导入提示
        ImportStatusMessage = string.Empty;
    }

    partial void OnServerUrlChanged(string value)
    {
        OnPropertyChanged(nameof(CanNextFromStep0));
        OnPropertyChanged(nameof(CanProceed));
    }

    partial void OnOrganizationIdChanged(string value)
    {
        OnPropertyChanged(nameof(CanNextFromStep1));
        OnPropertyChanged(nameof(CanProceed));
    }

    partial void OnContactPhoneChanged(string value)
    {
        OnPropertyChanged(nameof(CanNextFromStep2));
        OnPropertyChanged(nameof(CanProceed));
    }

    partial void OnClassNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanNextFromStep2));
        OnPropertyChanged(nameof(CanProceed));
    }

    partial void OnPersonInChargeChanged(string value)
    {
        OnPropertyChanged(nameof(CanNextFromStep2));
        OnPropertyChanged(nameof(CanProceed));
    }

    // ========== 加载组织信息 ==========

    private void LoadCurrentOrganization()
    {
        Console.WriteLine("[DEBUG] OrganizationViewModel: 开始加载当前组织信息");

        var org = _organizationService.CurrentOrganization;

        if (org != null)
        {
            Console.WriteLine($"[DEBUG] OrganizationViewModel: 找到组织信息，ID={org.Id}, Name={org.Name}, ServerUrl={org.ServerUrl}, IsActive={org.IsActive}");

            if (!string.IsNullOrEmpty(org.ServerUrl))
            {
                HasJoinedOrganization = true;
                OrganizationName = org.Name;
                OrganizationDescription = org.Description;
                JoinedAt = org.JoinedAt;
                LastSyncTime = org.LastSyncTime;
                ServerUrl = org.ServerUrl;
                OrganizationId = org.Id;
                JoinedAtText = org.JoinedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
                LastSyncTimeText = org.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";

                if (org.IsActive)
                {
                    _ = _organizationService.DeviceService.RegisterDeviceAsync();
                }
            }
            else
            {
                HasJoinedOrganization = false;
                ClearOrganizationInfo();
            }
        }
        else
        {
            HasJoinedOrganization = false;
            ClearOrganizationInfo();
        }

        Console.WriteLine($"[DEBUG] OrganizationViewModel: 加载完成，HasJoinedOrganization={HasJoinedOrganization}");
    }

    private void ClearOrganizationInfo()
    {
        OrganizationName = string.Empty;
        OrganizationDescription = string.Empty;
        JoinedAt = null;
        LastSyncTime = null;
        JoinedAtText = string.Empty;
        LastSyncTimeText = string.Empty;
    }

    /// <summary>
    /// 刷新组织信息（供外部调用）
    /// </summary>
    public void RefreshOrganizationInfo()
    {
        LoadCurrentOrganization();
    }

    // ========== 向导命令 ==========

    [RelayCommand]
    private void OpenJoinDialog()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        ImportStatusMessage = string.Empty;
        CurrentJoinStep = 0;
        IsJoinDialogOpen = true;
    }

    [RelayCommand]
    private void CloseJoinDialog()
    {
        if (!IsLoading)
        {
            IsJoinDialogOpen = false;
        }
    }

    [RelayCommand]
    private void NextStep()
    {
        if (!CanProceed) return;
        if (CurrentJoinStep < 3)
        {
            CurrentJoinStep++;
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentJoinStep > 0)
        {
            CurrentJoinStep--;
        }
    }

    // ========== 加入组织 ==========

    [RelayCommand]
    private async Task JoinOrganizationAsync()
    {
        if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(OrganizationId))
        {
            ErrorMessage = "请填写服务器地址和组织 ID";
            SuccessMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(ContactPhone))
        {
            ErrorMessage = "请填写联系电话";
            SuccessMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(ClassName))
        {
            ErrorMessage = "请填写班级";
            SuccessMessage = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(PersonInCharge))
        {
            ErrorMessage = "请填写负责人";
            SuccessMessage = string.Empty;
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;

        try
        {
            var (success, errorMsg) = await _organizationService.JoinOrganizationAsync(ServerUrl, OrganizationId, ContactPhone, ClassName, PersonInCharge);
            if (success)
            {
                Console.WriteLine($"[DEBUG] OrganizationViewModel: 加入组织成功，调用 LoadCurrentOrganization");
                LoadCurrentOrganization();
                await Task.Delay(100);

                OrganizationService.Instance.StartPeriodicSyncWithTimer();
                _ = _organizationService.DeviceService.UploadSoftwareListAsync();

                // 关闭弹窗并显示通知
                IsJoinDialogOpen = false;
                SuccessMessage = "成功加入组织！配置将自动同步";
                NotificationService.Instance.ShowSuccess("成功加入组织！配置将自动同步");
            }
            else
            {
                ErrorMessage = errorMsg;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"加入组织失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LeaveOrganizationAsync()
    {
        await _organizationService.LeaveOrganizationAsync();
        LoadCurrentOrganization();

        ServerUrl = string.Empty;
        OrganizationId = string.Empty;
        ContactPhone = string.Empty;
        ClassName = string.Empty;
        PersonInCharge = string.Empty;
    }

    [RelayCommand]
    private async Task SyncConfigurationAsync()
    {
        if (!HasJoinedOrganization)
        {
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        try
        {
            await _organizationService.SyncConfigurationAsync();
            LoadCurrentOrganization();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"同步配置失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        try
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                ImportStatusMessage = "无法访问文件系统";
                return;
            }

            var mainWindow = desktop.MainWindow;
            if (mainWindow == null)
            {
                ImportStatusMessage = "无法访问文件系统";
                return;
            }

            var storageProvider = mainWindow.StorageProvider;

            var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入组织配置文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("ClassScreenLock 配置文件")
                    {
                        Patterns = new[] { "*.cslcfg" },
                        MimeTypes = new[] { "text/plain" }
                    },
                    new FilePickerFileType("所有文件")
                    {
                        Patterns = new[] { "*.*" }
                    }
                }
            });

            if (files.Count == 0)
            {
                return;
            }

            var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var base64Content = await reader.ReadToEndAsync();

            var jsonBytes = Convert.FromBase64String(base64Content.Trim());
            var jsonContent = Encoding.UTF8.GetString(jsonBytes);

            var config = JsonSerializer.Deserialize<OrganizationConfig>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (config == null || config.Type != "ClassScreenLock.OrganizationConfig")
            {
                ImportStatusMessage = "无效的配置文件格式";
                return;
            }

            ServerUrl = config.Server?.Url ?? string.Empty;
            OrganizationId = config.Organization?.Id ?? string.Empty;

            ImportStatusMessage = string.IsNullOrEmpty(config.Organization?.Name)
                ? "已导入配置"
                : $"已导入配置：{config.Organization.Name}";
            ErrorMessage = string.Empty;

            // 更新验证状态
            OnPropertyChanged(nameof(CanNextFromStep0));
            OnPropertyChanged(nameof(CanNextFromStep1));
            OnPropertyChanged(nameof(CanProceed));

            // 导入成功后，短暂展示提示再自动跳转到需要填写的步骤
            _ = AutoAdvanceAfterImportAsync();
        }
        catch (FormatException)
        {
            ImportStatusMessage = "配置文件格式错误，请确保文件未损坏";
        }
        catch (JsonException)
        {
            ImportStatusMessage = "配置文件解析失败，请确保文件格式正确";
        }
        catch (Exception ex)
        {
            ImportStatusMessage = $"导入配置失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 导入配置成功后，短暂展示提示再自动跳转到第一个需要用户填写的步骤：
    /// - 服务器地址和组织 ID 均已填好 → 跳到步骤 2（设备信息）
    /// - 仅服务器地址填好 → 跳到步骤 1（组织 ID）
    /// - 均未填好 → 留在步骤 0
    /// </summary>
    private async Task AutoAdvanceAfterImportAsync()
    {
        await Task.Delay(1200);

        // 确保弹窗仍处于打开状态（用户可能已手动关闭）
        if (!IsJoinDialogOpen) return;

        if (CanNextFromStep0 && CanNextFromStep1)
        {
            CurrentJoinStep = 2;
        }
        else if (CanNextFromStep0)
        {
            CurrentJoinStep = 1;
        }
    }

    private class OrganizationConfig
    {
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public OrganizationInfo? Organization { get; set; }
        public ServerInfo? Server { get; set; }
        public string? ExportedAt { get; set; }
        public string? ExportedBy { get; set; }
    }

    private class OrganizationInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    private class ServerInfo
    {
        public string Url { get; set; } = string.Empty;
        public string? Host { get; set; }
        public int Port { get; set; }
    }
}

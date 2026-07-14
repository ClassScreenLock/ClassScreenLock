using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
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

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool HasSuccess => !string.IsNullOrEmpty(SuccessMessage);

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

    private void LoadCurrentOrganization()
    {
        Console.WriteLine("[DEBUG] OrganizationViewModel: 开始加载当前组织信息");
        
        var org = _organizationService.CurrentOrganization;
        
        if (org != null)
        {
            Console.WriteLine($"[DEBUG] OrganizationViewModel: 找到组织信息，ID={org.Id}, Name={org.Name}, ServerUrl={org.ServerUrl}, IsActive={org.IsActive}");
            
            if (!string.IsNullOrEmpty(org.ServerUrl))
            {
                // 只要有组织信息就显示已加入，不检查 IsActive 状态
                // IsActive 状态仅用于控制设备注册和心跳
                Console.WriteLine($"[DEBUG] OrganizationViewModel: 组织服务器地址有效，设置 HasJoinedOrganization=true");
                HasJoinedOrganization = true;
                OrganizationName = org.Name;
                OrganizationDescription = org.Description;
                JoinedAt = org.JoinedAt;
                LastSyncTime = org.LastSyncTime;
                ServerUrl = org.ServerUrl;
                OrganizationId = org.Id;
                JoinedAtText = org.JoinedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
                LastSyncTimeText = org.LastSyncTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知";
                
                // 如果组织是活跃的，确保设备已注册
                if (org.IsActive)
                {
                    Console.WriteLine($"[DEBUG] OrganizationViewModel: 组织是活跃状态，启动设备注册");
                    _ = _organizationService.DeviceService.RegisterDeviceAsync();
                }
                else
                {
                    Console.WriteLine($"[DEBUG] OrganizationViewModel: 组织是非活跃状态，不自动注册设备");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] OrganizationViewModel: 组织服务器地址为空，设置 HasJoinedOrganization=false");
                HasJoinedOrganization = false;
                OrganizationName = string.Empty;
                OrganizationDescription = string.Empty;
                JoinedAt = null;
                LastSyncTime = null;
                JoinedAtText = string.Empty;
                LastSyncTimeText = string.Empty;
            }
        }
        else
        {
            Console.WriteLine($"[DEBUG] OrganizationViewModel: 未找到组织信息，设置 HasJoinedOrganization=false");
            HasJoinedOrganization = false;
            OrganizationName = string.Empty;
            OrganizationDescription = string.Empty;
            JoinedAt = null;
            LastSyncTime = null;
            JoinedAtText = string.Empty;
            LastSyncTimeText = string.Empty;
        }
        
        Console.WriteLine($"[DEBUG] OrganizationViewModel: 加载完成，HasJoinedOrganization={HasJoinedOrganization}");
    }

    /// <summary>
    /// 刷新组织信息（供外部调用）
    /// </summary>
    public void RefreshOrganizationInfo()
    {
        LoadCurrentOrganization();
    }

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
                // 强制更新 UI 状态
                await Task.Delay(100); // 短暂延迟确保状态更新
                Console.WriteLine($"[DEBUG] OrganizationViewModel: 加入组织后，HasJoinedOrganization={HasJoinedOrganization}");
                
                // 启动定期同步定时器
                OrganizationService.Instance.StartPeriodicSyncWithTimer();
                Console.WriteLine("[DEBUG] 已启动定期同步定时器");
                
                // 上传软件列表
                _ = _organizationService.DeviceService.UploadSoftwareListAsync();
                Console.WriteLine("[DEBUG] 已触发软件列表上传");
                
                SuccessMessage = "成功加入组织！配置将自动同步";
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
                ErrorMessage = "无法访问文件系统";
                return;
            }

            var mainWindow = desktop.MainWindow;
            if (mainWindow == null)
            {
                ErrorMessage = "无法访问文件系统";
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
                ErrorMessage = "无效的配置文件格式";
                SuccessMessage = string.Empty;
                return;
            }

            ServerUrl = config.Server?.Url ?? string.Empty;
            OrganizationId = config.Organization?.Id ?? string.Empty;

            SuccessMessage = $"已导入配置：{config.Organization?.Name ?? "未知组织"}";
            ErrorMessage = string.Empty;
        }
        catch (FormatException)
        {
            ErrorMessage = "配置文件格式错误，请确保文件未损坏";
            SuccessMessage = string.Empty;
        }
        catch (JsonException)
        {
            ErrorMessage = "配置文件解析失败，请确保文件格式正确";
            SuccessMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"导入配置失败：{ex.Message}";
            SuccessMessage = string.Empty;
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

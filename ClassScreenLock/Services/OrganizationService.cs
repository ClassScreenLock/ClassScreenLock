using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class OrganizationService
{
    private static OrganizationService? _instance;
    public static OrganizationService Instance => _instance ??= new OrganizationService();
    
    private readonly string _organizationConfigPath;
    private readonly HttpClient _httpClient;
    private readonly DeviceService _deviceService;
    private readonly WebSocketService _wsService;
    private OrganizationModel? _currentOrganization;
    private Timer? _syncTimer;
    private CancellationTokenSource? _syncCancellationTokenSource;

    public OrganizationService()
    {
        var dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        if (!Directory.Exists(dataDir))
        {
            Directory.CreateDirectory(dataDir);
        }
        _organizationConfigPath = Path.Combine(dataDir, "organization.json");
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15); // 设置默认超时时间为30秒
        _deviceService = new DeviceService();
        _deviceService.Initialize(this);
        
        // 初始化WebSocket服务
        _wsService = WebSocketService.Instance;
        _wsService.Initialize(this);
        
        // 监听配置更新
        _wsService.OnConfigUpdate += (securityConfig, networkConfig) =>
        {
            if (_currentOrganization != null)
            {
                _currentOrganization.SecurityConfig = securityConfig;
                _currentOrganization.NetworkConfig = networkConfig;
                _ = SaveOrganizationAsync();
                ApplyConfigurationAsync();
                LogService.Instance.Log("Info", "Organization", "WebSocket", "配置已通过WebSocket实时更新");
            }
        };

        // 监听课表更新
        _wsService.OnScheduleUpdate += (scheduleJson) =>
        {
            if (_currentOrganization != null)
            {
                try
                {
                    var count = WeeklyScheduleService.Instance.SyncScheduleFromCentralized(scheduleJson);
                    LogService.Instance.Log("Info", "Organization", "WebSocket", 
                        $"课表已通过WebSocket实时更新，同步了 {count} 周");
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Error", "Organization", "WebSocket", 
                        $"WebSocket课表同步失败: {ex.Message}");
                }
            }
        };
    }

    public OrganizationModel? CurrentOrganization => _currentOrganization;

    public bool HasJoinedOrganization => _currentOrganization != null && !string.IsNullOrEmpty(_currentOrganization.ServerUrl);

    public DeviceService DeviceService => _deviceService;

    public async Task LoadOrganizationAsync()
    {
        try
        {
            Console.WriteLine($"[DEBUG] 开始加载组织配置，配置文件路径：{_organizationConfigPath}");
            
            if (!File.Exists(_organizationConfigPath))
            {
                Console.WriteLine($"[DEBUG] 组织配置文件不存在：{_organizationConfigPath}");
                return;
            }

            Console.WriteLine($"[DEBUG] 找到组织配置文件，开始读取...");
            var json = await File.ReadAllTextAsync(_organizationConfigPath);
            Console.WriteLine($"[DEBUG] 读取组织配置文件内容：{json}");
            
            _currentOrganization = DeserializeOrganization(json);
            
            if (_currentOrganization == null)
            {
                Console.WriteLine("[DEBUG] 反序列化组织信息失败，_currentOrganization 为 null");
                return;
            }

            Console.WriteLine($"[DEBUG] 成功反序列化组织信息：ID={_currentOrganization.Id}, Name={_currentOrganization.Name}, ServerUrl={_currentOrganization.ServerUrl}, IsActive={_currentOrganization.IsActive}");
            
            if (string.IsNullOrEmpty(_currentOrganization.ServerUrl))
            {
                Console.WriteLine($"[DEBUG] 组织信息存在但服务器地址为空：{_currentOrganization.ServerUrl}");
                return;
            }

            Console.WriteLine($"✓ 加载已绑定的组织：{_currentOrganization.Name ?? "Unknown"} (ID: {_currentOrganization.Id})");
            
            // 后台异步执行网络操作，不阻塞启动
            HandleInactiveOrganizationBackground();
            
            Console.WriteLine($"[DEBUG] 组织加载完成，HasJoinedOrganization={HasJoinedOrganization}");
            Console.WriteLine($"[DEBUG] 当前组织状态：ID={_currentOrganization.Id}, IsActive={_currentOrganization.IsActive}, ServerUrl={_currentOrganization.ServerUrl}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 加载组织配置失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 异常详情：{ex.StackTrace}");
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"加载组织配置失败：{ex.Message}");
            _currentOrganization = null;
        }
    }

    /// <summary>
    /// 反序列化组织信息
    /// </summary>
    private OrganizationModel? DeserializeOrganization(string json)
    {
        return JsonSerializer.Deserialize<OrganizationModel>(json);
    }

    /// <summary>
    /// 后台处理非活跃组织状态
    /// </summary>
    private void HandleInactiveOrganizationBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_currentOrganization == null)
                {
                    return;
                }

                if (!_currentOrganization.IsActive)
                {
                    Console.WriteLine("组织为非活跃状态，后台尝试重新激活...");
                    await ReactivateOrganizationAsync();
                }
                else
                {
                    Console.WriteLine("组织已是活跃状态，后台执行同步...");
                    
                    // 注册设备
                    await _deviceService.RegisterDeviceAsync();
                    
                    // 连接WebSocket
                    try
                    {
                        await _wsService.ConnectAsync(_currentOrganization.ServerUrl);
                        LogService.Instance.Log("Info", "Organization", "WebSocket", "WebSocket连接已建立");
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Warning", "Organization", "WebSocket", $"WebSocket连接失败: {ex.Message}");
                    }
                    
                    // 同步配置
                    await SyncConfigurationAsync();
                    
                    // 同步课表
                    try
                    {
                        await _wsService.SyncScheduleAsync();
                    }
                    catch (Exception ex)
                    {
                        LogService.Instance.Log("Warning", "Organization", "OrganizationService", $"课表同步失败: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 后台网络操作失败：{ex.Message}");
                LogService.Instance.Log("Error", "Organization", "Background", ex.Message);
            }
        });
    }

    public async Task<(bool Success, string ErrorMessage)> JoinOrganizationAsync(string serverUrl, string organizationId, string contactPhone, string className, string personInCharge)
    {
        try
        {
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"尝试加入组织：{organizationId}，服务器：{serverUrl}");
            Console.WriteLine($"[DEBUG] 尝试加入组织：{organizationId}，服务器：{serverUrl}");
            
            // 从服务器获取组织信息
            var (org, errorMessage) = await FetchOrganizationInfoAsync(serverUrl, organizationId);
            if (org == null)
            {
                return (false, errorMessage ?? "获取组织信息失败");
            }

            // 更新组织信息并保存
            UpdateOrganizationInfo(org, serverUrl, contactPhone, className, personInCharge);
            _currentOrganization = org;
            await SaveOrganizationAsync();
            
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"成功加入组织：{org.Name}");
            Console.WriteLine($"[DEBUG] 成功加入组织：{org.Name}");
            Console.WriteLine($"[DEBUG] 组织信息：ID={org.Id}, Name={org.Name}, ServerUrl={org.ServerUrl}, IsActive={org.IsActive}, Phone={org.ContactPhone}, Class={org.ClassName}, Person={org.PersonInCharge}");
            
            // 禁用本地超级管理员账户（立即执行）
            DisableLocalSuperAdmin();
            
            // 后台异步执行非关键操作，不阻塞用户
            RegisterDeviceBackground();
            
            // 连接WebSocket（实时通信）
            _ = Task.Run(async () =>
            {
                try
                {
                    await _wsService.ConnectAsync(serverUrl);
                    LogService.Instance.Log("Info", "Organization", "WebSocket", "WebSocket连接已建立");
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Warning", "Organization", "WebSocket", $"WebSocket连接失败: {ex.Message}");
                }
            });
            
            return (true, string.Empty);
        }
        catch (TaskCanceledException ex)
        {
            var errorMessage = "加入组织超时，请检查网络连接";
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"{errorMessage}: {ex.Message}");
            Console.WriteLine($"[ERROR] {errorMessage}");
            return (false, errorMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = $"加入组织失败：{ex.Message}";
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"{errorMessage}\n{ex.StackTrace}");
            Console.WriteLine($"[ERROR] {errorMessage}");
            Console.WriteLine($"[ERROR] 异常详情：{ex.StackTrace}");
            return (false, errorMessage);
        }
    }

    /// <summary>
    /// 从服务器获取组织信息
    /// </summary>
    private async Task<(OrganizationModel? Org, string? ErrorMessage)> FetchOrganizationInfoAsync(string serverUrl, string organizationId)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        var response = await httpClient.GetAsync($"{serverUrl}/api/organizations/{organizationId}");
        var responseContent = await response.Content.ReadAsStringAsync();
        
        LogService.Instance.Log("Info", "Organization", "OrganizationService", $"服务器响应：{response.StatusCode}, 内容：{responseContent}");
        Console.WriteLine($"[DEBUG] 服务器响应：{response.StatusCode}, 内容：{responseContent}");
        
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = $"获取组织信息失败：{response.StatusCode}";
            errorMessage = TryExtractErrorMessage(responseContent, errorMessage);
            
            LogService.Instance.Log("Error", "Organization", "OrganizationService", errorMessage);
            Console.WriteLine($"[ERROR] {errorMessage}");
            return (null, errorMessage);
        }

        var org = JsonSerializer.Deserialize<OrganizationModel>(responseContent);
        if (org == null)
        {
            var errorMessage = "反序列化组织信息失败";
            LogService.Instance.Log("Error", "Organization", "OrganizationService", errorMessage);
            Console.WriteLine($"[ERROR] {errorMessage}");
            return (null, errorMessage);
        }

        return (org, null);
    }

    /// <summary>
    /// 尝试从响应内容中提取错误信息
    /// </summary>
    private static string TryExtractErrorMessage(string responseContent, string defaultMessage)
    {
        try
        {
            using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseContent);
            if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
            {
                return errorElement.GetString() ?? defaultMessage;
            }
        }
        catch { }
        return defaultMessage;
    }

    /// <summary>
    /// 更新组织信息
    /// </summary>
    private static void UpdateOrganizationInfo(OrganizationModel org, string serverUrl, string contactPhone, string className, string personInCharge)
    {
        org.ServerUrl = serverUrl;
        org.JoinedAt = DateTime.Now;
        org.IsActive = true;
        org.ContactPhone = contactPhone;
        org.ClassName = className;
        org.PersonInCharge = personInCharge;
    }

    /// <summary>
    /// 后台注册设备和同步配置
    /// </summary>
    private void RegisterDeviceBackground()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // 注册设备到服务器（10秒超时）
                var deviceRegistered = await _deviceService.RegisterDeviceAsync()
                    .WaitAsync(TimeSpan.FromSeconds(10));
                
                LogService.Instance.Log("Info", "Device", "OrganizationService", $"设备注册结果：{(deviceRegistered ? "成功" : "失败")}");
                Console.WriteLine($"[DEBUG] 设备注册结果：{(deviceRegistered ? "成功" : "失败")}");
                
                // 同步配置（15秒超时）
                await SyncConfigurationAsync().WaitAsync(TimeSpan.FromSeconds(15));

                // 同步课表（10秒超时）
                try
                {
                    await _wsService.SyncScheduleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (Exception ex)
                {
                    LogService.Instance.Log("Warning", "Organization", "OrganizationService", $"课表同步失败: {ex.Message}");
                }
            }
            catch (TaskCanceledException ex)
            {
                LogService.Instance.Log("Error", "Organization", "OrganizationService", $"后台配置同步超时：{ex.Message}");
                Console.WriteLine($"[ERROR] 后台配置同步超时：{ex.Message}");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "Organization", "OrganizationService", $"后台配置同步失败：{ex.Message}");
                Console.WriteLine($"[ERROR] 后台配置同步失败：{ex.Message}");
            }
        });
    }

    public async Task LeaveOrganizationAsync()
    {
        // 保存服务器地址（因为后面会删除组织文件）
        string? serverUrl = _currentOrganization?.ServerUrl;
        
        // 1. 断开WebSocket连接并发送离线通知
        await _wsService.SendOfflineAsync("user_logout");
        _wsService.Disconnect();
        
        // 2. 通知服务器设备退出
        await NotifyServerLogoutAsync(serverUrl);
        
        // 3. 停止心跳
        _deviceService.StopHeartbeat();
        
        // 4. 清理组织文件和状态
        CleanupOrganizationFiles();
        
        // 5. 停止配置同步
        StopPeriodicSyncTimer();
        
        // 6. 恢复本地网络拦截数据
        RestoreNetworkInterceptData();
        
        // 7. 检查并恢复本地账户
        RestoreLocalAccounts();
        
        // 8. 刷新 UI 状态
        RefreshUiState();
        
        LogService.Instance.Log("Info", "Organization", "OrganizationService", "已退出组织，设备数据已从服务器移除");
        Console.WriteLine("✓ 已退出组织，设备数据已从服务器移除");
    }

    /// <summary>
    /// 通知服务器设备退出
    /// </summary>
    private async Task NotifyServerLogoutAsync(string? serverUrl)
    {
        if (string.IsNullOrEmpty(serverUrl))
        {
            return;
        }

        try
        {
            var deviceId = _deviceService.DeviceId;
            if (string.IsNullOrEmpty(deviceId))
            {
                LogService.Instance.Log("Debug", "Organization", "OrganizationService", "无法获取设备 ID");
                Console.WriteLine("[DEBUG] 无法获取设备 ID");
                return;
            }

            var exitInfo = new
            {
                deviceId = deviceId,
                exitReason = "user_logout",
                exitTime = DateTime.Now
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(exitInfo);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var cleanedServerUrl = serverUrl.TrimEnd('/');
            var requestUrl = $"{cleanedServerUrl}/api/devices/{deviceId}/logout";
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"发送退出请求到：{requestUrl}");
            Console.WriteLine($"[INFO] 发送退出请求到：{requestUrl}");
            Console.WriteLine($"[INFO] 请求内容：{json}");
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var response = await httpClient.PostAsync(requestUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                LogService.Instance.Log("Info", "Organization", "OrganizationService", $"已通知服务器设备已退出，响应：{responseContent}");
                Console.WriteLine($"✓ 已通知服务器设备已退出，响应：{responseContent}");
            }
            else
            {
                LogService.Instance.Log("Debug", "Organization", "OrganizationService", $"标记退出状态失败：{response.StatusCode}, 响应：{responseContent}");
                Console.WriteLine($"[DEBUG] 标记退出状态失败：{response.StatusCode}, 响应：{responseContent}");
            }
        }
        catch (HttpRequestException ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"发送退出请求失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 发送退出请求失败：{ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"发送退出请求超时：{ex.Message}");
            Console.WriteLine($"[ERROR] 发送退出请求超时：{ex.Message}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Debug", "Organization", "OrganizationService", $"通知服务器失败：{ex.Message}");
            Console.WriteLine($"[DEBUG] 通知服务器失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 清理组织配置文件和状态
    /// </summary>
    private void CleanupOrganizationFiles()
    {
        // 删除组织配置文件
        try
        {
            if (File.Exists(_organizationConfigPath))
            {
                File.Delete(_organizationConfigPath);
                LogService.Instance.Log("Info", "Organization", "OrganizationService", "已删除组织配置文件");
                Console.WriteLine("✓ 已删除组织配置文件");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"删除组织配置文件失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 删除组织配置文件失败：{ex.Message}");
        }
        
        // 清空内存中的组织信息
        _currentOrganization = null;
    }

    /// <summary>
    /// 检查并恢复本地账户
    /// </summary>
    private void RestoreLocalAccounts()
    {
        // 检查是否有非集控端的超级管理员账户
        var allAccounts = AccountService.Instance.GetAllAccountsForRestore();
        var hasLocalSuperAdmin = allAccounts.Any(a => !a.IsFromOrganization && a.AccountType == AccountType.SuperAdmin);
        
        if (!hasLocalSuperAdmin)
        {
            LogService.Instance.Log("Warning", "Organization", "OrganizationService", "退出集控端后没有本地超级管理员账户，请创建一个新的超级管理员账户");
            Console.WriteLine("[WARNING] 退出集控端后没有本地超级管理员账户，请创建一个新的超级管理员账户");
        }
        
        // 恢复本地超级管理员账户
        RestoreLocalSuperAdmin();
    }

    /// <summary>
    /// 刷新 UI 状态
    /// </summary>
    private void RefreshUiState()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is ViewModels.MainWindowViewModel mainVm)
        {
            mainVm.OrganizationViewModel?.RefreshOrganizationInfo();
        }
    }

    private void DisableLocalSuperAdmin()
    {
        try
        {
            var allAccounts = AccountService.Instance.GetAllAccountsForRestore();
            
            // 禁用所有本地账户（包括本地超级管理员）
            foreach (var account in allAccounts.Where(a => !a.IsFromOrganization))
            {
                if (!account.IsDisabled)
                {
                    AccountService.Instance.DisableAccount(account.Id);
                    LogService.Instance.Log("Security", "LocalAccountDisabled", "Organization", $"本地账户已被禁用：{account.Username}");
                    Console.WriteLine($"[INFO] 本地账户已被禁用：{account.Username}");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DisableLocalSuperAdmin", "Organization", ex.Message);
            Console.WriteLine($"[ERROR] 禁用本地账户失败：{ex.Message}");
        }
    }

    private void RestoreLocalSuperAdmin()
    {
        try
        {
            // 获取所有账户（包括被禁用的）
            var allAccounts = AccountService.Instance.GetAllAccountsForRestore();
            
            // 删除集控端分发的账户
            foreach (var account in allAccounts.Where(a => a.IsFromOrganization))
            {
                AccountService.Instance.DeleteAccountInternal(account.Id);
                LogService.Instance.Log("Security", "OrganizationAccountDeleted", "Organization", $"已删除集控端分发的账户：{account.Username}");
                Console.WriteLine($"[INFO] 已删除集控端分发的账户：{account.Username}");
            }
            
            // 重新启用所有本地账户
            foreach (var account in allAccounts.Where(a => !a.IsFromOrganization && a.IsDisabled))
            {
                AccountService.Instance.ReenableAccount(account.Id);
                LogService.Instance.Log("Security", "LocalAccountReenabled", "Organization", $"已恢复本地账户：{account.Username}");
                Console.WriteLine($"[INFO] 已恢复本地账户：{account.Username}");
            }
            
            // 不再自动创建超级管理员账户，避免与用户已创建的超级管理员账户冲突
            // 用户应该在初始化时自行创建超级管理员账户
            Console.WriteLine("[INFO] 组织服务初始化完成，使用现有的超级管理员账户");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "RestoreLocalSuperAdmin", "Organization", ex.Message);
            Console.WriteLine($"[ERROR] 恢复本地超级管理员账户失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 正常退出时调用，保留组织信息
    /// </summary>
    public void OnApplicationExit()
    {
        // 正常退出时不做任何操作，保留组织配置文件
        // 下次启动时会自动重新激活
        LogService.Instance.Log("Info", "Organization", "OrganizationService", "应用程序退出，保留组织绑定信息");
    }

    public async Task<bool> ReactivateOrganizationAsync()
    {
        if (_currentOrganization == null || string.IsNullOrEmpty(_currentOrganization.ServerUrl))
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", "组织信息不完整，无法重新激活");
            return false;
        }

        try
        {
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"重新激活组织：{_currentOrganization.Name ?? "Unknown"}");
            Console.WriteLine($"正在连接到服务器：{_currentOrganization.ServerUrl}");
            
            // 创建临时 HttpClient 实例，设置超时时间
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10); // 启动时连接操作设置10秒超时
            
            // 验证服务器连接
            var response = await httpClient.GetAsync($"{_currentOrganization.ServerUrl}/api/organizations/{_currentOrganization.Id}");
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"服务器响应：{response.StatusCode} - {responseContent}");
            
            if (!response.IsSuccessStatusCode)
            {
                LogService.Instance.Log("Error", "Organization", "OrganizationService", $"验证组织失败：{response.StatusCode}");
                Console.WriteLine($"验证组织失败：{response.StatusCode}");
                
                // 即使验证失败，也保留组织信息，用户可以稍后重试
                // 将组织标记为活跃，但设备注册会在后续重试
                _currentOrganization.IsActive = true;
                _currentOrganization.JoinedAt = DateTime.Now;
                await SaveOrganizationAsync();
                Console.WriteLine("✓ 保留组织信息，等待后续重试");
                return false;
            }

            // 重新激活
            _currentOrganization.IsActive = true;
            _currentOrganization.JoinedAt = DateTime.Now;
            await SaveOrganizationAsync();
            Console.WriteLine("✓ 组织状态已更新为活跃");
            
            // 注册设备（使用带超时的操作）
            var deviceRegistered = await Task.Run(async () => {
                try
                {
                    return await _deviceService.RegisterDeviceAsync();
                }
                catch (TaskCanceledException ex)
                {
                    LogService.Instance.Log("Error", "Device", "OrganizationService", $"设备注册超时：{ex.Message}");
                    Console.WriteLine($"[ERROR] 设备注册超时：{ex.Message}");
                    return false;
                }
            }).WaitAsync(TimeSpan.FromSeconds(15)); // 设备注册15秒超时
            
            LogService.Instance.Log("Info", "Device", "OrganizationService", $"设备注册结果：{(deviceRegistered ? "成功" : "失败")}");
            Console.WriteLine($"设备注册结果：{(deviceRegistered ? "成功" : "失败")}");
            
            // 连接WebSocket（实时通信）
            try
            {
                await _wsService.ConnectAsync(_currentOrganization.ServerUrl);
                LogService.Instance.Log("Info", "Organization", "WebSocket", "WebSocket连接已建立");
                Console.WriteLine("✓ WebSocket连接已建立");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "Organization", "WebSocket", $"WebSocket连接失败: {ex.Message}");
                Console.WriteLine($"[WARNING] WebSocket连接失败: {ex.Message}");
            }
            
            // 同步配置（使用带超时的操作）
            await Task.Run(async () => {
                try
                {
                    await SyncConfigurationAsync();
                }
                catch (TaskCanceledException ex)
                {
                    LogService.Instance.Log("Error", "Organization", "OrganizationService", $"配置同步超时：{ex.Message}");
                    Console.WriteLine($"[ERROR] 配置同步超时：{ex.Message}");
                }
            }).WaitAsync(TimeSpan.FromSeconds(20)); // 配置同步20秒超时

            // 同步课表
            try
            {
                await _wsService.SyncScheduleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "Organization", "OrganizationService", $"课表同步失败: {ex.Message}");
            }
            
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"✓ 成功重新激活组织：{_currentOrganization.Name ?? "Unknown"}");
            Console.WriteLine($"✓ 成功重新激活组织：{_currentOrganization.Name ?? "Unknown"}");
            
            return true;
        }
        catch (TaskCanceledException ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"重新激活组织超时：{ex.Message}");
            Console.WriteLine($"重新激活组织超时：{ex.Message}");
            
            // 即使超时，也保留组织信息
            _currentOrganization.IsActive = true;
            _currentOrganization.JoinedAt = DateTime.Now;
            await SaveOrganizationAsync();
            Console.WriteLine("✓ 保留组织信息，等待后续重试");
            return false;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"重新激活组织失败：{ex.Message}\n{ex.StackTrace}");
            Console.WriteLine($"重新激活组织失败：{ex.Message}");
            
            // 即使出错，也保留组织信息
            _currentOrganization.IsActive = true;
            _currentOrganization.JoinedAt = DateTime.Now;
            await SaveOrganizationAsync();
            Console.WriteLine("✓ 保留组织信息，等待后续重试");
            return false;
        }
    }

    private async Task SaveOrganizationAsync()
    {
        try
        {
            if (_currentOrganization != null)
            {
                var json = JsonSerializer.Serialize(_currentOrganization, new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                await File.WriteAllTextAsync(_organizationConfigPath, json);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存组织配置失败：{ex.Message}");
            throw;
        }
    }

    public async Task SyncConfigurationAsync()
    {
        if (_currentOrganization == null || string.IsNullOrEmpty(_currentOrganization.ServerUrl))
        {
            return;
        }

        try
        {
            // 从服务器获取最新的安全配置和网络配置
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var securityConfigTask = httpClient.GetAsync($"{_currentOrganization.ServerUrl}/api/organizations/{_currentOrganization.Id}/security-config");
            var networkConfigTask = httpClient.GetAsync($"{_currentOrganization.ServerUrl}/api/organizations/{_currentOrganization.Id}/network-config");

            await Task.WhenAll(securityConfigTask, networkConfigTask);

            if (securityConfigTask.Result.IsSuccessStatusCode)
            {
                var securityJson = await securityConfigTask.Result.Content.ReadAsStringAsync();
                _currentOrganization.SecurityConfig = JsonSerializer.Deserialize<SecurityConfiguration>(securityJson);
            }

            if (networkConfigTask.Result.IsSuccessStatusCode)
            {
                var networkJson = await networkConfigTask.Result.Content.ReadAsStringAsync();
                _currentOrganization.NetworkConfig = JsonSerializer.Deserialize<NetworkConfiguration>(networkJson);
                
                // 同步域名拦截规则到 Networkblockage.json
                if (_currentOrganization.NetworkConfig?.DomainRules != null)
                {
                    SyncDomainRulesToNetworkBlockage(_currentOrganization.NetworkConfig.DomainRules);
                }
            }

            _currentOrganization.LastSyncTime = DateTime.Now;
            await SaveOrganizationAsync();

            // 应用配置
            ApplyConfigurationAsync();
        }
        catch (TaskCanceledException ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"配置同步超时：{ex.Message}");
            Console.WriteLine($"配置同步超时：{ex.Message}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"同步配置失败：{ex.Message}");
            Console.WriteLine($"同步配置失败：{ex.Message}");
        }
    }

    private void ApplyConfigurationAsync()
    {
        Console.WriteLine($"[DEBUG] ApplyConfigurationAsync 开始执行");
        
        if (_currentOrganization == null)
        {
            Console.WriteLine($"[DEBUG] _currentOrganization 为 null，返回");
            return;
        }

        var securityConfig = _currentOrganization.SecurityConfig;
        if (securityConfig == null)
        {
            Console.WriteLine($"[DEBUG] securityConfig 为 null，返回");
            return;
        }

        // 记录配置同步日志
        LogService.Instance.Log("Security", "ConfigSynced", "Organization", "安全中心配置已与集控端同步");
        LogService.Instance.Log("Security", "ConfigOverridden", "Organization", "安全中心配置已被集控端覆盖");
        Console.WriteLine("[INFO] 安全中心配置已与集控端同步");
        Console.WriteLine("[INFO] 安全中心配置已被集控端覆盖");

        // 应用各类配置
        ApplyAdminConfig(securityConfig);
        ApplySecurityConfig(securityConfig);
        ApplyAccountConfig(securityConfig);
        ApplyPermissionConfig(securityConfig);
        ApplyNetworkConfig(_currentOrganization.NetworkConfig);
    }

    /// <summary>
    /// 应用管理员配置
    /// </summary>
    private void ApplyAdminConfig(SecurityConfiguration securityConfig)
    {
        if (securityConfig.Admin == null)
        {
            return;
        }

        // 更新管理员用户名
        if (!string.IsNullOrWhiteSpace(securityConfig.Admin.AdminUsername))
        {
            SecurityService.Instance.Settings.AdminUsername = securityConfig.Admin.AdminUsername;
        }

        // 更新管理员密码（如果提供了新密码）
        if (!string.IsNullOrWhiteSpace(securityConfig.Admin.Password))
        {
            SecurityService.Instance.UpdateAdminPassword(securityConfig.Admin.AdminUsername, securityConfig.Admin.Password);
        }
    }

    /// <summary>
    /// 应用安全配置（2FA等）
    /// </summary>
    private void ApplySecurityConfig(SecurityConfiguration securityConfig)
    {
        if (securityConfig.Security == null)
        {
            return;
        }

        // 完全覆盖 2FA 设置
        SecurityService.Instance.Settings.IsTwoFactorEnabled = securityConfig.Security.IsTwoFactorEnabled;
        SecurityService.Instance.Settings.TwoFactorSecret = securityConfig.Security.TwoFactorSecret ?? string.Empty;
        SecurityService.Instance.Settings.LoginVerificationMode = (AdminLoginVerificationMode)securityConfig.Security.LoginVerificationMode;
        
        // 持久化保存安全设置
        SecurityService.Instance.SaveSettings(SecurityService.Instance.Settings);
        
        LogService.Instance.Log("Security", "TwoFactorConfigOverridden", "Organization", $"2FA 设置已被集控端覆盖，状态：{(securityConfig.Security.IsTwoFactorEnabled ? "启用" : "禁用")}");
        Console.WriteLine($"[INFO] 2FA 设置已被集控端覆盖，状态：{(securityConfig.Security.IsTwoFactorEnabled ? "启用" : "禁用")}");
    }

    /// <summary>
    /// 应用账户配置
    /// </summary>
    private void ApplyAccountConfig(SecurityConfiguration securityConfig)
    {
        if (securityConfig.Accounts == null || securityConfig.Accounts.Length == 0)
        {
            return;
        }

        var existingAccounts = AccountService.Instance.GetAllAccountsForRestore();
        
        // 删除所有现有的组织账户
        DeleteOrganizationAccounts(existingAccounts);
        
        // 禁用所有本地账户
        DisableLocalAccounts(existingAccounts);

        // 创建新的账户
        int accountCount = CreateOrganizationAccounts(securityConfig.Accounts);
        
        LogService.Instance.Log("Security", "AccountsSynced", "Organization", $"已同步 {accountCount} 个账户");
        Console.WriteLine($"[INFO] 已同步 {accountCount} 个账户");
    }

    /// <summary>
    /// 删除组织账户
    /// </summary>
    private void DeleteOrganizationAccounts(IEnumerable<AccountModel> accounts)
    {
        foreach (var account in accounts.Where(a => a.IsFromOrganization))
        {
            AccountService.Instance.DeleteAccountInternal(account.Id);
            LogService.Instance.Log("Security", "OrganizationAccountDeleted", "Organization", $"已删除旧的集控端账户：{account.Username}");
            Console.WriteLine($"[INFO] 已删除旧的集控端账户：{account.Username}");
        }
    }

    /// <summary>
    /// 禁用本地账户
    /// </summary>
    private void DisableLocalAccounts(IEnumerable<AccountModel> accounts)
    {
        foreach (var account in accounts.Where(a => !a.IsFromOrganization && !a.IsDisabled))
        {
            AccountService.Instance.DisableAccount(account.Id);
        }
    }

    /// <summary>
    /// 创建组织账户
    /// </summary>
    private int CreateOrganizationAccounts(AccountConfig[] accountConfigs)
    {
        int accountCount = 0;
        const int maxAccounts = 5;

        foreach (var accountConfig in accountConfigs)
        {
            if (accountCount >= maxAccounts)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(accountConfig.Username))
            {
                continue;
            }

            if (accountConfig.AccountType == 2) // 超级管理员
            {
                CreateSuperAdminAccount(accountConfig);
            }
            else // 普通账户或管理员
            {
                CreateNormalAccount(accountConfig);
            }
            
            accountCount++;
        }

        return accountCount;
    }

    /// <summary>
    /// 创建超级管理员账户
    /// </summary>
    private void CreateSuperAdminAccount(AccountConfig accountConfig)
    {
        var result = AccountService.Instance.CreateSubAccountInternal(
            accountConfig.Username,
            accountConfig.Password,
            AccountType.SuperAdmin
        );
        
        if (!result.success)
        {
            LogService.Instance.Log("Security", "AccountCreationFailed", "Organization", $"集控端创建超级管理员账户失败: {result.message}");
            Console.WriteLine($"[ERROR] 控端创建超级管理员账户失败: {result.message}");
            return;
        }

        // 标记为集控端账户并设置2FA
        var allAccounts = AccountService.Instance.GetAllAccountsForRestore();
        var superAdmin = allAccounts.FirstOrDefault(a => a.Username == accountConfig.Username);
        if (superAdmin != null)
        {
            AccountService.Instance.UpdateAccountIsFromOrganization(superAdmin.Id, true);
            ConfigureAccountTwoFactor(superAdmin.Id, accountConfig);
        }
        LogService.Instance.Log("Security", "AccountCreated", "Organization", $"集控端创建超级管理员账户: {accountConfig.Username}");
    }

    /// <summary>
    /// 创建普通账户
    /// </summary>
    private void CreateNormalAccount(AccountConfig accountConfig)
    {
        if (string.IsNullOrWhiteSpace(accountConfig.Password))
        {
            return;
        }

        var result = AccountService.Instance.CreateSubAccountAsync(
            accountConfig.Username,
            accountConfig.Password,
            (AccountType)accountConfig.AccountType
        ).Result;
        
        if (!result.success)
        {
            return;
        }

        // 设置 2FA
        var allAccountsFor2FA = AccountService.Instance.GetAllAccountsForRestore();
        var account = allAccountsFor2FA.FirstOrDefault(a => a.Username == accountConfig.Username);
        if (account != null)
        {
            ConfigureAccountTwoFactor(account.Id, accountConfig);
            AccountService.Instance.UpdateAccountIsFromOrganization(account.Id, true);
        }
        LogService.Instance.Log("Security", "AccountCreated", "Organization", $"集控端创建账户: {accountConfig.Username}");
    }

    /// <summary>
    /// 配置账户2FA
    /// </summary>
    private void ConfigureAccountTwoFactor(Guid accountId, AccountConfig accountConfig)
    {
        if (accountConfig.IsTwoFactorEnabled && !string.IsNullOrWhiteSpace(accountConfig.TwoFactorSecret))
        {
            AccountService.Instance.UpdateAccountTwoFactorAsync(accountId, accountConfig.IsTwoFactorEnabled, accountConfig.TwoFactorSecret).Wait();
        }
    }

    /// <summary>
    /// 应用权限配置
    /// </summary>
    private void ApplyPermissionConfig(SecurityConfiguration securityConfig)
    {
        if (securityConfig.Permissions == null)
        {
            return;
        }

        var lockSettings = SettingsService.Lock;
        var permissions = securityConfig.Permissions;
        
        // 应用各种权限设置
        lockSettings.ExitAppMinAccountType = ConvertPermissionType(permissions.ExitAppMinAccountType);
        lockSettings.SidebarHomeMinAccountType = ConvertPermissionType(permissions.SidebarHomeMinAccountType);
        lockSettings.SidebarLockSettingsMinAccountType = ConvertPermissionType(permissions.SidebarLockSettingsMinAccountType);
        lockSettings.BreakTimeLockSettingsMinAccountType = ConvertPermissionType(permissions.BreakTimeLockSettingsMinAccountType);
        lockSettings.SidebarScheduleMinAccountType = ConvertPermissionType(permissions.SidebarScheduleMinAccountType);
        lockSettings.SidebarAppManagementMinAccountType = ConvertPermissionType(permissions.SidebarAppManagementMinAccountType);
        lockSettings.SidebarNetworkInterceptionMinAccountType = ConvertPermissionType(permissions.SidebarNetworkInterceptionMinAccountType);
        lockSettings.SidebarSecurityLogsMinAccountType = ConvertPermissionType(permissions.SidebarSecurityLogsMinAccountType);
        lockSettings.SidebarScreenshotHistoryMinAccountType = ConvertPermissionType(permissions.SidebarScreenshotHistoryMinAccountType);
        lockSettings.SidebarWebcamHistoryMinAccountType = ConvertPermissionType(permissions.SidebarWebcamHistoryMinAccountType);
        lockSettings.SidebarAutomationMinAccountType = ConvertPermissionType(permissions.SidebarAutomationMinAccountType);
        lockSettings.SidebarSecurityCenterMinAccountType = ConvertPermissionType(permissions.SidebarSecurityCenterMinAccountType);
        lockSettings.SidebarSettingsMinAccountType = ConvertPermissionType(permissions.SidebarSettingsMinAccountType);
        lockSettings.SidebarAboutMinAccountType = ConvertPermissionType(permissions.SidebarAboutMinAccountType);
        lockSettings.EarlyUnlockMinAccountType = (AccountType)permissions.EarlyUnlockMinAccountType;
        
        SettingsService.SaveLock(lockSettings);
        
        LogService.Instance.Log("Security", "PermissionsApplied", "Organization", "权限配置已应用");
        Console.WriteLine($"[INFO] 权限配置已应用 - 退出应用权限：{lockSettings.ExitAppMinAccountType?.ToString() ?? "无"}, 早期解锁权限：{lockSettings.EarlyUnlockMinAccountType}");
    }

    /// <summary>
    /// 转换权限类型（0表示无限制，转换为null）
    /// </summary>
    private static AccountType? ConvertPermissionType(int value)
    {
        return value == 0 ? null : (AccountType?)value;
    }

    /// <summary>
    /// 应用网络拦截配置
    /// </summary>
    private void ApplyNetworkConfig(NetworkConfiguration? networkConfig)
    {
        if (networkConfig == null)
        {
            Console.WriteLine($"[DEBUG] NetworkConfig 为 null，跳过网络配置应用");
            return;
        }

        Console.WriteLine($"[DEBUG] 开始应用网络拦截配置，Enabled={networkConfig.Enabled}, DomainRules 数量={networkConfig.DomainRules?.Count ?? 0}");

        if (networkConfig.DomainRules == null || networkConfig.DomainRules.Count == 0)
        {
            LogService.Instance.Log("Network", "NetworkConfigApplied", "Organization", "网络拦截配置已应用（无规则）");
            Console.WriteLine($"[INFO] 网络拦截配置已应用（无规则）");
            return;
        }

        // 同步域名拦截规则
        SyncDomainRulesToNetworkBlockage(networkConfig.DomainRules);
        LogService.Instance.Log("Network", "NetworkConfigApplied", "Organization", $"已应用网络拦截配置，共 {networkConfig.DomainRules.Count} 条规则，Enabled={networkConfig.Enabled}");
        Console.WriteLine($"[INFO] 已应用网络拦截配置，共 {networkConfig.DomainRules.Count} 条规则");
        
        // 启用网络拦截功能
        SettingsService.UpdateBlockage(s => s.IsNetworkLockEnabled = networkConfig.Enabled);
        Console.WriteLine($"[DEBUG] 网络拦截功能已{(networkConfig.Enabled ? "启用" : "禁用")}");
        
        // 立即应用网络拦截规则
        try
        {
            _ = NetworkBlockingService.Instance.ApplyRulesAsync("Organization config synced");
            Console.WriteLine($"[DEBUG] 已触发网络拦截规则应用");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 应用网络拦截规则失败：{ex.Message}");
        }
    }

    public async Task StartPeriodicSyncAsync(int intervalMinutes = 30)
    {
        _syncCancellationTokenSource?.Cancel();
        _syncCancellationTokenSource = new CancellationTokenSource();
        
        while (!_syncCancellationTokenSource.Token.IsCancellationRequested)
        {
            if (HasJoinedOrganization)
            {
                await SyncConfigurationAsync();
                
                if (_currentOrganization?.SecurityConfig?.SyncInterval > 0)
                {
                    intervalMinutes = _currentOrganization.SecurityConfig.SyncInterval;
                }
            }
            
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), _syncCancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void StopPeriodicSync()
    {
        _syncCancellationTokenSource?.Cancel();
        _syncCancellationTokenSource?.Dispose();
        _syncCancellationTokenSource = null;
    }

    public void StartPeriodicSyncWithTimer()
    {
        _syncTimer?.Dispose();
        
        var interval = TimeSpan.FromMinutes(_currentOrganization?.SecurityConfig?.SyncInterval > 0 
            ? _currentOrganization.SecurityConfig.SyncInterval 
            : 30);
        
        _syncTimer = new Timer(async _ => 
        {
            if (HasJoinedOrganization)
            {
                await SyncConfigurationAsync();
                
                if (_currentOrganization?.SecurityConfig?.SyncInterval > 0)
                {
                    var newInterval = TimeSpan.FromMinutes(_currentOrganization.SecurityConfig.SyncInterval);
                    _syncTimer?.Change(newInterval, newInterval);
                }
            }
        }, null, TimeSpan.Zero, interval);
    }

    public void StopPeriodicSyncTimer()
    {
        _syncTimer?.Dispose();
        _syncTimer = null;
    }

    /// <summary>
    /// 将域名拦截规则同步到 Networkblockage.json 文件
    /// </summary>
    /// <param name="domainRules">从服务器获取的域名规则列表</param>
    private void SyncDomainRulesToNetworkBlockage(List<DomainRule> domainRules)
    {
        try
        {
            var networkBlockagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Networkblockage.json");
            
            // 将 DomainRule 转换为 NetworkRule
            var networkRules = domainRules.Select(r => new NetworkRule
            {
                Domain = r.Domain,
                Description = r.Description,
                IsEnabled = r.IsEnabled,
                Type = "Domain"
            }).ToList();
            
            // 保存规则
            NetworkRuleService.SaveRules(networkRules);
            
            // 同步主开关状态
            var networkConfig = _currentOrganization?.NetworkConfig;
            if (networkConfig != null)
            {
                NetworkRuleService.SetEnabled(networkConfig.Enabled);
                Console.WriteLine($"[DEBUG] 网络拦截主开关状态：{networkConfig.Enabled}");
            }
            
            LogService.Instance.Log("Info", "Network", "OrganizationService", $"已同步 {networkRules.Count} 条域名拦截规则到 Networkblockage.json");
            Console.WriteLine($"[INFO] 已同步 {networkRules.Count} 条域名拦截规则");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Network", "OrganizationService", $"同步域名拦截规则失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 同步域名拦截规则失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 恢复本地网络拦截数据（退出组织时调用）
    /// </summary>
    private void RestoreNetworkInterceptData()
    {
        try
        {
            var networkBlockagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Networkblockage.json");
            
            // 清空所有从集控端同步的域名拦截规则
            var emptyRules = new List<NetworkRule>();
            NetworkRuleService.SaveRules(emptyRules);
            
            // 恢复主开关为默认启用状态
            NetworkRuleService.SetEnabled(true);
            
            LogService.Instance.Log("Info", "Network", "OrganizationService", "已恢复本地网络拦截数据（清空集控端同步的规则）");
            Console.WriteLine($"[INFO] 已恢复本地网络拦截数据（清空集控端同步的规则）");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Network", "OrganizationService", $"恢复网络拦截数据失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 恢复网络拦截数据失败：{ex.Message}");
        }
    }
}

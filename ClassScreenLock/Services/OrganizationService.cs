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
    }

    public OrganizationModel? CurrentOrganization => _currentOrganization;

    public bool HasJoinedOrganization => _currentOrganization != null && !string.IsNullOrEmpty(_currentOrganization.ServerUrl);

    public DeviceService DeviceService => _deviceService;

    public async Task LoadOrganizationAsync()
    {
        try
        {
            Console.WriteLine($"[DEBUG] 开始加载组织配置，配置文件路径：{_organizationConfigPath}");
            
            if (File.Exists(_organizationConfigPath))
            {
                Console.WriteLine($"[DEBUG] 找到组织配置文件，开始读取...");
                var json = await File.ReadAllTextAsync(_organizationConfigPath);
                Console.WriteLine($"[DEBUG] 读取组织配置文件内容：{json}");
                
                _currentOrganization = JsonSerializer.Deserialize<OrganizationModel>(json);
                
                // 如果组织存在且有服务器地址，自动重新激活
                if (_currentOrganization != null)
                {
                    Console.WriteLine($"[DEBUG] 成功反序列化组织信息：ID={_currentOrganization.Id}, Name={_currentOrganization.Name}, ServerUrl={_currentOrganization.ServerUrl}, IsActive={_currentOrganization.IsActive}");
                    
                    if (!string.IsNullOrEmpty(_currentOrganization.ServerUrl))
                    {
                        Console.WriteLine($"✓ 加载已绑定的组织：{_currentOrganization.Name ?? "Unknown"} (ID: {_currentOrganization.Id})");
                        
                        // 无论 IsActive 状态如何，都尝试重新激活
                        if (!_currentOrganization.IsActive)
                        {
                            Console.WriteLine("组织为非活跃状态，尝试重新激活...");
                            var result = await ReactivateOrganizationAsync();
                            Console.WriteLine($"重新激活结果：{(result ? "成功" : "失败")}");
                            
                            // 如果重新激活失败，但仍希望保持组织信息，可以选择将组织设为活跃状态
                            // 并让后续的心跳检测来决定设备是否在线
                            if (!result)
                            {
                                // 即使重新激活失败，也保留组织信息，用户可以手动重新连接
                                Console.WriteLine("重新激活失败，但仍保留组织信息以便用户重试");
                            }
                        }
                        else
                        {
                            Console.WriteLine("组织已是活跃状态，直接注册设备...");
                            // 已经是活跃状态，直接注册设备
                            await _deviceService.RegisterDeviceAsync();
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] 组织信息存在但服务器地址为空：{_currentOrganization.ServerUrl}");
                    }
                }
                else
                {
                    Console.WriteLine("[DEBUG] 反序列化组织信息失败，_currentOrganization 为 null");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] 组织配置文件不存在：{_organizationConfigPath}");
            }
            
            // 输出最终状态
            Console.WriteLine($"[DEBUG] 组织加载完成，HasJoinedOrganization={HasJoinedOrganization}");
            if (_currentOrganization != null)
            {
                Console.WriteLine($"[DEBUG] 当前组织状态：ID={_currentOrganization.Id}, IsActive={_currentOrganization.IsActive}, ServerUrl={_currentOrganization.ServerUrl}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 加载组织配置失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 异常详情：{ex.StackTrace}");
            LogService.Instance.Log("Error", "Organization", "OrganizationService", $"加载组织配置失败：{ex.Message}");
            _currentOrganization = null;
        }
    }

    public async Task<(bool Success, string ErrorMessage)> JoinOrganizationAsync(string serverUrl, string organizationId, string contactPhone, string className, string personInCharge)
    {
        try
        {
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"尝试加入组织：{organizationId}，服务器：{serverUrl}");
            Console.WriteLine($"[DEBUG] 尝试加入组织：{organizationId}，服务器：{serverUrl}");
            
            // 创建临时 HttpClient 实例，设置更短的超时时间
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15); // 加入组织操作设置15秒超时
            
            // 从服务器获取组织信息
            var response = await httpClient.GetAsync($"{serverUrl}/api/organizations/{organizationId}");
            var responseContent = await response.Content.ReadAsStringAsync();
            
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"服务器响应：{response.StatusCode}, 内容：{responseContent}");
            Console.WriteLine($"[DEBUG] 服务器响应：{response.StatusCode}, 内容：{responseContent}");
            
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"获取组织信息失败：{response.StatusCode}";
                
                // 尝试解析服务器返回的错误信息
                try
                {
                    using var jsonDoc = System.Text.Json.JsonDocument.Parse(responseContent);
                    if (jsonDoc.RootElement.TryGetProperty("error", out var errorElement))
                    {
                        errorMessage = errorElement.GetString() ?? errorMessage;
                    }
                }
                catch { }
                
                LogService.Instance.Log("Error", "Organization", "OrganizationService", errorMessage);
                Console.WriteLine($"[ERROR] {errorMessage}");
                return (false, errorMessage);
            }

            var org = JsonSerializer.Deserialize<OrganizationModel>(responseContent);
            
            if (org == null)
            {
                var errorMessage = "反序列化组织信息失败";
                LogService.Instance.Log("Error", "Organization", "OrganizationService", errorMessage);
                Console.WriteLine($"[ERROR] {errorMessage}");
                return (false, errorMessage);
            }

            org.ServerUrl = serverUrl;
            org.JoinedAt = DateTime.Now;
            org.IsActive = true;
            org.ContactPhone = contactPhone;
            org.ClassName = className;
            org.PersonInCharge = personInCharge;

            _currentOrganization = org;
            await SaveOrganizationAsync();
            
            LogService.Instance.Log("Info", "Organization", "OrganizationService", $"成功加入组织：{org.Name}");
            Console.WriteLine($"[DEBUG] 成功加入组织：{org.Name}");
            Console.WriteLine($"[DEBUG] 组织信息：ID={org.Id}, Name={org.Name}, ServerUrl={org.ServerUrl}, IsActive={org.IsActive}, Phone={org.ContactPhone}, Class={org.ClassName}, Person={org.PersonInCharge}");
            
            // 注册设备到服务器（使用带超时的操作）
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
            Console.WriteLine($"[DEBUG] 设备注册结果：{(deviceRegistered ? "成功" : "失败")}");
            
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
            
            // 禁用本地超级管理员账户
            DisableLocalSuperAdmin();
            
            // 强制应用安全配置，确保 2FA 设置被覆盖
            if (_currentOrganization?.SecurityConfig != null)
            {
                ApplyConfigurationAsync();
            }
            
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

    public async Task LeaveOrganizationAsync()
    {
        // 保存服务器地址（因为后面会删除组织文件）
        string? serverUrl = _currentOrganization?.ServerUrl;
        
        // 1. 调用后端 API 通知服务器设备已退出（标记为已退出状态）
        if (!string.IsNullOrEmpty(serverUrl))
        {
            try
            {
                // 先获取设备 ID
                var deviceId = _deviceService.DeviceId;
                if (!string.IsNullOrEmpty(deviceId))
                {
                    // 发送请求标记设备为已退出状态
                    var exitInfo = new
                    {
                        deviceId = deviceId,
                        exitReason = "user_logout",
                        exitTime = DateTime.Now
                    };
                    
                    var json = System.Text.Json.JsonSerializer.Serialize(exitInfo);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    
                    // 修复 URL 格式，移除末尾斜杠避免重复
                    var cleanedServerUrl = serverUrl.TrimEnd('/');
                    var requestUrl = $"{cleanedServerUrl}/api/devices/{deviceId}/logout";
                    LogService.Instance.Log("Info", "Organization", "OrganizationService", $"发送退出请求到：{requestUrl}");
                    Console.WriteLine($"[INFO] 发送退出请求到：{requestUrl}");
                    Console.WriteLine($"[INFO] 请求内容：{json}");
                    
                    // 创建新的 HttpClient 实例来发送退出请求
                    using var httpClient = new HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(10);
                    
                    try
                    {
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
                        LogService.Instance.Log("Error", "Organization", "OrganizationService", $"发送退出请求异常：{ex.Message}");
                        Console.WriteLine($"[ERROR] 发送退出请求异常：{ex.Message}");
                    }
                }
                else
                {
                    LogService.Instance.Log("Debug", "Organization", "OrganizationService", "无法获取设备 ID");
                    Console.WriteLine("[DEBUG] 无法获取设备 ID");
                }
                
                // 停止设备心跳服务
                _deviceService.StopHeartbeat();
                LogService.Instance.Log("Info", "Organization", "OrganizationService", "设备心跳服务已停止");
                Console.WriteLine($"✓ 设备心跳服务已停止");
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Debug", "Organization", "OrganizationService", $"通知服务器失败：{ex.Message}");
                Console.WriteLine($"[DEBUG] 通知服务器失败：{ex.Message}");
            }
        }
        
        // 2. 停止心跳
        _deviceService.StopHeartbeat();
        
        // 3. 删除组织配置文件
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
        
        // 4. 清空内存中的组织信息
        _currentOrganization = null;
        
        // 5. 停止配置同步
        StopPeriodicSyncTimer();
        
        // 6. 恢复本地网络拦截数据（删除从集控端同步的规则）
        RestoreNetworkInterceptData();
        
        // 7. 检查是否有非集控端的超级管理员账户
        var allAccounts = AccountService.Instance.GetAllAccountsForRestore();
        var hasLocalSuperAdmin = allAccounts.Any(a => !a.IsFromOrganization && a.AccountType == AccountType.SuperAdmin);
        
        if (!hasLocalSuperAdmin)
        {
            // 如果没有本地超级管理员，提示用户创建一个
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopApp &&
                desktopApp.MainWindow?.DataContext is ViewModels.MainWindowViewModel mainWindowVm)
            {
                // 这里可以添加一个对话框，提示用户创建新的超级管理员账户
                // 暂时先记录日志
                LogService.Instance.Log("Warning", "Organization", "OrganizationService", "退出集控端后没有本地超级管理员账户，请创建一个新的超级管理员账户");
                Console.WriteLine("[WARNING] 退出集控端后没有本地超级管理员账户，请创建一个新的超级管理员账户");
            }
        }
        
        // 8. 恢复本地超级管理员账户
        RestoreLocalSuperAdmin();
        
        // 9. 刷新 UI 状态
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.DataContext is ViewModels.MainWindowViewModel mainVm)
        {
            mainVm.OrganizationViewModel?.RefreshOrganizationInfo();
        }
        
        LogService.Instance.Log("Info", "Organization", "OrganizationService", "已退出组织，设备数据已从服务器移除");
        Console.WriteLine("✓ 已退出组织，设备数据已从服务器移除");
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
            
            // 检查是否有本地超级管理员账户
            var hasLocalSuperAdmin = allAccounts.Any(a => !a.IsFromOrganization && a.AccountType == AccountType.SuperAdmin);
            if (!hasLocalSuperAdmin)
            {
                // 创建默认超级管理员账户
                var securitySettings = SecurityService.Instance.Settings;
                var result = AccountService.Instance.CreateSubAccountAsync(
                    securitySettings.AdminUsername,
                    "admin123", // 默认密码，用户可以后续修改
                    AccountType.SuperAdmin
                ).Result;
                
                if (result.success)
                {
                    LogService.Instance.Log("Security", "LocalSuperAdminRestored", "Organization", "本地超级管理员账户已恢复");
                    Console.WriteLine("[INFO] 本地超级管理员账户已恢复");
                }
            }
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
            // 确保设备已注册（简单检查，如果已加入组织则注册）
            if (_currentOrganization.IsActive)
            {
                await _deviceService.RegisterDeviceAsync();
            }

            // 从服务器获取最新的安全配置和网络配置
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(20); // 配置同步设置20秒超时
            
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
        
        // 记录配置同步日志
        LogService.Instance.Log("Security", "ConfigSynced", "Organization", "安全中心配置已与集控端同步");
        Console.WriteLine("[INFO] 安全中心配置已与集控端同步");
        
        // 显示同步通知
        if (securityConfig == null)
        {
            Console.WriteLine($"[DEBUG] securityConfig 为 null，返回");
            return;
        }
        
        Console.WriteLine($"[DEBUG] securityConfig 不为 null，继续执行");
        Console.WriteLine($"[DEBUG] securityConfig.Accounts 为 null: {securityConfig.Accounts == null}");
        if (securityConfig.Accounts != null)
        {
            Console.WriteLine($"[DEBUG] securityConfig.Accounts.Length: {securityConfig.Accounts.Length}");
        }

        // 记录配置覆盖日志
        LogService.Instance.Log("Security", "ConfigOverridden", "Organization", "安全中心配置已被集控端覆盖");
        Console.WriteLine("[INFO] 安全中心配置已被集控端覆盖");

        // 应用管理员配置
        if (securityConfig.Admin != null)
        {
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

        // 应用安全配置
        if (securityConfig.Security != null)
        {
            // 完全覆盖 2FA 设置
            SecurityService.Instance.Settings.IsTwoFactorEnabled = securityConfig.Security.IsTwoFactorEnabled;
            
            // 强制更新 2FA 密钥
            SecurityService.Instance.Settings.TwoFactorSecret = securityConfig.Security.TwoFactorSecret ?? string.Empty;

            // 更新登录验证模式
            SecurityService.Instance.Settings.LoginVerificationMode = (AdminLoginVerificationMode)securityConfig.Security.LoginVerificationMode;
            
            // 持久化保存安全设置
            var settings = SecurityService.Instance.Settings;
            SecurityService.Instance.SaveSettings(settings);
            
            // 记录 2FA 配置覆盖日志
            LogService.Instance.Log("Security", "TwoFactorConfigOverridden", "Organization", $"2FA 设置已被集控端覆盖，状态：{(securityConfig.Security.IsTwoFactorEnabled ? "启用" : "禁用")}");
            Console.WriteLine($"[INFO] 2FA 设置已被集控端覆盖，状态：{(securityConfig.Security.IsTwoFactorEnabled ? "启用" : "禁用")}");
        }

        // 应用账户配置
        if (securityConfig.Accounts != null && securityConfig.Accounts.Length > 0)
        {
            // 禁用所有现有账户（包括本地超级管理员和已禁用的账户）
            var existingAccounts = AccountService.Instance.GetAllAccountsForRestore();
            
            // 先删除所有现有的组织账户，避免账户数量达到上限
            foreach (var account in existingAccounts.Where(a => a.IsFromOrganization))
            {
                AccountService.Instance.DeleteAccountInternal(account.Id);
                LogService.Instance.Log("Security", "OrganizationAccountDeleted", "Organization", $"已删除旧的集控端账户：{account.Username}");
                Console.WriteLine($"[INFO] 已删除旧的集控端账户：{account.Username}");
            }
            
            // 禁用所有本地账户
            foreach (var account in existingAccounts.Where(a => !a.IsFromOrganization))
            {
                // 禁用现有账户，而不是删除
                if (!account.IsDisabled)
                {
                    AccountService.Instance.DisableAccount(account.Id);
                }
            }

            // 处理新的账户配置
            int accountCount = 0;
            foreach (var accountConfig in securityConfig.Accounts)
            {
                if (accountCount >= 5) break; // 最多5个账户

                if (!string.IsNullOrWhiteSpace(accountConfig.Username))
                {
                    if (accountConfig.AccountType == 2) // 超级管理员
                            {
                                // 创建新的超级管理员账户（而不是覆盖本地超级管理员）
                                var result = AccountService.Instance.CreateSubAccountInternal(
                                    accountConfig.Username,
                                    accountConfig.Password,
                                    AccountType.SuperAdmin
                                );
                                
                                if (result.success)
                                {
                                    // 标记为集控端账户
                                    var allAccounts = AccountService.Instance.GetAllAccountsForRestore();
                                    var superAdmin = allAccounts.FirstOrDefault(a => a.Username == accountConfig.Username);
                                    if (superAdmin != null)
                                    {
                                        AccountService.Instance.UpdateAccountIsFromOrganization(superAdmin.Id, true);
                                        // 设置 2FA
                                        if (accountConfig.IsTwoFactorEnabled && !string.IsNullOrWhiteSpace(accountConfig.TwoFactorSecret))
                                        {
                                            AccountService.Instance.UpdateAccountTwoFactorAsync(superAdmin.Id, accountConfig.IsTwoFactorEnabled, accountConfig.TwoFactorSecret).Wait();
                                        }
                                    }
                                    LogService.Instance.Log("Security", "AccountCreated", "Organization", $"集控端创建超级管理员账户: {accountConfig.Username}");
                                }
                                else
                                {
                                    // 如果创建失败，记录日志
                                    LogService.Instance.Log("Security", "AccountCreationFailed", "Organization", $"集控端创建超级管理员账户失败: {result.message}");
                                    Console.WriteLine($"[ERROR] 集控端创建超级管理员账户失败: {result.message}");
                                }
                            }
                    else // 普通账户或管理员
                    {
                        if (!string.IsNullOrWhiteSpace(accountConfig.Password))
                        {
                            // 创建新账户
                            var result = AccountService.Instance.CreateSubAccountAsync(
                                accountConfig.Username,
                                accountConfig.Password,
                                (AccountType)accountConfig.AccountType
                            ).Result;
                            
                            if (result.success)
                            {
                                // 设置 2FA
                                if (accountConfig.IsTwoFactorEnabled && !string.IsNullOrWhiteSpace(accountConfig.TwoFactorSecret))
                                {
                                    var allAccountsFor2FA = AccountService.Instance.GetAllAccountsForRestore();
                                    var account = allAccountsFor2FA.FirstOrDefault(a => a.Username == accountConfig.Username);
                                    if (account != null)
                                    {
                                        AccountService.Instance.UpdateAccountTwoFactorAsync(account.Id, accountConfig.IsTwoFactorEnabled, accountConfig.TwoFactorSecret).Wait();
                                    }
                                }
                                // 标记为集控端账户
                                var allAccountsForMark = AccountService.Instance.GetAllAccountsForRestore();
                                var newAccount = allAccountsForMark.FirstOrDefault(a => a.Username == accountConfig.Username);
                                if (newAccount != null)
                                {
                                    AccountService.Instance.UpdateAccountIsFromOrganization(newAccount.Id, true);
                                }
                                LogService.Instance.Log("Security", "AccountCreated", "Organization", $"集控端创建账户: {accountConfig.Username}");
                            }
                        }
                    }
                    accountCount++;
                }
            }
            
            // 记录账户同步日志
            LogService.Instance.Log("Security", "AccountsSynced", "Organization", $"已同步 {accountCount} 个账户");
            Console.WriteLine($"[INFO] 已同步 {accountCount} 个账户");
        }

        // 应用权限设置
        if (securityConfig.Permissions != null)
        {
            var lockSettings = SettingsService.Lock;
            
            // 应用各种权限设置
            lockSettings.ExitAppMinAccountType = securityConfig.Permissions.ExitAppMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.ExitAppMinAccountType;
            lockSettings.SidebarHomeMinAccountType = securityConfig.Permissions.SidebarHomeMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarHomeMinAccountType;
            lockSettings.SidebarLockSettingsMinAccountType = securityConfig.Permissions.SidebarLockSettingsMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarLockSettingsMinAccountType;
            lockSettings.BreakTimeLockSettingsMinAccountType = securityConfig.Permissions.BreakTimeLockSettingsMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.BreakTimeLockSettingsMinAccountType;
            lockSettings.SidebarScheduleMinAccountType = securityConfig.Permissions.SidebarScheduleMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarScheduleMinAccountType;
            lockSettings.SidebarAppManagementMinAccountType = securityConfig.Permissions.SidebarAppManagementMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarAppManagementMinAccountType;
            lockSettings.SidebarNetworkInterceptionMinAccountType = securityConfig.Permissions.SidebarNetworkInterceptionMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarNetworkInterceptionMinAccountType;
            lockSettings.SidebarSecurityLogsMinAccountType = securityConfig.Permissions.SidebarSecurityLogsMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarSecurityLogsMinAccountType;
            lockSettings.SidebarScreenshotHistoryMinAccountType = securityConfig.Permissions.SidebarScreenshotHistoryMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarScreenshotHistoryMinAccountType;
            lockSettings.SidebarWebcamHistoryMinAccountType = securityConfig.Permissions.SidebarWebcamHistoryMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarWebcamHistoryMinAccountType;
            lockSettings.SidebarAutomationMinAccountType = securityConfig.Permissions.SidebarAutomationMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarAutomationMinAccountType;
            lockSettings.SidebarSecurityCenterMinAccountType = securityConfig.Permissions.SidebarSecurityCenterMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarSecurityCenterMinAccountType;
            lockSettings.SidebarSettingsMinAccountType = securityConfig.Permissions.SidebarSettingsMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarSettingsMinAccountType;
            lockSettings.SidebarAboutMinAccountType = securityConfig.Permissions.SidebarAboutMinAccountType == 0 ? null : (AccountType?)securityConfig.Permissions.SidebarAboutMinAccountType;
            lockSettings.EarlyUnlockMinAccountType = (AccountType)securityConfig.Permissions.EarlyUnlockMinAccountType;
            
            SettingsService.SaveLock(lockSettings);
            
            // 记录权限配置应用日志
            LogService.Instance.Log("Security", "PermissionsApplied", "Organization", "权限配置已应用");
            Console.WriteLine($"[INFO] 权限配置已应用 - 退出应用权限：{lockSettings.ExitAppMinAccountType?.ToString() ?? "无"}, 早期解锁权限：{lockSettings.EarlyUnlockMinAccountType}");
        }

        // 应用网络拦截配置
        var networkConfig = _currentOrganization.NetworkConfig;
        if (networkConfig != null)
        {
            Console.WriteLine($"[DEBUG] 开始应用网络拦截配置，Enabled={networkConfig.Enabled}, DomainRules 数量={networkConfig.DomainRules?.Count ?? 0}");
            
            // 同步域名拦截规则到 Networkblockage.json
            if (networkConfig.DomainRules != null && networkConfig.DomainRules.Count > 0)
            {
                SyncDomainRulesToNetworkBlockage(networkConfig.DomainRules);
                LogService.Instance.Log("Network", "NetworkConfigApplied", "Organization", $"已应用网络拦截配置，共 {networkConfig.DomainRules.Count} 条规则，Enabled={networkConfig.Enabled}");
                Console.WriteLine($"[INFO] 已应用网络拦截配置，共 {networkConfig.DomainRules.Count} 条规则");
                
                // 启用网络拦截功能（默认启用，除非集控端明确禁用）
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
            else
            {
                LogService.Instance.Log("Network", "NetworkConfigApplied", "Organization", "网络拦截配置已应用（无规则）");
                Console.WriteLine($"[INFO] 网络拦截配置已应用（无规则）");
            }
        }
        else
        {
            Console.WriteLine($"[DEBUG] NetworkConfig 为 null，跳过网络配置应用");
        }

        return;
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

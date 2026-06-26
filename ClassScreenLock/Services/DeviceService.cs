using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

public class DeviceService
{
    private static DeviceService? _instance;
    public static DeviceService Instance => _instance ??= new DeviceService();
    
    private readonly HttpClient _httpClient;
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly string _ipAddress;
    private readonly string _macAddress;
    private Timer? _heartbeatTimer;
    private OrganizationService? _organizationService;
    private bool _isRegistered;

    public DeviceService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10); // 设置默认超时时间为10秒
        _deviceId = GenerateDeviceId();
        _deviceName = Environment.MachineName;
        _ipAddress = GetLocalIpAddress();
        _macAddress = GetPrimaryMacAddress();
    }

    public string DeviceId => _deviceId;
    public string DeviceName => _deviceName;
    public string IpAddress => _ipAddress;
    public string MacAddress => _macAddress;

    public void Initialize(OrganizationService organizationService)
    {
        _organizationService = organizationService;
        
        // 同时初始化 WebSocketService
        WebSocketService.Instance.Initialize(organizationService);
        
        // 当组织发生变化时自动处理设备注册
        // 这里我们不需要额外的监听器，因为 OrganizationService 会在 JoinOrganization 时调用 RegisterDeviceAsync
    }

    /// <summary>
    /// 注册设备到组织服务器
    /// </summary>
    public async Task<bool> RegisterDeviceAsync()
    {
        if (_organizationService == null || !_organizationService.HasJoinedOrganization)
        {
            LogService.Instance.Log("Debug", "Device", "DeviceService", "未加入组织，跳过设备注册");
            return false;
        }

        var org = _organizationService.CurrentOrganization;
        if (org == null || string.IsNullOrEmpty(org.ServerUrl) || string.IsNullOrEmpty(org.Id))
        {
            LogService.Instance.Log("Error", "Device", "DeviceService", "组织信息不完整，无法注册设备");
            return false;
        }

        try
        {
            LogService.Instance.Log("Info", "Device", "DeviceService", $"开始注册设备：{_deviceId} ({_deviceName}) 到组织：{org.Name} (ID: {org.Id})");
            Console.WriteLine($"[INFO] 开始注册设备：{_deviceId} ({_deviceName}) 到组织：{org.Name} (ID: {org.Id})");

            var currentIpAddress = GetLocalIpAddress();
            LogService.Instance.Log("Debug", "Device", "DeviceService", $"当前IP地址：{currentIpAddress}");
            Console.WriteLine($"[DEBUG] 当前IP地址：{currentIpAddress}");

            var deviceInfo = new
            {
                id = _deviceId,
                name = _deviceName,
                ipAddress = currentIpAddress,
                macAddress = _macAddress,
                organizationId = org.Id,
                osVersion = Environment.OSVersion.ToString(),
                appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                dotnetVersion = Environment.Version.ToString(),
                registeredAt = DateTime.Now,
                // 组织登记信息
                contactPhone = org.ContactPhone,
                className = org.ClassName,
                personInCharge = org.PersonInCharge
            };

            var json = JsonSerializer.Serialize(deviceInfo, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            LogService.Instance.Log("Debug", "Device", "DeviceService", $"发送设备信息：{json}");
            Console.WriteLine($"[DEBUG] 发送设备信息：{json}");

            var apiUrl = $"{org.ServerUrl}/api/organizations/{org.Id}/devices";
            LogService.Instance.Log("Debug", "Device", "DeviceService", $"API URL: {apiUrl}");
            Console.WriteLine($"[DEBUG] API URL: {apiUrl}");

            var response = await _httpClient.PostAsync(apiUrl, content);

            var responseContent = await response.Content.ReadAsStringAsync();
            LogService.Instance.Log("Info", "Device", "DeviceService", $"服务器响应：{response.StatusCode}, 内容：{responseContent}");
            Console.WriteLine($"[INFO] 服务器响应：{response.StatusCode}, 内容：{responseContent}");

            if (response.IsSuccessStatusCode)
            {
                _isRegistered = true;
                StartHeartbeat();
                LogService.Instance.Log("Info", "Device", "DeviceService", "设备注册成功，启动心跳");
                Console.WriteLine("[INFO] 设备注册成功，启动心跳");
                return true;
            }

            LogService.Instance.Log("Error", "Device", "DeviceService", $"设备注册失败：{response.StatusCode}");
            Console.WriteLine($"[ERROR] 设备注册失败：{response.StatusCode}");
            return false;
        }
        catch (TaskCanceledException ex)
        {
            LogService.Instance.Log("Error", "Device", "DeviceService", $"设备注册超时：{ex.Message}");
            Console.WriteLine($"[ERROR] 设备注册超时：{ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Device", "DeviceService", $"注册设备失败：{ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private async Task SendHeartbeatAsync()
    {
        if (!_isRegistered || _organizationService == null || !_organizationService.HasJoinedOrganization)
        {
            return;
        }

        var org = _organizationService.CurrentOrganization;
        if (org == null || string.IsNullOrEmpty(org.ServerUrl))
        {
            return;
        }

        try
        {
            // 使用 WebSocketService 的本地 IP（保持一致）
            var currentIpAddress = WebSocketService.Instance.IpAddress;
            
            var heartbeat = new
            {
                deviceId = _deviceId,
                timestamp = DateTime.Now,
                status = "online",
                cpuUsage = GetCpuUsage(),
                memoryUsage = GetMemoryUsage(),
                diskUsage = GetDiskUsage(),
                osVersion = Environment.OSVersion.ToString(),
                appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
                dotnetVersion = Environment.Version.ToString(),
                deviceName = _deviceName,
                ipAddress = currentIpAddress,
                macAddress = _macAddress
            };

            var json = JsonSerializer.Serialize(heartbeat);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{org.ServerUrl}/api/devices/{_deviceId}/heartbeat",
                content
            );

            if (!response.IsSuccessStatusCode)
            {
                LogService.Instance.Log("Warning", "Heartbeat", "DeviceService", $"HTTP心跳发送失败: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException ex)
        {
            LogService.Instance.Log("Error", "Heartbeat", "DeviceService", $"HTTP心跳发送超时：{ex.Message}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Heartbeat", "DeviceService", $"HTTP心跳发送失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 发送离线通知（关机时调用）
    /// </summary>
    public async Task SendOfflineNotificationAsync()
    {
        // 使用 WebSocket 发送离线通知
        await WebSocketService.Instance.SendOfflineAsync("shutdown");
        WebSocketService.Instance.Disconnect();
    }

    private void StartHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        
        // 启动 WebSocket 连接（主心跳，每 5 秒）
        if (_organizationService?.CurrentOrganization?.ServerUrl != null)
        {
            _ = WebSocketService.Instance.ConnectAsync(_organizationService.CurrentOrganization.ServerUrl);
        }
        
        // 启动 HTTP 心跳作为兜底（每 15 秒）
        // 当 WebSocket 断开时，HTTP 心跳保持设备在线
        _heartbeatTimer = new Timer(async _ =>
        {
            await SendHeartbeatAsync();
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        
        // 软件列表上传仍然使用 HTTP（一次性操作）
        _ = UploadSoftwareListAsync();
    }

    public async Task UploadSoftwareListAsync()
    {
        if (!_isRegistered || _organizationService == null || !_organizationService.HasJoinedOrganization)
        {
            return;
        }

        var org = _organizationService.CurrentOrganization;
        if (org == null || string.IsNullOrEmpty(org.ServerUrl))
        {
            return;
        }

        try
        {
            var softwareList = SoftwareInfoService.Instance.GetInstalledSoftware(true);
            
            var softwareData = softwareList.Select(s => new
            {
                name = s.Name,
                publisher = s.Publisher,
                version = s.Version,
                installDate = s.InstallDate,
                installLocation = s.InstallLocation,
                estimatedSize = s.EstimatedSize,
                uninstallString = s.UninstallString,
                isSystemSoftware = s.IsSystemSoftware
            }).ToList();

            var payload = new { software = softwareData };
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions 
            { 
                WriteIndented = false,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{org.ServerUrl}/api/devices/{_deviceId}/software",
                content
            );

            if (response.IsSuccessStatusCode)
            {
                LogService.Instance.Log("Info", "Software", "DeviceService", $"软件列表上传成功，共 {softwareList.Count} 个软件");
            }
            else
            {
                LogService.Instance.Log("Warning", "Software", "DeviceService", $"软件列表上传失败: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "Software", "DeviceService", $"上传软件列表失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 停止心跳
    /// </summary>
    public void StopHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
        _isRegistered = false;
    }

    /// <summary>
    /// 注销设备
    /// </summary>
    /// <param name="overrideServerUrl">可选的服务器地址，用于在组织信息被清空前注销</param>
    public async Task UnregisterDeviceAsync(string? overrideServerUrl = null)
    {
        StopHeartbeat();

        // 优先使用传入的服务器地址，如果没有则从组织服务获取
        string? serverUrl = overrideServerUrl;
        if (string.IsNullOrEmpty(serverUrl))
        {
            if (_organizationService == null || !_organizationService.HasJoinedOrganization)
            {
                LogService.Instance.Log("Debug", "DeviceUnregister", "DeviceService", "未加入组织，跳过注销");
                Console.WriteLine("[DEBUG] 未加入组织，跳过注销");
                return;
            }

            var org = _organizationService.CurrentOrganization;
            if (org == null || string.IsNullOrEmpty(org.ServerUrl))
            {
                LogService.Instance.Log("Debug", "DeviceUnregister", "DeviceService", "组织信息不完整，跳过注销");
                Console.WriteLine("[DEBUG] 组织信息不完整，跳过注销");
                return;
            }
            
            serverUrl = org.ServerUrl;
        }

        try
        {
            LogService.Instance.Log("Info", "Device", "DeviceService", $"正在注销设备：{_deviceId} -> {serverUrl}");
            Console.WriteLine($"[INFO] 正在注销设备：{_deviceId} -> {serverUrl}");
            
            var response = await _httpClient.DeleteAsync($"{serverUrl}/api/devices/{_deviceId}");
            
            if (response.IsSuccessStatusCode)
            {
                LogService.Instance.Log("Info", "Device", "DeviceService", $"设备注销成功：{_deviceId}");
                Console.WriteLine($"✓ 设备注销成功：{_deviceId}");
            }
            else
            {
                var content = await response.Content.ReadAsStringAsync();
                LogService.Instance.Log("Warning", "DeviceUnregister", "DeviceService", $"设备注销失败：{response.StatusCode} - {content}");
                Console.WriteLine($"[WARNING] 设备注销失败：{response.StatusCode} - {content}");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "DeviceUnregister", "DeviceService", $"注销设备失败：{ex.Message}");
            Console.WriteLine($"[ERROR] 注销设备失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 生成设备 ID（使用 MAC 地址和机器名）
    /// </summary>
    private static string GenerateDeviceId()
    {
        var mac = GetPrimaryMacAddress();
        var machineName = Environment.MachineName;
        var combined = $"{mac}-{machineName}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(combined))
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }

    /// <summary>
    /// 获取主 MAC 地址（优先选择物理网卡）
    /// </summary>
    private static string GetPrimaryMacAddress()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => 
                    adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    !adapter.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                    !adapter.Name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(adapter => 
                {
                    switch (adapter.NetworkInterfaceType)
                    {
                        case NetworkInterfaceType.Ethernet:
                            return 3;
                        case NetworkInterfaceType.Wireless80211:
                            return 2;
                        default:
                            return 1;
                    }
                })
                .ToList();

            foreach (var adapter in adapters)
            {
                var mac = adapter.GetPhysicalAddress();
                var macString = mac.ToString();
                if (!string.IsNullOrEmpty(macString) && macString != "000000000000")
                {
                    return macString;
                }
            }
        }
        catch { }

        return "000000000000";
    }

    /// <summary>
    /// 获取公网 IP 地址
    /// </summary>
    private static string GetLocalIpAddress()
    {
        var ipServices = new[]
        {
            "https://api.ipify.org?format=text",
            "https://icanhazip.com",
            "https://checkip.amazonaws.com"
        };

        foreach (var service in ipServices)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);
                
                Console.WriteLine($"[DEBUG] 尝试从 {service} 获取公网IP...");
                var response = client.GetStringAsync(service).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(response))
                {
                    var ip = response.Trim();
                    
                    // 确保只返回IPv4地址
                    if (System.Net.IPAddress.TryParse(ip, out var ipAddress))
                    {
                        if (ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            Console.WriteLine($"[INFO] 成功获取公网IPv4：{ip} (来源：{service})");
                            return ip;
                        }
                        else
                        {
                            Console.WriteLine($"[WARNING] {service} 返回了IPv6地址：{ip}，跳过");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] {service} 返回了无效的IP地址：{ip}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARNING] 从 {service} 获取公网IP失败：{ex.Message}");
            }
        }

        Console.WriteLine("[WARNING] 所有公网IP服务都失败，使用本地IP");
        
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                .OrderByDescending(a => a.Speed) // 优先选择速度快的接口（通常是以太网）
                .ThenBy(a => a.Description))
            {
                // 跳过虚拟网卡、回环接口和禁用的接口
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    adapter.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                    adapter.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    // 只获取IPv4地址
                    if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var localIp = unicast.Address.ToString();
                        
                        // 跳过169.254.x.x（APIPA地址）和127.x.x.x（回环地址）
                        if (localIp.StartsWith("169.254.") || localIp.StartsWith("127."))
                        {
                            continue;
                        }
                        
                        Console.WriteLine($"[INFO] 使用本地IPv4：{localIp} (接口：{adapter.Description})");
                        return localIp;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 获取本地IP失败：{ex.Message}");
        }
  
        return "127.0.0.1";
    }

    /// <summary>
    /// 获取 CPU 使用率（简化版，避免阻塞）
    /// </summary>
    private static double GetCpuUsage()
    {
        try
        {
            // 使用 WebSocketService 的缓存值，避免阻塞
            return WebSocketService.Instance.GetCachedCpuUsage();
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// 获取内存使用率
    /// </summary>
    private static double GetMemoryUsage()
    {
        try
        {
            var memory = Environment.WorkingSet / (1024.0 * 1024.0); // MB
            var totalMemory = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);
            return Math.Round((memory / totalMemory) * 100, 2);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// 获取磁盘使用率
    /// </summary>
    private static double GetDiskUsage()
    {
        try
        {
            var drive = System.IO.DriveInfo.GetDrives()[0];
            var total = drive.TotalSize;
            var free = drive.TotalFreeSpace;
            return Math.Round(((total - free) / (double)total) * 100, 2);
        }
        catch
        {
            return 0.0;
        }
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClassScreenLock.Models;
using SocketIOClient;

namespace ClassScreenLock.Services;

/// <summary>
/// WebSocket实时通信服务
/// 使用Socket.IO实现真正的WebSocket连接
/// </summary>
public class WebSocketService
{
    private static WebSocketService? _instance;
    public static WebSocketService Instance => _instance ??= new WebSocketService();
    
    private readonly string _deviceId;
    private readonly string _deviceName;
    private readonly string _ipAddress;
    private readonly string _macAddress;
    private OrganizationService? _organizationService;
    
    // Socket.IO客户端
    private SocketIO? _socket;
    private bool _isConnected;
    private bool _isRegistered;
    private bool _isConnecting;  // 防止并发连接
    private string? _serverUrl;
    private Timer? _heartbeatTimer;
    private Timer? _registerCheckTimer;  // 注册超时检查定时器
    private Timer? _reconnectWatchdogTimer;  // WS 连接看门狗（兜底重连）
    private DateTime _connectTime;       // 连接时间
    
    // CPU 使用率缓存（避免阻塞心跳线程）
    private double _cachedCpuUsage = 0.0;
    private Timer? _cpuUpdateTimer;
    private PerformanceCounter? _cpuCounter;
    
    // 事件回调队列
    private readonly ConcurrentQueue<Action> _actionQueue = new();
    private Timer? _actionProcessTimer;
    
    // 配置更新回调
    public event Action<SecurityConfiguration, NetworkConfiguration>? OnConfigUpdate;
    public event Action<string>? OnNotification;

    /// <summary>
    /// 收到集控端推送的消息时触发。
    /// 参数1: 消息内容
    /// 参数2: 是否朗读
    /// 参数3: 发送者用户名（可能为空）
    /// 参数4: 横幅尺寸（默认 Small）
    /// 参数5: 文字大小（默认 Medium，独立于尺寸）
    /// 参数6: 持续时间模式（默认 Auto）
    /// 参数7: 自定义持续秒数（默认 10）
    /// 参数8: 是否在通知时段禁止关闭窗口（默认 false）
    /// </summary>
    public event Action<string, bool, string, BannerSize, BannerFontSize, BannerDurationMode, int, bool>? OnDeviceMessage;
    
    public WebSocketService()
    {
        _deviceId = GenerateDeviceId();
        _deviceName = Environment.MachineName;
        _ipAddress = GetLocalIpAddress();
        _macAddress = GetPrimaryMacAddress();
    }
    
    public string DeviceId => _deviceId;
    public string DeviceName => _deviceName;
    public string IpAddress => _ipAddress;
    public string MacAddress => _macAddress;
    public bool IsConnected => _isConnected;
    
    /// <summary>
    /// 获取缓存的 CPU 使用率（供其他服务使用，避免阻塞）
    /// </summary>
    public double GetCachedCpuUsage() => _cachedCpuUsage;
    
    public void Initialize(OrganizationService organizationService)
    {
        _organizationService = organizationService;

        // 启动动作处理定时器
        _actionProcessTimer = new Timer(ProcessActionQueue, null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));

        // 启动 CPU 使用率缓存定时器（每 2 秒更新一次，避免阻塞心跳线程）
        StartCpuMonitor();

        // 启动 WS 连接看门狗（每 30 秒检查一次，若未连接则尝试重连）
        StartReconnectWatchdog();
    }

    /// <summary>
    /// WS 连接看门狗：当 Socket.IO 内置重连耗尽后，定期尝试重新建立连接
    /// </summary>
    private void StartReconnectWatchdog()
    {
        _reconnectWatchdogTimer?.Dispose();
        _reconnectWatchdogTimer = new Timer(async _ =>
        {
            try
            {
                // 仅在已知服务器地址且当前未连接时尝试重连
                if (!_isConnected && !_isConnecting && !string.IsNullOrEmpty(_serverUrl))
                {
                    LogService.Instance.Log("Info", "WebSocket", "WebSocketService",
                        $"WS 看门狗：连接已断开，尝试重新连接 {_serverUrl}");
                    await ConnectAsync(_serverUrl);
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", $"WS 看门狗重连失败: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }
    
    /// <summary>
    /// 启动 CPU 监控（后台定时更新缓存）
    /// </summary>
    private void StartCpuMonitor()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();  // 初始化
            
            _cpuUpdateTimer = new Timer(_ =>
            {
                try
                {
                    if (_cpuCounter != null)
                    {
                        _cachedCpuUsage = Math.Round(_cpuCounter.NextValue(), 2);
                    }
                }
                catch { }
            }, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }
        catch
        {
            // 如果无法创建性能计数器，使用备用方法
            _cachedCpuUsage = 0.0;
        }
    }
    
    /// <summary>
    /// 连接到服务器WebSocket
    /// </summary>
    public async Task ConnectAsync(string serverUrl)
    {
        // 防止并发连接
        if (_isConnecting)
        {
            return;
        }
        
        if (_isConnected && _serverUrl == serverUrl)
        {
            return;
        }
        
        _isConnecting = true;
        
        try
        {
            // 断开旧连接
            if (_socket != null)
            {
                await DisconnectSocketAsync();
            }
            
            _serverUrl = serverUrl;
            _connectTime = DateTime.Now;
            _isRegistered = false;
            
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", $"正在连接WebSocket: {serverUrl}");
            
            // 创建Socket.IO客户端
            var uri = new Uri(serverUrl);
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService",
                $"Socket.IO 配置: URI={uri}, Path=/socket.io, Timeout=30s");

            _socket = new SocketIO(uri, new SocketIOOptions
            {
                Path = "/socket.io",
                Reconnection = true,
                ReconnectionAttempts = 10,
                ConnectionTimeout = TimeSpan.FromSeconds(30),  // 增加到30秒
                AutoUpgrade = false  // 禁止自动升级到 WebSocket，强制使用长轮询
            });

            // 注册事件处理器
            RegisterSocketEvents();

            // 连接
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "开始调用 ConnectAsync...");
            await _socket.ConnectAsync();

            LogService.Instance.Log("Info", "WebSocket", "WebSocketService",
                $"ConnectAsync 返回，当前状态: Connected={_socket?.Connected}, _isConnected={_isConnected}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"连接失败: {ex.Message}");
        }
        finally
        {
            _isConnecting = false;
        }
    }
    
    /// <summary>
    /// 启动注册超时检查定时器（兜底机制）
    /// </summary>
    private void StartRegisterCheckTimer()
    {
        _registerCheckTimer?.Dispose();
        
        _registerCheckTimer = new Timer(async _ =>
        {
            try
            {
                // 如果连接成功但 10 秒内未注册成功，尝试重新注册
                if (_isConnected && !_isRegistered)
                {
                    var elapsed = (DateTime.Now - _connectTime).TotalSeconds;
                    if (elapsed > 10)
                    {
                        LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", "注册超时，尝试重新注册");
                        await RegisterDeviceAsync();
                        _connectTime = DateTime.Now;  // 重置计时
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"注册检查失败: {ex.Message}");
            }
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    
    /// <summary>
    /// 注册Socket.IO事件处理器
    /// </summary>
    private void RegisterSocketEvents()
    {
        if (_socket == null) return;
        
        // Socket.IO 内置连接成功事件
        _socket.OnConnected += async (sender, e) =>
        {
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "Socket.IO 连接成功");
            
            _isConnected = true;
            _isConnecting = false;  // 重置连接标志
            
            // 启动注册超时检查（兜底机制：10秒内未注册成功则重试）
            StartRegisterCheckTimer();
            
            // 注册设备
            await RegisterDeviceAsync();
        };
        
        // Socket.IO 内置断开连接事件
        _socket.OnDisconnected += async (sender, e) =>
        {
            _isConnected = false;
            _isRegistered = false;
            _isConnecting = false;  // 重置连接标志
            LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", "Socket.IO 断开连接");
        };
        
        // 服务器自定义确认连接事件
        _socket.On("connected", async (response) =>
        {
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "服务器确认连接");
            
            await Task.CompletedTask;
        });
        
        // 设备注册成功
        _socket.On("device_registered", async (response) =>
        {
            _isRegistered = true;
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "设备注册成功");
            
            // 停止注册超时检查定时器
            _registerCheckTimer?.Dispose();
            _registerCheckTimer = null;
            
            // 启动心跳
            StartHeartbeat();
            
            await Task.CompletedTask;
        });
        
        // 心跳确认
        _socket.On("heartbeat_ack", async (response) =>
        {
            try
            {
                var data = response.GetValue<HeartbeatAckResponse>(0);
                if (data != null && data.ConfigUpdated)
                {
                    _actionQueue.Enqueue(() => _ = SyncConfigAsync());
                }
            }
            catch { }
            
            await Task.CompletedTask;
        });
        
        // 配置更新推送
        _socket.On("config_update", async (response) =>
        {
            try
            {
                LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "收到配置更新推送");
                
                _actionQueue.Enqueue(() => _ = SyncConfigAsync());
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"配置更新处理失败: {ex.Message}");
            }
            
            await Task.CompletedTask;
        });
        
        // 通知推送
        _socket.On("notification", async (response) =>
        {
            try
            {
                var data = response.GetValue<NotificationResponse>(0);
                if (data != null)
                {
                    _actionQueue.Enqueue(() => OnNotification?.Invoke(data.Message));
                }
            }
            catch { }

            await Task.CompletedTask;
        });

        // 集控端推送的消息（可选朗读）
        _socket.On("device_message", async (response) =>
        {
            try
            {
                LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "===== 收到 device_message 事件 =====");

                // RawText 格式: ["device_message", {"deviceId":..., "message":..., ...}]
                // 需要解析数组后取 index 1 的 payload 对象
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(response.RawText);
                var root = jsonDoc.RootElement;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2)
                {
                    var payloadElement = root[1]; // index 1 是 payload 对象
                    // 使用大小写不敏感的序列化选项（Python 后端使用小写字段名 message, readAloud, sender）
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var data = System.Text.Json.JsonSerializer.Deserialize<DeviceMessageResponse>(payloadElement.GetRawText(), jsonOptions);

                    if (data != null && !string.IsNullOrEmpty(data.Message))
                    {
                        LogService.Instance.Log("Info", "WebSocket", "WebSocketService",
                            $"收到集控消息：{(string.IsNullOrEmpty(data.Sender) ? "" : $"来自 {data.Sender}：")}{data.Message}（朗读={data.ReadAloud}, size={data.Size}, fontSize={data.FontSize}, durationMode={data.DurationMode}, lockWindow={data.LockWindow}）");

                        var msg = data.Message;
                        var readAloud = data.ReadAloud;
                        var sender = data.Sender ?? string.Empty;
                        var size = ParseSize(data.Size);
                        var fontSize = ParseFontSize(data.FontSize);
                        var durationMode = ParseDurationMode(data.DurationMode);
                        var customSeconds = data.CustomDurationSeconds > 0 ? data.CustomDurationSeconds : 10;
                        var lockWindow = data.LockWindow;

                        LogService.Instance.Log("Info", "WebSocket", "WebSocketService",
                            $"准备入队 OnDeviceMessage 事件, msg='{msg}', readAloud={readAloud}, size={size}, fontSize={fontSize}, durationMode={durationMode}, lockWindow={lockWindow}");
                        _actionQueue.Enqueue(() =>
                        {
                            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "执行 OnDeviceMessage 事件");
                            OnDeviceMessage?.Invoke(msg, readAloud, sender, size, fontSize, durationMode, customSeconds, lockWindow);
                        });
                    }
                    else
                    {
                        LogService.Instance.Log("Warning", "WebSocket", "WebSocketService",
                            $"device_message payload 解析后 Message 为空, payload={payloadElement.GetRawText()}");
                    }
                }
                else
                {
                    LogService.Instance.Log("Warning", "WebSocket", "WebSocketService",
                        $"device_message RawText 格式异常: {response.RawText}");
                }
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"处理设备消息失败: {ex.Message}");
            }

            await Task.CompletedTask;
        });
        
        // 错误消息
        _socket.On("error", async (response) =>
        {
            try
            {
                var data = response.GetValue<ErrorResponse>(0);
                if (data != null)
                {
                    LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"服务器错误: {data.Message}");
                }
            }
            catch { }
            
            await Task.CompletedTask;
        });
        
        // 断开连接
        _socket.On("disconnect", async (response) =>
        {
            _isConnected = false;
            _isRegistered = false;
            LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", "连接断开");
            
            await Task.CompletedTask;
        });
        
        // 重连成功
        _socket.On("reconnect", async (response) =>
        {
            _isConnected = true;
            _isConnecting = false;  // 重置连接标志
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "重连成功");
            
            // 重新注册设备
            await RegisterDeviceAsync();
        });
    }
    
    /// <summary>
    /// 注册设备
    /// </summary>
    private async Task RegisterDeviceAsync()
    {
        if (_socket == null || !_isConnected || _organizationService?.CurrentOrganization == null)
        {
            LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", "无法注册设备：连接未建立或组织信息缺失");
            return;
        }
        
        try
        {
            var org = _organizationService.CurrentOrganization;
            
            // 发送参数数组
            await _socket.EmitAsync("device_register", new object[] { _deviceId, org.Id, _deviceName, GetLocalIpAddress(), _macAddress });
            
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", $"发送设备注册请求: {_deviceId}, org={org.Id}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"注册失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 启动心跳
    /// </summary>
    private void StartHeartbeat()
    {
        _heartbeatTimer?.Dispose();
        
        // 立即发送一次心跳
        _ = SendHeartbeatAsync();
        
        // 每5秒发送心跳
        _heartbeatTimer = new Timer(async _ =>
        {
            await SendHeartbeatAsync();
        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }
    
    /// <summary>
    /// 发送心跳
    /// </summary>
    private async Task SendHeartbeatAsync()
    {
        if (_socket == null || !_isConnected || !_isRegistered)
        {
            return;
        }
        
        try
        {
            // 使用缓存的 CPU 使用率（避免阻塞）
            await _socket.EmitAsync("device_heartbeat", new object[] { 
                _deviceId, 
                _deviceName, 
                GetLocalIpAddress(), 
                _cachedCpuUsage,  // 使用缓存值
                GetMemoryUsage(), 
                GetDiskUsage() 
            });
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", $"心跳发送失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 同步配置
    /// </summary>
    private async Task SyncConfigAsync()
    {
        if (_organizationService == null || string.IsNullOrEmpty(_serverUrl))
        {
            return;
        }
        
        try
        {
            var org = _organizationService.CurrentOrganization;
            if (org == null)
            {
                return;
            }
            
            // 获取最新配置
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            var securityResponse = await httpClient.GetAsync($"{_serverUrl}/api/organizations/{org.Id}/security-config");
            var networkResponse = await httpClient.GetAsync($"{_serverUrl}/api/organizations/{org.Id}/network-config");
            
            SecurityConfiguration? securityConfig = null;
            NetworkConfiguration? networkConfig = null;
            
            if (securityResponse.IsSuccessStatusCode)
            {
                var securityJson = await securityResponse.Content.ReadAsStringAsync();
                securityConfig = JsonSerializer.Deserialize<SecurityConfiguration>(securityJson);
            }
            
            if (networkResponse.IsSuccessStatusCode)
            {
                var networkJson = await networkResponse.Content.ReadAsStringAsync();
                networkConfig = JsonSerializer.Deserialize<NetworkConfiguration>(networkJson);
            }
            
            // 触发配置更新回调
            if (securityConfig != null || networkConfig != null)
            {
                _actionQueue.Enqueue(() => OnConfigUpdate?.Invoke(securityConfig!, networkConfig!));
                
                LogService.Instance.Log("Info", "WebSocket", "WebSocketService", "配置已更新");
            }
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"配置同步失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 处理动作队列
    /// </summary>
    private void ProcessActionQueue(object? state)
    {
        while (_actionQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Error", "WebSocket", "WebSocketService", $"动作执行失败: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// 发送离线通知
    /// </summary>
    public async Task SendOfflineAsync(string reason = "shutdown")
    {
        if (_socket == null || !_isConnected || !_isRegistered)
        {
            return;
        }
        
        try
        {
            // 发送参数数组
            await _socket.EmitAsync("device_offline", new object[] { _deviceId, reason });
            
            LogService.Instance.Log("Info", "WebSocket", "WebSocketService", $"已发送离线通知: {reason}");
        }
        catch (Exception ex)
        {
            LogService.Instance.Log("Warning", "WebSocket", "WebSocketService", $"发送离线通知失败: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 断开Socket连接
    /// </summary>
    private async Task DisconnectSocketAsync()
    {
        if (_socket != null)
        {
            try
            {
                await _socket.DisconnectAsync();
            }
            catch { }
            
            _socket = null;
        }
    }
    
    /// <summary>
    /// 断开连接
    /// </summary>
    public void Disconnect()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        _registerCheckTimer?.Dispose();
        _registerCheckTimer = null;

        _reconnectWatchdogTimer?.Dispose();
        _reconnectWatchdogTimer = null;

        _cpuUpdateTimer?.Dispose();
        _cpuUpdateTimer = null;
        
        _cpuCounter?.Dispose();
        _cpuCounter = null;
        
        _actionProcessTimer?.Dispose();
        _actionProcessTimer = null;
        
        _ = DisconnectSocketAsync();
        
        _isConnected = false;
        _isRegistered = false;
    }
    
    /// <summary>
    /// 生成设备ID
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
    /// 获取主MAC地址
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
    /// 获取本地IP地址
    /// </summary>
    private static string GetLocalIpAddress()
    {
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces()
                .OrderByDescending(a => a.Speed))
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    adapter.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var unicast in adapter.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var localIp = unicast.Address.ToString();
                        if (!localIp.StartsWith("169.254.") && !localIp.StartsWith("127."))
                        {
                            return localIp;
                        }
                    }
                }
            }
        }
        catch { }
        
        return "127.0.0.1";
    }
    
    /// <summary>
    /// 获取内存使用率
    /// </summary>
    private static double GetMemoryUsage()
    {
        try
        {
            var memory = Environment.WorkingSet / (1024.0 * 1024.0);
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
    
    // 响应数据类
    private class HeartbeatAckResponse
    {
        public string Timestamp { get; set; } = "";
        public bool ConfigUpdated { get; set; }
    }
    
    private class NotificationResponse
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 集控端推送的消息响应
    /// </summary>
    private class DeviceMessageResponse
        {
            public string DeviceId { get; set; } = "";
            public string Message { get; set; } = "";
            public bool ReadAloud { get; set; }
            public string? Sender { get; set; }
            public string? Timestamp { get; set; }
            // 横幅尺寸：small / medium / large / xlarge（默认 small）
            public string? Size { get; set; }
            // 文字大小：small / medium / large / xlarge（默认 medium，独立于尺寸）
            public string? FontSize { get; set; }
            // 持续时间模式：auto / short / medium / long / custom / persistent（默认 auto）
            public string? DurationMode { get; set; }
            // 自定义持续秒数（仅 durationMode=custom 时使用）
            public int CustomDurationSeconds { get; set; }
            // 是否在通知时段禁止关闭窗口（默认 false）
            public bool LockWindow { get; set; }
        }

        /// <summary>
        /// 解析尺寸字符串
        /// </summary>
        private static BannerSize ParseSize(string? sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr)) return BannerSize.Small;
            return sizeStr.Trim().ToLowerInvariant() switch
            {
                "small" or "s" or "小" => BannerSize.Small,
                "medium" or "m" or "中" => BannerSize.Medium,
                "large" or "l" or "大" => BannerSize.Large,
                "xlarge" or "xl" or "超大" or "超长" => BannerSize.XLarge,
                _ => BannerSize.Small
            };
        }

        /// <summary>
        /// 解析文字大小字符串
        /// </summary>
        private static BannerFontSize ParseFontSize(string? fontSizeStr)
        {
            if (string.IsNullOrWhiteSpace(fontSizeStr)) return BannerFontSize.Medium;
            return fontSizeStr.Trim().ToLowerInvariant() switch
            {
                "small" or "s" or "小" => BannerFontSize.Small,
                "medium" or "m" or "中" => BannerFontSize.Medium,
                "large" or "l" or "大" => BannerFontSize.Large,
                "xlarge" or "xl" or "超大" => BannerFontSize.XLarge,
                _ => BannerFontSize.Medium
            };
        }

        /// <summary>
        /// 解析持续时间模式字符串
        /// </summary>
        private static BannerDurationMode ParseDurationMode(string? modeStr)
        {
            if (string.IsNullOrWhiteSpace(modeStr)) return BannerDurationMode.Auto;
            return modeStr.Trim().ToLowerInvariant() switch
            {
                "auto" or "a" or "自动" => BannerDurationMode.Auto,
                "short" or "s" or "短" => BannerDurationMode.Short,
                "medium" or "m" or "中" => BannerDurationMode.Medium,
                "long" or "l" or "长" => BannerDurationMode.Long,
                "custom" or "c" or "自定义" => BannerDurationMode.Custom,
                "persistent" or "p" or "持久" => BannerDurationMode.Persistent,
                _ => BannerDurationMode.Auto
            };
        }

    private class ErrorResponse
    {
        public string Message { get; set; } = "";
    }
}
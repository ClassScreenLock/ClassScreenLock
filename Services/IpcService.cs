using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClassScreenLock.Services;

public class IpcService
{
    private static readonly IpcService _instance = new();
    public static IpcService Instance => _instance;

    private CancellationTokenSource? _cts;

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        LogService.Observe(Task.Run(() => RunServer(_cts.Token)), "IPC.RunServer");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task RunServer(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // 注意：不要在这里使用 using，由 HandleClient 负责释放
                var server = new NamedPipeServerStream("ClassScreenLock_IPC", PipeDirection.InOut, 50, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);
                LogService.Observe(Task.Run(() => HandleClient(server)), "IPC.HandleClient");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Log("IPC", "ServerError", "Server", ex.Message); } catch { }
                await Task.Delay(1000, token); // 发生错误时稍作等待
            }
        }
    }

    private void HandleClient(NamedPipeServerStream server)
    {
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8);
            while (server.IsConnected)
            {
                var line = reader.ReadLine();
                if (line == null) break;
                ProcessMessage(line, server);
            }
        }
        catch
        {
        }
        finally
        {
            try { server.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// 校验当前 IPC 客户端是否具备管理员/SYSTEM 权限。
    /// 通过模拟客户端令牌判断，普通学生账户无法通过校验，
    /// 因此无法利用 EXIT 命令绕过防护退出主程序。
    /// </summary>
    private static bool IsElevatedClient(NamedPipeServerStream? server)
    {
        if (server == null) return false;

        try
        {
            bool elevated = false;
            server.RunAsClient(() =>
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                if (identity.IsSystem)
                {
                    elevated = true;
                    return;
                }

                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                elevated = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            });
            return elevated;
        }
        catch
        {
            return false;
        }
    }

    private void ProcessMessage(string msg, NamedPipeServerStream? server = null)
    {
        try
        {
            var m = msg.Trim();
            if (string.Equals(m, "LOCK", StringComparison.OrdinalIgnoreCase))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                {
                    if (InitializationService.Instance.RequiresInitialization)
                    {
                        LogService.Instance.Log("Warning", "IPC", "Lock", "Cannot lock: initialization required");
                        return;
                    }
                    
                    var mode = SettingsService.Lock.BreakTimeLockMode;
                    LockScreenService.Instance.ActivateLock(mode);
                });
                LogService.Instance.Log("IPC", "Lock", "BreakButtonProcess");
            }
            else if (string.Equals(m, "PING", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Instance.Log("IPC", "Ping", "BreakButtonProcess");
            }
            else if (string.Equals(m, "SHOW_MAIN", StringComparison.OrdinalIgnoreCase) || string.Equals(m, "SHOW", StringComparison.OrdinalIgnoreCase))
            {
                LogService.Instance.Log("IPC", "ShowMain", "Server", "收到显示主窗口请求");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            if (desktop.MainWindow != null)
                            {
                                var window = desktop.MainWindow;
                                LogService.Instance.Log("IPC", "ShowMain", "Server", $"主窗口状态：IsVisible={window.IsVisible}, WindowState={window.WindowState}, Position=({window.Position.X}, {window.Position.Y}), Size=({window.Bounds.Width}x{window.Bounds.Height})");
                                
                                // 强制将窗口移到屏幕中央
                                window.WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen;
                                
                                window.Show();
                                window.WindowState = Avalonia.Controls.WindowState.Normal;
                                window.Activate();
                                window.Focus();
                                
                                // 临时设置为最顶层，确保窗口可见
                                window.Topmost = true;
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    window.Topmost = false;
                                }, Avalonia.Threading.DispatcherPriority.ApplicationIdle);
                                
                                LogService.Instance.Log("IPC", "ShowMain", "Server", "主窗口已显示并激活");
                            }
                            else
                            {
                                LogService.Instance.Log("Error", "ShowMain", "Server", "主窗口不存在！desktop.MainWindow = null");
                            }
                        }
                        else
                        {
                            LogService.Instance.Log("Error", "ShowMain", "Server", "ApplicationLifetime 不是 IClassicDesktopStyleApplicationLifetime");
                        }
                    }
                    catch (Exception ex)
                    {
                        try { LogService.Instance.Log("IPC", "ShowMainError", "Server", ex.Message); } catch { }
                    }
                });
                LogService.Instance.Log("IPC", "ShowMain", "Client");
            }
            else if (string.Equals(m, "EXIT", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(m, "SHUTDOWN", StringComparison.OrdinalIgnoreCase))
            {
                // 卸载程序专用退出接口：仅管理员/SYSTEM 客户端可调用。
                // 走 normal shutdown，触发 OnApplicationExit 完整清理流程
                // （停服务监控、通知占坑实例退出、写退出标志等），
                // 保证退出后不会被任何守护进程重新拉起。
                if (!IsElevatedClient(server))
                {
                    LogService.Instance.Log("Warning", "IPC", "ExitDenied", "拒绝非管理员退出请求");
                    return;
                }

                LogService.Instance.Log("IPC", "Exit", "Uninstaller", "收到管理员退出请求，正在关闭主程序");
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (Avalonia.Application.Current?.ApplicationLifetime is
                            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.Shutdown();
                        }
                        else
                        {
                            Environment.Exit(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        try { LogService.Instance.Log("Error", "IPC", "Exit", ex.Message); } catch { }
                        Environment.Exit(0);
                    }
                });
            }
        }
        catch (Exception ex)
        {
            try { LogService.Instance.Log("IPC", "Error", "Server", ex.Message); } catch { }
        }
    }
}

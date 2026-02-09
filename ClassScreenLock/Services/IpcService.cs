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
                ProcessMessage(line);
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

    private void ProcessMessage(string msg)
    {
        try
        {
            var m = msg.Trim();
            if (string.Equals(m, "LOCK", StringComparison.OrdinalIgnoreCase))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                {
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
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                        {
                            desktop.MainWindow.Show();
                            desktop.MainWindow.WindowState = Avalonia.Controls.WindowState.Normal;
                            desktop.MainWindow.Activate();
                        }
                    }
                    catch (Exception ex)
                    {
                        try { LogService.Instance.Log("IPC", "ShowMainError", "Server", ex.Message); } catch { }
                    }
                });
                LogService.Instance.Log("IPC", "ShowMain", "Client");
            }
        }
        catch (Exception ex)
        {
            try { LogService.Instance.Log("IPC", "Error", "Server", ex.Message); } catch { }
        }
    }
}

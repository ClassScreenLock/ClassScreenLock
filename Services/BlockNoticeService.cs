using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClassScreenLock.Views;

namespace ClassScreenLock.Services;

/// <summary>
/// 阻止提示 IPC 服务。
///
/// 劫持程序（CSL.IfeoRedirector）在拒绝启动目标进程时，通过命名管道通知本服务；
/// 主程序以 SYSTEM + UIAccess 身份显示 WDAC 风格置顶弹窗——UIAccess band 窗口
/// 可覆盖锁屏遮罩等一切普通窗口，这是劫持程序（普通进程，无 UIAccess）自己
/// 弹窗做不到的，也是"类似真的 WDAC"的关键。
///
/// 权限要点：劫持程序可能以普通用户身份运行（学生尝试启动被拦截进程），
/// 因此管道 ACL 必须放行 Everyone 读写；服务端由 SYSTEM 主程序常驻监听。
/// 主程序不可达时（未运行 / 重启中），劫持程序自动降级为自身弹窗，拦截不受影响。
/// </summary>
public sealed class BlockNoticeService
{
    public const string PipeName = "CSL.BlockNotice";

    private static readonly BlockNoticeService _instance = new();
    public static BlockNoticeService Instance => _instance;

    private CancellationTokenSource? _cts;

    private BlockNoticeService()
    {
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        LogService.Observe(Task.Run(() => RunServer(_cts.Token)), "BlockNotice.RunServer");
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
                // 注意：不要在这里 using，由 HandleClient 负责释放
                var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 8,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096);
                AllowEveryoneAccess(server);
                await server.WaitForConnectionAsync(token);
                LogService.Observe(Task.Run(() => HandleClient(server)), "BlockNotice.HandleClient");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                try { LogService.Instance.Log("Error", "BlockNotice", "RunServer", ex.Message); } catch { }
                await Task.Delay(1000, token); // 发生错误时稍作等待
            }
        }
    }

    /// <summary>劫持程序可能以普通用户身份运行，管道必须允许 Everyone 读写。</summary>
    private static void AllowEveryoneAccess(NamedPipeServerStream server)
    {
        try
        {
            var security = new PipeSecurity();
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            security.AddAccessRule(new PipeAccessRule(everyone, PipeAccessRights.ReadWrite, AccessControlType.Allow));
            server.SetAccessControl(security);
        }
        catch
        {
            // ACL 设置失败则维持默认（仅 SYSTEM/管理员），劫持程序连接会失败并本地降级弹窗
        }
    }

    private void HandleClient(NamedPipeServerStream server)
    {
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8);
            var line = reader.ReadLine();
            if (line == null) return;

            // 主程序已接手显示：立即回 OK，客户端据此不再本地弹窗
            try
            {
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                writer.Write("OK\n");
            }
            catch
            {
            }

            ProcessMessage(line);
        }
        catch
        {
        }
        finally
        {
            try { server.Dispose(); } catch { }
        }
    }

    private void ProcessMessage(string message)
    {
        try
        {
            // 协议：BLOCK|{appName}|{appPath}|{epochMs}
            // Windows 文件名不允许 '|'（保留字符），用 '|' 作字段分隔安全
            var parts = message.Trim().Split('|');
            if (parts.Length < 3 || !string.Equals(parts[0], "BLOCK", StringComparison.OrdinalIgnoreCase)) return;

            var appName = parts[1];
            var appPath = parts[2];

            // 切到 UI 线程显示弹窗
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    WdacBlockWindow.Show(appName, appPath);
                }
                catch (Exception ex)
                {
                    try { LogService.Instance.Log("Error", "BlockNotice", "ShowWindow", ex.Message); } catch { }
                }
            });
        }
        catch
        {
        }
    }
}

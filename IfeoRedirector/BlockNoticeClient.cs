using System;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace CSL.IfeoRedirector;

/// <summary>
/// 阻止提示通知客户端：拒绝启动时优先通知主程序（SYSTEM + UIAccess 身份），
/// 由主程序显示 WDAC 风格置顶弹窗——UIAccess band 窗口可覆盖锁屏遮罩等一切窗口，
/// 这是劫持程序（普通进程，无 UIAccess）自身弹窗做不到的。
///
/// 主程序不可达（未运行 / 重启中 / 管道忙）时返回 false，由调用方降级为本地弹窗，
/// "拒绝"这一核心结果在任何情况下都不受影响。
/// </summary>
internal static class BlockNoticeClient
{
    private const string PipeName = "CSL.BlockNotice";
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(1200);
    private static readonly TimeSpan AckTimeout = TimeSpan.FromMilliseconds(800);

    /// <summary>
    /// 尝试通知主程序显示阻止提示。
    /// 返回 true 表示主程序已确认接手显示；false 表示主程序不可达，需要本地降级弹窗。
    /// </summary>
    public static bool TryNotifyMainApp(string appName, string appPath)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
            if (!pipe.IsConnected) return false;

            // Windows 文件名不允许 '|'（保留字符），用 '|' 作字段分隔安全
            var line = $"BLOCK|{appName}|{appPath}|{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();

            // 等待主程序确认接手（回 "OK"），避免"已通知但主程序未显示"的竞态
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
            reader.BaseStream.ReadTimeout = (int)AckTimeout.TotalMilliseconds;
            var ack = reader.ReadLine();
            return ack != null && ack.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // 管道不存在（主程序未运行）/ 连接超时 / 读写失败 → 全部视为不可达
            return false;
        }
    }
}

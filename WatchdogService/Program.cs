using System.ServiceProcess;

namespace CSL.WatchdogService;

/// <summary>
/// Windows 服务入口点。
/// 该服务作为 SYSTEM 级看门狗运行，使用 CreateProcessAsUser API
/// 在活动用户的会话中启动并监控 ClassScreenLock 主程序。
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        ServiceLogger.Log("WatchdogService", $"进程启动，参数: {string.Join(" ", args)}");

        var servicesToRun = new ServiceBase[]
        {
            new WatchdogService()
        };

        ServiceBase.Run(servicesToRun);
    }
}

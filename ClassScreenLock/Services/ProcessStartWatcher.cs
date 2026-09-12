using System;
using System.Management;

namespace ClassScreenLock.Services;

/// <summary>
/// WMI 进程创建事件订阅器。
///
/// 背景：AppBlockingService 原先靠轮询全量扫描进程来拦截，存在"空窗期"
/// （进程启动到下一次扫描之间的窗口，锁屏 300ms / 防护 500ms / 普通 2000ms）。
/// 本类改为事件驱动：订阅 WMI 进程创建事件，进程启动瞬间（毫秒级）即回调，
/// 从架构上消除空窗期。
///
/// 通道（自动降级）：
///   1. Win32_ProcessStartTrace —— 内核级进程跟踪事件，创建即触发，延迟毫秒级；
///      需要管理员权限（本软件 manifest 已声明 requireAdministrator）。
///   2. __InstanceCreationEvent WITHIN 2 —— WMI 服务层每 2 秒查询一次进程创建，
///      延迟约 1-2 秒；在权限受限或跟踪事件不可用时作为降级通道。
///
/// 线程模型：事件回调运行在 WMI 事件线程上，回调内必须 try/catch 全包，
/// 不能把异常抛回 WMI 事件线程。
/// </summary>
public sealed class ProcessStartWatcher : IDisposable
{
    private ManagementEventWatcher? _watcher;
    private readonly object _sync = new();
    private volatile bool _running;
    private string _mode = "none";

    /// <summary>是否正在监听。</summary>
    public bool IsRunning => _running;

    /// <summary>当前使用的 WMI 通道名称（诊断用）。</summary>
    public string Mode => _mode;

    /// <summary>进程创建事件：参数为进程 PID。</summary>
    public event Action<int>? ProcessStarted;

    /// <summary>
    /// 启动订阅。先尝试 Win32_ProcessStartTrace（内核事件，实时），
    /// 失败时降级为 __InstanceCreationEvent（WMI 轮询，约 1-2 秒）。
    /// 重复调用安全（已运行则直接返回 true）。
    /// </summary>
    public bool Start()
    {
        lock (_sync)
        {
            if (_running) return true;

            // 1) 首选：内核级进程跟踪事件（实时，毫秒级）
            try
            {
                var watcher = new ManagementEventWatcher(
                    new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                watcher.EventArrived += OnEventArrived;
                watcher.Start();
                _watcher = watcher;
                _mode = "Win32_ProcessStartTrace";
                _running = true;
                LogService.Instance.Log("Info", "ProcWatcher", "Start",
                    "WMI 进程创建事件订阅已启动（Win32_ProcessStartTrace，实时）");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "ProcWatcher", "Start",
                    $"Win32_ProcessStartTrace 订阅失败，尝试降级通道: {ex.Message}");
            }

            // 2) 降级：WMI 服务层每 2 秒查询一次进程创建
            try
            {
                var watcher = new ManagementEventWatcher(
                    new WqlEventQuery("__InstanceCreationEvent", new TimeSpan(0, 0, 2),
                        "TargetInstance ISA 'Win32_Process'"));
                watcher.EventArrived += OnEventArrived;
                watcher.Start();
                _watcher = watcher;
                _mode = "__InstanceCreationEvent";
                _running = true;
                LogService.Instance.Log("Info", "ProcWatcher", "Start",
                    "WMI 进程创建事件订阅已启动（__InstanceCreationEvent 降级模式，约 1-2 秒延迟）");
                return true;
            }
            catch (Exception ex2)
            {
                LogService.Instance.Log("Error", "ProcWatcher", "Start",
                    $"WMI 进程创建订阅全部失败: {ex2.Message}");
                _watcher = null;
                _running = false;
                return false;
            }
        }
    }

    /// <summary>停止订阅并释放资源。重复调用安全。</summary>
    public void Stop()
    {
        lock (_sync)
        {
            _running = false;
            if (_watcher == null) return;

            try
            {
                _watcher.EventArrived -= OnEventArrived;
                _watcher.Stop();
                _watcher.Dispose();
            }
            catch (Exception ex)
            {
                LogService.Instance.Log("Warning", "ProcWatcher", "Stop", $"停止订阅失败: {ex.Message}");
            }
            finally
            {
                _watcher = null;
                _mode = "none";
            }
        }
    }

    private void OnEventArrived(object sender, EventArrivedEventArgs e)
    {
        if (!_running) return;

        int pid;
        try
        {
            if (_mode == "Win32_ProcessStartTrace")
            {
                pid = Convert.ToInt32(e.NewEvent?["ProcessID"]);
            }
            else
            {
                // __InstanceCreationEvent 的 TargetInstance 才是 Win32_Process
                using var target = e.NewEvent?["TargetInstance"] as ManagementBaseObject;
                pid = target == null ? 0 : Convert.ToInt32(target["ProcessID"]);
            }
        }
        catch
        {
            return;
        }

        if (pid <= 0) return;
        ProcessStarted?.Invoke(pid);
    }

    public void Dispose()
    {
        Stop();
    }
}

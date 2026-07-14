using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;

namespace MonitorProcess;

[SupportedOSPlatform("windows")]
class Program
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

    private static readonly Dictionary<int, (PerformanceCounter Read, PerformanceCounter Write)> _ioCounters = new();
    
    // 备选方案数据
    private static readonly Dictionary<int, (long Read, long Write, DateTime Stamp)> _ioFallback = new();
    
    static async Task Main(string[] args)
    {
        // 设置独立的 AppUserModelID
        try
        {
            SetCurrentProcessExplicitAppUserModelID("CSL.Monitor");
        }
        catch { }

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var cts = new CancellationTokenSource();

        // 监听退出信号
        var t = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    var line = Console.ReadLine();
                    if (line == "exit" || line == null)
                    {
                        cts.Cancel();
                        break;
                    }
                }
            }
            catch { }
        });
        t.ContinueWith(ct => { try { Console.Error.WriteLine(ct.Exception?.ToString()); } catch { } }, TaskContinuationOptions.OnlyOnFaulted);

        Console.Error.WriteLine("MonitorProcess started.");

        while (!cts.IsCancellationRequested)
        {            try
            {
                var start = DateTime.Now;

                // 收集所有进程数据
                var stats = CollectAllStats();
                
                // 输出 JSON
                if (stats.Count > 0)
                {                    foreach (var s in stats)
                    {
                        var line = JsonSerializer.Serialize(s);
                        Console.WriteLine(line);
                    }
                    Console.Out.Flush();
                }

                // 控制频率，确保采样间隔约为 1 秒
                var elapsed = (DateTime.Now - start).TotalMilliseconds;
                var delay = Math.Max(200, 1000 - (int)elapsed);
                await Task.Delay(delay, cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error in main loop: {ex.Message}");
                await Task.Delay(2000, cts.Token);
            }
        }

        Cleanup();
    }

    private static List<ProcessStats> CollectAllStats()
    {
        var results = new List<ProcessStats>();
        var processes = Process.GetProcesses();

        int processedCount = 0;
        foreach (var p in processes)
        {
            int pid = p.Id;
            if (pid <= 4) continue; // 跳过 System 和 Idle

            try
            {
                var stats = new ProcessStats { Pid = pid };
                
                // IO 采样
                var io = GetIoUsage(p);
                stats.IoRead = io.Read;
                stats.IoWrite = io.Write;

                results.Add(stats);
                processedCount++;
            }
            catch { }
            finally
            {
                p.Dispose();
            }
        }

        return results;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr hProcess, out IO_COUNTERS lpIoCounters);

    private static (float Read, float Write) GetIoUsage(Process p)
    {
        int pid = p.Id;
        try
        {
            if (GetProcessIoCounters(p.Handle, out var counters))
            {
                var now = DateTime.Now;
                long currentRead = (long)counters.ReadTransferCount;
                long currentWrite = (long)counters.WriteTransferCount;

                if (_ioFallback.TryGetValue(pid, out var last))
                {
                    double sec = (now - last.Stamp).TotalSeconds;
                    _ioFallback[pid] = (currentRead, currentWrite, now);
                    if (sec > 0.1)
                    {
                        return ((float)((currentRead - last.Read) / sec), (float)((currentWrite - last.Write) / sec));
                    }
                }
                else
                {
                    _ioFallback[pid] = (currentRead, currentWrite, now);
                }
            }
        }
        catch { _ioFallback.Remove(pid); }
        return (0, 0);
    }

    private static void Cleanup()
    {
        foreach (var c in _ioCounters.Values)
        {
            c.Read.Dispose();
            c.Write.Dispose();
        }
    }
}

public class ProcessStats
{
    public int Pid { get; set; }
    public float IoRead { get; set; }
    public float IoWrite { get; set; }
}

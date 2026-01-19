using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using ClassScreenLock.Models;

namespace ClassScreenLock.Services;

public class TestResult
{
    public string TestName { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Details { get; set; } = string.Empty;
    public long ResponseTimeMs { get; set; }
}

public class AutomationTestService
{
    private static readonly AutomationTestService _instance = new();
    public static AutomationTestService Instance => _instance;

    private AutomationTestService() { }

    public async Task<List<TestResult>> RunFullDiagnosticsAsync()
    {
        var results = new List<TestResult>();

        // 1. 测试内容分析引擎准确率
        results.Add(TestContentAnalysis());

        // 2. 测试拦截性能（模拟 60Hz 循环）
        results.Add(await TestDetectionPerformance());

        // 3. 测试数据库持久化
        results.Add(TestDatabasePersistence());

        return results;
    }

    private TestResult TestContentAnalysis()
    {
        var sw = Stopwatch.StartNew();
        var rules = new List<NetworkRule> { new NetworkRule { Domain = "test-bad-site.com", IsEnabled = true } };
        
        var res1 = ContentAnalysisEngine.Instance.Analyze("Welcome to test-bad-site.com", rules);
        var res2 = ContentAnalysisEngine.Instance.Analyze("Play casino games online", rules);
        var res3 = ContentAnalysisEngine.Instance.Analyze("Safe education site", rules);

        sw.Stop();
        bool passed = res1.IsViolation && res2.IsViolation && !res3.IsViolation;

        return new TestResult
        {
            TestName = "Content Analysis Accuracy",
            Passed = passed,
            ResponseTimeMs = sw.ElapsedMilliseconds,
            Details = $"Match: {res1.IsViolation}, Heuristic: {res2.IsViolation}, Safe: {!res3.IsViolation}"
        };
    }

    private async Task<TestResult> TestDetectionPerformance()
    {
        var sw = Stopwatch.StartNew();
        int cycles = 60;
        long totalMs = 0;

        for (int i = 0; i < cycles; i++)
        {
            var cycleSw = Stopwatch.StartNew();
            // 模拟一次扫描循环
            var processes = Process.GetProcesses();
            cycleSw.Stop();
            totalMs += cycleSw.ElapsedMilliseconds;
            await Task.Delay(1); 
        }

        sw.Stop();
        double avgMs = (double)totalMs / cycles;

        return new TestResult
        {
            TestName = "60Hz Detection Performance",
            Passed = avgMs < 16.6, // 必须能在 16.6ms 内完成一次全进程扫描
            ResponseTimeMs = (long)avgMs,
            Details = $"Average cycle time: {avgMs:F2}ms (Target < 16.6ms)"
        };
    }

    private TestResult TestDatabasePersistence()
    {
        var sw = Stopwatch.StartNew();
        var entry = new InterceptedContent
        {
            Timestamp = DateTime.Now,
            Domain = "test.com",
            Reason = "Automation Test"
        };
        
        InterceptionDatabase.Instance.Add(entry);
        var history = InterceptionDatabase.Instance.GetHistory();
        
        sw.Stop();
        bool found = history.Exists(h => h.Reason == "Automation Test");

        return new TestResult
        {
            TestName = "Database Persistence",
            Passed = found,
            ResponseTimeMs = sw.ElapsedMilliseconds,
            Details = found ? "Entry successfully persisted and retrieved" : "Failed to retrieve test entry"
        };
    }
}

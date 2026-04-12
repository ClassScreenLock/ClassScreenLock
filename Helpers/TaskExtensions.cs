using System;
using System.Threading.Tasks;
using ClassScreenLock.Services;

namespace ClassScreenLock.Helpers;

public static class TaskExtensions
{
    /// <summary>
    /// Safely executes a task without awaiting it, logging any exceptions to the log service.
    /// </summary>
    public static void FireAndForget(this Task task, string? source = null)
    {
        _ = task.ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                var ex = t.Exception.Flatten().InnerException ?? t.Exception;
                LogService.Instance.Log("Error", "FireAndForgetException", source ?? "TaskExtensions", ex.ToString());
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }
}

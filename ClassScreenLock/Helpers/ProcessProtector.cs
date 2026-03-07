using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ClassScreenLock.Helpers;

/// <summary>
/// 提供进程级别的保护，通过提升优先级和系统权限来增强稳定性
/// </summary>
public static class ProcessProtector
{
    private static bool _isProtected;
    
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);
    
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);
    
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();
    
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES
    {
        public LUID Luid;
        public uint Attributes;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinitiy;
        public uint PriorityClass;
        public uint SchedulingClass;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }
    
    /// <summary>
    /// 启用进程保护
    /// </summary>
    public static void EnableProtection()
    {
        if (_isProtected) return;
        
        try
        {
            EnablePrivileges();
            SetHighPriority();
            // 不使用 Job Object，避免和看门狗绑定
            // 让看门狗独立监控主进程，主进程被杀后看门狗还能存活并重启它
            _isProtected = true;
            
            Console.WriteLine("Process protection enabled with high priority.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to enable process protection: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 启用必要的系统权限
    /// </summary>
    private static void EnablePrivileges()
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out IntPtr tokenHandle))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            
            var tkp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 3,
                Privileges = new LUID_AND_ATTRIBUTES[3]
            };
            
            string[] privileges = { "SeDebugPrivilege", "SeIncreasePriorityPrivilege", "SeLockMemoryPrivilege" };
            
            for (int i = 0; i < privileges.Length; i++)
            {
                if (LookupPrivilegeValue(null, privileges[i], out tkp.Privileges[i].Luid))
                {
                    tkp.Privileges[i].Attributes = SE_PRIVILEGE_ENABLED;
                }
            }
            
            if (!AdjustTokenPrivileges(tokenHandle, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not enable all privileges: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 设置进程为高优先级
    /// </summary>
    private static void SetHighPriority()
    {
        try
        {
            using var currentProcess = Process.GetCurrentProcess();
            currentProcess.PriorityClass = ProcessPriorityClass.High;
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not set high priority: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 清理保护资源
    /// </summary>
    public static void Cleanup()
    {
        try
        {
            _isProtected = false;
        }
        catch { }
    }
}

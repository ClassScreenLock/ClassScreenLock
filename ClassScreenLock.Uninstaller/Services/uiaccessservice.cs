using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ClassScreenLock.Uninstaller.Helpers;

namespace ClassScreenLock.Uninstaller.Services;

/// <summary>
/// UIAccess 提权服务（与主程序 UiAccessService 逻辑一致）。
/// 
/// 提权流程：
/// 1. 先通过 manifest 的 requireAdministrator 获得管理员权限（UAC 弹窗）
/// 2. 复制 winlogon.exe 的令牌 → 获得 SYSTEM 权限
/// 3. 设置 TokenUIAccess = 1 → 获得 UIAccess 权限
/// 4. 用新令牌创建新进程 → 原进程退出
/// 
/// 这样卸载程序就能以 SYSTEM + UIAccess 权限运行，
/// 可以操作 HKLM 注册表、停止/删除加固的服务、终止 SYSTEM 进程等。
/// </summary>
public sealed class UiAccessService
{
    private static UiAccessService? _instance;
    public static UiAccessService Instance => _instance ??= new UiAccessService();

    public bool HasUiAccess { get; private set; }
    public uint LastError { get; private set; }
    public string StatusMessage { get; private set; } = "未检测";

    private bool? _cachedUiAccessStatus;
    private DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CacheValidityDuration = TimeSpan.FromMinutes(5);

    private UiAccessService()
    {
    }

    /// <summary>
    /// 检查并执行 UIAccess 提权。
    /// 如果当前进程还没有 UIAccess 权限，则复制 winlogon 令牌创建新进程后退出当前进程。
    /// </summary>
    /// <returns>true=已拥有 UIAccess（或提权成功正在重启），false=提权失败</returns>
    public bool CheckAndElevate()
    {
        var now = DateTime.Now;
        if (_cachedUiAccessStatus.HasValue && (now - _lastCheckTime) < CacheValidityDuration)
        {
            HasUiAccess = _cachedUiAccessStatus.Value;
            StatusMessage = HasUiAccess ? "UIAccess 已启用 (缓存)" : "UIAccess 未启用 (缓存)";
            return HasUiAccess;
        }

        if (CheckForUIAccess(out var dwErr, out var fUIAccess))
        {
            _cachedUiAccessStatus = fUIAccess;
            _lastCheckTime = now;

            if (fUIAccess)
            {
                HasUiAccess = true;
                StatusMessage = "UIAccess 已启用";
                LastError = 0;
                UninstallLogger.Info("[UIAccess] 已启用");
                return true;
            }
            else
            {
                UninstallLogger.Info("[UIAccess] 当前无 UIAccess，开始提权...");
                dwErr = CreateSystemProcess();
                if (dwErr == 0)
                {
                    // 提权成功，新进程已创建，退出当前进程
                    UninstallLogger.Info("[UIAccess] 提权成功，新进程已启动，当前进程退出");
                    Environment.Exit(0);
                    return true; // 不会执行到这里
                }
                else
                {
                    LastError = dwErr;
                    HasUiAccess = false;
                    StatusMessage = $"SYSTEM 提权失败 (错误: {LastError})";
                    UninstallLogger.Warning($"[UIAccess] CreateSystemProcess 失败: {dwErr}");
                    return false;
                }
            }
        }
        else
        {
            LastError = dwErr;
            HasUiAccess = false;
            StatusMessage = $"UIAccess 检测失败 (错误: {dwErr})";
            UninstallLogger.Warning($"[UIAccess] CheckForUIAccess 失败: {dwErr}");
            return false;
        }
    }

    /// <summary>
    /// 检查当前进程是否具有 UIAccess 权限
    /// （通过创建一个测试窗口并查询其 z-band 来判断）
    /// </summary>
    private bool CheckForUIAccess(out uint pdwErr, out bool pfUIAccess)
    {
        pfUIAccess = false;
        pdwErr = 0;

        var hWnd = CreateWindowEx(
            0x00000008, // WS_EX_TOOLWINDOW
            "STATIC",
            "",
            0x80000000, // WS_POPUP
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (hWnd == IntPtr.Zero)
        {
            pdwErr = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] CreateWindowEx 失败: {pdwErr}");
            return false;
        }

        try
        {
            if (GetWindowBand(hWnd, out var band))
            {
                pfUIAccess = band == ZBID_UIACCESS;
                return true;
            }

            pdwErr = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] GetWindowBand 失败: {pdwErr}");
            return false;
        }
        finally
        {
            DestroyWindow(hWnd);
        }
    }

    #region CreateSystemProcess — 复制 winlogon 令牌创建 SYSTEM + UIAccess 进程

    private uint CreateSystemProcess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out var hTokenSelf))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] OpenProcessToken 失败: {err}");
            return err;
        }

        int sessionId = 0;
        if (!GetTokenInformationInt(hTokenSelf, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sessionId, sizeof(int), out _))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CleanupTokenHandle(hTokenSelf, IntPtr.Zero, IntPtr.Zero);
            UninstallLogger.Warning($"[UIAccess] GetTokenInformation SessionId 失败: {err}");
            return err;
        }

        var dwErr = DuplicateWinloginToken(sessionId, TOKEN_IMPERSONATE, out var hTokenImpersonation);
        if (dwErr != 0)
        {
            CleanupTokenHandle(hTokenSelf, IntPtr.Zero, IntPtr.Zero);
            UninstallLogger.Warning($"[UIAccess] DuplicateWinloginToken 失败: {dwErr}");
            return dwErr;
        }

        if (!SetThreadToken(IntPtr.Zero, hTokenImpersonation))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            CleanupTokenHandle(hTokenSelf, hTokenImpersonation, IntPtr.Zero);
            UninstallLogger.Warning($"[UIAccess] SetThreadToken 失败: {dwErr}");
            return dwErr;
        }

        dwErr = DuplicateTokenForProcess(hTokenSelf, hTokenImpersonation, out var hTokenPrimary);
        if (dwErr != 0)
        {
            RevertToSelf();
            CleanupTokenHandle(hTokenSelf, hTokenImpersonation, IntPtr.Zero);
            return dwErr;
        }

        dwErr = CreateProcessWithToken(hTokenPrimary);

        CleanupTokenHandle(hTokenSelf, hTokenImpersonation, hTokenPrimary);
        RevertToSelf();

        return dwErr;
    }

    /// <summary>
    /// 为进程创建复制令牌（设置 UIAccess）
    /// </summary>
    private uint DuplicateTokenForProcess(IntPtr hTokenSelf, IntPtr hTokenImpersonation, out IntPtr hTokenPrimary)
    {
        hTokenPrimary = IntPtr.Zero;

        if (!OpenThreadToken(GetCurrentThread(), TOKEN_QUERY | TOKEN_DUPLICATE, false, out var hThreadToken))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] OpenThreadToken 失败: {err}");
            return err;
        }

        if (!DuplicateTokenEx(hThreadToken, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID,
                IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TOKEN_TYPE.TokenPrimary, out hTokenPrimary))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hThreadToken);
            UninstallLogger.Warning($"[UIAccess] DuplicateTokenEx 失败: {err}");
            return err;
        }

        CloseHandle(hThreadToken);

        // 设置 UIAccess 权限
        int bUIAccess = 1;
        if (!SetTokenInformationInt(hTokenPrimary, TOKEN_INFORMATION_CLASS.TokenUIAccess, ref bUIAccess, sizeof(int)))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hTokenPrimary);
            hTokenPrimary = IntPtr.Zero;
            UninstallLogger.Warning($"[UIAccess] SetTokenInformation UIAccess 失败: {err}");
            return err;
        }

        return 0;
    }

    /// <summary>
    /// 使用令牌创建新进程（卸载程序自身）
    /// </summary>
    private uint CreateProcessWithToken(IntPtr hTokenPrimary)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? AppPathHelper.UninstallerDirectory;
        var args = Environment.GetCommandLineArgs();
        var commandLine = BuildCommandLine(exePath, args);

        var si = new STARTUPINFOEX();
        si.cb = (uint)Marshal.SizeOf<STARTUPINFOEX>();

        if (CreateProcessWithTokenW(hTokenPrimary, 0, null, commandLine, 0, IntPtr.Zero, null, ref si, out var pi))
        {
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            UninstallLogger.Info("[UIAccess] 提权进程创建成功");
            return 0;
        }

        var err = (uint)Marshal.GetLastWin32Error();
        UninstallLogger.Warning($"[UIAccess] CreateProcessWithTokenW 失败: {err}");
        return err;
    }

    private static string BuildCommandLine(string exePath, string[] args)
    {
        if (args.Length > 1)
        {
            return $"\"{exePath}\" {string.Join(" ", args.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";
        }
        return $"\"{exePath}\"";
    }

    private void CleanupTokenHandle(IntPtr hTokenSelf, IntPtr hTokenImpersonation, IntPtr hTokenPrimary)
    {
        if (hTokenPrimary != IntPtr.Zero)
            CloseHandle(hTokenPrimary);
        if (hTokenImpersonation != IntPtr.Zero)
            CloseHandle(hTokenImpersonation);
        if (hTokenSelf != IntPtr.Zero)
            CloseHandle(hTokenSelf);
    }

    #endregion

    #region DuplicateWinloginToken — 查找并复制 winlogon.exe 的令牌

    private uint DuplicateWinloginToken(int dwSessionId, uint dwDesiredAccess, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        if (!LookupPrivilegeValue(null, SE_TCB_NAME, out var luidTcb))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] LookupPrivilegeValue 失败: {err}");
            return err;
        }

        var hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (hSnapshot == (IntPtr)(-1))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] CreateToolhelp32Snapshot 失败: {err}");
            return err;
        }

        var result = FindAndDuplicateWinlogonToken(hSnapshot, dwSessionId, dwDesiredAccess, luidTcb, out phToken);
        CloseHandle(hSnapshot);
        return result;
    }

    private uint FindAndDuplicateWinlogonToken(IntPtr hSnapshot, int dwSessionId, uint dwDesiredAccess, LUID luidTcb, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;
        var pe = new PROCESSENTRY32();
        pe.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();
        uint dwErr = ERROR_NOT_FOUND;
        int winlogonCount = 0;

        if (!Process32First(hSnapshot, ref pe))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] Process32First 失败: {dwErr}");
            return dwErr;
        }

        do
        {
            if (string.IsNullOrEmpty(pe.szExeFile) || !pe.szExeFile.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            winlogonCount++;

            var result = TryDuplicateWinlogonToken(pe.th32ProcessID, dwSessionId, dwDesiredAccess, luidTcb, out phToken);
            if (result == 0)
                return 0;

            dwErr = result;
        } while (Process32Next(hSnapshot, ref pe));

        UninstallLogger.Warning($"[UIAccess] 未找到符合条件的 winlogon.exe，共检查 {winlogonCount} 个");
        return dwErr;
    }

    private uint TryDuplicateWinlogonToken(int processId, int dwSessionId, uint dwDesiredAccess, LUID luidTcb, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            return ERROR_NOT_FOUND;
        }

        try
        {
            return GetWinlogonTokenFromProcess(hProcess, dwSessionId, dwDesiredAccess, luidTcb, out phToken);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private uint GetWinlogonTokenFromProcess(IntPtr hProcess, int dwSessionId, uint dwDesiredAccess, LUID luidTcb, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        if (!OpenProcessToken(hProcess, TOKEN_QUERY | TOKEN_DUPLICATE, out var hToken))
        {
            return ERROR_NOT_FOUND;
        }

        try
        {
            if (!PrivilegeCheck(hToken, luidTcb, out var fTcb))
            {
                return ERROR_NOT_FOUND;
            }

            if (!fTcb)
                return ERROR_NOT_FOUND;

            return ValidateAndDuplicateToken(hToken, dwSessionId, dwDesiredAccess, out phToken);
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    private uint ValidateAndDuplicateToken(IntPtr hToken, int dwSessionId, uint dwDesiredAccess, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        int sid = 0;
        if (!GetTokenInformationInt(hToken, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sid, sizeof(int), out _))
        {
            return ERROR_NOT_FOUND;
        }

        if (sid != dwSessionId)
            return ERROR_NOT_FOUND;

        return DuplicateTokenWithAccess(hToken, dwDesiredAccess, out phToken);
    }

    private uint DuplicateTokenWithAccess(IntPtr hToken, uint dwDesiredAccess, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        TOKEN_TYPE tokenType = (dwDesiredAccess & TOKEN_IMPERSONATE) != 0
            ? TOKEN_TYPE.TokenImpersonation
            : TOKEN_TYPE.TokenPrimary;
        SECURITY_IMPERSONATION_LEVEL impLevel = tokenType == TOKEN_TYPE.TokenImpersonation
            ? SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation
            : SECURITY_IMPERSONATION_LEVEL.SecurityIdentification;

        if (!DuplicateTokenEx(hToken, dwDesiredAccess, IntPtr.Zero, impLevel, tokenType, out phToken))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            UninstallLogger.Warning($"[UIAccess] DuplicateTokenEx 失败: {err}");
            return err;
        }

        return 0;
    }

    private bool PrivilegeCheck(IntPtr hToken, LUID luidTcb, out bool fTcb)
    {
        fTcb = false;

        var ps = new PRIVILEGE_SET();
        ps.PrivilegeCount = 1;
        ps.Control = PRIVILEGE_SET_ALL_NECESSARY;
        ps.Privilege = new LUID_AND_ATTRIBUTES[1];
        ps.Privilege[0].Luid = luidTcb;
        ps.Privilege[0].Attributes = SE_PRIVILEGE_ENABLED;

        return PrivilegeCheckNative(hToken, ref ps, out fTcb);
    }

    #endregion

    #region Native Methods and Structures

    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PRIVILEGE_SET_ALL_NECESSARY = 1;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint ERROR_NOT_FOUND = 1168;

    private const string SE_TCB_NAME = "SeTcbPrivilege";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentThread();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenThreadToken(
        IntPtr ThreadHandle,
        uint DesiredAccess,
        bool OpenAsSelf,
        out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "GetTokenInformation")]
    private static extern bool GetTokenInformationInt(
        IntPtr TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        ref int TokenInformation,
        uint TokenInformationLength,
        out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetThreadToken(IntPtr Thread, IntPtr Token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool RevertToSelf();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL ImpersonationLevel,
        TOKEN_TYPE TokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "SetTokenInformation")]
    private static extern bool SetTokenInformationInt(
        IntPtr TokenHandle,
        TOKEN_INFORMATION_CLASS TokenInformationClass,
        ref int TokenInformation,
        uint TokenInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "PrivilegeCheck")]
    private static extern bool PrivilegeCheckNative(
        IntPtr ClientToken,
        ref PRIVILEGE_SET RequiredPrivileges,
        out bool pfResult);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool CreateProcessWithTokenW(
        IntPtr hToken,
        uint dwLogonFlags,
        string? lpApplicationName,
        string? lpCommandLine,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public uint cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

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
    private struct PRIVILEGE_SET
    {
        public uint PrivilegeCount;
        public uint Control;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privilege;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public int th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public int th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenUIAccess = 26,
        TokenSessionId = 12
    }

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous = 0,
        SecurityIdentification = 1,
        SecurityImpersonation = 2,
        SecurityDelegation = 3
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation = 2
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowBand(IntPtr hWnd, out uint pdwBand);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    private const uint ZBID_UIACCESS = 2;

    #endregion
}

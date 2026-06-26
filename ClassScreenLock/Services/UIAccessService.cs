using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace ClassScreenLock.Services;

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

    public void CheckAndElevate()
    {
        var now = DateTime.Now;
        if (_cachedUiAccessStatus.HasValue && (now - _lastCheckTime) < CacheValidityDuration)
        {
            HasUiAccess = _cachedUiAccessStatus.Value;
            StatusMessage = HasUiAccess ? "UIAccess 已启用 (缓存)" : "UIAccess 未启用 (缓存)";
            LogService.Instance.Log("Debug", "UIAccess", "CheckAndElevate", "使用缓存的UIAccess状态");
            return;
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
                LogService.Instance.Log("Info", "UIAccess", "CheckAndElevate", "UIAccess 已启用");
            }
            else
            {
                LogService.Instance.Log("Debug", "UIAccess", "CheckAndElevate", "UIAccess 未启用，尝试提权到SYSTEM...");
                dwErr = CreateSystemProcess();
                if (dwErr == 0)
                {
                    LogService.Instance.Log("Info", "UIAccess", "CheckAndElevate", "SYSTEM进程已启动，退出当前进程");
                    Environment.Exit(0);
                }
                else
                {
                    LastError = dwErr;
                    HasUiAccess = false;
                    StatusMessage = $"SYSTEM 提权失败 (错误: {LastError})";
                    LogService.Instance.Log("Error", "UIAccess", "CheckAndElevate", $"CreateSystemProcess 失败: {dwErr}");
                }
            }
        }
        else
        {
            LastError = dwErr;
            HasUiAccess = false;
            StatusMessage = $"UIAccess 检测失败 (错误: {dwErr})";
            LogService.Instance.Log("Error", "UIAccess", "CheckAndElevate", $"CheckForUIAccess 失败: {dwErr}");
        }
    }

    private bool CheckForUIAccess(out uint pdwErr, out bool pfUIAccess)
    {
        pfUIAccess = false;
        pdwErr = 0;

        var hWnd = CreateWindowEx(
            0x00000008,
            "STATIC",
            "",
            0x80000000,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (hWnd == IntPtr.Zero)
        {
            pdwErr = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "CheckForUIAccess", $"CreateWindowEx 失败: {pdwErr}");
            return false;
        }

        try
        {
            if (GetWindowBand(hWnd, out var band))
            {
                pfUIAccess = band == ZBID_UIACCESS;
                LogService.Instance.Log("Info", "UIAccess", "CheckForUIAccess", $"GetWindowBand = {band} (ZBID_UIACCESS = {ZBID_UIACCESS})");
                return true;
            }

            pdwErr = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "CheckForUIAccess", $"GetWindowBand 失败: {pdwErr}");
            return false;
        }
        finally
        {
            DestroyWindow(hWnd);
        }
    }

    #region CreateSystemProcess 重构方法

    private uint CreateSystemProcess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out var hTokenSelf))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "CreateSystemProcess", $"OpenProcessToken 失败: {err}");
            return err;
        }

        int sessionId = 0;
        if (!GetTokenInformationInt(hTokenSelf, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sessionId, sizeof(int), out _))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CleanupTokenHandle(hTokenSelf, IntPtr.Zero, IntPtr.Zero);
            LogService.Instance.Log("Error", "UIAccess", "CreateSystemProcess", $"GetTokenInformation SessionId 失败: {err}");
            return err;
        }

        LogService.Instance.Log("Debug", "UIAccess", "CreateSystemProcess", $"当前 SessionId: {sessionId}");

        var dwErr = DuplicateWinloginToken(sessionId, TOKEN_IMPERSONATE, out var hTokenImpersonation);
        if (dwErr != 0)
        {
            CleanupTokenHandle(hTokenSelf, IntPtr.Zero, IntPtr.Zero);
            LogService.Instance.Log("Error", "UIAccess", "CreateSystemProcess", $"DuplicateWinloginToken 失败: {dwErr}");
            return dwErr;
        }

        if (!SetThreadToken(IntPtr.Zero, hTokenImpersonation))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            CleanupTokenHandle(hTokenSelf, hTokenImpersonation, IntPtr.Zero);
            LogService.Instance.Log("Error", "UIAccess", "CreateSystemProcess", $"SetThreadToken 失败: {dwErr}");
            return dwErr;
        }

        LogService.Instance.Log("Debug", "UIAccess", "CreateSystemProcess", "已模拟SYSTEM身份");

        dwErr = DuplicateTokenForProcess(hTokenSelf, hTokenImpersonation, out var hTokenPrimary);
        if (dwErr != 0)
        {
            RevertToSelf();
            CleanupTokenHandle(hTokenSelf, hTokenImpersonation, IntPtr.Zero);
            return dwErr;
        }

        LogService.Instance.Log("Info", "UIAccess", "CreateSystemProcess", "SYSTEM + UIAccess 主令牌创建成功");

        dwErr = CreateProcessWithToken(hTokenPrimary);
        
        CleanupTokenHandle(hTokenSelf, hTokenImpersonation, hTokenPrimary);
        RevertToSelf();

        return dwErr;
    }

    /// <summary>
    /// 为进程创建复制令牌
    /// </summary>
    private uint DuplicateTokenForProcess(IntPtr hTokenSelf, IntPtr hTokenImpersonation, out IntPtr hTokenPrimary)
    {
        hTokenPrimary = IntPtr.Zero;

        if (!OpenThreadToken(GetCurrentThread(), TOKEN_QUERY | TOKEN_DUPLICATE, false, out var hThreadToken))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "DuplicateTokenForProcess", $"OpenThreadToken 失败: {err}");
            return err;
        }

        if (!DuplicateTokenEx(hThreadToken, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID,
                IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TOKEN_TYPE.TokenPrimary, out hTokenPrimary))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hThreadToken);
            LogService.Instance.Log("Error", "UIAccess", "DuplicateTokenForProcess", $"DuplicateTokenEx 失败: {err}");
            return err;
        }

        CloseHandle(hThreadToken);

        int bUIAccess = 1;
        if (!SetTokenInformationInt(hTokenPrimary, TOKEN_INFORMATION_CLASS.TokenUIAccess, ref bUIAccess, sizeof(int)))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hTokenPrimary);
            hTokenPrimary = IntPtr.Zero;
            LogService.Instance.Log("Error", "UIAccess", "DuplicateTokenForProcess", $"SetTokenInformation UIAccess 失败: {err}");
            return err;
        }

        return 0;
    }

    /// <summary>
    /// 使用令牌创建新进程
    /// </summary>
    private uint CreateProcessWithToken(IntPtr hTokenPrimary)
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? AppContext.BaseDirectory;
        var args = Environment.GetCommandLineArgs();
        var commandLine = BuildCommandLine(exePath, args);

        var si = new STARTUPINFOEX();
        si.cb = (uint)Marshal.SizeOf<STARTUPINFOEX>();

        LogService.Instance.Log("Debug", "UIAccess", "CreateProcessWithToken", $"以SYSTEM身份创建新进程: {commandLine}");

        if (CreateProcessWithTokenW(hTokenPrimary, 0, null, commandLine, 0, IntPtr.Zero, null, ref si, out var pi))
        {
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
            LogService.Instance.Log("Info", "UIAccess", "CreateProcessWithToken", "进程创建成功");
            return 0;
        }

        var err = (uint)Marshal.GetLastWin32Error();
        LogService.Instance.Log("Error", "UIAccess", "CreateProcessWithToken", $"CreateProcessWithTokenW 失败: {err}");
        return err;
    }

    /// <summary>
    /// 构建命令行字符串
    /// </summary>
    private static string BuildCommandLine(string exePath, string[] args)
    {
        if (args.Length > 1)
        {
            return $"\"{exePath}\" {string.Join(" ", args.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";
        }
        return $"\"{exePath}\"";
    }

    /// <summary>
    /// 清理令牌句柄
    /// </summary>
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

    #region DuplicateWinloginToken 重构方法

    private uint DuplicateWinloginToken(int dwSessionId, uint dwDesiredAccess, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        if (!LookupPrivilegeValue(null, SE_TCB_NAME, out var luidTcb))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "DuplicateWinloginToken", $"LookupPrivilegeValue 失败: {err}");
            return err;
        }

        LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"SeTcbPrivilege LUID: {luidTcb.LowPart}");

        var hSnapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (hSnapshot == (IntPtr)(-1))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "DuplicateWinloginToken", $"CreateToolhelp32Snapshot 失败: {err}");
            return err;
        }

        var result = FindAndDuplicateWinlogonToken(hSnapshot, dwSessionId, dwDesiredAccess, luidTcb, out phToken);
        CloseHandle(hSnapshot);
        return result;
    }

    /// <summary>
    /// 查找并复制 winlogon 令牌
    /// </summary>
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
            LogService.Instance.Log("Error", "UIAccess", "FindAndDuplicateWinlogonToken", $"Process32First 失败: {dwErr}");
            return dwErr;
        }

        do
        {
            if (string.IsNullOrEmpty(pe.szExeFile) || !pe.szExeFile.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            winlogonCount++;
            LogService.Instance.Log("Debug", "UIAccess", "FindAndDuplicateWinlogonToken", $"找到 winlogon.exe PID: {pe.th32ProcessID}");

            var result = TryDuplicateWinlogonToken(pe.th32ProcessID, dwSessionId, dwDesiredAccess, luidTcb, out phToken);
            if (result == 0)
                return 0;

            dwErr = result;
        } while (Process32Next(hSnapshot, ref pe));

        LogService.Instance.Log("Error", "UIAccess", "FindAndDuplicateWinlogonToken", $"未找到符合条件的 winlogon.exe，共检查 {winlogonCount} 个");
        return dwErr;
    }

    /// <summary>
    /// 尝试从指定进程复制 winlogon 令牌
    /// </summary>
    private uint TryDuplicateWinlogonToken(int processId, int dwSessionId, uint dwDesiredAccess, LUID luidTcb, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Debug", "UIAccess", "TryDuplicateWinlogonToken", $"OpenProcess 失败 PID {processId}: {err}");
            return ERROR_NOT_FOUND;
        }

        try
        {
            return GetWinlogonTokenFromProcess(hProcess, processId, dwSessionId, dwDesiredAccess, luidTcb, out phToken);
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// 从进程获取 winlogon 令牌
    /// </summary>
    private uint GetWinlogonTokenFromProcess(IntPtr hProcess, int processId, int dwSessionId, uint dwDesiredAccess, LUID luidTcb, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        if (!OpenProcessToken(hProcess, TOKEN_QUERY | TOKEN_DUPLICATE, out var hToken))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Debug", "UIAccess", "GetWinlogonTokenFromProcess", $"OpenProcessToken 失败 PID {processId}: {err}");
            return ERROR_NOT_FOUND;
        }

        try
        {
            if (!PrivilegeCheck(hToken, luidTcb, out var fTcb))
            {
                var err = (uint)Marshal.GetLastWin32Error();
                LogService.Instance.Log("Debug", "UIAccess", "GetWinlogonTokenFromProcess", $"PrivilegeCheck 调用失败: {err}");
                return ERROR_NOT_FOUND;
            }

            LogService.Instance.Log("Debug", "UIAccess", "GetWinlogonTokenFromProcess", $"PrivilegeCheck PID {processId}: fTcb={fTcb}");

            if (!fTcb)
                return ERROR_NOT_FOUND;

            return ValidateAndDuplicateToken(hToken, processId, dwSessionId, dwDesiredAccess, out phToken);
        }
        finally
        {
            CloseHandle(hToken);
        }
    }

    /// <summary>
    /// 验证会话ID并复制令牌
    /// </summary>
    private uint ValidateAndDuplicateToken(IntPtr hToken, int processId, int dwSessionId, uint dwDesiredAccess, out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        int sid = 0;
        if (!GetTokenInformationInt(hToken, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sid, sizeof(int), out _))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Debug", "UIAccess", "ValidateAndDuplicateToken", $"GetTokenInformation SessionId 失败: {err}");
            return ERROR_NOT_FOUND;
        }

        LogService.Instance.Log("Debug", "UIAccess", "ValidateAndDuplicateToken", $"SessionId PID {processId}: {sid} (目标: {dwSessionId})");

        if (sid != dwSessionId)
            return ERROR_NOT_FOUND;

        return DuplicateTokenWithAccess(hToken, dwDesiredAccess, processId, out phToken);
    }

    /// <summary>
    /// 根据访问权限复制令牌
    /// </summary>
    private uint DuplicateTokenWithAccess(IntPtr hToken, uint dwDesiredAccess, int processId, out IntPtr phToken)
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
            LogService.Instance.Log("Error", "UIAccess", "DuplicateTokenWithAccess", $"DuplicateTokenEx 失败: {err}");
            return err;
        }

        LogService.Instance.Log("Info", "UIAccess", "DuplicateTokenWithAccess", 
            $"成功复制 winlogon {(tokenType == TOKEN_TYPE.TokenImpersonation ? "模拟" : "主")}令牌 PID: {processId}");
        return 0;
    }

    #endregion

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

    #region Native Methods and Structures

    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint TOKEN_ADJUST_SESSIONID = 0x0100;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PRIVILEGE_SET_ALL_NECESSARY = 1;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint ERROR_NOT_FOUND = 1168;

    private const string SE_TCB_NAME = "SeTcbPrivilege";
    private const string SE_ASSIGNPRIMARYTOKEN_NAME = "SeAssignPrimaryTokenPrivilege";
    private const string SE_INCREASE_QUOTA_NAME = "SeIncreaseQuotaPrivilege";

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
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandle,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

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

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public LUID_AND_ATTRIBUTES[] Privileges;
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
    private struct STARTUPINFO
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

    private const uint ZBID_DESKTOP = 1;
    private const uint ZBID_UIACCESS = 2;

    #endregion
}
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

    private UiAccessService()
    {
    }

    public void CheckAndElevate()
    {
        if (CheckForUIAccess(out var dwErr, out var fUIAccess))
        {
            if (fUIAccess)
            {
                HasUiAccess = true;
                StatusMessage = "UIAccess 已启用";
                LastError = 0;
                LogService.Instance.Log("Info", "UIAccess", "CheckAndElevate", "UIAccess 已启用");
            }
            else
            {
                LogService.Instance.Log("Debug", "UIAccess", "CheckAndElevate", "UIAccess 未启用，尝试提权...");
                dwErr = CreateUIAccessToken(out var hTokenUIAccess);
                if (dwErr == 0)
                {
                    var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? AppContext.BaseDirectory;
                    var args = Environment.GetCommandLineArgs();
                    var commandLine = exePath;
                    if (args.Length > 1)
                    {
                        commandLine = $"\"{exePath}\" {string.Join(" ", args.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}";
                    }
                    else
                    {
                        commandLine = $"\"{exePath}\"";
                    }

                    var si = new STARTUPINFO();
                    si.cb = (uint)Marshal.SizeOf<STARTUPINFO>();

                    LogService.Instance.Log("Debug", "UIAccess", "CheckAndElevate", $"创建新进程: {commandLine}");
                    
                    if (CreateProcessAsUser(hTokenUIAccess, null, commandLine, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out var pi))
                    {
                        CloseHandle(pi.hProcess);
                        CloseHandle(pi.hThread);
                        CloseHandle(hTokenUIAccess);
                        LogService.Instance.Log("Info", "UIAccess", "CheckAndElevate", "新进程已启动，退出当前进程");
                        Environment.Exit(0);
                    }
                    else
                    {
                        LastError = (uint)Marshal.GetLastWin32Error();
                        HasUiAccess = false;
                        StatusMessage = $"UIAccess 重启失败 (错误: {LastError})";
                        LogService.Instance.Log("Error", "UIAccess", "CheckAndElevate", $"CreateProcessAsUser 失败: {LastError}");
                    }

                    CloseHandle(hTokenUIAccess);
                }
                else
                {
                    LastError = dwErr;
                    HasUiAccess = false;
                    StatusMessage = $"UIAccess 提权失败 (错误: {LastError})";
                    LogService.Instance.Log("Error", "UIAccess", "CheckAndElevate", $"CreateUIAccessToken 失败: {dwErr}");
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

    private uint CreateUIAccessToken(out IntPtr phToken)
    {
        phToken = IntPtr.Zero;

        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY | TOKEN_DUPLICATE, out var hTokenSelf))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            LogService.Instance.Log("Error", "UIAccess", "CreateUIAccessToken", $"OpenProcessToken 失败: {err}");
            return err;
        }

        int sessionId = 0;
        if (!GetTokenInformationInt(hTokenSelf, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sessionId, sizeof(int), out _))
        {
            var err = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hTokenSelf);
            LogService.Instance.Log("Error", "UIAccess", "CreateUIAccessToken", $"GetTokenInformation SessionId 失败: {err}");
            return err;
        }

        LogService.Instance.Log("Debug", "UIAccess", "CreateUIAccessToken", $"当前 SessionId: {sessionId}");

        var dwErr = DuplicateWinloginToken(sessionId, TOKEN_IMPERSONATE, out var hTokenSystem);
        if (dwErr != 0)
        {
            CloseHandle(hTokenSelf);
            LogService.Instance.Log("Error", "UIAccess", "CreateUIAccessToken", $"DuplicateWinloginToken 失败: {dwErr}");
            return dwErr;
        }

        if (!SetThreadToken(IntPtr.Zero, hTokenSystem))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hTokenSystem);
            CloseHandle(hTokenSelf);
            LogService.Instance.Log("Error", "UIAccess", "CreateUIAccessToken", $"SetThreadToken 失败: {dwErr}");
            return dwErr;
        }

        if (!DuplicateTokenEx(hTokenSelf, TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT,
                IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityAnonymous,
                TOKEN_TYPE.TokenPrimary, out phToken))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            RevertToSelf();
            CloseHandle(hTokenSystem);
            CloseHandle(hTokenSelf);
            LogService.Instance.Log("Error", "UIAccess", "CreateUIAccessToken", $"DuplicateTokenEx 失败: {dwErr}");
            return dwErr;
        }

        int bUIAccess = 1;
        if (!SetTokenInformationInt(phToken, TOKEN_INFORMATION_CLASS.TokenUIAccess, ref bUIAccess, sizeof(int)))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            CloseHandle(phToken);
            phToken = IntPtr.Zero;
            RevertToSelf();
            CloseHandle(hTokenSystem);
            CloseHandle(hTokenSelf);
            LogService.Instance.Log("Error", "UIAccess", "CreateUIAccessToken", $"SetTokenInformation UIAccess 失败: {dwErr}");
            return dwErr;
        }

        RevertToSelf();
        CloseHandle(hTokenSystem);
        CloseHandle(hTokenSelf);
        LogService.Instance.Log("Info", "UIAccess", "CreateUIAccessToken", "UIAccess 令牌创建成功");
        return 0;
    }

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

        var pe = new PROCESSENTRY32();
        pe.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();
        uint dwErr = ERROR_NOT_FOUND;
        int winlogonCount = 0;

        if (!Process32First(hSnapshot, ref pe))
        {
            dwErr = (uint)Marshal.GetLastWin32Error();
            CloseHandle(hSnapshot);
            LogService.Instance.Log("Error", "UIAccess", "DuplicateWinloginToken", $"Process32First 失败: {dwErr}");
            return dwErr;
        }

        do
        {
            if (!string.IsNullOrEmpty(pe.szExeFile) &&
                pe.szExeFile.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase))
            {
                winlogonCount++;
                LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"找到 winlogon.exe PID: {pe.th32ProcessID}");

                var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pe.th32ProcessID);
                if (hProcess == IntPtr.Zero)
                {
                    var openErr = (uint)Marshal.GetLastWin32Error();
                    LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"OpenProcess 失败 PID {pe.th32ProcessID}: {openErr}");
                    continue;
                }

                if (OpenProcessToken(hProcess, TOKEN_QUERY | TOKEN_DUPLICATE, out var hToken))
                {
                    if (PrivilegeCheck(hToken, luidTcb, out var fTcb))
                    {
                        LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"PrivilegeCheck PID {pe.th32ProcessID}: fTcb={fTcb}");
                        
                        if (fTcb)
                        {
                            int sid = 0;
                            if (GetTokenInformationInt(hToken, TOKEN_INFORMATION_CLASS.TokenSessionId,
                                    ref sid, sizeof(int), out _))
                            {
                                LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"SessionId PID {pe.th32ProcessID}: {sid} (目标: {dwSessionId})");
                                
                                if (sid == dwSessionId)
                                {
                                    if (DuplicateTokenEx(hToken, dwDesiredAccess, IntPtr.Zero,
                                            SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                                            TOKEN_TYPE.TokenImpersonation, out phToken))
                                    {
                                        dwErr = 0;
                                        LogService.Instance.Log("Info", "UIAccess", "DuplicateWinloginToken", $"成功复制 winlogon 令牌 PID: {pe.th32ProcessID}");
                                    }
                                    else
                                    {
                                        dwErr = (uint)Marshal.GetLastWin32Error();
                                        LogService.Instance.Log("Error", "UIAccess", "DuplicateWinloginToken", $"DuplicateTokenEx 失败: {dwErr}");
                                    }
                                    CloseHandle(hToken);
                                    CloseHandle(hProcess);
                                    CloseHandle(hSnapshot);
                                    return dwErr;
                                }
                            }
                            else
                            {
                                var sidErr = (uint)Marshal.GetLastWin32Error();
                                LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"GetTokenInformation SessionId 失败: {sidErr}");
                            }
                        }
                    }
                    else
                    {
                        var privErr = (uint)Marshal.GetLastWin32Error();
                        LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"PrivilegeCheck 调用失败: {privErr}");
                    }
                    CloseHandle(hToken);
                }
                else
                {
                    var tokenErr = (uint)Marshal.GetLastWin32Error();
                    LogService.Instance.Log("Debug", "UIAccess", "DuplicateWinloginToken", $"OpenProcessToken 失败 PID {pe.th32ProcessID}: {tokenErr}");
                }
                CloseHandle(hProcess);
            }
        } while (Process32Next(hSnapshot, ref pe));

        CloseHandle(hSnapshot);
        LogService.Instance.Log("Error", "UIAccess", "DuplicateWinloginToken", $"未找到符合条件的 winlogon.exe，共检查 {winlogonCount} 个");
        return dwErr;
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

    #region Native Methods and Structures

    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_IMPERSONATE = 0x0004;
    private const uint TOKEN_ASSIGN_PRIMARY = 0x0001;
    private const uint TOKEN_ADJUST_DEFAULT = 0x0080;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PRIVILEGE_SET_ALL_NECESSARY = 1;
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint ERROR_NOT_FOUND = 1168;

    private const string SE_TCB_NAME = "SeTcbPrivilege";

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

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

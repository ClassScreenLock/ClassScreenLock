using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CSL.WatchdogService;

/// <summary>
/// 使用 CreateProcessAsUser API 在活动用户的会话中创建进程。
/// 这是微软官方推荐的标准做法，用于从 Windows 服务（Session 0）启动需要与用户桌面交互的 UI 进程。
///
/// 核心步骤：
/// 1. WTSGetActiveConsoleSessionId()  — 获取活动控制台会话 ID
/// 2. WTSQueryUserToken()             — 获取该会话对应用户的令牌
/// 3. GetTokenInformation()           — 尝试获取提升的链接令牌（用于启动 requireAdministrator 进程）
/// 4. DuplicateTokenEx()              — 复制令牌为可用于创建进程的主令牌
/// 5. CreateEnvironmentBlock()        — 为新进程创建正确的环境变量
/// 6. CreateProcessAsUser()           — 在目标用户的会话中启动进程
/// </summary>
internal static class CreateProcessAsUserHelper
{
    // ==================== Win32 API 导入 ====================

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint SessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        ref SECURITY_ATTRIBUTES lpThreadAttributes,
        int ImpersonationLevel,
        int TokenType,
        out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateEnvironmentBlock(
        out IntPtr lpEnvironment,
        IntPtr hToken,
        [MarshalAs(UnmanagedType.Bool)] bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUserW(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        IntPtr TokenInformation,
        int TokenInformationLength,
        out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        IntPtr TokenInformation,
        int TokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr TokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool DisableAllPrivileges,
        ref TOKEN_PRIVILEGES NewState,
        uint BufferLength,
        IntPtr PreviousState,
        IntPtr ReturnLength);

    // ==================== 常量 ====================

    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    // TokenInformationClass 值
    private const int TokenElevation = 20;       // TOKEN_ELEVATION
    private const int TokenLinkedToken = 19;     // TOKEN_LINKED_TOKEN
    private const int TokenGroups = 2;           // TOKEN_GROUPS
    private const int TokenRestrictedSids = 11;  // TOKEN_RESTRICTED_SIDS

    // 组属性标志：SE_GROUP_USE_FOR_DENY_ONLY —— 出现在链接令牌的 Administrators 组上，
    // 会导致 ShellExecute 启动 requireAdministrator 进程时被 UAC 拦截。
    private const uint SE_GROUP_USE_FOR_DENY_ONLY = 0x00000010;

    // 特权属性：SE_PRIVILEGE_ENABLED
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    // 创建标志
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint CREATE_DEFAULT_ERROR_MODE = 0x04000000;

    // ==================== 结构体 ====================

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public short wShowWindow;
        public short cbReserved2;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool TokenIsElevated;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_LINKED_TOKEN
    {
        public IntPtr LinkedToken;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
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
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public LUID_AND_ATTRIBUTES[] Privileges;
    }

    // ==================== 公开方法 ====================

    /// <summary>
    /// 在活动用户的会话中以该用户身份（尽可能提升权限）创建进程。
    /// </summary>
    /// <param name="exePath">要启动的可执行文件的完整路径</param>
    /// <param name="arguments">命令行参数（可为 null）</param>
    /// <param name="workingDir">工作目录（可为 null，默认使用 exe 所在目录）</param>
    /// <returns>启动成功返回 true，否则返回 false</returns>
    public static bool LaunchProcessAsActiveUser(string exePath, string? arguments = null, string? workingDir = null)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            ServiceLogger.Log("CreateProcessAsUser: exePath 为空");
            return false;
        }

        if (!File.Exists(exePath))
        {
            ServiceLogger.Log($"CreateProcessAsUser: 文件不存在: {exePath}");
            return false;
        }

        // 步骤 1：获取活动控制台会话 ID
        uint sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            ServiceLogger.Log("CreateProcessAsUser: 没有活动控制台会话（可能尚未有用户登录），跳过启动");
            return false;
        }

        ServiceLogger.Log($"CreateProcessAsUser: 活动会话 ID = {sessionId}");

        // 步骤 2：获取该会话对应用户的令牌
        if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
        {
            int error = Marshal.GetLastWin32Error();
            ServiceLogger.Log($"CreateProcessAsUser: WTSQueryUserToken 失败，错误码 = {error} ({new Win32Exception(error).Message})");
            return false;
        }

        try
        {
            // 尝试获取提升的链接令牌（用于启动 requireAdministrator 进程，避免 UAC 弹窗）
            IntPtr tokenToDuplicate = GetElevatedTokenIfNeeded(userToken);

            try
            {
                // 步骤 4：复制令牌为可用于创建进程的主令牌（Primary Token）
                var sa = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(), bInheritHandle = false };
                if (!DuplicateTokenEx(tokenToDuplicate, TOKEN_ALL_ACCESS, ref sa, SecurityImpersonation, TokenPrimary, out IntPtr primaryToken))
                {
                    int error = Marshal.GetLastWin32Error();
                    ServiceLogger.Log($"CreateProcessAsUser: DuplicateTokenEx 失败，错误码 = {error} ({new Win32Exception(error).Message})");
                    return false;
                }

                // 关键：移除令牌的受限 SID 和 deny-only 组标志。
                // 否则用链接令牌启动的进程（如看门狗）无法再启动 requireAdministrator 的子进程
                // （ShellExecute 会被 UAC 拦截，导致“看门狗拉不起主程序”）。
                RemoveTokenRestrictions(primaryToken);

                // 启用 SeDebugPrivilege 等特权，使被启动的主程序能自我提权为 SYSTEM
                //（主程序启动后需要复制 winlogon 令牌自我提升，缺少该特权会导致提权失败、进程退出）。
                EnableRequiredPrivileges(primaryToken);

                try
                {
                    // 步骤 5：为新进程创建正确的环境变量块
                    IntPtr envBlock = IntPtr.Zero;
                    if (!CreateEnvironmentBlock(out envBlock, primaryToken, false))
                    {
                        envBlock = IntPtr.Zero;
                        ServiceLogger.Log("CreateProcessAsUser: CreateEnvironmentBlock 失败，将使用空环境块");
                    }

                    try
                    {
                        // 步骤 6：在目标用户的会话中启动进程
                        var startupInfo = new STARTUPINFO
                        {
                            cb = Marshal.SizeOf<STARTUPINFO>(),
                            lpDesktop = @"winsta0\default"
                        };

                        string commandLine = string.IsNullOrWhiteSpace(arguments)
                            ? $"\"{exePath}\""
                            : $"\"{exePath}\" {arguments}";

                        string? currentDir = string.IsNullOrWhiteSpace(workingDir)
                            ? Path.GetDirectoryName(exePath)
                            : workingDir;

                        uint creationFlags = CREATE_UNICODE_ENVIRONMENT | CREATE_DEFAULT_ERROR_MODE;

                        bool success = CreateProcessAsUserW(
                            primaryToken,
                            exePath,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            creationFlags,
                            envBlock,
                            currentDir,
                            ref startupInfo,
                            out PROCESS_INFORMATION procInfo);

                        if (success)
                        {
                            CloseHandle(procInfo.hProcess);
                            CloseHandle(procInfo.hThread);
                            ServiceLogger.Log($"CreateProcessAsUser: 成功启动进程 PID={procInfo.dwProcessId}  ({exePath})");
                            return true;
                        }
                        else
                        {
                            int error = Marshal.GetLastWin32Error();
                            ServiceLogger.Log($"CreateProcessAsUser: CreateProcessAsUserW 失败，错误码 = {error} ({new Win32Exception(error).Message})");
                            return false;
                        }
                    }
                    finally
                    {
                        if (envBlock != IntPtr.Zero)
                        {
                            DestroyEnvironmentBlock(envBlock);
                        }
                    }
                }
                finally
                {
                    CloseHandle(primaryToken);
                }
            }
            finally
            {
                // 如果我们分配了提升的链接令牌，关闭它
                if (tokenToDuplicate != IntPtr.Zero && tokenToDuplicate != userToken)
                {
                    CloseHandle(tokenToDuplicate);
                }
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    /// <summary>
    /// 移除令牌的受限 SID 列表和 deny-only 组标志，使其成为“完全提升”的令牌。
    /// 链接令牌虽然 TokenIsElevated=true，但 Administrators 组是 deny-only 且带受限 SID，
    /// 用这种令牌启动的进程（如看门狗）无法再启动 requireAdministrator 子进程（ShellExecute 会被 UAC 拦截）。
    /// 此方法需要 SeCreateTokenPrivilege，服务以 LocalSystem 运行天然具备。
    /// </summary>
    private static void RemoveTokenRestrictions(IntPtr token)
    {
        try
        {
            // 1. 移除受限 SID 列表（TokenRestrictedSids）
            if (SetTokenInformation(token, TokenRestrictedSids, IntPtr.Zero, 0))
            {
                ServiceLogger.Log("CreateProcessAsUser: 已移除令牌的受限 SID");
            }
            else
            {
                ServiceLogger.Log($"CreateProcessAsUser: 移除受限 SID 失败，错误码 = {Marshal.GetLastWin32Error()}");
            }

            // 2. 清除所有组的 deny-only 标志（SE_GROUP_USE_FOR_DENY_ONLY）
            ClearDenyOnlyGroups(token);
        }
        catch (Exception ex)
        {
            ServiceLogger.Log($"CreateProcessAsUser: 移除令牌受限属性异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 在令牌上启用 SeDebugPrivilege 等必要特权。
    /// 主程序启动后会自我提权为 SYSTEM（复制 winlogon 令牌），需要 SeDebugPrivilege；
    /// 看门狗启动主程序前会先启用该特权，服务也必须同步启用，否则服务直接拉起的主程序
    /// 会因提权失败而退出，只能靠看门狗兜底。
    /// </summary>
    private static void EnableRequiredPrivileges(IntPtr token)
    {
        try
        {
            var tkp = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Privileges = new LUID_AND_ATTRIBUTES[1]
            };

            if (LookupPrivilegeValue(null, "SeDebugPrivilege", out tkp.Privileges[0].Luid))
            {
                tkp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;
                if (AdjustTokenPrivileges(token, false, ref tkp, 0, IntPtr.Zero, IntPtr.Zero))
                {
                    ServiceLogger.Log("CreateProcessAsUser: 已启用 SeDebugPrivilege");
                }
                else
                {
                    ServiceLogger.Log($"CreateProcessAsUser: 启用 SeDebugPrivilege 失败，错误码 = {Marshal.GetLastWin32Error()}");
                }
            }
            else
            {
                ServiceLogger.Log($"CreateProcessAsUser: 查找 SeDebugPrivilege 失败，错误码 = {Marshal.GetLastWin32Error()}");
            }
        }
        catch (Exception ex)
        {
            ServiceLogger.Log($"CreateProcessAsUser: 启用特权异常: {ex.Message}");
        }
    }

    private static void ClearDenyOnlyGroups(IntPtr token)
    {
        try
        {
            int size = 0;
            GetTokenInformation(token, TokenGroups, IntPtr.Zero, 0, out size);
            if (size <= 0)
            {
                ServiceLogger.Log("CreateProcessAsUser: 无法获取令牌组大小，跳过 deny-only 清除");
                return;
            }

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, TokenGroups, buffer, size, out _))
                {
                    ServiceLogger.Log($"CreateProcessAsUser: 获取令牌组失败，错误码 = {Marshal.GetLastWin32Error()}");
                    return;
                }

                uint count = (uint)Marshal.ReadInt32(buffer);
                // TOKEN_GROUPS 布局：GroupCount(DWORD) + 对齐填充 + SID_AND_ATTRIBUTES 数组。
                // Groups 数组按指针大小对齐，故起始偏移为 IntPtr.Size。
                IntPtr groupsPtr = IntPtr.Add(buffer, IntPtr.Size);

                bool modified = false;
                int entrySize = Marshal.SizeOf<SID_AND_ATTRIBUTES>();
                for (uint i = 0; i < count; i++)
                {
                    IntPtr entryPtr = IntPtr.Add(groupsPtr, (int)(i * entrySize));
                    var entry = Marshal.PtrToStructure<SID_AND_ATTRIBUTES>(entryPtr);
                    if ((entry.Attributes & SE_GROUP_USE_FOR_DENY_ONLY) != 0)
                    {
                        entry.Attributes &= ~SE_GROUP_USE_FOR_DENY_ONLY;
                        Marshal.StructureToPtr(entry, entryPtr, false);
                        modified = true;
                    }
                }

                if (modified)
                {
                    if (SetTokenInformation(token, TokenGroups, buffer, size))
                    {
                        ServiceLogger.Log("CreateProcessAsUser: 已清除 deny-only 组标志");
                    }
                    else
                    {
                        ServiceLogger.Log($"CreateProcessAsUser: 清除 deny-only 组失败，错误码 = {Marshal.GetLastWin32Error()}");
                    }
                }
                else
                {
                    ServiceLogger.Log("CreateProcessAsUser: 未发现 deny-only 组标志");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            ServiceLogger.Log($"CreateProcessAsUser: 清除 deny-only 组异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查用户令牌是否已提升；如果未提升，尝试获取其链接的提升令牌。
    /// 这样可以启动带有 requireAdministrator 清单的进程而不会触发 UAC。
    /// </summary>
    private static IntPtr GetElevatedTokenIfNeeded(IntPtr userToken)
    {
        try
        {
            // 直接分配足够大小的缓冲区查询提升状态。
            // 注意：不要依赖“NULL 缓冲区第一次调用返回 122(ERROR_INSUFFICIENT_BUFFER)”的边界行为，
            // 某些环境（如远程会话/特定令牌类型）下错误码可能不同，导致旧逻辑直接退回未提升令牌，
            // 进而使 CreateProcessAsUserW 启动 requireAdministrator 进程时失败 740。
            int elevationSize = Marshal.SizeOf<TOKEN_ELEVATION>();
            IntPtr elevationBuffer = Marshal.AllocHGlobal(elevationSize);
            try
            {
                if (GetTokenInformation(userToken, TokenElevation, elevationBuffer, elevationSize, out _))
                {
                    var elevation = Marshal.PtrToStructure<TOKEN_ELEVATION>(elevationBuffer);
                    if (elevation.TokenIsElevated)
                    {
                        ServiceLogger.Log("CreateProcessAsUser: 用户令牌已提升，直接使用");
                        return userToken;
                    }
                }
                else
                {
                    ServiceLogger.Log($"CreateProcessAsUser: 查询令牌提升状态失败，错误码 = {Marshal.GetLastWin32Error()}，尝试获取链接令牌");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(elevationBuffer);
            }

            // 令牌未提升，尝试获取链接的提升令牌（TokenLinkedToken）
            ServiceLogger.Log("CreateProcessAsUser: 用户令牌未提升，尝试获取链接的提升令牌");

            int linkedSize = Marshal.SizeOf<TOKEN_LINKED_TOKEN>();
            IntPtr linkedBuffer = Marshal.AllocHGlobal(linkedSize);
            try
            {
                if (GetTokenInformation(userToken, TokenLinkedToken, linkedBuffer, linkedSize, out _))
                {
                    var linkedToken = Marshal.PtrToStructure<TOKEN_LINKED_TOKEN>(linkedBuffer);
                    if (linkedToken.LinkedToken != IntPtr.Zero)
                    {
                        ServiceLogger.Log("CreateProcessAsUser: 成功获取提升的链接令牌");
                        return linkedToken.LinkedToken;
                    }
                    else
                    {
                        ServiceLogger.Log("CreateProcessAsUser: 链接令牌为空，将使用未提升的令牌");
                    }
                }
                else
                {
                    ServiceLogger.Log($"CreateProcessAsUser: 获取链接令牌失败，错误码 = {Marshal.GetLastWin32Error()}。将使用未提升的令牌");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(linkedBuffer);
            }
        }
        catch (Exception ex)
        {
            ServiceLogger.Log($"CreateProcessAsUser: 获取提升令牌时异常: {ex.Message}");
        }

        return userToken;
    }
}

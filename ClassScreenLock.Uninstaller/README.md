# ClassScreenLock 卸载程序 (CSL.Uninstaller)

## 概述

ClassScreenLock 的官方卸载程序，使用与主程序一致的 **Fluent 2 设计系统**，提供完整的安全清理能力。

## 功能特性

### 1. UIAccess / SYSTEM 提权（与主程序一致）
卸载程序采用与主程序完全相同的两段式提权机制：

1. **基础管理员权限**：通过 `app.manifest` 声明 `requireAdministrator`，启动时触发 UAC 弹窗
2. **SYSTEM + UIAccess 提权**：运行时复制 `winlogon.exe` 的令牌，设置 `TokenUIAccess=1`，创建新进程后原进程退出
   - 获得 SYSTEM 权限，可操作 HKLM 注册表、停止/删除加固的服务、终止 SYSTEM 进程
   - 获得 UIAccess 权限，窗口可显示在更高的 z-band 层级
   - 如果提权失败，降级为管理员权限继续运行（仍可完成大部分卸载操作）

### 2. 管理员验证
- 与主程序完全一致的验证逻辑（读取 `Data/security.json`）
- 支持多种验证模式：仅密码 / 仅 2FA / 密码或 2FA / 密码 + 2FA
- BCrypt 密码哈希校验 + TOTP 双重验证码
- 账户锁定机制（10 次失败后锁定 30 分钟）

### 3. 卸载不可中断
卸载一旦开始，将不可中途取消或关闭窗口：
- 进度页无取消按钮
- 窗口关闭按钮被禁用（Closing 事件拦截）
- 确保卸载流程完整执行，避免留下半卸载的不一致状态

### 4. 完整安全清理
卸载按正确顺序执行，确保所有防护机制被彻底清除：

| 步骤 | 内容 | 说明 |
|------|------|------|
| 停止进程 | 终止主程序、看门狗、IFEO 重定向器 | 先通过全局事件通知占坑实例退出 |
| 停止服务 | 停止 CSL.WatchdogService | 先恢复加固的服务权限再停止 |
| 卸载服务 | 卸载看门狗 Windows 服务 | 从 SCM 中删除服务注册 |
| 清理 IFEO | 删除映像劫持注册表项 | 只删除指向 CSL.IfeoRedirector 的 Debugger 值 |
| 清理组策略 | 恢复 DisableCMD 策略 | 删除 HKLM\...\Policies\...\DisableCMD |
| 清理自启动 | 移除注册表 Run + 任务计划 | 主程序和看门狗的自启动项 |
| 清理用户注册表 | 删除 HKCU\Software\ClassScreenLock | PowerShell 放行标记等 |
| 清理卸载项 | 删除添加/删除程序条目 | HKLM\...\Uninstall\ClassScreenLock |
| 清理 ProgramData | 删除 %ProgramData%\ClassScreenLock | IFEO 重定向程序固定部署目录 |
| 删除文件 | 删除程序目录下的所有文件 | 排除卸载程序自身（最后自删除） |

### 5. 自删除机制
卸载完成后，通过 `cmd.exe` 延迟删除卸载程序自身，确保程序完全退出后再删除文件。

### 6. 卸载日志
所有卸载操作记录到 `%TEMP%\CSL_Uninstall_时间戳.log`，便于排查问题。

### 7. 静默卸载
支持命令行参数实现无人值守卸载：
```
CSL.Uninstaller.exe /silent
CSL.Uninstaller.exe --silent
CSL.Uninstaller.exe /S
CSL.Uninstaller.exe /quiet
```

### 8. 安装检测
多种方式检测 ClassScreenLock 是否已安装：
- 主程序 exe 是否存在
- 看门狗服务是否已注册
- 卸载注册表项是否存在
- IFEO 规则是否存在
- ProgramData 部署目录是否存在

## 设计系统

卸载程序完全复用主程序的 **Fluent 2 (Windows UI Kit)** 设计系统：

### 设计令牌 (Fluent2Tokens.axaml)
- **字阶**：Display / TitleLarge / Title / Subtitle / Body / Caption 全档
- **圆角**：4px 基础圆角 + 20px 胶囊圆角
- **间距**：4px 基准，1/2/3/4/5/6/8 级
- **颜色**：Light/Dark 双主题语义 token，包括背景层、文本、强调色、控件填充、状态色、描边、阴影
- **强调色**：Light `#0067C0` / Dark `#60CDFF`

### 控件样式 (FluentStyles.axaml)
- **按钮**：基础按钮 / Accent 强调按钮 / Danger 危险按钮 / Outline 轮廓按钮
- **输入框**：Fluent 2 Underlined 下划线风格
- **卡片**：Card Background + Card Stroke + Rest 阴影
- **进度条**：4px 细线样式
- **焦点环**：Fluent 双环（外环 2px 主色 + 内环 1px 对比色）
- **文本层级**：h1/h3/subtitle/caption

## 项目结构

```
ClassScreenLock.Uninstaller/
├── Assets/                    # 静态资源
├── Converters/                # 值转换器
│   └── ValueConverters.cs
├── Helpers/                   # 辅助类
│   ├── AppPathHelper.cs       # 路径解析
│   └── UninstallLogger.cs     # 卸载日志
├── Models/                    # 数据模型
│   └── SecuritySettingsModel.cs
├── Services/                  # 业务服务
│   ├── InstallationDetector.cs # 安装检测
│   ├── UninstallSecurityService.cs # 管理员验证
│   └── UninstallService.cs    # 核心卸载逻辑
├── Styles/                    # 设计系统
│   ├── Fluent2Tokens.axaml    # Fluent 2 设计令牌
│   └── FluentStyles.axaml     # 控件样式
├── ViewModels/                # MVVM 视图模型
│   ├── ViewModelBase.cs
│   ├── VerifyViewModel.cs     # 验证页 VM
│   └── UninstallViewModel.cs  # 卸载页 VM
├── Views/                     # 视图
│   ├── VerifyWindow.axaml     # 管理员验证窗口
│   ├── VerifyWindow.axaml.cs
│   ├── UninstallWindow.axaml  # 卸载主窗口（确认/进度/结果三页）
│   └── UninstallWindow.axaml.cs
├── App.axaml                  # 应用入口
├── App.axaml.cs
├── Program.cs                 # 程序入口（支持静默模式）
├── ViewLocator.cs
├── app.manifest               # UAC 管理员权限声明
└── ClassScreenLock.Uninstaller.csproj
```

## 编译与部署

### 编译
卸载程序作为主程序的编译依赖，会在编译主项目时自动编译：

```xml
<!-- ClassScreenLock.csproj 中已添加 -->
<ProjectReference Include="..\ClassScreenLock.Uninstaller\ClassScreenLock.Uninstaller.csproj">
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    <SkipGetTargetFrameworkProperties>true</SkipGetTargetFrameworkProperties>
    <Private>false</Private>
</ProjectReference>
```

### 输出
- 输出文件名：`CSL.Uninstaller.exe`
- 输出目录：与主程序相同（`bin/Release/net9.0-windows/win-x64/`）
- 单文件发布：与主程序一致的 PublishSingleFile 配置

### 系统要求
- Windows 10 / 11 x64
- .NET 9.0 运行时
- **管理员权限**（必需，UAC 自动提权）

## 用户交互流程

```
启动 → UAC 提权 → 管理员验证窗口
                       ↓
                  验证成功 → 卸载确认页
                                     ↓
                                点击"开始卸载" → 卸载进度页
                                                       ↓
                                                  卸载完成 → 结果页
                                                                  ↓
                                                              完成 → 自删除 → 退出
```

### 取消行为
- **确认页取消**：直接退出程序，不做任何修改
- **进度页取消**：立即终止卸载流程，直接退出（已删除的文件不恢复）
  > 符合需求：如果卸载时正在删除文件时正在进行直接退出

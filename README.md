# 注意：需要下载的看Releases，这个只是测试版本，请不要大规模使用，造成问题后果自负！，有BUG赶紧去Issues提！我会在寒假修掉！

<p align="center">
  <img width="70%" align="center" src="img/xuanchuan.png" alt="xuanchuan">
</p>

# ClassScreenLock

面向 Windows 11/Windows 10 的跨平台课堂屏幕锁定与课堂纪律辅助系统。使用 Avalonia + .NET 9 开发，遵循 Win11 Fluent UI 风格，支持“仅防护”“仅锁屏”“完全锁定”等模式，提供网络拦截、应用拦截、时间表管理、日志与安全中心等功能。

## 功能概览
- 屏幕锁定与防护
  - 模式：仅防护、仅锁屏、完全锁定。
- 课间浮动锁定按钮（“下课按钮”）
  - 在课间显示，支持二次确认，减少误触。
- 网络拦截与“防火墙式”阻断
  - Hosts 层阻断 + 防火墙 IP 规则组合；支持自定义域名规则。
- 应用拦截与保护设置
  - 课间/上课策略、基础防护开关、计划任务驱动。
- 时间表与计划
  - 课表时间点、状态判定与自动提前解锁。
- 安全中心与账户
  - 管理员登录、权限控制、可选二次验证。
- 日志与事件记录
  - 安全日志、拦截记录。


## 安装与发布
提供两种分发方案：

1) 自包含单文件（免安装运行时、双击即用）
- 生成命令：
  ```bash
  dotnet publish .\ClassScreenLock\ClassScreenLock.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:DebugType=none /p:DebugSymbols=false
  ```

- 说明：包含 .NET 运行时，体积约 109 MB；适合“点开就用”。


## 使用指南
- 启动：双击发布目录中的 ClassScreenLock.exe;
- 管理员运行：网络拦截（修改 Hosts/防火墙规则）需要管理员权限，建议以管理员身份运行；
- 锁定：主界面“开始锁定”，可选择 LockMode（仅防护/仅锁屏/完全锁定）；
- 课间按钮：课间自动显示，可点击进行二次确认锁定；上课自动隐藏；
- 网络拦截：在“网络拦截”页面添加域名规则，可选择拦截方式（App/Hosts/Both）；
- 日志查看：在“安全日志”页面查看拦截与系统事件；
- 安全中心：管理员登录后可进行设置、解锁、账户管理。

## Fluent UI 与体验
- 使用 Avalonia.Themes.Fluent，整体视觉遵循 Win11 Fluent 风格；
- 交互反馈保留轻量涟漪效果；
- 统一字体与配色。

## 权限与系统要求
- Windows 11 推荐；Windows 10 可运行但体验以 Win11 为基准；
- 至少要有 300MB 运行内存；
- 至少要有 500MB 硬盘空间；
- 管理员权限：应用 Hosts 与防火墙规则时需要，非管理员运行会跳过相应操作。

## 安全与隐私
- 不收集个人隐私数据；日志仅包括拦截与系统事件。
- 管理员密码本地加密存储（BCrypt）；可选 TOTP 二次验证。

## 常见问题
- 体积为何较大？
  - 自包含单文件打包会包含 .NET 运行时与平台库，保证免安装、即插即用。
- 无管理员权限运行时网络拦截无效？
  - 是的；应用会跳过需要管理员权限的规则应用与清理。

## 开发与贡献
- 开发流程：Fork → 创建分支 → 提交改动 → 发起 Pull Request。
- 代码规范：
  - 遵循 MVVM，统一命名与样式；
  - 不在代码中添加无关注释与日志；
  - 构建无警告（warnaserror）。
- 问题报告：请附复现步骤、系统信息与日志片段。

## 许可证
- GNU General Public License v3.0


## 操作解释

当您打开时，请注意按照设置向导操作。

<img width="1596" height="939" alt="1" src="https://github.com/user-attachments/assets/bfb3a0ff-aa06-4d8d-957c-c7be1ba35481" />

在此页面您会有两个选择：
- 2FA 验证加管理员验证；
- 纯密码验证。
如不需要 2FA，请选择仅密码。

<img width="1597" height="980" alt="image" src="https://github.com/user-attachments/assets/d7f6a8ff-db18-4691-bff7-6fbb04e64c3a" />

当你进入了这个页面，即恭喜你这代表你已经可以使用了。

<img width="801" height="494" alt="image" src="https://github.com/user-attachments/assets/89d552aa-4d09-456d-bf28-58f66aa73c93" />

但注意，状态还是未登录的，需要登录您在设置向导设置的超级管理员账户后才能配置权限和创建其它账户。

<img width="801" height="494" alt="image" src="https://github.com/user-attachments/assets/3bbe9861-b836-4aaa-a638-b6eecbc3c2c9" />

感谢您的使用！

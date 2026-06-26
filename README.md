

<div align="center">
  
# ClassScreenLock - 班级屏幕管理系统

<image src="img/xuanchuan.png" width="1080" height="1980" />


[![GitHub Issues](https://img.shields.io/github/issues-search/jiugulixiaoniu/ClassScreenLock?query=is%3Aopen&style=for-the-badge&color=667eea&logo=github&label=问题)](https://github.com/jiugulixiaoniu/ClassScreenLock/issues)
[![最新版本](https://img.shields.io/github/v/release/jiugulixiaoniu/ClassScreenLock?style=for-the-badge&color=667eea&label=最新正式版)](https://github.com/jiugulixiaoniu/ClassScreenLock/releases/latest)
[![最新Beta版本](https://img.shields.io/github/v/release/jiugulixiaoniu/ClassScreenLock?include_prereleases&style=for-the-badge&label=测试版)](https://github.com/jiugulixiaoniu/ClassScreenLock/releases/)
[![上次更新](https://img.shields.io/github/last-commit/jiugulixiaoniu/ClassScreenLock?style=for-the-badge&color=667eea&label=最后更新时间)](https://github.com/jiugulixiaoniu/ClassScreenLock/commits/main)
[![下载统计](https://img.shields.io/github/downloads/jiugulixiaoniu/ClassScreenLock/total?style=for-the-badge&color=667eea&label=累计下载)](https://github.com/jiugulixiaoniu/ClassScreenLock/releases)

[![License](https://img.shields.io/badge/License-GPLV3-blue.svg?style=for-the-badge)](https://opensource.org/licenses/GPLV3)

![Alt](https://repobeats.axiom.co/api/embed/f0fe9669cfcf1885296dd928b0299d62d4e006a9.svg "Repobeats analytics image")

</div>

--------

# ClassScreenLock

由于电子教育的兴起，各个班级安装上了多媒体设备，有很多学生会在下课/放学后对计算机进行操作而造成不必要的麻烦。

而 ClassScreenLock 专为此情况设计。

目前，它可以帮你实现：

### 1. 灵活的时段管控
支持按课程表设置锁屏时间段，上课解锁、下课锁屏。一天内的不同时段可配置不同策略，完全适配真实教学节奏。

### 2. 强力的锁屏保护
锁屏状态下采用多重防退出机制，阻止任务管理器、强制关闭等常见学生“破解”手段，让管控真正落地。

### 3. 网络管控
内置网址过滤模块，可在自动拦截娱乐、游戏、短视频等网站。支持自定义黑名单，兼顾教学需要与上网规范。

### 4. 应用与文件拦截
支持禁止运行指定程序（如游戏、聊天工具）、禁止打开特定文件或文件夹。即使学生自带U盘，也无法绕过策略运行非授权软件。

### 5. 屏幕截图（可视化记录）
可按设定时间间隔自动截取屏幕内容，帮助老师了解课堂实况、课后回顾学生行为。截图本地保存，隐私安全可控。

### 6. 摄像头拍摄（选配）
支持定时调用摄像头拍摄教室画面，用于远程巡查上课秩序或记录一天课程情况。该功能可选关，充分尊重使用场景。

### 7. 进程级防护
持续监控关键进程，一旦发现异常退出或篡改行为，自动恢复锁屏状态。从底层提升软件生存能力，减少人为破坏风险。

### 8. 数据加密与权限分级
所有配置及密码数据均加密存储，防止通过替换文件方式绕过鉴权。内置三级权限（用户、管理员、超级管理员），敏感操作独立授权，满足分权管理需求。

### 9. 集控中心统一管理
支持通过独立集控平台对多台设备进行统一配置、策略下发和状态监控。可分级分权管理组织、设备及人员，适合年级/学校级部署。

### 即将支持 / 持续迭代中
- 更丰富的数据统计与行为报表
- 与教务课表系统自动同步
- 学生端轻量提示与课堂互动能力
- 更强力的防护方式

> 只要您能想到的课堂屏幕管控场景，ClassScreenLock 都在不断进化中。

---

## 下载与安装

### 请确认您的设备是否满足以下要求再进行安装
  #### 最低硬件要求：
  
  - 处理器（CPU）：基础频率必须大于或等于 1 GHz 的64位处理器。
  - 内存（RAM）：在运行本软件时，系统必须拥有 300 MB 及以上的空余物理内存。
  - 存储空间：安装及运行分区必须拥有 1500 MB 及以上的可用磁盘空间。

  #### 强制软件与系统要求：
  
  - 操作系统：支持 Microsoft Windows 10 或 Windows 11 操作系统。本软件理论支持 Windows 10 的各个版本，但推荐使用最新稳定版本以获得最佳兼容性。
  - 系统框架：必须安装 .NET 9.0 Runtime 或更高版本。
  - 系统权限：安装及部分核心功能的正常运作，必须要求以“管理员身份”运行本软件。

  #### 网络与环境要求：
  
  本软件为本地单机应用程序，主要功能无需连接互联网即可使用。
  
### 官方下载渠道
- **[GitHub Releases](https://github.com/ClassScreenLock/ClassScreenLock/releases)**
- **[官方下载中心](https://classscreenlock.github.io/download/)**
- **安装说明**：下载对应版本的安装包/绿色包，解压后直接运行主程序，无需复杂安装步骤

---

## 贡献者感谢

<!-- ALL-CONTRIBUTORS-LIST:START - Do not remove or modify this section -->
<!-- prettier-ignore-start -->
<!-- markdownlint-disable -->
<table>
  <tbody>
    <tr>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/jiugulixiaoniu"><img width=auto height=auto alt="image" src="https://avatars.githubusercontent.com/u/172874396" /><br /><sub><b>jiugulixiaoniu</b></sub></a><br /><a href="#content-jiugulixiaoniu" title="Content">🖋</a> <a href="#code-jiugulixiaoniu" title="Code">💻</a> <a href="#maintenance-jiugulixiaoniu" title="Maintenance">🚧</a></td>
      <td align="center" valign="top" width="11.11%"><a href="https://github.com/RuanhoR"><img width=auto height=auto alt="image" src="https://avatars.githubusercontent.com/u/217868362" /><br /><sub><b>RuanhoR</b></sub></a><br /><a href="#content-RuanhoR" title="Content">🖋</a> <a href="#code-RuanhoR" title="Code">💻</a> <a href="#maintenance-RuanhoR" title="Maintenance">🚧</a></td>
      <td align="center" valign="second" width="11.11%"><a href="https://github.com/ThreeMonthAgo"><img width=auto height=auto alt="image" src="https://avatars.githubusercontent.com/u/225839283" /><br /><sub><b>ThreeMonthAgo</b></sub></a><br /><a href="#code-ThreeMonthAgo" title="Code">💻</a></td>
      <td align="center" valign="second" width="11.11%"><a href="https://github.com/Purrbyte-zdy"><img width=auto height=auto alt="image" src="https://avatars.githubusercontent.com/u/210122017" /><br /><sub><b>Purrbyte-zdy</b></sub></a><br /><a href="#docx editor-Purrbyte-zdy" title="Docx Editor">✍</a></td>
    </tr>
  </tbody>
</table>


### 为我们提供帮助的公司/组织


| 智教联盟 · 论坛支持 | 汇智卓创 · 下载服务 |
| :---: | :---: |
| <div align="center"><a href="https://forum.smart-teach.cn/" target="_blank"><img src="https://static.smart-teach.cn/logos/banner.jpg" width="380" alt="智教联盟" style="border:none;background:transparent;"></a></div> | <div align="center"><a href="https://smart-teach.cn/" target="_blank"><img src="https://smart-teach.cn/images/logos/logo-full.png" width="380" alt="汇智卓创" style="border:none;background:transparent;"></a></div> |
| 感谢智教联盟提供论坛平台支持 | 感谢天津静海汇智卓创文化发展有限公司提供免费下载服务 |

---

## 支持与反馈

如果您在使用过程中遇到问题，或有功能建议，欢迎通过以下方式反馈：
- **问题提交**：在GitHub Issues中详细描述问题场景和复现步骤
- **功能建议**：提交Feature Request，我们会评估并纳入迭代计划

---

## 赞助

### 为了让 ClassScreenLock 持续活下去、长出更多有用的功能，我需要一点点您的支持。

https://afdian.com/a/jiugulixiaoniu

### 我郑重承诺：

完全自愿：赞助纯粹是您对我的支持与鼓励，绝非强制行为。

功能无关：无论是否赞助，您都可以免费、完整地使用软件的所有主要核心功能，不存在“付费解锁”或“赞助特权功能”。

简单说：您给的是情分，我用的是本分。 每一份心意都会成为我持续维护、更新的动力。感谢相遇！

### [爱发电赞助通道](https://afdian.com/a/jiugulixiaoniu)

> 你的名字会出现在赞助名单里，债我还不起，但恩情我记得住。

---

## 联系方式

- [邮箱](ClassScreenLock@outlook.com)
- [问题反馈](https://github.com/ClassScreenLock/ClassScreenLock/issues)
- [QQ群：1081181845](https://qm.qq.com/q/1081181845)
- [官方漏洞枚举库](https://github.com/ClassScreenLock/ClassScreenLock/wiki)

---

## 官方文档

查看[官方网站](https://classscreenlock.github.io)
查看[用户协议](https://classscreenlock.us.ci/eula)

---

## 贡献指南

欢迎所有开发者参与项目贡献，无论是修复Bug、新增功能还是优化文档：

---

## Star历程

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/svg?repos=jiugulixiaoniu/ClassScreenLock&type=Date&theme=dark">
  <img alt="Star History" src="https://api.star-history.com/svg?repos=jiugulixiaoniu/ClassScreenLock&type=Date">
</picture>

---

**Copyright © 2025-2026 jiugulixiaoniu**

<div align="center">

# MpvSettings

**mpv 的图形设置工具。只做配置，不做播放器。**

<a href="https://github.com/louissgd/MpvSettings/releases/latest"><img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011-0078D4?logo=windows11&logoColor=white" alt="Platform: Windows 10 / 11" height="20"></a>
<a href="https://github.com/louissgd/MpvSettings/releases/latest"><img src="https://img.shields.io/badge/Architecture-x86--64-4C8BF5" alt="Architecture: x86-64" height="20"></a>
<a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white" alt=".NET 10" height="20"></a>
<a href="#许可"><img src="https://img.shields.io/badge/License-MIT-2EA44F" alt="License: MIT" height="20"></a>

</div>

## 它是什么

一个 Windows 程序，把 mpv 的常用配置做成图形界面。它是 **MyMPC 的配套工具**——按 MyMPC 的安装布局读写 `MediaPlayer\mpv\portable_config`,需要先装好 MyMPC(并勾选 MPV 组件)才能使用;单独拿一个 EXE 到空机器上是跑不起来的。

mpv 本身是纯播放器——配置项全写在 `mpv.conf` / `input.conf` 等文本文件里,选项多、格式各异、改错一个键就可能不生效。MpvSettings 做的只有一件事:**把这些配置可视化地读出来、改掉、写回去**,并保证写回去的东西格式正确、只动你改过的部分。

它**不是**播放器整合包,不内置 mpv,不提供解码器、滤镜、脚本或 UI 主题。它也不给 mpv 增加任何播放功能——超分、补帧、弹幕、媒体库、网络串流这些一律不做。你要的是一台干净的播放器,它就是给这台播放器调参数用的。

## 它解决的具体问题

- **不用记选项名**。硬解、色调映射、声道、截图格式、快捷键,都在界面上。
- **不写坏配置**。只写 diff——没改的键一个字节都不动;颜色值自动加引号(mpv 里 `#` 会被当注释);数值用固定文化格式写,不受系统区域设置影响。
- **下拉值不认得就置灰**。盘上出现程序不认识的值(比如 `hwdec=auto-copy` 之外的自定义值),追加成"自定义"项并选中,但禁止写入,避免误覆盖。
- **改完可回滚**。所有编辑先进草稿,点"确定/应用"才落盘;"取消"不留痕。
- **能备份能迁移**。配置一键导出/导入到文件夹,导入前自动再备份一份。
- **能脚本化**。恢复默认、导出、导入都有命令行入口,退出码即结果,给安装程序和 AHK 脚本调用。

## 功能

五个设置页,加一个字幕样式弹窗:

| 页面 | 能调什么 |
|---|---|
| 播放 | 硬解方式、隔行扫描、循环/记忆播放位置、字幕轨选择、外挂字幕自动重读、封面大小、无封面窗口比例、多实例 |
| 画面 | HDR 直通 / HDR→SDR、色调修复档位、色域范围、NVIDIA RTX 视频增强(VSR / HDR)及硬件前提提示 |
| 音频 | 音效滤镜(常驻)、声道输出、比特流直通及前提提示 |
| 截图 | 保存位置、文件名模板、图片格式(JPG / PNG) |
| 快捷键 | 常用按键的绑定与清除(清除 = 删行恢复默认,不是禁用) |
| 字幕样式(SRT) | 字体、颜色、字号、行间距、描边、阴影、边距——只作用于 SRT 等纯文本字幕;ASS/SSA 作者样式和 SUP 位图字幕不受影响 |

其他:配置备份/恢复、恢复出厂默认、当前配置只读视图(配置来源、mpv 路径、读取时间、音效状态、比特流状态)。

详见 [docs/功能说明.md](docs/功能说明.md)。

## 下载与运行

**第一步:先装 MyMPC(前置条件)。** 本工具是 MyMPC 的配套组件,不能独立使用。MyMPC 整包下载(固定链接,内容更新但链接不变),安装时勾选 MPV 组件:

| 渠道 | 链接 | 提取码 |
|---|---|---|
| **123云盘(推荐)** | https://www.123pan.com/s/iF8iVv-Xbeh3.html | `mnCb` |
| 电信天翼云盘 | https://cloud.189.cn/t/AbieUzNzyqUf | `s4lr` |
| 移动云盘 | [打开链接](https://yun.139.com/shareweb/#/w/i/2yA8CH8rxAax5) | 无 |

**第二步:更新 MpvSettings.exe(可选)。** 从 [GitHub Releases](https://github.com/louissgd/MpvSettings/releases/latest) 下载单文件,覆盖到 `<安装根>\MediaPlayer\mpv\MpvSettings.exe`。整包里已带一份,覆盖只为升到最新版。

跑起来的三个硬条件:已装 MyMPC 主程序、安装时勾选了 MPV 组件、`<安装根>\MediaPlayer\mpv\portable_config\mpv.conf` 存在。路径由注册表 `HKLM\SOFTWARE\...\App Paths\mplayerl.exe` 的 `Path` 锚定,程序不会往 EXE 旁边回退找配置。三条缺一不可,否则窗口显示禁用原因。完整说明见 [docs/安装与更新.md](docs/安装与更新.md)。

改完点"应用"或"确定"保存,再重启 mpv 生效。左下角"打开 MPV 播放器"可直接拉起播放器。首次配置可先跑 `MpvSettings.exe --default` 生成默认值。

### 从源码构建

需要 Windows 10/11 x64、[.NET 10 SDK](https://dotnet.microsoft.com/download)。

```bat
dotnet publish -c Release -r win-x64 -o publish
```

产物在 `publish\MpvSettings.exe`。项目用 `PublishSingleFile=true` + `SelfContained=false`,所以是**单文件、框架依赖**——运行时机器上要有 .NET 10 运行时。编译出的 EXE 仍需放进已装好的 MyMPC 环境才能用,不是独立程序。

### 命令行

```bat
MpvSettings.exe --default                        :: 恢复出厂默认,无窗口
MpvSettings.exe --export[=<目录>]                :: 导出配置
MpvSettings.exe --import[=<目录>]                :: 导入配置
MpvSettings.exe --config-dir=<配置目录>          :: 显式指定配置目录(验证/多环境)
MpvSettings.exe --default --dry-run              :: 只算不改,打印计划
```

全部参数、退出码和示例见 [docs/命令行参数.md](docs/命令行参数.md)。

## 设计要点

- **草稿 / 基线 / 写计划三层**。窗口控件绑定到 `SettingsDraft`,打开时从磁盘反推一次;点保存时用 `WritePlanBuilder` 和打开时的基线比 diff,生成 `WritePlan`,再由 `ConfWriter` 落盘。反推和写入共用同一份选项表,不会各写各的。
- **只管自己管的键**。`ManagedProps` 明确登记本程序接管的属性;你手写的其它配置、第三方脚本、自定义快捷键和菜单条目都保留。
- **保护 ASS**。`sub-ass-override=no` 让 ASS/SSA 完全走作者参数,SRT 因 `converted=true` 照常吃样式。一行开关隔开两类字幕。
- **默认值分层**。配置模板写跨机器通用的静态默认;安装时 `MpvSettings.exe --default` 探测本机字体,有华文楷体就升级成 52。探测结果只属于这台机器,不污染模板。

技术实现与踩坑记录见 [docs/development/](docs/development/)——那是开发过程的技术档案,不是使用文档。

## 目录结构

```
.
├── Assets/             内嵌资源(图标、Lua 脚本、菜单配置)
├── Controls/           自定义 WPF 控件
├── Models/             草稿、写计划、配置快照
├── Services/           定位、写盘、备份、命令行、系统状态
├── Views/              五个设置页 + 字幕样式弹窗
├── Themes/             配色与样式
├── docs/
│   ├── 安装与更新.md    前置条件、下载、覆盖更新
│   ├── 发布说明.md      维护者用的 Release 资产与模板
│   ├── 功能说明.md
│   ├── 命令行参数.md
│   ├── 字幕样式.md
│   ├── 快捷键.md
│   └── development/    开发日志(已脱敏)
└── MpvSettings.csproj
```

## 许可

MIT © 2026 louissgd。详见 [LICENSE](LICENSE)。

本项目只设置 mpv,不分发 mpv 本体,Releases 仅发布 `MpvSettings.exe` 单文件。mpv 及其文档、源码按各自许可发布;`docs/development/` 中引用的 mpv 选项说明等资料版权归 mpv 项目所有。发布资产与版本说明见 [docs/发布说明.md](docs/发布说明.md)。

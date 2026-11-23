# MpvSettings 1.0.6 改动记录

> 1.0.5 → 1.0.6。两处修复：**① 无 HDR 显示器时的显示与写盘一致性**——其中写盘分支是**功能正确性修复**（不只是显示）：`tone-mapping` 在无 HDR 屏上从错误的 `clip` 改为 `spline`；**② 删除过时的右键菜单播放列表说明**。
> 前次基线见 `MpvSettings_1.0.5_改动记录.md`（1.0.5，末次产物 md5 `6cecd962…`，含 1.9 描边=1 改动后重编；1.0.5 文档第五节的 `6025b354…` 未随 1.9 同步，以实测 `6cecd962…` 为准）。
>
> 这两处原写在 1.0.5 草稿记录的 1.10 节（当时判"版本号仍 1.0.5"、未发版、未进 `docs/development/`）。用户事后要求复查"改动前是否影响功能正确性"，核对 AHK / lua 后确认写盘分支会实际影响 HDR 片源在 SDR 屏上的显示，故升 1.0.6 单独发版。
>
> 记录日期：2026-11-23　｜　归档人：本次收尾

## 一、功能改动（1.0.5 → 1.0.6）

### 1. 【功能正确性修复】无 HDR 显示器时 `tone-mapping` 写 `spline`，不再写 `clip`

**问题链条（改动前的实际后果）**：

1. `WritePlanBuilder` 改动前只按「HDR 模式=直通 + 色调档位=关 + 非自定义」判定 `clip`，**完全不看机器有没有 HDR 显示器**。
2. `hdr-autoswitch.lua` 启动时检测到无 HDR 显示器，会在内存里把 `mode` 降级成 `tonemap`（lua 第 566–569 行：`if startup_count == 0 and o.mode == "passthrough" then o.mode = "tonemap"`），但它**只管不开系统 HDR，从不碰 mpv.conf 的 `tone-mapping`**（全文件对 tone-mapping 只有 `mp.get_property` 读取，无 `set`）。
3. tonemap 模式的 HDR→SDR 全靠 mpv.conf 里已有的 `spline`（lua 头注释第 29 行：`tonemap : 不碰系统开关，用 mpv.conf 的 spline 做 HDR->SDR`）。
4. 于是改动前的组合是：**无 HDR 显示器 + 默认配置 → mpv.conf 写 `clip` → HDR 片源 PQ 信号被原样裁剪送进 SDR 屏 → 画面泛白 / 高光削顶**。这是实际的显示错误，不是观感差异或单纯界面不一致。

**为什么 setting.exe（AHK）能躲过而 MpvSettings 不能**：AHK 的 `MpvSyncToneMapping()` 有 `if (!HdrSupported) MpvSet("tone-mapping","spline")`（setting.ahk 第 1240 行），但它在播放器设置界面点 HDR/色调按钮时才触发（第 1502 行），是运行期 IPC、不写盘；只用 MpvSettings 配好、之后不再点 AHK 按钮的用户，`clip` 就是最终生效值。

**修法**：`Services/WritePlanBuilder.cs` 的 `tone-mapping` 判定纳入 `d.HdrSupported`：

```csharp
string tm = d.HdrSupported &&
            d.HdrModeIndex == 0 && d.ToneIndex == 0 &&
            !d.HdrModeCustom && !d.ToneCustom ? "clip" : "spline";
```

无 HDR 显示器一律 `spline`，与 AHK `MpvSyncToneMapping` 的 `!HdrSupported` 分支同源。有 HDR 显示器的默认组合仍写 `clip`（真直通零处理），行为不变。

### 2. 【显示一致性】无 HDR 显示器时三控件置灰 + HDR 模式显示「HDR转SDR」

setting.exe 的 `HdrSupported = DetectHdrSupported()`（枚举活动显示器有无 HDR 能力）为 false 时：「直通显示器」按钮、色调修复四档按钮、「仅全屏切换 HDR」都置灰，HDR 模式强制显示「HDR转SDR」（`mModeOn := "MpvHdrMode2"`），且**不写配置文件**——播放时由 hdr-autoswitch.lua 自己降级。MpvSettings 之前缺这套判据，界面显示成可改的「直通显示器」，与实际降级行为不符。

| 位置 | 改动 |
|---|---|
| `Services/WindowsHdrState.cs` | 新增 `IsHdrSupported()`：枚举支持 HDR 的活动 target 非空即 true，对应 AHK `HdrWinEnum()`；检测失败返回 false |
| `Models/SettingsDraft.cs` | 新增只读环境快照 `_hdrSupported` + `HdrSupported`（不参与 diff / 写盘）；`HdrModeDisplayIndex`（显示层，无 HDR 时固定=1，吞掉 setter 不改底层）、`HdrModeInteractive`、`OnlyFullscreenInteractive`；`ToneLockedBySdr` / `ToneInteractive` 纳入 `!HdrSupported` 条件 |
| `Models/SettingsDraft.cs` `LoadFrom` / `Services/DefaultProfile.cs` `DefaultDraft` | 注入 `HdrSupported = WindowsHdrState.IsHdrSupported()`；Default 里在 `ResetAll()` 前注入（与 NV/Windows HDR 同一批只读快照） |
| `Views/VideoView.xaml` | HDR 模式 ComboBox 改绑 `HdrModeDisplayIndex` + `HdrModeInteractive`；仅全屏开关加 `IsEnabled=OnlyFullscreenInteractive`；两处 Description 说明无 HDR 时置灰/降级 |

关键设计：**底层 `_hdrModeIndex` / 写盘值不动**。`HdrModeDisplayIndex` 只是显示层映射，无 HDR 时控件已置灰、setter 吞掉；盘上原始值保留，diff 不误判，与 AHK「GUI 显示覆盖、配置写 spline、mode 交给 lua 降级」一致。

判据核对：AHK `HdrWinRefreshButton()` 在 `HdrWinEnum()` 返回 0 时把按钮写成「不支持」并提示"Windows HDR 开关和色调修复已禁用"；C# `EnumTargets()` 只收「Query 返回 supported=true」的 target，非空等价 AHK 的枚举非空，判据同源。

### 3. 【文案】删除过时的右键菜单播放列表说明

`Models/ShortcutCatalog.cs` E5 组「显示播放列表」原说明"右键菜单「播放列表」仍可打开"已过时——右键菜单配置（`menu.conf` / `context_menu.conf` / `mympc-menu.lua`）里已无 playlist 项，实测右键菜单也确实没有。改为"原默认 F8 已被本包占用，可在此自行指定按键"，不再承诺不存在的入口。

## 二、版本号（1.0.5 → 1.0.6）

| 位置 | 改动 |
|---|---|
| `MpvSettings.csproj` | Version / InformationalVersion → `1.0.6`；AssemblyVersion / FileVersion → `1.0.6.0` |
| `Views/AboutView.xaml` | 「MPV 简单设置 1.0.5」→「MPV 简单设置 1.0.6」 |

标题栏 `· v1.0.6` 由 `MainWindow.xaml.cs` 读 `InformationalVersion` 自动显示，`AppVersion` 里 `IndexOf('+')` 截断 SourceLink 追加的 commit hash，无需改 XAML。EXE 元数据实测：`FileVersion=1.0.6.0` / `ProductVersion=1.0.6+08c79f60…`。

## 三、文件改动清单（8 个公开文件）

- `Models/SettingsDraft.cs`：`HdrSupported` 环境快照 + 显示层属性（`HdrModeDisplayIndex` / `HdrModeInteractive` / `OnlyFullscreenInteractive`），`ToneLockedBySdr` / `ToneInteractive` 纳入无 HDR 条件，`LoadFrom` 注入快照。
- `Models/ShortcutCatalog.cs`：E5 播放列表说明文案。
- `MpvSettings.csproj` / `Views/AboutView.xaml`：版本号 1.0.6。
- `Services/WindowsHdrState.cs`：新增 `IsHdrSupported()`。
- `Services/WritePlanBuilder.cs`：`tone-mapping` 判定纳入 `HdrSupported`（功能修复）。
- `Views/VideoView.xaml`：HDR 模式 / 仅全屏开关绑定与 Description。

未改任何 `mpv.conf` / `input.conf` / 快捷键绑定：写盘逻辑在 EXE 里，配置模板保持原样。

## 四、验证

- `dotnet build -c Release --nologo`：**0 警告 0 错误**；`dotnet publish -c Release -r win-x64 -o publish`：成功。
- 三处 EXE md5 完全一致：`1c38c8a4dd3746b51db7baf5db042ac6`（487,126 B）；生产目录副本元数据实测 `FileVersion=1.0.6.0`。
- 隔离配置目录跑 `publish/MpvSettings.exe --plan-only --config-dir=<临时目录> --dump-plan=<临时JSON>`：退出码 0，18 项变更 / 6 个文件；本机有 HDR 能力 → `IsHdrSupported()=true` → `tone-mapping` 仍算 `clip`（有 HDR 的默认组合不变）；`HdrSupported` / `HdrModeDisplayIndex` / `OnlyFullscreenInteractive` 均未出现在写计划里，环境快照没有被误识别为用户改动、不制造误写。
- 无 HDR 显示器分支（本机有 HDR，无法实机复现）：`tone-mapping=spline` 由同源判据保证——C# `IsHdrSupported()` 与 AHK `DetectHdrSupported()` 都是"枚举支持 HDR 的活动 target 非空"，lua 启动降级逻辑已实测。
- GUI 置灰 / 字体 / 颜色对话框需用户侧实机确认（无头环境测不了）。

## 五、产物与部署状态

| 位置 | 文件 | 版本 | 说明 |
|---|---|---|---|
| 源码 | `MpvSettings.csproj` | **1.0.6** | 四处版本号 + AboutView 硬编码均已改 |
| 发布产物 | `MpvSettings\publish\MpvSettings.exe` | **1.0.6** | 487,126 B，md5 `1c38c8a4dd3746b51db7baf5db042ac6` |
| 打包源 | `12.mpv\MpvSettings.exe` | **1.0.6** | 已同步，与 publish md5 一致 |
| 生产目录 | `<MPV安装目录>\MpvSettings.exe` | **1.0.6** | 已同步，与 publish md5 一致（元数据实测 1.0.6.0） |
| 旧版备份 | `<备份目录>` | 1.0.5 | 三份：`MpvSettings_{publish,12mpv,production}_105.exe`，均 487,638 B / `6cecd962…` |

> 顺序：先备份三处旧 EXE，再 publish，再覆盖 12.mpv / 生产目录，最后 md5 比对。备份放在 `_bak_*` 目录，不污染打包源。

## 六、使用说明 / 边界

- 无 HDR 显示器时，HDR 模式下拉固定显示「HDR转SDR」且置灰，色调档位与「仅全屏切换」一并置灰——这是**显示层覆盖**，盘上原有模式值不动；换回支持 HDR 的显示器后下次启动自动恢复原显示与行为。
- 无 HDR 显示器时 MpvSettings 保存会把 `tone-mapping` 写成 `spline`（HDR 片源转 SDR）；这是本次修复的正确行为，不要再手动改回 `clip`。
- 有 HDR 显示器且保持「直通 + 色调关」默认组合时才写 `clip`（PQ 原样送显示器，零处理）。
- 播放列表说明只改文案，未新增/删除任何快捷键绑定；想给「显示播放列表」指定按键，到快捷键页自行填写。

## 七、发版状态

- 提交：本次 8 个公开文件 + 本文件（脱敏版）一起提交，tag `v1.0.6`。
- Release：仅附单个 `MpvSettings.exe`（487,126 B / `1c38c8a4…`），正文套 `docs/发布说明.md` 模板；上传后回读验哈希。
- 国内网盘链接是 MyMPC 整包的，不属于本项目资产，本次不动。

# MpvSettings 1.0.6 改动记录

> 1.0.5 → 1.0.6。两处修复：**① 无 HDR 显示器时的显示与写盘一致性**——其中写盘分支是**功能正确性修复**（不只是显示）：`tone-mapping` 在无 HDR 屏上从错误的 `clip` 改为 `spline`；**② 删除过时的右键菜单播放列表说明**。
> 同日（2026-11-23）晚间又追加两项并**并入 1.0.6、不升版**：**③ 底部细横线进度条（新功能，默认开）**；**④ 纯音频封面时自动关闭 RTX HDR / VSR（崩溃缓解）**。详见第八节；最终产物 md5 `4e007c87…` / 492,758 B。
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

标题栏 `· v1.0.6` 由 `MainWindow.xaml.cs` 读 `InformationalVersion` 自动显示，`AppVersion` 里 `IndexOf('+')` 截断 SourceLink 追加的 commit hash，无需改 XAML。EXE 元数据实测：`FileVersion=1.0.6.0` / `ProductVersion=1.0.6+<commit>…`。

## 三、文件改动清单

初版（第一~三节）8 个公开文件：

- `Models/SettingsDraft.cs`：`HdrSupported` 环境快照 + 显示层属性（`HdrModeDisplayIndex` / `HdrModeInteractive` / `OnlyFullscreenInteractive`），`ToneLockedBySdr` / `ToneInteractive` 纳入无 HDR 条件，`LoadFrom` 注入快照。
- `Models/ShortcutCatalog.cs`：E5 播放列表说明文案。
- `MpvSettings.csproj` / `Views/AboutView.xaml`：版本号 1.0.6。
- `Services/WindowsHdrState.cs`：新增 `IsHdrSupported()`。
- `Services/WritePlanBuilder.cs`：`tone-mapping` 判定纳入 `HdrSupported`（功能修复）。
- `Views/VideoView.xaml`：HDR 模式 / 仅全屏开关绑定与 Description。

第八节追加（合并进 1.0.6，不升版）：`Assets/MyMPC_ui.lua`、`Models/SettingsDraft.cs`、`Models/WritePlan.cs`、`Services/WritePlanBuilder.cs`、`Views/PlaybackView.xaml`、`docs/功能说明.md`。

未改任何 `mpv.conf` / `input.conf` / 快捷键绑定：写盘逻辑在 EXE 里，配置模板保持原样（第八节的 `osd_progress` 写 `script-opts/MyMPC.conf`）。

## 四、验证

- `dotnet build -c Release --nologo`：**0 警告 0 错误**；`dotnet publish -c Release -r win-x64 -o publish`：成功。
- 初版三处 EXE md5 完全一致：`1c38c8a4dd3746b51db7baf5db042ac6`（487,126 B）；合并后最终 md5 见第八节。
- 隔离配置目录跑 `publish/MpvSettings.exe --plan-only --config-dir=<临时目录> --dump-plan=<临时JSON>`：退出码 0，18 项变更 / 6 个文件；本机有 HDR 能力 → `IsHdrSupported()=true` → `tone-mapping` 仍算 `clip`（有 HDR 的默认组合不变）；`HdrSupported` / `HdrModeDisplayIndex` / `OnlyFullscreenInteractive` 均未出现在写计划里，环境快照没有被误识别为用户改动、不制造误写。
- 无 HDR 显示器分支（本机有 HDR，无法实机复现）：`tone-mapping=spline` 由同源判据保证——C# `IsHdrSupported()` 与 AHK `DetectHdrSupported()` 都是"枚举支持 HDR 的活动 target 非空"，lua 启动降级逻辑已实测。
- GUI 置灰 / 字体 / 颜色对话框需用户侧实机确认（无头环境测不了）。

## 五、产物与部署状态（合并后最终状态）

| 位置 | 文件 | 版本 | 说明 |
|---|---|---|---|
| 源码 | `MpvSettings.csproj` | **1.0.6** | 四处版本号 + AboutView 硬编码均已改 |
| 发布产物 | `MpvSettings\publish\MpvSettings.exe` | **1.0.6** | 492,758 B，md5 `4e007c8703071c411f9ccfa25b359990`（含第八节 lua 资源与文案） |
| 打包源 | `12.mpv\MpvSettings.exe` | **1.0.6** | 已同步，与 publish md5 一致 |
| 生产目录 | `<MPV安装目录>\MpvSettings.exe` | **1.0.6** | 已同步，与 publish md5 一致（元数据实测 1.0.6.0） |
| 旧版备份 | `<备份目录>\` | 1.0.5 | 三份：`MpvSettings_{publish,12mpv,production}_105.exe`，均 487,638 B / `6cecd962…` |
| 合并前备份 | `<备份目录2>\` | 1.0.6 初版 | 两份 EXE + `MyMPC_ui_prev.lua`（进度条/RTX 改动前的旧 lua） |

> 顺序：先备份三处旧 EXE，再 publish，再覆盖 12.mpv / 生产目录，最后 md5 比对。备份放在 `_bak_*` 目录，不污染打包源。

## 六、使用说明 / 边界

- 无 HDR 显示器时，HDR 模式下拉固定显示「HDR转SDR」且置灰，色调档位与「仅全屏切换」一并置灰——这是**显示层覆盖**，盘上原有模式值不动；换回支持 HDR 的显示器后下次启动自动恢复原显示与行为。
- 无 HDR 显示器时 MpvSettings 保存会把 `tone-mapping` 写成 `spline`（HDR 片源转 SDR）；这是本次修复的正确行为，不要再手动改回 `clip`。
- 有 HDR 显示器且保持「直通 + 色调关」默认组合时才写 `clip`（PQ 原样送显示器，零处理）。
- 播放列表说明只改文案，未新增/删除任何快捷键绑定；想给「显示播放列表」指定按键，到快捷键页自行填写。
- 底部进度条改动需重启播放器生效；纯音频无封面时由信息窗自带进度条接管，不重复显示。

## 七、发版状态

- 提交：公开文件 + `docs/development/MpvSettings_1.0.6_改动记录.md`（脱敏版）一起提交，tag `v1.0.6`。
- Release：仅附单个 `MpvSettings.exe`，正文套 `docs/发布说明.md` 模板；上传后回读验哈希。第八节追加后**不新建 Release**，删除旧资产、上传最终 EXE，并把 `v1.0.6` tag 移到最终提交。
- 国内网盘链接是 MyMPC 整包的，不属于本项目资产，本次不动。

## 八、同版本追加（2026-11-23 晚，并入 1.0.6，不升版）

1.0.6 发布后用户又提两项，核对 mpv-lazy / uosc 与 FrankyRenderer 相关链路后实现，用户实测通过后决定并入 1.0.6。

### 8.1 【新功能】底部细横线进度条（默认开）

**来源核对**：mpv-lazy 播放时常驻的"置顶细横线进度条"实际来自 **uosc 的 `progress` 元素**（`progress=windowed` + `progress_size=2` + line 游标），不是 mpv 原生 OSD bar（后者只在主动 seek 时闪现）。

**方案**：用户选「自研精简版」，不搬整套 uosc（40+ 文件，会与 MyMPC 原生右键菜单补丁、配色体系、`MyMPC_ui.lua` 的封面信息窗 / 单实例 / RTX 逻辑大面积冲突）。在 `Assets/MyMPC_ui.lua` 新增 ①a 节，独立 `ass-events` overlay：

- 贴屏幕**底部**一条 2px 细横线游标，宽度 `max(12, W/60)`，随 `time-pos/duration` 左右移动；
- **不画整条轨道**（透明），只显示游标本身——避免初版"整条黑线被覆盖"的观感；
- 颜色用**主题绿 `#A3F6BE`**（ASS BGR 写法 `\c&HBEF6A3&`，与 `Themes/Theme.xaml` 的 `AccentBrush` 一致）；
- 0.10s 周期 timer；`osd-dimensions` 变化时重画；纯音频无封面（`no_art`）时跳过，避免与信息窗自带进度条重复；
- 窗口与全屏都显示（用户选「始终显示」，比 mpv-lazy 的 windowed 更全）。

**观感迭代**：初版贴顶 / `W/16`（1920 下约 120px）/ 暗灰整条轨道 / 紫色 `#B59AFF`；用户实测后按上述改为贴底 / `W/60`（约 32px）/ 透明轨道 / 主题绿。信息窗自身的 `PURPLE` 常量（与进度条无关）保留未动。

**开关**：`osd_progress` 写 `script-opts/MyMPC.conf`，默认 `yes`。

### 8.2 【崩溃缓解】纯音频封面时自动关闭 RTX HDR / VSR

- `apply_rtx()` 前置判据加封面检测：`current-tracks/video` 的 `image==true`（与 `cover-art.conf` 同源）→ `rtx_remove()` 返回，HDR 与 VSR 都关（用户选「都关」，静态封面无超分意义，最大限度降低 d3d11vpp 挂卸带来的崩溃几率）。
- 补 `mp.observe_property("current-tracks", ...)`：封面标志可能比 `file-loaded` 晚到，初版 0.25s 判不到、1s 兜底又因 `rtx_filter` 已挂被跳过的时序漏洞，靠这个 observe 保证误挂的滤镜被摘掉。
- 用户实测：RTX HDR 关闭**测试通过**；VSR 不崩溃，测试略过。

### 8.3 MpvSettings 侧改动（支持进度条开关）

- `Models/SettingsDraft.cs`：加 `OsdProgress` 字段 + `LoadFrom`（缺键按 `yes`）+ `ResetDefaults`。
- `Services/WritePlanBuilder.cs`：`BuildMyMpcConf` 写 `osd_progress`。
- **两处易漏登记补齐**：`WritePlanBuilder` 的 `uiChanged` 字段表、`Models/WritePlan.cs` 的 `ManagedProps`——漏登记会导致单独切换开关不写盘，或每次保存都误判为改动。
- `Views/PlaybackView.xaml`：「播放」卡片加「底部进度条」开关；`docs/功能说明.md` 同步一行。

### 8.4 验证

- `dotnet build` / `dotnet publish` 0 错误；lua 用 `mpv --no-config --idle --script` 实际加载验证语法（反向用坏语法确认检测方法有效，好脚本无 `Lua error`）。
- 隔离目录 `--default` 实测写出 `MyMPC.conf` 含 `osd_progress=yes`、lua 资源含新代码；观感调整后复验写出的 lua 含 `BEF6A3` / `H-2` / `W/60` 且旧紫暗轨代码已清除。
- 三处 EXE md5 一致 `4e007c8703071c411f9ccfa25b359990`（492,758 B）；两处运行目录 lua md5 一致 `c866f066…`（CRLF，含新配色与贴底）。

### 8.5 部署与 Release 更新

- 合并前备份三处旧 EXE 与旧 lua 到 `<备份目录2>\`。
- v1.0.6 已有 Release（初版资产为旧 EXE），本次**不新建 Release**：删除旧资产、上传最终 EXE（492,758 B / `4e007c87…`），`v1.0.6` tag 移到最终提交，回读下载验哈希。

## 9. 8K60 硬解回拷优化 + 色调档位文案（2026-11-24）

### 9.1 现象与定位

- 片源 `Bulgaria 8K 60P (FUHD).mkv`：VP9 7680×4320 / 60fps / yuv420p / SDR。
- 生产配置 `hwdec=auto-copy` → 实际走 `vp9-d3d11va-copy`：GPU 解码后回拷内存，再上传给 gpu-next。
- 同条件 8 秒实测（`--geometry=1920x1080 --ao=null --loop-file=no --length=8`）：
  - `auto-copy`：`drop-frame-count=363`、`hwdec-current=d3d11va-copy`；
  - `d3d11va` 零拷贝：`drop-frame-count=2`、`hwdec-current=d3d11va`。
- 解码器自身 `decoder-frame-drop-count=0`，瓶颈在回拷/再上传，不在解码单元（与 MPCVR / FrankyRenderer 零拷贝路径 GPU 仅 50% 一致）。

### 9.2 改法

- 生产 `mpv.conf`：`hwdec=auto-copy` → `hwdec=d3d11va`，新增 `include=~~/dolby-vision.conf`。
- 新建 `portable_config/dolby-vision.conf`：`profile-cond=p["current-tracks/video/dolby-vision-profile"] ~= nil` 时强制 `hwdec=auto-copy`，保住 P4/P5/P7/P8（本版 D3D11 零拷贝拿不到 P5 的 RPU 系数会黑屏）。独立 include，避免 auto-profile 里的 `hwdec` 行污染 EXE 对 mpv.conf 下拉值的反推。
- `Models/SettingsDraft.cs`：默认硬解改 `d3d11va`（`HwdecIndex=3`，`LoadFrom` 缺键兜底同步），标签改「auto-copy（杜比视界兼容）」/「d3d11va（推荐·零拷贝）」。
- `Views/VideoView.xaml`：「色调增强档位」说明去掉杜比视界 Profile4，改为「支持杜比视界Profile5、7、8」。
- `docs/功能说明.md` 同步硬解默认值说明。
- `dotnet publish` 后把新 EXE 部署到 `<安装根>\MediaPlayer\mpv\MpvSettings.exe`（发布目录与生产目录 SHA256 一致）。

### 9.3 复测

- 8K VP9：改后 `HW=d3d11va`、`DROP=2`、`EST≈59.94`，流畅。
- DV P5 / DV P8：`Applying auto profile: dolby-vision-copy`、`Using hardware decoding (d3d11va-copy)`，回退生效。
- HDR10 4K（`Sony Bravia OLED 4K Demo.mp4`）：`HW=d3d11va`、`GAMMA=pq`，无异常。

### 9.4 定为出厂默认并纳入 EXE 管理

用户拍板「把现在的值重新定义成默认值」，按完整出厂态处理，不只是改一行 `hwdec`：

- `Models/SettingsDraft.cs`：`HwdecIndex` 出厂 3（`d3d11va`）、`LoadFrom` 缺键兜底 `d3d11va`；标签改成「d3d11va（推荐·零拷贝）」/「auto-copy（杜比视界兼容）」。
- `Assets/dolby-vision.conf` 新增并作为内嵌资源（`LogicalName=MpvSettings.dolby-vision.conf`）：恢复默认 / 导入都从 EXE 重写，避免只改 `hwdec` 把 DV P5 搞黑屏。
- `Services/ConfigLocator.cs` 加 `DolbyVisionConf`；`Services/BackupService.cs` 受管文件 11 → 12，备份/迁移带上兜底文件。
- `Services/DefaultProfile.cs`：`EnsureFixedAssets` 增加 dolby 整文件重写 + `include=~~/dolby-vision.conf` 只补缺（照 cover-art 的规则）。
- 验证基准 `_verify/stage2_verify.py` 同步：baseline `HwdecIndex=3`、变异值改 `d3d11va-copy`（`auto-copy` 已在预设内）、自定义索引 6；顺带修两条历史遗留断言（con=6 才是 0.8 档；`mode=tonemap` 时色调档位按设计强制归 0）。
- **三处 EXE 同版本**：`publish/`、`<安装根>\MediaPlayer\mpv\`、`12.mpv\` MD5 均 `616aba3bf5da7bf8b1e3e002d50da9ed`。
- **模板同步**：`12.mpv\portable_config\mpv.conf` 写 `hwdec=d3d11va` + dolby include，新增 `dolby-vision.conf`；字幕字体保留跨机器静态默认（微软雅黑/48），由 `--default` 在本机探测后升级成 STKaiti/52，与生产态一致。
- 复验：`stage2_verify.py` 94 项全部通过；`--default --dry-run` 在生产目录只提示 cover-art.conf 整文件重写（版本历史遗留），不再产生任何硬解键改动。

# MpvSettings 1.0.4 改动记录

> 1.0.3 → 1.0.4。四项改进：托盘联动、无配置诊断按钮、播放统计快捷键、声道输出问题查证。
> 前次基线见 `MpvSettings_1.0.2_后续修改记录.md`（1.0.2 / 1.0.3）。
>
> **2026-11-21 14:2x 追加（版本号仍 1.0.4）**：窗口最小尺寸失效修复——`MinWidth/MinHeight`
> 一直被 WM_GETMINMAXINFO 接管逻辑跳过，实测可拖到 0；下限按内容实测确认为 **880 × 540**。
> 详见第六节。
>
> **2026-11-21 14:5x 追加（版本号仍 1.0.4）**：RTX VSR 回归修复——凌晨那次崩溃防抖重构让
> `apply_rtx` 被 30 Hz 的 `video-target-params` 通知饿死，Alt+4 放大窗口不再挂 VSR。
> 与最小尺寸补丁无关。详见第七节。

## 一、功能改动

### 1. 「打开 MPV 播放器」同时拉起 BassMax 托盘（对齐 mplayerl.exe）

- 参考 `mplayerl\native64\src\mplayerl.c` 的 `StartBassMaxTray()`：播放器启动时先
  `ShellExecute <dir>\Codecs\bassmax_tray.exe --follow "<播放器 exe 名>"`，
  托盘跟随播放器生命周期，播放器退出后 2.5s 自退；**找不到托盘就静默跳过**，不弹任何提示。
- 落点：
  - `Services/ConfigLocator.cs` 新增 `BassMaxTray` 属性，候选顺序
    `<InstallRoot>\Codecs` → `<InstallRoot>\00.Core` → `<Root>\Codecs` → `<Root>\12.mpv`
    → `<Root>\` → `<ConfigDir 父目录>\` → `<ConfigDir>\`，取第一个存在的文件。
    生产安装命中 `<安装根>\Codecs\bassmax_tray.exe`，
    开发包命中 `<项目根>/12.mpv/bassmax_tray.exe`。
  - `MainWindow.xaml.cs` 新增 `StartBassMaxTray()`，在 `RestartMpv()` 启动 mpv **之前**调用
    （图标先出现；托盘自身有 30s 等播放器的宽限），带 `--follow "mpv.exe"`。
    缺失 / 启动异常全部静默吞掉，与 `OpenPath` / `OpenBassMaxPanel` 一致。
- **重复运行的结论：只会显示一个托盘图标。** 两条独立证据：
  1. 源码 `bassmax\bassmax_tray\bassmax_tray.cpp:326` 用
     `CreateMutexW(NULL, FALSE, L"Local\\BassMaxTraySingleton")`，
     `GetLastError()==ERROR_ALREADY_EXISTS` 直接 `return 0`，不弹窗、不建第二个图标。
  2. 真机连跑两次 `bassmax_tray.exe`，`tasklist` 里始终只有一个 PID。
  单实例是按窗口站（`Local\`）计的，同一桌面下重复点击「打开 MPV 播放器」不会叠图标。

### 2. 截图页「动作」卡片新增「打开无配置 MPV」按钮（诊断用）

- 落点：`Views/ScreenshotView.xaml`（动作卡片顶部新 OptionRow）+ `ScreenshotView.xaml.cs`
  `OpenNoConfig_Click` → `MainWindow.OpenMpvNoConfig()`。
- 行为：`mpv.exe --no-config --idle=yes --force-window=yes`。
  `--no-config` 屏蔽全部配置（portable_config / input.conf / 脚本），
  不传 `--config-dir`；**不关现有 mpv**（与「打开 MPV 播放器」相反），
  因此可以开着正常窗口对照，也可以连点开多个做 A/B。
- 用途：画面 / 声音异常时先点它。干净窗口正常 → 配置写错；干净窗口也异常 → 程序或环境问题。

### 3. 快捷键：播放统计 Tab + Ctrl+j

- 查证 mpv 0.42 原始默认（`_research/input_default_master.conf:103-105` 与
  `MPV快捷键_默认绑定全表.md:217-219`）：
  - `i` → `script-binding stats/display-stats`（临时显示）
  - `I` → `script-binding stats/display-stats-toggle`（常显切换）
  - `?` → `script-binding stats/display-page-4-toggle`（键位表）
  **有默认键**。用户拍板：保留 mpv 弱绑定 i/I，另加两条本包发行强绑定（不同命令，互不覆盖）：
  - `TAB` → `script-binding stats/display-stats`（同 PotPlayer，临时）
  - `Ctrl+j` → `script-binding stats/display-stats-toggle`（同 MPC-HC / MPC-BE，常显切换）
- 落点：
  - `Models/ShortcutCatalog.cs`：`e2.stats` 改为 TAB 发行键（原 `i` 是 mpv 弱绑定，仍可用），
    新增 `e2.stats-toggle` = Ctrl+j 发行键；清单 38 → 39 行。
  - 两处 `input.conf`（生产 + `12.mpv` 模板）末尾追加两行，保证非设置程序启动也生效。
  - `_verify/stage6_verify.py`：行数 38→39、自定义行 11→13，新增两条键断言。
- 真机 `input-bindings` 断言：两条均在 priority=15 生效；`TAB` 未被系统 / 浏览器抢走时正常。

### 4. 声道输出：auto / auto-safe 都变立体声的根因与可用方案（只查证，未改默认值）

**结论：不是 mpv 把 `auto` 写错，也不是 BassMax 滤镜下混，是 WASAPI 共享模式的系统混音器决定声道数。**

**为什么其他播放器不受「选择播放声音的位置」影响**（2026-11-20 二轮补查）：
MPC-HC / MPC-BE / PotPlayer 用**自带音频渲染器**直接和端点协商格式，绕过 Windows 系统混音器。
本机注册表证据：
- `HKCU\Software\MPC-HC\MPC-HC\Internal Filters\Audio Renderer` → `IgnoreSystemChannelMixer=1`、
  `AllowBitstreaming=1`
- `HKCU\Software\MPC-BE\Filters\MPC Audio Renderer` → `UseBitExactOutput=1`、
  `UseSystemLayoutChannels=1`
- mpv 的 WASAPI AO 没有等价开关，共享模式只能读系统给的格式。

四个输出端点的 Windows 默认格式**全部是 2ch / 48kHz**（注册表
`MMDevices\Audio\Render\{guid}\Properties\{f19f064d-...},0`，解析出 `nChannels=2`、
`dwChannelMask=0x3`）：NVIDIA HDMI `50f8a72d`、Realtek 扬声器 `221e7dbe`、
Realtek 光纤 `21dbfed8`、VB-CABLE `bf6a610a`。
驱动声明的可用格式里，NVIDIA HDMI **只有 1ch 和 2ch**（无 5.1），
Realtek USB 扬声器则支持 2/4/6/8ch。所以切到 HDMI 耳机时 mpv 必然立体声，
这是驱动能力上限，改 mpv 配置救不了。

证据链（本机 mpv v0.42.0-94 + RTX 5070 + 2026-11-20 实测，5.1 测试片
`<测试片名>.mkv`，源 6ch eac3，探针 `_verify/audiotest/chan_probe.lua`）：

| 配置 | AO | 输出 | 判定 |
|---|---|---|---|
| `audio-channels=auto`，共享，默认设备 | wasapi | 48000Hz **stereo 2ch** | 下混 |
| `audio-channels=auto-safe`，共享，默认设备 | wasapi | stereo 2ch | 下混 |
| `audio-channels=5.1,stereo`，共享 | wasapi | stereo 2ch | 仍下混（设备不接受 5.1） |
| `audio-channels=5.1`（字面值），共享 | wasapi | stereo 2ch | 仍下混 |
| `audio-channels=stereo` | wasapi | stereo 2ch | 强制下混 |
| `audio-exclusive=yes` + `auto`，Realtek USB 扬声器 | wasapi | stereo 2ch | **更正：auto 仍降到 2ch** |
| **`audio-exclusive=yes` + `5.1`，Realtek USB 扬声器** | wasapi | **48000Hz 5.1 6ch s32** | **唯一可用的原始声道** |
| `audio-exclusive=yes`，默认设备（NVIDIA HDMI） | — | `AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED` | 驱动拒绝独占 |
| `audio-exclusive=yes` + 显式 5.1，NVIDIA HDMI / 光纤 | — | 初始化失败 | 同上 |
| `audio-exclusive=yes` + auto-safe，Realtek USB | wasapi | stereo 2ch | auto-safe 仍按系统布局 |
| `audio-spdif=eac3,ac3,dts` 位流直通（光纤端点，共享/独占） | — | AO 初始化失败 | 该链路在本机不可用 |

- **重要更正**：上一版记录第 69 行写「独占 + `auto` = 6ch」是漏了前提的错误结论。
  二轮复测确认：独占下必须**显式 `audio-channels=5.1`** 才是 6ch s32；
  用 `auto` 仍回落到 2ch。原因同系统默认格式：`auto` 跟随端点首选布局（2ch）。
- `af=bassmax` 不是元凶：现版 `af_bassmax.c` 只 `mp_autoconvert_add_afmt(AF_FORMAT_FLOAT)`，
  **不再 `add_chmap(STEREO)`**（旧备份 `_bak_20261116/af_bassmax.c:217` 才有），
  声道布局原样透传；生产配置 + 独占下实测 6ch 输出正常。
- **可用的原始声道方案**（唯一）：`audio-device=wasapi/{221e7dbe-...}`（Realtek USB 扬声器）
  + `audio-exclusive=yes` + `audio-channels=5.1`。
  独占会独占设备（其它程序暂时无声），属正常取舍。
- **本机实际听音设备是 HDMI 耳机与 2.0 音箱，都是立体声，原始多声道对用户无实际意义**，
  故**未改任何默认配置**：`mpv.conf` 仍是 `audio-channels=auto`，没有 `audio-device` /
  `audio-exclusive`。设置程序下拉仍提供 `auto / auto-safe / stereo / mono` 四项，不扩表。
- **界面说明**（用户要求）：`Views/AudioView.xaml`「输出与逃生口」卡片在「声道输出」下方
  新增两条只读说明行（IsNote），用通用措辞、不指向具体机器：
  - 「为什么是立体声」/ 标记「系统限制」——讲 clear 系统格式锁定 + 其他播放器自带渲染器的差别。
  - 「需要原始声道时」/ 标记「按需设置」——讲 NVIDIA HDMI 不支持独占、需多声道端点 + 独占 + 显式 5.1、
    独占会静音其它程序、立体声设备无需改动。
  验证：`_verify/stage104_audio_note_probe.py`（标记文字 UIA 可见），
  完整 UI 回归 `_verify/stage104_ui_probe.py` 16/16 通过。

## 二、版本号与文件改动

版本 1.0.3 → 1.0.4，共 2 处：`MpvSettings.csproj`（Version / AssemblyVersion / FileVersion /
InformationalVersion）、`Views/AboutView.xaml`。标题栏 `· v1.0.4` 由 `MainWindow.xaml.cs`
读程序集 `InformationalVersion` 自动显示。

**源码**
- `Services/ConfigLocator.cs`：`BassMaxTray` 属性。
- `MainWindow.xaml.cs`：`StartBassMaxTray()`、`OpenMpvNoConfig()`、`RestartMpv()` 插入调用。
- `Views/ScreenshotView.xaml` / `.xaml.cs`：动作卡片新按钮与事件转发。
- `Models/ShortcutCatalog.cs`：39 行清单、`e2.stats` 改 TAB、新增 `e2.stats-toggle`。
- `MpvSettings.csproj` / `Views/AboutView.xaml`：版本号。
- `Views/VideoView.xaml`（1.0.4 发布后追加，版本号不变）：「NVIDIA RTX 视频增强」卡片末尾
  新增一条只读说明行（IsNote）「已知问题 · 等上游修（2026-11-21）」，把 mpv 偶发崩溃的
  根因与结论写进程序界面：d3d11vpp 的 nvidia-true-hdr 动态挂载空指针、hint=yes/auto/no
  无差别、RTX HDR 保留接受偶发、约 2 秒延迟是等色彩协商、MPCVR 内建无此延迟，
  指向排查文档的路径按用户要求未写进界面（MD 不打包，写了也没用）；
  措辞与三处 `MyMPC_ui.lua` 注释块一致。
- `NoticeWindow.xaml.cs` / `MainWindow.xaml.cs`（1.0.4 发布后追加，版本号不变）：修复清理缓存
  确认框按钮误写「继续导入」的问题。`NoticeWindow.Confirm` 新增可选参数 `okLabel`
  （默认「继续导入」，导入场景不变）；清理缓存调用显式传 `okLabel: "确认删除"`。
  用户 2026-11-21 01:50 截图指出。

**配置 / 脚本（模板 + 生产）**
- `12.mpv\portable_config\input.conf`、`<MPV配置目录>\input.conf`：
  末尾追加 TAB / Ctrl+j 两行。

**验证脚本**
- `_verify/stage6_verify.py`：行数 / 自定义行数 / 新增两条断言。
- `_verify/stage104_ui_probe.py`（新增）：真机 UIA 点按钮验证托盘联动与无配置启动。
- `_verify/stage104_audio_note_probe.py`（新增）：音频页声道说明标记 UIA 可见性验证。

**发布产物**
- `publish\MpvSettings.exe`：**1.0.4**，464,598 B，md5 `fa96f8f82d12a681884e018482c2dbe9`
  （= 1.0.4 原版 + RTX 已知问题界面说明 + 清理缓存按钮文案修复；2026-11-21 01:52 重编，版本号不变）。
  - 中间版：`3cb8c011308348381772f5f00531eb86`（说明无 MD 路径，但清理缓存按钮仍误写「继续导入」，已修）。
  - 中间版：`db1aca47821aa82c2eaee36a83f6cf59`（含说明但末尾写了 MD 路径，已按用户要求去掉）。
  - 1.0.4 原版：462,550 B / `a0e9b8b8cbf25059a22298b7a3a4c901`
    （含二轮补入的音频页声道说明；更早的 462,038 B / `bb1e51e5…` 已被覆盖）。
- `12.mpv\MpvSettings.exe`、生产 `<MPV安装目录>\MpvSettings.exe`：
  三处逐字节一致，md5 同为 `fa96f8f82d12a681884e018482c2dbe9`。
  - ⚠ 本条已被 **2026-11-21 14:27 的重编取代**（最小尺寸修复，版本号不变）：
    三处 md5 现为 `091d184e6135f4e4cedfc7443d61a485`（465,110 B），见第六节。
  - ⚠ 现以 **第七节** 为准：RTX VSR 回归修复后三处 md5 是
    `9d7f471674f92d57b42dd4d13766aa9f`（468,694 B，14:43 重编）。
- 旧 EXE 备份：
  - `<备份目录>/`（1.0.4 首次发布前的 1.0.3 / `5751bd52…`）。
  - `<备份目录>/`（含 MD 路径的中间版 `db1aca47…`，三份）。
  - `<备份目录>/`（中间版 `db1aca47…` 三份）。
  - `<备份目录>/`（中间版 `3cb8c011…` 三份）。
- 源码 / 配置备份：`<备份目录>/`。

## 三、验证

- `dotnet build -c Release` / `dotnet publish`：0 警告 0 错误。
- `_verify/stage6_verify.py`：13 节全过，新增 1.9c / 1.9d 断言通过，生产目录哈希未变。
- `_verify/stage104_ui_probe.py`：**16/16 全过**——
  旧 mpv PID 消失、新 mpv 唯一且带 `--config-dir`、托盘启动且只有一个、
  无配置 mpv 带 `--no-config` 不带 `--config-dir`、连点两个无配置 mpv 共存、托盘始终一个。
- mpv 运行时 `input-bindings`：`TAB` 与 `Ctrl+j` 均为 priority=15 强绑定。
- EXE 元数据：FileVersion `1.0.4.0` / ProductVersion `1.0.4`。

## 四、使用说明 / 边界

- 托盘联动只在「打开 MPV 播放器」按钮触发；双击视频文件走 mplayerl.exe 时托盘本来就由它拉起，
  两条路径都是单实例互斥，不会叠图标。
- 托盘 exe 缺失时按钮照常只开 mpv，没有任何提示——这是对齐 mplayerl 的有意设计。
- 「打开无配置 MPV」不会关现有窗口，排查完直接关掉那个窗口即可；它不写任何配置。
- 播放统计两条键与 mpv 原生 i/I 并存：Tab/I 临时、Ctrl+j/I 常显，按哪个都行；
  改键请到「快捷键」页，本包发行态就是强绑定（state=自定义）。
- 多声道原始输出需要手动改 `mpv.conf`（`audio-device` + `audio-exclusive=yes`），
  且只对本机 Realtek USB 端点实测有效；换 HDMI / 光纤端点需重新验证。

## 五、备份放置约定（项目长期规则）

**备份文件不得放进打包目录**——即 `<项目根>` 下所有数字开头的文件夹：

```
00.Core   01.ico   02.AVS+   03.MediaInfo   04.MPC-HC   05.XySubFilter
06.LAV    07.MPCVR 08.PotPlayer64   09.madVR   10.MPC-BE   11.FrankyRenderer   12.mpv
```

这些目录会被整个打进发行包，备份混进去会污染成品、增大体积、带入过期文件。
备份一律放到上述目录之外的独立位置（如 `<备份目录>/*`）。

本次 1.0.4 的备份已按此约定放在数字目录之外：
`_bak_104_20261120_215847\`、`_bak_exe_104_20261120_220507\`、`_bak_audio_note_20261120_222759\`。

## 六、2026-11-21 14:2x 补丁：窗口最小尺寸失效（版本号仍为 1.0.4）

### 1. 现象

用户截图（`PixPin_2026-11-21_14-19-08/18.png`）：窗口能被拖到 620 DIP 出头，
顶部「配置：mpv.conf · audio-info.conf」标签被裁成 `mpv.c…`，
标题下的提示文字只剩半句，右侧按钮组顶到窗口外。

`MainWindow.xaml` 里**本来就有** `MinWidth="880" MinHeight="540"`——值不是没写，是没生效。

### 2. 根因：自己接管了 WM_GETMINMAXINFO，把 WPF 的最小尺寸换算整段跳过

`MainWindow.xaml.cs` 的 `WndProc` 从 1.0.2 起就为「最大化别盖住任务栏」接管了
`WM_GETMINMAXINFO`（见 XAML 注释与 `ApplyWorkAreaBounds`），处理完把 `handled` 置 `true`。
hook 在 WPF 自己的窗口过程**之前**执行，置 `true` 之后 WPF 对这条消息的处理完全被跳过，
而 `MinWidth / MinHeight → ptMinTrackSize` 的换算正是 WPF 在这条消息里做的。
结果 `ptMinTrackSize` 从来没被写过，窗口上限只剩系统给的默认值（≈0），可以拖到任意小。

**硬证据**（`_verify/minmax_probe.ps1`：对窗口发 `WM_GETMINMAXINFO` 再读回结构体，
本机 150% 缩放，DPI 144）：

| EXE | ptMinTrackSize | 换算 DIP |
| --- | --- | --- |
| 修复前 `publish\MpvSettings.exe`（`fa96f8f8…`） | **0 × 0 px** | 0 × 0 |
| 修复后 `publish\MpvSettings.exe`（`091d184e…`） | **1320 × 810 px** | 880 × 540 |

`maxTrackSize` 两次都是 3840 × 2100 px（= 4K 屏工作区），说明工作区钳制没被这次改动影响。

### 3. 改法

**`MainWindow.xaml.cs`**

- `ApplyWorkAreaBounds()` 由 `static` 改为实例方法，末尾补上最小尺寸换算：

  ```csharp
  double scale = CurrentDpiScale(hwnd);
  int minW = (int)Math.Round((double.IsNaN(MinWidth) ? 0 : MinWidth) * scale);
  int minH = (int)Math.Round((double.IsNaN(MinHeight) ? 0 : MinHeight) * scale);
  if (mmi.MinTrackSize.X < minW) mmi.MinTrackSize.X = minW;
  if (mmi.MinTrackSize.Y < minH) mmi.MinTrackSize.Y = minH;
  ```

  - `MinWidth / MinHeight` 是 DIP，`ptMinTrackSize` 是**物理像素**，按当前窗口 DPI 换算
    （新增 `GetDpiForWindow` P/Invoke + `CurrentDpiScale()`，取不到时回退
    `VisualTreeHelper.GetDpi`）。
  - 每次 `WM_GETMINMAXINFO` 都重算：跨显示器拖动、改系统缩放到 125% / 200% 都跟着走。
  - 顺带兜一条：工作区比下限还小时（1280×720 + 150% 这类），保证
    `MaxTrackSize` 不小于 `MinTrackSize`，避免系统拿到自相矛盾的两个值。
- 新增验证开关 `--shot-size=宽x高`（仅配合已有的 `--shot=` 使用）。
  以前要验证某个尺寸的排版只能手拖窗口，现在一条命令按指定尺寸渲染 PNG。
  小于 `MinWidth / MinHeight` 时 WPF 会自己夹回去，正好也是「下限生效」的断言。

**`MainWindow.xaml`**

- `MinWidth="880" MinHeight="540"` **数值不变**，只补注释说明：这两个数字的出处、
  必须在代码里补换算、改这里即生效。避免下一个人看着 XAML 以为已经管住了。

### 4. 880 × 540 这个值怎么来的

顶部工具行右栏是 `Auto` 列，宽度由内容决定且不能压缩：

| 元素 | 宽度（DIP） |
| --- | --- |
| 配置标签 Chip（`配置：mpv.conf · audio-info.conf`，11px + padding 8/3 + 边框） | ≈ 158 |
| 搜索框（固定 `Width="150"`） | 150 |
| 「全部重置」「重置页面」（`MinWidth="78"` × 2） | 156 |
| 各处间距（10 + 8 + 8） | 26 |
| **小计** | **≈ 490** |

再加导航条 56、页面 padding 24×2、`ScrollViewer` 右侧 8（拉伸命中区留白，见 XAML 注释），
以及标题 / 提示文字至少留 220 才不至于只剩一个字，**内容硬下界 ≈ 820**。

实测（临时把 `MinWidth/MinHeight` 降到 700×400 重编，只测不改发布件）：

- `820x500`：各控件仍完整，但页面提示被截到「…信息窗、Alt+4…」；
- `760x480`：标题区已被挤扁，页脚「配置源」路径截成 `…12.mpv\portal`。

所以取 **880 × 540**（比内容下界多 60 × 40 余量），**不改原来的数值**：
本次真正缺的是生效，不是数值。

逐页渲染确认（`_verify/min/shot880_*.png`，150% 缩放下 1320 × 810 px）：
播放 / 截图 / 快捷键 / 音频 / 关于 五页在 880 × 540 下均无裁切、无重叠，
卡片右侧的下拉框、开关、键位框都完整。

### 5. 验证

- `dotnet build -c Release` / `dotnet publish`：0 警告 0 错误。
- `_verify/minmax_probe.ps1`（新增）：修复前后对比见上表；发布版复测 1320 × 810 px。
- 端到端夹回：`MpvSettings.exe --shot=… --shot-size=640x400`（修复后发布版）→
  渲染结果的实际尺寸是 `actual=880x540 dpi=1.5`，即请求 640 × 400 被夹到下限。
- 五页最小尺寸截图：`_verify/min/shot880_{播放,截图,快捷键,音频,关于}.png`（另有 900×600 对照）。

### 6. 产物

- `publish\MpvSettings.exe`：**465,110 B**，md5 `091d184e6135f4e4cedfc7443d61a485`
  （1.0.4 + 本节最小尺寸修复，版本号不变，2026-11-21 14:27 重编）。
- `12.mpv\MpvSettings.exe`、生产 `<MPV安装目录>\MpvSettings.exe`：
  已同步，三处 md5 同为 `091d184e6135f4e4cedfc7443d61a485`。
  （⚠ 已被第七节的 RTX VSR 修复版 `9d7f4716…` / 468,694 B 取代。）
- 旧 EXE 备份：`<备份目录>/MpvSettings.exe`
  （修复前 `fa96f8f82d12a681884e018482c2dbe9`，464,598 B）。

### 7. 边界

- 下限是 DIP，不随 DPI 变：同一块 4K 屏 150% 下占 880 DIP，100% 下还是 880 DIP，
  物理像素分别是 1320 / 880。
- 1280 × 720 + 150% 缩放的机器可用 DIP 只有约 853 × 480，低于 880 × 540 的下限，
  窗口会略大于屏幕（可以最大化，但拖不出更小）。这是「按内容定下限」的必然取舍，
  真遇到这种机器再把下限调到 820 × 500，改动只在 `MainWindow.xaml` 两个数字上。
- 最大化仍由同一段代码钳在工作区内，本次没动那部分逻辑；`MaxTrackSize` 也仍等于工作区。

## 七、2026-11-21 14:5x 补丁：RTX VSR 回归（防抖把 apply_rtx 饿死）

### 1. 现象

用户报「RTX VSR 无效，测试片 `Simon & Garfunkel - The Sound of Silence (1982).mp4`（640×480）
按 Alt+4 放大窗口也不激活」，并怀疑是上一节（窗口最小尺寸）的回归。

**和最小尺寸补丁无关**——那节只动了 WPF 的 `WM_GETMINMAXINFO` 钩子，一行都没碰播放链路。
真正的回归来自 **2026-11-21 01:43 的「偶发崩溃」防抖重构**（就是同一天凌晨那次）。

### 2. 根因：单一防抖 timer 被 30 Hz 的属性通知饿死

崩溃修复把「每个触发源各排各的 timer」改成「所有触发源共用一个 timer，后一次 kill 前一次」。
问题出在 `video-target-params` 这个触发源上：

- `mpv.conf` 出厂就是 `hdr-compute-peak=yes`，所以 `video-target-params` 里的 `max-luma`
  **每帧都在变**；实测 6 s 内 **180 次通知（≈30 Hz）**，而 `gamma` / `primaries`
  全程稳定在 `pq` / `bt.2020`（HDR 桌面）。
- 30 Hz ≫ 0.15 s 的防抖延迟 ⇒ 每次通知都把 timer kill 掉重排 ⇒ **0.15 s 永远走不到**，
  `apply_rtx()` 实际只在开场跑过一两次，之后再不执行。
- 于是滤镜永远停在开场那次判定（`zoom=1` → 只挂 `nvidia-true-hdr`，不挂 VSR），
  Alt+4 / Alt+3 / 拖窗口都不会触发重算，**VSR 永远挂不上**。
- 重构前每个观察器各自 `add_timeout`，window-scale 那一路照样能跑，所以 1.0.2 时代正常。

**实测证据**（带调试输出的 lua 副本，生产配置，同一测试片）：

| 版本 | 6 s 内 schedule 次数 | apply_rtx 执行次数 | Alt+4 后 vf |
| --- | --- | --- | --- |
| 修复前（生产 lua `545dc28b…`） | **314** | **2**（都在开场） | 无变化，仍只有 `nvidia-true-hdr` |
| 修复后 | 正常（每轮 2 次左右） | 按需执行 | 加上 `scaling-mode=nvidia:scale=1.001` |

旁证：`_verify/obs_probe.lua` 证明 `window-scale` 观察器本身回调正常（1 → 4.05 → 2），
所以不是观察器没注册，是排了队永远轮不到执行。重复通知里唯一变化的字段是 `max-luma`
（`gamma`/`primaries` 不变），这也是下面「按判据过滤」的依据。

### 3. 改法（`scripts/MyMPC_ui.lua`，三处同步）

1. **触发源侧过滤**：`video-target-params` 只在 `gamma` / `primaries` 变化时才排程，
   其余（每帧刷新的 `max-luma`）直接丢弃。首次 `nil` 通知照常排，用来拿初始判据。
2. **防抖加最大等待** `RTX_DEBOUNCE_MAX = 0.6`：连续刷屏时满 0.6 s 不再重置，
   让已排的 timer 照常触发，保证任何来源刷屏都不会再饿死 `apply_rtx`。
3. `end-file` 里顺带清 `rtx_wait_from`，换片不残留计时状态。

两个改动都保留了崩溃修复的初衷：仍然只有一个 pending timer，仍不与 VO 重建抢时序。

### 4. 顺手修掉的一个隐患：`Assets/MyMPC_ui.lua` 是旧的

- 生产 / `12.mpv` 的 lua 是 `545dc28b…`（含 01:43 防抖修复），
  而 EXE 内嵌源 `MpvSettings/Assets/MyMPC_ui.lua` 还是 `cbfbfd51…`（**没有**防抖修复）。
- 后果：只要用户点一次「确定 / 应用」并触发脚本重写（`uiChanged` 命中任一 rtx / 单实例 /
  无封面开关），EXE 就会把**旧版**刷回盘上，崩溃修复当场失效。
- 本次已把三份 lua 全部同步成同一内容（含本节修复），并重建 EXE。

**内嵌资源验证**：把**旧** lua 放进临时配置目录 → 用新 EXE 改一项脚本开关（`SubAutoReload`）
强制重写 → 写出的文件含 `RTX_DEBOUNCE_MAX` / `rtx_target_key`（729 行），
证明发布件里嵌的就是修复版，不是旧版。

### 5. 验证

- 单次路径（`_verify/rtx_vsr_driver.lua`，生产中真实按键）：
  - 开场 100%：`vf=@MyMPC-rtx:d3d11vpp=nvidia-true-hdr=,…`，日志 `[MyMPC/rtx] RTX-HDR (640x480)`；
  - **Alt+4 之后**：`vf=@MyMPC-rtx:d3d11vpp=scaling-mode=nvidia:scale=1.001:nvidia-true-hdr=,…`，
    日志 `[MyMPC/rtx] VSR RTX-HDR (640x480)` + `NVIDIA RTX Super Resolution enabled.`；
  - Alt+2 回 100%：VSR 摘掉，只剩 HDR（符合 MPCVR 策略：100% 不挂）。
- 压力（`_verify/rtx_vsr_stress.lua`，8 轮 Alt+4 / Alt+2）：
  **8/8 轮全部正确**（放大挂上、回 100% 摘下），`NVIDIA RTX Super Resolution enabled.` 恰好 8 次，
  `apply_rtx` 共 17 次（对应的正是 8 轮 × 两次意图，不再有 30 Hz 空转），
  无脚本报错、无崩溃、进程正常退出（exit 0）。
- 探针脚本：`_verify/rtx_vsr_probe.py`（IPC 版）、`_verify/rtx_vsr_driver.lua`（mpv 内部驱动）、
  `_verify/obs_probe.lua`（观察器连通性）、`_verify/rtx_vsr_stress.lua`（压力）。

### 6. 产物

- `publish\MpvSettings.exe`：**468,694 B**，md5 `9d7f471674f92d57b42dd4d13766aa9f`
  （1.0.4 + 最小尺寸修复 + 内嵌 lua 更新，版本号不变，2026-11-21 14:43 重编）。
- `12.mpv\MpvSettings.exe`、生产 `<MPV安装目录>\MpvSettings.exe`：
  三处 md5 同为 `9d7f471674f92d57b42dd4d13766aa9f`。
- `scripts\MyMPC_ui.lua`：生产 / `12.mpv` / `Assets` 三份同为 `fcf65b05631a2e0e7f54a88573a83944`
  （LF、729 行；EXE 写出时统一转 CRLF，属正常）。
- 旧 EXE 备份：`<备份目录>/`（`091d184e…`，465,110 B）。
- 旧 lua 备份：`<备份目录>/`（3 份，含旧的 Assets 版）。

### 7. 边界 / 下次注意

- `hdr-compute-peak=yes` 是出厂配置，`video-target-params` 天然每帧脏。
  **任何「每帧会变」的属性都不能直接当防抖触发源**，必须按判据字段过滤，或给防抖留最大等待。
- 只开 VSR、关掉 RTX HDR 时，`video-target-params` 照样在刷，所以这个过滤不能只在 HDR 分支做。
- 本次没有改 `rtx_vsr` / `rtx_hdr` 的判定逻辑与阈值（阈值仍照抄 MPCVR），
  只改了「什么时候重新判」。VSR 设计与阈值历史见 `MpvSettings_1.0.2_后续修改记录.md`。
- 崩溃（`mpv.exe+0x248CE63` 空指针）是 d3d11vpp 的 `nvidia-true-hdr` 上游问题，
  本次改动减少了无意义的重复判定，但没有、也不可能修掉它；仍按 1.0.2 那节的结论「接受偶发」。

## 八、2026-11-21 17:2x 改动：右键菜单「打开剪贴板」改走 `Assoc.exe --openfile`

> ⚠ 本节的「剪贴板判定」与「装机必须替换绝对路径」两条，已被**第九节**取代：
> 菜单项改名「打开本地文件」，`--openfile` 不再读剪贴板，路径由 `MpvSettings` 自检修正。
> 其余内容（`run` 不展开 `~~/`、`Assoc.exe` 不调 `mplayerl.exe`、AHK v1 没有
> `A_LocalAppData`、`local` 编译报错）仍然有效。

### 1. 现象

`portable_config\menu.conf` 原来第一行是：

```
打开剪贴板	update-clipboard text; loadfile ${clipboard/text}; show-text "+ ${clipboard/text}"
```

剪贴板里不是「能直接 loadfile 的东西」时（复制的是一段文字、复制的是文件本身而不是路径、路径已失效），
mpv 只会静默失败或闪一行 OSD，没有任何补救入口。

### 2. 两条候选方案的结论

选 **方案 1（`--openfile` 直接弹选择框）**。依据是三条实测事实，不是偏好：

| | 方案 1 `--openfile` | 方案 2 `--checkClipboard` |
| --- | --- | --- |
| 剪贴板无效时 | 弹框选文件 | 弹框选文件 + **写回剪贴板** |
| 播放方式 | 起播放器进程 | 再走 `loadfile ${clipboard/text}` |
| 用户原本复制的内容 | 不动 | 被选中的文件路径**覆盖** |
| 先后顺序 | 不需要 | 需要「Assoc 先跑完，mpv 再读剪贴板」 |

否决点在顺序：

- `menu.conf` 没有条件分支，也无法等待。唯一能串行的手段是 `run wait`。
  实测本机 mpv：`run wait` 里跑 3 秒的 `timeout`，IPC 回包 **0.00 s** 就返回——**wait 不生效**。
  方案 2 的「设完剪贴板再 loadfile」不成立，只会读到旧内容。
- 退一步改成让核心真阻塞，就是把播放画面冻在对话框后面，比闪一下更难看。
- 方案 1 担心的「会重新开一个进程」是伪问题：`MyMPC_ui.lua` 的 `single_instance=yes`
  会把新实例命令行上的文件通过命名管道转发给已运行实例，0.15 s 后自己退出，
  与双击媒体文件同一条路。

「新开进程」的代价是 0.15 s 握手，「写剪贴板」的代价是把用户剪贴板吃掉。后者不可接受。

### 3. 改法

**`00.Core\Assoc.ahk`**（新增 CLI 模式，GUI 部分未动）

- `--openfile` 的解析放在 **UAC 提权之前**。原脚本无条件 `Run *RunAs`，
  照原样加参数的话，从播放器点一次菜单就弹一次 UAC。
- `ClipCandidate()`：读剪贴板 → 去空白、去「复制为路径」带的引号 → 只认单行 →
  `http/https/rtsp/rtmp/mms/ftp` 直接放行 → 否则必须是存在的文件、扩展名在 `extMap` 里
  且分类为 `media`（字幕 `text` / 图形字幕 `picture` / 工程文件全部排除）。
- `MediaFilter()`：用分类为 `media` 的扩展名拼 `*.ext;*.ext;…` 过滤器，
  选择框里不会混进 srt/avs 这类不能当媒体打开的东西。
- `CliOpenFile()`：剪贴板命中就直接播；没命中才弹 `FileSelectFile`，
  并把目录记到 `%LOCALAPPDATA%\MyMPC\assoc.ini`（下次从这里开始）。
- `LaunchPlayer()`：**不调 `mplayerl.exe`**。它会按 `mplayerl.ini` 的 `MainPlayer`
  去拉「主播放器」，用户在 mpv 里点一下却弹出 PotPlayer/MPC-HC 是错的。
  改为复刻它的启动序列：`bassmax_tray.exe --follow "mpv.exe"`（已在跑就跳过）→
  `MediaPlayer\mpv\mpv.exe "文件"`。

**`menu.conf`**（生产 + `12.mpv` 两处同步）

```
打开剪贴板	run "<安装根>/Assoc.exe" --openfile
```

### 4. 踩到的三个坑

1. **menu.conf 的命令串解析把 `\` 当转义符**。写成 `run "C:\Users\..."` 时 mpv 日志报
   `[input] Broken string escapes` + `Command run: error in argument 1`，静默什么都不发生。
   必须用**正斜杠**（或反斜杠双写）。
2. **AHK v1 没有 `A_LocalAppData`**（那是 v2 的）。用了拿到空串，叠加 `#Warn` 就是一个运行时警告框。
   改用 `EnvGet, lad, LOCALAPPDATA`。
3. 这版 Ahk2Exe + 这份脚本，函数内写 `local xxx` 直接编译报
   `Local variables must not be declared in this function`。改用带前缀的变量名绕开。

原 `Assoc.exe` 是 **UPX 压缩**过的（516,096 B）。这次不压，1,287,168 B。
体积换少一类杀软误报，与之前弃用 UPX 的结论一致。

### 5. 验证

| 用例 | 做法 | 结果 |
| --- | --- | --- |
| 剪贴板是有效路径（带引号） | 设 `"<测试视频目录>/<测试片名>.mp4"` 后 `--openfile` | 退出码 0，`bassmax_tray` + `mpv.exe 该文件` 起来 |
| 同上，IPC 复核 | 从 `\\.\pipe\MyMPC_mpv` 读 `path` | `path` == 目标文件 |
| 剪贴板是中文文字 | `--openfile` | 出现标题「选择要播放的媒体文件」的 `#32770` 对话框 |
| 对话框里选文件 | 往 ComboBoxEx32→Edit 写路径 + `WM_COMMAND IDOK` | 退出码 0，IPC 读到 `path` == 目标文件 |
| menu.conf 命令串能否分词 | lua 里 `mp.command('run "C:/.../Assoc.exe" --openfile')` | 反斜杠版报 `Broken string escapes`；正斜杠版 Assoc 常驻、对话框出现 |
| menu.conf 解析 | 带 `--log-file` 起 mpv 后 grep `menu` | 无 error/warn，`menu.conf -> …` 正常加载 |

探针：`_verify\openfile_cmd_test.lua`；`%TEMP%\test_openfile.py`、`%TEMP%\test_menu_cmd.py`。

### 6. 产物

- `00.Core\Assoc.exe`、生产 `<安装根>\Assoc.exe`：
  **1,287,168 B**，md5 `ec48d80e8e914e4828860be500adab84`，两处一致。
- `menu.conf`（生产 + `12.mpv`）：md5 `e3948a2fe8a79e36d20c53e465cb2122`，两处一致（LF、70 行）。
- 备份：`<备份目录>/`（旧 `Assoc.ahk` / `Assoc.exe`）。
- 旧 `Assoc.exe`：md5 `590bf1a85f3d0bbe999e9db8258b5cf6`，516,096 B，UPX。

### 7. 边界 / 下一步

- **menu.conf 里是绝对路径**。换安装目录、换机器就失效（mpv 的 `run` 不展开 `~~/`，
  实测 `run "~~/x.cmd"` 返回 success 但什么都没启动）。装机必须替换这一行。
  最省事的落点：`MpvSettings` 写 `menu.conf` 时已经会补「选项(&O)」那一行
  （`DefaultProfile.EnsureMenuEntry`），顺手把「打开剪贴板」按实际安装路径重写即可。
- 每次点击起一个 `Assoc.exe`（约 0.2 s）；`#SingleInstance Force` 会顶掉正在开着的
  「文件关联设置」窗口。暂未处理。
- 在资源管理器里**复制文件本身**（CF_HDROP）时 `Clipboard` 文本为空，会走选择框，不解压文件列表。
- `bassmax_tray` 只在没运行时才启动，已在跑就不重复拉。

## 九、2026-11-21 17:4x 改动：菜单项改名「打开本地文件」+ 路径自检

> 本节取代第八节的「剪贴板判定」与「装机器必须手工改路径」两条。

### 1. 两处语义调整

**① 不再检查剪贴板。** 第八节那版 `--openfile` 会先读剪贴板：命中有效的媒体路径/URL
就直接播，否则弹框。实际用下来这条路是多余的——用户点菜单的意图就是「选个文件」，
而剪贴板里恰好是什么，跟他此刻想打开什么没有关系。于是：

- `00.Core\Assoc.ahk`：删掉 `ClipCandidate()`（整个函数，含「只读剪贴板、绝不写入」那段注释），
  `CliOpenFile()` 直接从 `FileSelectFile` 开始。剪贴板一个字节都不碰，取消选择就静默退出。
  `MediaFilter()`（只列 `media` 类扩展名）与「记住上次目录」的 `assoc.ini` 保留。
- `menu.conf`（生产 + `12.mpv`）：首行标签 `打开剪贴板` → **`打开本地文件`**，命令不变。

**② 路径自检搬到 `MpvSettings`。** 见下节——为什么没按原计划写在 `.iss` 里。

### 2. 为什么最终没在 `.iss` 里写路径

按原计划改了 `<安装包脚本>.iss`：新增 `WriteMenuOpenFilePath()`，在 `CurStepChanged`
的 `ssPostInstall` 里按 `{app}\Assoc.exe` 重写那一行。写完用 ISCC（`<Inno Setup目录>`）
真编了两个最小安装包做端到端验证，发现 Inno 7 的写文件函数有个硬性行为：

> `SaveStringsToUTF8File(FileName, Lines, Append)` —— **第三个参数是 Append，不是 AddBOM；
> 它永远写 BOM**（docs: "with UTF-8 encoding with a BOM"）。

实测（最小安装包 → 静默装到临时目录 → 用真实 mpv 读回 `menu-data`）：

| 现场 | 装完后首项 title | 结论 |
| --- | --- | --- |
| 原样 LF 无 BOM | `打开本地文件` | 正常 |
| 经 `SaveStringsToUTF8File` 重写 | **`\uFEFF打开本地文件`** | BOM 混进了菜单标签 |
| 同时 CRLF | 命令串无 `\r` | `select.lua` 用 `(.-)\r?\n` 切行，CRLF 无害 |

BOM 之所以是问题：`menu.conf` 不是 mpv 的配置解析器读的，是内置 `select.lua` 用
`io.open(...):read("*a")` 直读，它**不剥 BOM**，于是 BOM 变成了第一个菜单项标题的首字符
（`menu-data` 里能看到 `\uFEFF`）。而 Inno 里没有「写 UTF-8 无 BOM」的函数
（`SaveStringToUTF8File` 不存在，`SaveStringToFile` 收 `AnsiString`，中文会按系统代码页走样）。

所以改成让 `MpvSettings` 来写——它本来就是 UTF-8 无 BOM，而且**安装包已经在调它**：
`[Run]` 里那条 `MpvSettings.exe --default --config-dir=…` 就在 `ssPostInstall` 之前跑完，
不需要在 `.iss` 里加任何东西。`.iss` 已还原成改动前的原样（`87706 B`）。

### 3. 新增 `Services\MenuConfPath.cs`

`public static string Fix(ConfigLocator loc)`：

1. `Assoc.exe` 位置：先 `InstallRoot\Assoc.exe`（与 `setting.ahk`、`ConfigLocator` 同一权威来源），
   没有再退 `Root\Assoc.exe`（开发包 / `--config-dir` 验证）。两者都没有就**什么都不做**——
   宁可不写，也不写一条指向空气的路径。
2. 找「同时含 `--openfile` 与 `Assoc.exe`」的那一行，从行内 `run ` 处整段替换成
   `run "<绝对路径，正斜杠>" --openfile`。标签文字、其他条目、行序一律不动。
3. 该行整条都没了（旧包 / 用户删过）时，补到第一行。
4. 写回：UTF-8 **无 BOM**、沿用文件原有的换行风格；并顺手剥掉已存在的 BOM。
5. 路径本来就对 → 直接返回 `null`，**不碰文件**（不刷 mtime）。

调用点两处：

| 位置 | 时机 | 作用 |
| --- | --- | --- |
| `MainWindow` 构造函数（`ConfigLocator.Resolve` 之后、快照之前） | 每次打开设置程序 | 换机器/换目录后自动改正 |
| `CliDefault.Run`（`--dry-run` 时跳过） | 安装包 `[Run] --default`、`.ahk` 的「恢复默认」 | 装完即正确 |

`--dry-run` 的语义是「只算计划，不写盘」，所以自检也跟着跳过——第一版忘了加这个判断，
`--default --dry-run` 照样把 `menu.conf` 改了，已修正。

### 4. 验证

| 用例 | 做法 | 结果 |
| --- | --- | --- |
| 剪贴板里有**有效**媒体路径，`--openfile` | 设好剪贴板再启动 | 仍然弹「选择要播放的媒体文件」（不再走剪贴板） |
| 对话框里选文件 | `ComboBoxEx32→Edit` 写路径 + `WM_COMMAND IDOK` | 退出码 0，IPC `path` == 目标文件 |
| 生产 `menu.conf` 路径写错 | 改成 `<测试路径>/Assoc.exe` 后启动设置程序 | 自动改回 `<安装根>/Assoc.exe` |
| 路径已正确再启动一次 | 对比 mtime | 未重写（mtime 不变） |
| BOM + CRLF + 错路径 | 同上 | BOM 剥掉、CRLF 保留、路径改对 |
| `--default --dry-run` | 错路径的临时配置目录 | 报告「改为…」但不写盘（首行仍是 `<旧配置目录>/…`） |
| `--default`（真跑） | 同上 | 写回正确路径，其余配置无变化 |
| 真 mpv 读菜单 | `menu-data` 探针 | `title=[打开本地文件] cmd=[run "C:/…/Assoc.exe" --openfile]` |

探针：`_verify\menu_bom_probe.lua`、`%TEMP%\test_openfile_noclip.py`、`%TEMP%\test_openfile.py`；
Inno 那两组实验用的最小安装包脚本在 `%TEMP%\menupath_test.iss` / `enctest.iss`（结论已记录，脚本可弃）。

### 5. 产物

- `MpvSettings\publish\MpvSettings.exe`：**469,718 B**，md5 `b9b6b01bb210453ae166971a668d8b2c`；
  已同步 `12.mpv\MpvSettings.exe` 与生产 `…\MediaPlayer\mpv\MpvSettings.exe`，三处一致。
- `00.Core\Assoc.exe`、生产 `<安装根>\Assoc.exe`：**1,286,656 B**，
  md5 `32a19e0242e897ad613fd5de246478f7`，两处一致（不压缩）。
- `menu.conf`（生产 + `12.mpv`）：md5 `aabd80b8b53df72b9061ebcde09d7057`（LF、70 行、无 BOM）。
- `<安装包脚本>.iss`：**未改动**（还原为 `87706 B`；备份 `_bak_assoc_20261121_1720\MyMPC_x64.iss.bak`）。
- 备份：`_bak_exe_menupath_20261121_1742\`（上一版 EXE，`9d7f4716…`）。

### 6. 边界

- `menu.conf` 只在 mpv **启动时**读一次，改完对已开着的窗口无效，下次启动才生效。
- 只有「含 `--openfile` + `Assoc.exe`」的那一行会被认领。用户若把菜单项改名、或把命令换成
  别的程序，自检就不认它——这是有意的，不去猜用户自定义的行。
- 安装目录含中文时，`run` 的引号内是 UTF-8 路径，mpv 走 `CreateProcessW`，实测无问题；
  但 `menu.conf` 里一旦出现 `"` 以外的特殊字符（如 `&`），仍然是 mpv 的字符串分词范围，
  暂未做转义（现有安装目录不会出现）。

### 7. 补记（17:55）：复用 `WriteMpvScreenshotDir()` 为什么不行，实测表格

提问是「iss 里已经有安装时写截图路径的代码，不能复用吗」。**框架能复用，最后一步写盘不能。**

`WriteMpvScreenshotDir()` 的注释写着「`SaveStringsToUTF8File` 写回不带 BOM」，这句在
Inno Setup 7 下已经不成立。用最小安装包在 `ssPostInstall` 里对同一份 menu.conf 实测：

| 写法 | 首 3 字节 | 换行 | 中文 |
| --- | --- | --- | --- |
| 输入模板（正常） | 无 BOM | LF | 正常 |
| `SaveStringsToUTF8File(...,False)` | **EF BB BF** | 全变 **CRLF** | 正常 |
| `SaveStringToFile` | 无 BOM | LF | **GBK**（`b4 f2` = 打） |
| `TStringList.SaveToFile` | 无 BOM | CRLF | **GBK** |
| `TFileStream.ReadBuffer(Buf[1],N)`（String 缓冲） | — | — | 运行时「流读取错误: 内存位置访问无效」 |
| `TFileStream.WriteBuffer(Buf[1],N)` | — | — | 同上，抛异常，产出 0 字节 |
| `TFileStream.ReadBuffer(B,1)`（Byte 缓冲） | — | — | 编译期「类型不匹配」 |
| `TFileStream.CopyFrom(S1, N)` | — | — | 编译期「参数数无效」（单参也只能从当前位置拷到结尾） |

也就是说 Inno 只有「UTF-8 但带 BOM」和「ANSI」两种落盘能力，没有无 BOM 的 UTF-8。
`mpv.conf` 一直没暴露问题，是因为 mpv 的 conf 解析器自己会剥 BOM；`menu.conf` 由内置
`select.lua` 用 `io.open` 直读，不剥。

还试过「让 BOM 落在空行上」这条歪路：menu.conf 首行留空 + BOM 写出后，真实 mpv 读
`menu-data` 得到 `[1] title=[\uFEFF] cmd=[]` —— 右键菜单顶部会多一个**空白可点击项**。
无 BOM + 首行空行才是干净的（首项 `title=nil`，被当分隔符）。所以 BOM 这条路彻底走不通。

结论：安装包不写 menu.conf，由 `MpvSettings.exe --default` 承担。`[Run]` 里本来就有这条
（第 476 行，带 `waituntilterminated skipifdoesntexist`），所以**不需要改 iss**。

### 8. 补记：`--default` 里 `MenuConfPath.Fix` 的位置前移修正

原实现把 `Fix` 放在 `DefaultProfile.Build` 之前，等于「先改盘、再算计划、再写盘」。
现在的计划不会整文件重写 menu.conf，所以结果对；但计划里存在 `IsWholeFile` 标记的可能
（`EnsureMenuEntry` 自己就有 `if (plan.Files.Any(x => x.Label == MenuLabel && x.IsWholeFile)) return;`
这条防御），一旦哪天真加了整文件重写，写在前面就会被刷回打包机路径。

已把 `Fix` 移到 `ConfWriter.Apply` **成功之后**，与 iss 里「`WriteMpvScreenshotDir` 必须排在
`--default` 之后」是同一条原则：对同一个文件的最后一次改动，必须是路径修正。

复测（临时配置目录，故意把首行改成 `<旧配置目录>/Assoc.exe`）：

| 用例 | 结果 |
| --- | --- |
| `--default --dry-run` | 打印计划，首行仍是 `<旧配置目录>/...`，不写盘 |
| `--default` | 输出「右键菜单：「打开本地文件」由 run "<旧配置目录>/..." 改为 run "C:/.../Assoc.exe"」，首行已改，**无 BOM、LF、70 行** |

产物更新：`MpvSettings.exe` **469,718 B**，md5 `2fc3e2d8a62adf035329e7331806113a`
（`publish` / `12.mpv` / 生产 mpv 目录三处一致）；备份 `_bak_exe_menufix_20261121_1755\`
（`b9b6b01b…`）。

探针：`%TEMP%\inno_bom_probe`、`inno_stream_probe`～`inno_stream_probe4`、`inno_sl_probe`、
`menu_variant`（菜单形态对照）。

## 十、2026-11-21 17:5x 改动：路径自检改由 `Assoc.exe` 承担（MpvSettings 依赖 .NET 运行时）

### 1. 为什么换人做

第九节的方案把路径修正放在 `MpvSettings.exe --default` 里。问题是这个 EXE 是**框架相关**的
单文件发布：`iss` 第 476 行带着 `Check: IsMpvSettingsRuntimePresent`，机器上没有 .NET 10
桌面运行时时整条 `[Run]` 被跳过——配置模板本身还能用，但 `menu.conf` 里那条打包机路径
就没人修了，右键「打开本地文件」会静默失败（`Assoc.exe` 找不到）。

`Assoc.exe` 是 Ahk2Exe 编译的原生 EXE，不依赖任何运行时，且安装包本来就会启动它
（`[Run]` 最后一条）。改成由它承担：

- **未安装 MPV 组件**（`menu.conf` 不存在）→ 不写文件，直接显示界面；
- **已安装** → 先修路径，再显示界面。

### 2. 改法

**`00.Core\Assoc.ahk`**

- 新增 `FixMenuOpenFilePath()`：

  | 处理 | 做法 | 原因 |
  | --- | --- | --- |
  | 读 | `FileOpen(conf, "r", "UTF-8")` | 显式指定编码。无 BOM 的文件若走 `FileRead`，AHK 按 ANSI(GBK) 解，中文标签会被写坏 |
  | 定位 | 只认「第一个 TAB 之后、同时含 `--openfile` 与 `Assoc.exe` 的 `run ` 段」 | 标签里一个字都不碰；用户自定义的菜单项不动 |
  | 新路径 | `run "C:/…/Assoc.exe" --openfile`（正斜杠） | 反斜杠会被 mpv 的命令行分词当转义符，日志报 `Broken string escapes` |
  | 写 | `FileOpen(conf, "w", "UTF-8-RAW")` | `UTF-8-RAW` 才是无 BOM。带 BOM 会让右键菜单顶部多出一个空白可点击项（见第九节实测） |
  | 换行 | 检测原文件是 CRLF 还是 LF，按原样拼回 | 不改用户文件的风格 |
  | 幂等 | 目标行已是正确路径 → 直接 return，不写盘 | 不动 mtime |

- GUI 模式：在 `SetWorkingDir` 之后、显示窗口之前调用 `FixMenuOpenFilePath()`。
- 新增 `--fixmenu`：只修路径、`ExitApp`，供安装包静默调用；`mplayerl` 读不到时也静默退出
  （安装包调用不该被错误框打断）。`--openfile` 仍在提权之前处理，行为不变。
- 提权重启保留参数：原来是 `Run *RunAs` 只带脚本路径，参数会丢，`--fixmenu` 会在第二段进程里
  变成「直接弹界面」。改成把第 1…N 个参数逐个加引号拼回去。

**`<安装包脚本>.iss`**（第 476 行之后新增一条，`[Run]` 顺序即执行顺序）

```
Filename: "{app}\Assoc.exe"; Parameters: "--fixmenu"; Flags: runhidden waituntilterminated skipifdoesntexist; Components: main
```

排在 `--default` 之后，保证「对 menu.conf 的最后一次改动」是路径修正；`Components: main`
而不是 `mpv`，是为了覆盖「上次装过 MPV、这次没勾选」但 `menu.conf` 仍在的情况（此时函数
自己会判断文件是否存在）。

### 3. 三个坑

1. **AHK 表达式里不能写 `%A_Index%` 做动态取值**。首次写成
   `rsArgs .= " " . Chr(34) . A_Index . Chr(34)`，实测拼出来是 `[ "1"]`（序号，不是参数值）。
   必须先 `rsArg := %A_Index%`（这是赋值语句，AHK 才会二次解引用）再拼进表达式。
2. **AHK 的双引号转义是两个连续引号，不是反斜杠**。拼接带引号的命令行时直接用 `Chr(34)`，
   避免数错；用反斜杠转义会得到一个字面的反斜杠加引号，静默出错。
3. **`FileRead` 不做编码猜测**。文件无 BOM 时按 ANSI 读，中文直接变 GBK 乱码；写回就毁了。

### 4. 验证

| 用例 | 结果 |
| --- | --- |
| LF 无 BOM + 错路径，`--fixmenu` | 首行改对，**md5 与标准模板 `aabd80b8…` 完全一致** |
| 路径已正确，再跑 `--fixmenu` | mtime 纳秒级不变（未重写） |
| BOM + CRLF + 错路径 | BOM 剥掉、CRLF 70 行保留、中文完好、路径改对 |
| `menu.conf` 不存在 + `--fixmenu` | 退出码 0，不创建文件，不弹任何框 |
| `menu.conf` 不存在 + 无参数 | GUI 正常出现（`MyMPC 文件关联设置`），未创建 `menu.conf` |
| 有错路径 + 无参数 | 先改好文件，再显示 GUI |
| `--openfile` | 仍弹「选择要播放的媒体文件」，未受影响 |
| iss 新增行语法 | 最小 iss 用 ISCC 真实编译通过（`skipifdoesntexist` / `Components` 均有效） |

### 5. 产物

- `00.Core\Assoc.exe`、生产 `<安装根>/Assoc.exe`：
  **1,287,680 B**，md5 `78c238aa6878e32a4026c31cd943ea46`，两处一致（`/compress 0`，不压）。
- `<安装包脚本>.iss`：**88,154 B**（原 87,706 B）。
- 备份：`_bak_assoc_fixmenu_20261121_1759\`（旧 `Assoc.exe` / 生产副本，`32a19e02…`）。
- 生产 `menu.conf` 已复位为标准模板（`aabd80b8…`，LF、70 行、无 BOM）。

### 6. 边界

- `MpvSettings` 里的 `MenuConfPath.Fix` **保留**：`--default` 与「打开设置」是另外两个触发点，
  与 `Assoc.exe` 幂等收敛到同一结果，互不冲突。.NET 存在的机器上三重保险，缺运行时也有兜底。
- 只在 `menu.conf` 已存在时动手。若用户删了 `menu.conf`，不去凭空创建（mpv 的 `select.lua`
  会回落到内置菜单）。
- `Assoc.exe` 仍带 `#SingleInstance Force`：安装时静默调用那一下如果正好顶掉用户开着的
  「文件关联设置」窗口，是既有行为，本次没改。

## 十一、2026-11-21 19:2x 改动：`--fixmenu` 不提权 + `Components` 收窄到 `mpv`

### 1. `Components: main` → `mpv`

`[Run]` 那条 `--fixmenu` 的唯一对象是 `MediaPlayer\mpv\portable_config\menu.conf`，
而 `[Files]` / `[InstallDelete]` 里所有 `portable_config\*` 都标 `Components: mpv`。
挂在 `main` 上，未勾选 MPV 的自定义安装也会白跑一次进程（靠 `skipifdoesntexist` 与函数内的
`FileExist` 兜住，属于兜底不是设计）。改成 `mpv` 后由 Inno 在「装了没装」这一层直接 gate。

链条自洽性已核对：

| 依赖 | 结论 |
| --- | --- |
| `mpv` 组件带 `Check: IsVulkanSupported` | 不支持 Vulkan → 组件不装 → `--default` 与 `--fixmenu` 一起跳过，行为一致 |
| 上一行 `--default` 也是 `Components: mpv` | 两条同源，不错位 |
| 后面 `Assoc.exe`（GUI，无参数）仍是 `main` | 正确：Assoc.exe 是核心组件，跟 MPV 无关 |
| `--fixmenu` 靠 `HKLM\...\App Paths\mplayerl.exe\Path` 定位 | `[Setup]` 是 `SetupArchitecture=x64`，卸载键 `MyMPC` 只落在 64 位视图，说明 HKLM 写入未被重定向；AHK 侧 `SetRegView 64` 对得上，自定义安装目录也能定位 |

### 2. 补的洞：非管理员安装会弹 UAC 卡住安装

`PrivilegesRequiredOverridesAllowed=commandline` 允许 `/CURRENTUSER` 低权限安装。那种情况下
`[Run]` 起出来的 `Assoc.exe` 没有管理员权限，会走进 `if not A_IsAdmin` 分支执行 `Run *RunAs`，
而这一行是 `runhidden waituntilterminated` —— UAC 一出，安装程序就挂在那儿等。

改法：在提权分支最前面加一个只针对 `--fixmenu` 的出口。

```ahk
if not A_IsAdmin {
    ; --fixmenu 由安装包 runhidden 静默调用：不提权，绝不弹 UAC。
    ; 放弃提权不影响结果：紧随其后的 GUI 模式 [Run] 会自己提权，那时同样会修一遍路径。
    if cliFixMenu
        ExitApp
    ...
```

GUI 模式（无参数）的提权行为一个字节没动 —— 关联设置本来就需要管理员。

### 3. 顺手修掉的不一致

> **【后续修正，20:0x】** 本小节的判断已被推翻：压缩是作者本人特意为之，目的是减小体积，报毒交给用户加排除项。
> 正式结论是 **保留 UPX --lzma**，见第十二节第 4 小节。下面这段保留原样，只作为「为什么当时会误判」的记录。

`00.Core\Assoc.exe` 在 18:49 被换成了 **UPX 压缩版**（517,120 B，段名 `UPX0`/`UPX1`），
生产目录那份仍是**不压**的 1,287,680 B。iss 的 `[Files]` 是 `Source: "<项目根>/00.Core/*.exe"`，
即「打包目录里是哪份就发哪份」—— 不修就会让下一个安装包发出压缩版，与「UPX 弃用」的结论相悖。
本次重编统一为不压，两处 md5 一致。

### 4. 验证

| 用例 | 做法 | 结果 |
| --- | --- | --- |
| 幂等 | 路径已正确时跑 `--fixmenu` | 退出码 0，md5 与 mtime 均未变（没写盘） |
| 修复能力 | 首行改成 `run "<测试路径>/Assoc.exe"` 再跑 | 改回正确路径，**md5 复原为 `aabd80b8…`**，LF、70 行、无 BOM（首字节 `E6`＝「打」） |
| 非管理员分支 | 源里把 `if not A_IsAdmin` 临时改成 `if true` 单独编译，配错路径跑 `--fixmenu` | **74 ms 退出、退出码 0、不弹 UAC、`menu.conf` 一个字节没动**，证明出口确实在提权之前 |
| 二进制形态 | `grep UPX0` | 0 命中（`/compress 0` 生效），`.text`/`.data`/`.rsrc` 正常 |
| 部署一致性 | `00.Core` 与生产两份 | md5 `28a752ae96f5bb493aa64074d84efba0`，**1,287,680 B**，两处一致 |

### 5. 产物

- `00.Core\Assoc.exe`、生产 `<安装根>/Assoc.exe`：
  **1,287,680 B**，md5 `28a752ae96f5bb493aa64074d84efba0`（`/compress 0`，不压）。
- `<安装包脚本>.iss` 第 481 行：`... Flags: runhidden waituntilterminated skipifdoesntexist; Components: mpv`
  （88,139 B）。
- 备份：`_bak_assoc_nouac_20261121_1925\`（`Assoc.ahk`、UPX 版 `Assoc.exe`、生产旧 `Assoc.exe`
  `78c238aa…`、`menu.conf.before`）。
- 生产 `menu.conf` 复位为 `aabd80b8…`（测试改坏过，已用修好的二进制写回验证）。

### 6. 边界

- 低权限安装时 `--fixmenu` 现在是**空转**（不提权就不写盘）。路径修正改由收尾那条 GUI `[Run]`
  提权后完成 —— 那条本来就在，只是顺序上晚一步。若用户在 UAC 上点「否」，Assoc.exe 不会启动，
  路径也就不修；此时右键菜单项仍是 `run "Assoc.exe"` 相对形式（模板原样），能从 mpv 目录解析时才可用。
- 两个分支都试过以外的场景没覆盖：域账户、被组策略禁用 UAC 提升的机器。
- `/silent` 编译不处理 `;@Ahk2Exe-*` 指令，产物**没有版本资源**（`FileDescription` 等为空）。
  与改动前那份 1,287,680 B 的产物一致，本次不为它单独改编译方式；加 `verbose`（去掉 `/silent`）
  能写进版本资源，但 `FileVersion` 会带乱码，不如没有。

## 十二、2026-11-21 19:5x 改动：卸载补清理 `%LOCALAPPDATA%\MyMPC`

### 1. 四条 AppData 路径的归属判定

| 路径 | 归属 | 证据 | 处置 |
| --- | --- | --- | --- |
| `%LOCALAPPDATA%\MyMPC\assoc.ini` | **本程序** | `Assoc.ahk` 第 1182 行 `idir := lad "\MyMPC"`，`CliOpenFile` 记「打开本地文件」上次目录；目录里只有这一个 1 KB 文件 | 整目录删 |
| `%LOCALAPPDATA%\mpv\cache` | 不是本程序 | 目录创建于 8-26 15:20，与 `%APPDATA%\mpv.net`（mpv.net 配置）同一分钟；本程序 mpv 便携运行，着色器缓存落在 `portable_config\cache`（当天 19:47 仍在写），而 `%LOCALAPPDATA%\mpv` 最后写入停在 11-20 00:46 | **不动** |
| `%APPDATA%\mpv\files\historybookmarks\*.mpv.history` | 不是本程序 | `mpv.exe` 二进制里搜不到 `historybookmarks` 字符串；`portable_config` 里没有任何脚本或配置项涉及它；内容是音乐播放历史 | **不动** |
| `%APPDATA%\mpv.net\settings.xml` | mpv.net（独立软件） | 安装包里根本没有 mpv.net（`[Files]`、`[Run]`、`12.mpv` 全部无引用） | **不动** |

结论：只有第一条是本程序的垃圾，其余三条属于别的播放器，写进卸载脚本就是破坏他人用户数据。

### 2. 改法

`[Code] ExtraCleanup()` 里、`PlaceboRenderer` 那行之后追加一条（`usPostUninstall` 时执行）：

```pascal
DelTree(ExpandConstant('{localappdata}') + '\MyMPC', True, True, True);
```

- 位置紧跟已有的 `{localappdata}\PlaceboRenderer` 清理，语义一致：能自动重建的缓存/记忆型数据才删。
- 安全性：该目录由 `Assoc.exe` 的 `FileCreateDir` 独占创建，没有第三方写入，整目录删不会误伤。
- iss 是 **GBK** 编码，这次用字节级替换写入（`open(...,'rb')` → GBK 编码的新片 → `'wb'`），
  没有走会转成 UTF-8 的文本编辑路径，避免整个脚本变成 mojibake。改完 1,067 → 1,072 行，
  CRLF 数量与行数一致，`iconv -f GBK` 全文件可解码。

### 3. 没做的事 / 风险

- **没有跑 ISCC 编译验证**：改动只是复制相邻那行 `DelTree` 换个路径，语法同构；但「未编译」就是未编译，
  下次真正打包时才能确认。
- `{localappdata}` 在卸载器里指向「执行卸载的那个用户」。多用户机器上，别的用户自己那份
  `assoc.ini` 不会被清 —— 与现有 `{userappdata}` 那批清理行为一致，这是取舍，不是漏。
- `Assoc.ahk` 里 `lad` 取不到时的兜底是 `A_AppData`（Roaming），那种极端情况下残留会落在
  `%APPDATA%\MyMPC`；LOCALAPPDATA 实际不会缺，没有为它再加一条。

### 4. 结论修正（20:0x）：UPX 保留，定为构建标准

19:29 那次压缩是作者本人做的，目标是**尽量减小体积**；报毒由用户自己加排除项处理（安装说明首页已用最大字号
红字提示「请将安装路径加入 Windows 安全中心的『病毒和威胁防护』排除项」），打包方不再规避。
因此第十一节「不压」的结论作废，正式配方固定为两步：

```
Ahk2Exe.exe /in Assoc.ahk /out Assoc.exe /base "Unicode 64-bit.bin" /icon ico.ico /compress 0 /silent
upx.exe --lzma -q Assoc.exe
```

- 1,287,680 B → **517,120 B**（40.16%，win64/pe，LZMA/7）。UPX 5.0.0。
- `/compress 0` 仍要写：Ahk2Exe 自带的压缩通道与 `upx --lzma` 不是同一套（`/compress 1` 实测没生效），
  两步分开才可控。
- 重新出包核对：压完仍能正常跑 `--fixmenu`，幂等不写盘；把首行改成 `<测试路径>/Assoc.exe` 再跑，
  复原为 `aabd80b8…`。压缩不影响行为。
- 产物：`00.Core\Assoc.exe`、生产 `Assoc.exe` 均为 **517,120 B**、md5
  `597b128955803fbc5313c91c4eacea80`（20:01 重编，源码 `Assoc.ahk` 19:23 含第十一节补丁）。
- 注意：**Ahk2Exe 产出不是字节可复现的**（同源码两次编译 md5 不同），所以「md5 对不上」不能当「源码变了」
  的证据，只能靠体积 + 行为核对。
- 顺带核对体积余量：`setting.exe`（526,848 B）**本来就是 UPX 压过的**（`upx` 直接报
  `AlreadyPackedException`），没有可再榨的空间；`MpvSettings.exe`（469,718 B）是 .NET 单文件，
  **不要 UPX**（单文件自解压本体再套壳，冷启动与加载都可能出问题，换不来多少体积）。
  也就是说全包里只有 `Assoc.exe` 之前是漏压的，现在补齐了。

# MpvSettings 1.0.8 改动记录

> 1.0.7 → 1.0.8。**推翻 1.0.4 第四节「本机 HDMI 只能 2ch、不是音效滤镜的问题」的结论**，
> 并给 BassMax 面板加上「滤镜实际收到的输入声道」显示，让这条链路当场可见。
>
> 记录日期：2026-11-26　｜　本机路径照实记录（根目录 `*.md` 已被 gitignore）

---

## 一、起因：1.0.4 的结论错在哪

1.0.4 第四节写的是「`af=bassmax` 不是元凶」「本机实际听音设备都是立体声，原始多声道无实际意义」。
用户反过来报告：同一段 5.1 音轨，mpv `audio-channels=auto` 与 MPC-HC「混缩到立体声」音量明显不同。
BassMax 在多声道 (>2ch) 下会把驱动钳到 **60%**，所以差值很可能来自这里。

**根因：1.0.4 测的是 AO 输出，不是滤镜输入。**

- mpv 音频链：`解码 → af=bassmax → 声道下混 → AO(wasapi)`。
- `audio-out-params/channel-count` 是 **AO** 的 2ch，是下混**之后**的结果；
- `af=bassmax` 位于下混**之前**，拿到的是 6ch，于是 `clamp_drive()` 把 100% 压成 60%。
- 1.0.4 的表只列了 AO 的 2ch，没有测滤镜自己看到的声道数，因此把因果链看反了。

MPC-HC 那边的路径不同：LAV Audio 先按「混缩到立体声」输出 2ch，BassMax DShow 滤镜
在 `SetFormat()` 拿到的就是 2ch，驱动保持 100%。这就是两边音量差的全部来源，
和 HDMI 驱动「只有 1ch/2ch」没有关系（那部分 1.0.4 的描述本身没错，只是不是本题的解）。

### 关键反证：`--audio-channels=stereo` 救不了

如果问题只是「声道输出选错」，`audio-channels=stereo` 应该让滤镜拿到 2ch。实测**没有**：

| 配置 | af=bassmax 输入 | 滤镜驱动 | AO 输出 |
|---|---|---|---|
| `audio-channels=auto`（出厂） | **6ch / 5.1(side)** | **0.60（多声道限60%）** | stereo 2ch |
| `--audio-channels=stereo` | **6ch / 5.1(side)** | **0.60（多声道限60%）** | stereo 2ch |
| `af=lavfi=[pan=stereo...],bassmax`（先下混） | **2ch / stereo** | **1.00** | stereo 2ch |

`audio-channels` 只影响 AO 协商，不往滤镜链插下混。所以 1.0.4「声道输出下拉四项、不扩表」
那套结论在本问题上无效；下拉框保持原样，不动。

### 硬证据（生产 mpv，2026-11-26，`_verify/audiotest/rebass_104_auto.log`）

```
[   0.020][i][cplayer] ● Audio ... 'DTS XLL 无损的DTS多声道音频 @ 3688 kb/s, 6 声道, 48.0 kHz'
[   0.022][v][bassmax] chmap=5.1(side)
[   0.025][v][bassmax] drive=0.60(多声道限60%) bass=+2.0dB treble=+2.0dB preset=4 sr=48000 ch=6 groups=4
[   0.057][i][cplayer] AO: [wasapi] 48000Hz stereo 2ch float
```

测试片：`<测试片目录>\Harry Gregson-Williams - Unstoppable (End Title).DTS-HDMA.mka`
（ffprobe：DTS / 48000 Hz / 6ch / 5.1(side)）。

---

## 二、改动 1：BassMax 面板红框显示「滤镜输入声道」

### 2.1 数据通路

滤镜和面板分属两个进程（mpv.exe 与 rundll32 加载的 bassmax_dshow.ax），
唯一现成的共享点是 `HKCU\Software\BassMax`。新增四个键：

| 键 | 类型 | 写入方 | 含义 |
|---|---|---|---|
| `InputChannels` | REG_DWORD | af_bassmax.c / bassmax_dsp.cpp | 滤镜**真正**收到的声道数 |
| `InputLayout` | REG_SZ | 同上 | `5.1(side)` / `stereo` / `7.1` 等 |
| `InputRate` | REG_DWORD | 同上 | 采样率 |
| `InputStamp` | REG_QWORD | 同上 | `GetTickCount64()`，面板 5 s 过期判定 |

- `bassmax_filter/af_bassmax.c`：`process()` 里 1 s 节流上报，调用新增 C ABI
  `bm_publish_input(nch, rate, layout_utf8)`；`reset()` 清 `last_publish`。
- `bassmax_filter/bassmax_core_c.cpp`：新增 `bm_publish_input()`，UTF-8 layout 转宽字符后写注册表。
- `bassmax_dshow/bassmax_settings.cpp/.h`：新增 `SetInputInfo()` / `GetInputInfo()`（含 5 s 过期）。
- `bassmax_dshow/bassmax_dsp.cpp`：`SetFormat()` 里写完整布局；`Tick()` 每秒续 stamp（播放中不掉回「未播放」）。

### 2.2 界面

`bassmax.rc` 的 `IDD_BASSMAX` 与 `IDD_BASSMAX_POPUP` 在预设按钮右侧（用户截图的红框位置）
新增三个只读文本：`IDC_LBL_INPUT`（"滤镜输入"）、`IDC_TXT_INPUT`（"6ch"）、
`IDC_TXT_INPUT_SUB`（"5.1(side) · 48.0kHz"）。

- `bassmax_ui.h` 新增 `UiUpdateInput()`：无数据/过期显示 `— / 未播放`。
- 两个对话框各加 500 ms 定时器刷新：独立面板在 `bassmax_tray.cpp` 的 `SettingsDlgProc`，
  属性页在 `bassmax_page.cpp` 的 `OnActivate` / `OnReceiveMessage`。
- BassMax 版本 1.0.6 → **1.0.7**（`version.h` 四处）。

截图：`_verify/stage108_panel_6ch.png`（播放中，红框显示 `6ch / 5.1(side) · 48.0kHz`）。

---

## 三、改动 2：更正设置程序里的错误说明（1.0.8 本体）

`Views/AudioView.xaml`「输出与逃生口」卡片：

| 项 | 1.0.4 原文 | 1.0.8 改为 |
|---|---|---|
| 标题 | 为什么是立体声 | 输出是立体声，但音效拿到的可能是多声道 |
| 说明 | …这不是配置错误，也不是音效滤镜的问题… | 播放统计里的 stereo 2ch 是 AO 输出（系统下混之后），不是音效滤镜的输入。mpv 的 af=bassmax 位于下混之前，5.1 音轨按 6ch 处理，驱动自动限到 60%，所以比先混缩成 2.0 再进音效的播放器音量小。打开 BassMax 面板可在右上角看到滤镜实际收到的声道数。 |
| 标记 | 系统限制 | 链路顺序 |

「需要原始声道时」那条不动（它讲的是独占/多声道端点，仍然成立）。
`_verify/stage104_audio_note_probe.py` 的标记断言同步改「链路顺序」。

---

## 四、版本号与文件改动清单（本机真实路径）

| 文件 | 改动 |
|---|---|
| `MpvSettings.csproj` / `Views/AboutView.xaml` | 1.0.7 → 1.0.8 |
| `Views/AudioView.xaml` | 更正声道说明标题/正文/标记 |
| `_verify/stage104_audio_note_probe.py` | 标记断言改「链路顺序」 |
| `_verify/stage108_input_probe.py`（新增） | 9 项端到端验证 |
| `<项目根>\bassmax\bassmax_filter\af_bassmax.c` | 上报输入声道 |
| `<项目根>\bassmax\bassmax_filter\bassmax_core_c.cpp` | `bm_publish_input()` |
| `<项目根>\bassmax\bassmax_filter\bassmax.patch` | 重新生成（两个新文件段按新内容重建） |
| `<项目根>\bassmax\bassmax_dshow\{bassmax.rc,resource.h,bassmax_ui.h,bassmax_settings.h,bassmax_settings.cpp,bassmax_dsp.h,bassmax_dsp.cpp,bassmax_tray.cpp,bassmax_page.cpp,version.h}` | 面板红框 + 输入声道 API + 版本 1.0.7 |

**mpv.exe 重新编译**：补丁改动在 `af_bassmax.c`，必须重编 mpv 才能让上报生效。
走 `/d/mpvbuild/build.sh`（幂等重打四个补丁 → force-meson-configure → ninja mpv）。
`build.sh` 末尾「找不到嵌套目录里的新 mpv.exe」是它的输出查找路径假设过时，
实际产物在 `/d/mpvbuild/build64/mpv-x86_64-v3-20261126-git-543a8c2c59/mpv.exe`，已人工核对。

---

## 五、验证

### 5.1 输入声道显示（`_verify/stage108_input_probe.py`，9/9 通过）

| 用例 | 结果 |
|---|---|
| 面板打开、未播放 | `— / 未播放` |
| auto 播放 5.1 | 注册表 `InputChannels=6, layout=5.1(side), rate=48000`；红框 `6ch / 5.1(side) · 48.0kHz` |
| lavfi 先下混再进滤镜 | `InputChannels=2, layout=stereo`；红框 `2ch` |
| 停止播放 6 s | 红框回到 `— / 未播放` |

### 5.2 UI 说明

`_verify/stage104_audio_note_probe.py`：新标记「链路顺序」可见（1.0.7 EXE 上该断言为 NO，1.0.8 为 YES）。
该探针另有 4 项旧断言（声道输出 / 比特流直通互斥 / 音效开关 / 输出与逃生口）
在 1.0.7 与 1.0.8 上**同样失败**，是探针自身的历史遗留问题，不是本版回归。

### 5.3 构建

`dotnet publish -c Release -r win-x64 -o publish`：0 错误。
BassMax `.ax`：MSVC 编译 0 错误（链接只有 4 条既有的 LNK4104 警告）。
mpv：四个补丁全部成功应用，`--af=help` 含 `bassmax`。

---

## 六、产物与备份

| 产物 | 值 |
|---|---|
| `MpvSettings.exe` | **520,406 B**，md5 `f10ca6ea076eb2c1628fdc5f82bc9036`；publish / `12.mpv` / 生产三处一致；FileVersion 1.0.8.0 / ProductVersion 1.0.8 |
| `bassmax_dshow.ax` | **191,488 B**，md5 `bba08f93eca995b2cebe19c70630ac8d`；`bassmax_dshow/` / `_publish/` / `00.Core` / 生产 `Codecs` 四处一致；版本 1.0.7.0 |
| `mpv.exe` | **99,793,949 B**，md5 `8aba307bd3127e8c8ebcb933d4efdd2d`；构建产物 / `12.mpv` / 生产三处一致 |

备份：`<项目根>\_bak_108_20261126_2305\`
（`bassmax_filter/` 四个源文件、`bassmax_dshow/` 十个源文件与旧 ax、`mpv_exe/` 两份旧 mpv.exe、
`exe/` 旧 MpvSettings.exe `e179ba7b…`、`deployed/` 三份旧 ax）。
mpv 构建日志：`_bak_108_20261126_2305/mpv_rebuild.log`。

---

## 七、边界 / 下次注意

- **1.0.4 第四节的表仍然成立的只有**：AO 在共享模式下跟随端点首选布局（本机 2ch）、
  NVIDIA HDMI 拒绝独占、Realtek USB 端点 + 独占 + 显式 5.1 是唯一能输出 6ch 的路径。
  被推翻的是「与音效滤镜无关」和「原始多声道对用户无实际意义」两句。
- 面板显示的是**滤镜输入**，不是最终输出。看到 `6ch` 同时 AO 是 `2ch` 是正常的：
  下混发生在滤镜之后。
- 两个播放器同时播放会竞争同一份注册表键，面板显示最后写入者；停止播放 5 s 后回退到「未播放」。
- 想让 mpv 的音量对齐 MPC-HC「混缩到立体声」，要在 `af=bassmax` **之前**插下混滤镜：
  `af=lavfi=[pan=stereo|FL=FL+0.707*FC+0.707*SL|FR=FR+0.707*FC+0.707*SR],bassmax`。
  本版**没有改生产默认配置**——是否采用属于听感取舍，等用户拍板；面板先行提供判断依据。
- `audio-channels=stereo` 不能替代上述下混（实测滤镜仍是 6ch）。
- 改 `af_bassmax.c` 必须重新生成 `bassmax.patch` 并重编 mpv；只换 `.ax` 不会让 mpv 上报。

---

## 八、增补（2026-11-26 晚）：把 bassmax 移到下混之后

> 用户拍板：既然改配置就能把 BassMax 挪到下混之后，就在两个「混缩到立体声」入口里做掉。
> **不升版**（仓库还没同步），继续用 1.0.8 / setting 3.2；本节点是 1.0.8 的功能增补，
> 版本号、AboutView、状态栏都保持不动。

### 1. 最终结论

第七节「想对齐 MPC-HC 就手工插 pan 滤镜」**升级为产品行为**：

- `setting.exe` 快速设置页「将音频混缩到立体声」**开** →
  `audio-channels=stereo` + `af=lavfi=[aresample=out_chlayout=stereo],bassmax`；
  **关** → `audio-channels=auto` + `af=bassmax`。
- MpvSettings「音频 → 声道输出」选 **stereo（2声道）** → 同一条 af 链；
  选 auto / auto-safe / mono / 自定义 → 不插下混，`af=bassmax`。
- 两处都只增删「下混」这一节，用户自己挂的 `scaletempo2` / `lavfi=[...]` 原样保留。

### 2. 为什么用 `aresample=out_chlayout=stereo`，不用 pan

`pan=stereo|FL=FL+...` 是按 5.1(side) 写死的，换 7.1 / 2.0 就得改字符串。
`out_chlayout` 走 swresample 的标准下混矩阵，布局无关；实测 5.1 与纯 2.0 片都能进：
生产 mpv 播 `<测试片目录>/Harry Gregson-Williams - Unstoppable (End Title).DTS-HDMA.mka`
（5.1/48kHz）与自造 2.0 tone，`--msg-level=af=v` 都给出
`[bassmax] drive=1.00 ... sr=48000 ch=2 groups=1`，AO 仍 stereo 2ch，无失败。
`aresample=out_channel_layout=stereo` / `ch_layout=` / `ochl=` 在本构建的 libavfilter 12.12
上全部「Option not found」，只有 `out_chlayout` 可用（ffmpeg `-h filter=aresample` 核对过）。

### 3. 文件改动清单

| 文件 | 改了什么 |
|---|---|
| `<项目根>/00.Core/setting.ahk` | 新增 `MpvAfIsDownmix` / `MpvAfSplit` / `MpvAfSetDownmix`；`MpvSyncGlobal` 的 Button7 分支在写 audio-channels 后调 `MpvAfSetDownmix(btnState)`；配置说明块补一段。版本号仍 3.2。 |
| `<项目根>/00.Core/setting.exe` | 由上面重编（Ahk2Exe：`/base` + `/icon` + `/silent`，见 `MPV设置EXE_V1_参数与布局规划.md` §6.14）；部署到 `<用户目录>/MyMPC/setting.exe`。 |
| `MpvSettings\Services\AfChain.cs` | 加 `DownmixFilter` / `ContainsDownmix` / `IsDownmix`；`Merge` 加可选 `downmix` 参数，先摘旧节再按位置插回。 |
| `MpvSettings\Services\WritePlanBuilder.cs` | 页 C 算出写盘后真正生效的 `effectiveChannels`（被自定义态拦住时取盘上值），传给 `EnsureAfChain(plan, s, wantDownmix)`；Transform 同步。 |
| `MpvSettings\Views\AudioView.xaml` | 「声道输出」与「链路顺序」两条描述改成新语义（选 stereo 才下混）。 |
| `MpvSettings\_verify\stage109_downmix_probe.py` | CLI `--apply-only` 端到端：auto→stereo、真机播放、stereo→auto、用户滤镜保留，12/12。 |
| `MpvSettings\_verify\stage109_doubleclick.py` | **真双击** setting.exe + Win32 点 Button7，测完自动恢复生产配置。 |

### 4. 验证（本机实测，硬门槛）

**MpvSettings 侧**（`python _verify/stage109_downmix_probe.py`，隔离副本，12/12）：

```
OK  apply 成功
OK  audio-channels=stereo      stereo
OK  af 含下混节                lavfi=[aresample=out_chlayout=stereo],bassmax
OK  下混在 bassmax 之前
OK  实测滤镜拿到 2ch / drive 100%
    [0.021][v][bassmax] drive=1.00 ... sr=48000 ch=2 groups=1
OK  apply(auto) 成功 / audio-channels=auto / af 不含下混节 / af 保留 bassmax
OK  用户滤镜 scaletempo2 保留   scaletempo2,lavfi=[aresample=out_chlayout=stereo],bassmax
```

**setting.exe 侧**（`python _verify/stage109_doubleclick.py`，真 `os.startfile` 双击 +
Win32 按「将音频混缩到立体声」标签定位按钮并 `BM_CLICK`，全程一个进程内避免 GUI 被回收）：

```
[开] 按钮 '点击开' -> '点击关'
[开] audio-channels=stereo  af=lavfi=[aresample=out_chlayout=stereo],bassmax
[关] 按钮 '点击关' -> '点击开'
[关] audio-channels=auto    af=bassmax
PASS
```

测试前后生产 `mpv.conf` 已比较并恢复（`audio-channels=auto` / `af=bassmax` 原样）。

**编译**：`dotnet build -c Release` 0 警告 0 错误；`dotnet publish` 后三处 EXE md5 一致
`2654377a8d3795dffd7205193af3fb88`（publish / 12.mpv / 生产）；
setting.exe 两处一致 `0753b3b4d186938ef8e4ca7f72a27bd4`。
两份 EXE 都 grep 到新字符串（.NET 为 UTF-16LE，AHK 为 ANSI）。

### 5. 边界

- 下混节与 `audio-channels=stereo` 绑定；想要 5.1 原样进音效就选 auto 或自定义 5.1。
- `setting.exe` 的「恢复为默认设置」与 MpvSettings `--default` 都会把声道复位成 auto，
  因此不会残留下混节。
- 第七节那条手工 pan 命令仍可用，但已不是唯一办法；`out_chlayout` 更稳，优先用产品开关。
- 面板显示的仍是**滤镜输入**：选 stereo 后应看到 `2ch`，选 auto 播 5.1 应看到 `6ch`。

---

## 九、增补（2026-11-26 深夜）：托盘双击与中键统一

> 用户反馈：`bassmax_tray.exe` 双击打开的设置窗口和按中键看到的 UI 不一样。
> 不升版（仓库未同步），作为 1.0.8 的又一处行为修正。

### 1. 根因与决定

双击原本直接调 `OpenSettings()`，经系统 DLL 导出启动独立设置窗口；中键弹的是托盘菜单（设置 / 退出）。
两条路径、两套 UI，再加上双击消息序列里 `LBUTTONUP → LBUTTONDBLCLK → LBUTTONUP`
会把开关切两次（净效果抵消），观感确实不一致。
用户给了两个选项：去掉双击显示，或让它和中键一样。**选后者**：双击 = 中键，都弹菜单；
菜单里点「设置」才进 `OpenSettings()`。这样两种手势走到设置窗口的路径完全相同。

### 2. 改动

| 文件 | 改了什么 |
|---|---|
| `<项目根>\bassmax\bassmax_tray\bassmax_tray.cpp` | `WM_LBUTTONDBLCLK` 由 `OpenSettings()` 改为 `ShowMenu(hwnd)`；悬浮提示改成「双击/中键 菜单」；顶部交互注释与双击说明同步。 |
| `<项目根>\bassmax\bassmax_tray\dist\bassmax_tray.exe` | 由上面重编（build.sh / MSVC /MT），部署到安装目录 `Codecs\` 与 `12.mpv\`。 |
| `MpvSettings\_verify\stage_tray_gesture_probe.py` | 真启动托盘，分别发双击/中键回调消息，枚举窗口断言「菜单出现且设置窗口不出现」。 |

### 3. 验证（本机实测）

```
[双击] 菜单窗口=1  设置窗口=无
[中键] 菜单窗口=1  设置窗口=无
PASS
```

新悬浮提示字符串 `双击/中键 菜单` 在 exe 中命中 1 次，旧 `双击 设置` 为 0。
三处 exe md5 一致 `d420a49abeb804b41f3fb4a7d86ff010`（构建输出 / 生产 / dev）。
托盘 exe 自身版本资源未动（1.0.0.0）。备份在 `<项目根>\_bak_tray_20261126_2358\`。

---

## 十、增补（2026-11-27）：bassmax_decoder 补齐

> 用户反馈：MPC-BE 播放时设置面板红框显示「未播放」，且 decoder 托盘没有设置入口。
> 仍不升版，作为 1.0.8 的又一处修正。

### 1. 根因

昨天只重编了 `bassmax_dshow.ax`，漏了共用同一份 `bassmax_dsp.cpp` 的 `bassmax_decoder.ax`：
它停在 11-11 的旧构建，二进制里没有 `InputChannels` / `InputStamp` 上报代码，所以面板读不到
输入信息，显示「未播放」。另外 decoder 托盘当初为躲属性页 CLSID 冲突砍掉了对话框资源，
没有设置入口。用户拍板：不重复集成界面，直接调通用命令打开 dshow.ax 的设置窗口。

### 2. 改动

- `bassmax_decoder_tray.cpp`：新增 `FindDshowAx()`（CLSID 注册 > 同目录 > AxPath）与
  `OpenSettings()`；双击走通用命令 `rundll32 "…\bassmax_dshow.ax",BassMaxConfig`；
  悬浮提示改「左/右键 开关　双击 设置」。左右键单击仍是切总开关。
- 重编 `bassmax_decoder.ax`（含新版 DSP 输入上报），md5 `d92952c8f5d04cc345413fe769980614`，
  部署 `Codecs\` 与 `_publish\`。
- 两个 .ax 读写同一份 `HKCU\Software\BassMax`，这是「decoder 不集成界面」成立的前提。

### 3. 验证

`MpvSettings\_verify\stage_decoder_20261127_probe.py` **10/10**：
清空上报后用 DirectShow host 播 5.1 测试片，decoder 自动写回 `6ch / 48000Hz / 5.1`；
dshow.ax 导出 `BassMaxConfig`、CLSID 路径存在；decoder.ax 含新字符串。
备份在 `<项目根>\_bak_dec_20261127_0018\`。

---

## 十一、增补（2026-11-27）：decoder 托盘加中键开设置

> 用户反馈：decoder 托盘只有双击开设置，而双击会顺带把音效关-开一次，观感不稳。
> 要求加中键。仍不升版。

### 1. 根因

托盘回调里双击的消息序列是 `LBUTTONUP -> LBUTTONDBLCLK -> LBUTTONUP`。前后两个 UP 各走一次
总开关翻转，净效果虽然抵消，但播放中能听到音效瞬间关再开、图标闪两下。中键是单条
`WM_MBUTTONUP`，不经过开关逻辑，只开设置，所以更稳。

### 2. 改动

| 文件 | 改了什么 |
|---|---|
| `bassmax_decoder_tray.cpp` | `kTrayMsg` 增加 `WM_MBUTTONUP -> OpenSettings()`；双击分支保留；悬浮提示改「左/右键 开关　中键 设置」；交互注释改写并说明为什么主推中键。 |
| 同上 `OpenSettings()` | 加 1 秒去重（`GetTickCount64`，不阻塞托盘线程），连点不会起两个设置宿主进程；被拦下时写 `OpenSettings: debounced (N ms)`。 |
| `bassmax_decoder.ax` | 重编，182,784 B / md5 `ea6818071e72cc34dec519d756166881`，部署到 `Codecs` 与 `_publish`。 |

### 3. 验证

`MpvSettings\_verify\stage_decoder_mb_20261127_probe.py` **10/10**：起真实播放宿主，按 PID 定位
decoder 托盘窗口，直接投递托盘回调消息。

```
[中键]     面板=1  开关不变
[中键 x2]  面板=1           (1 秒去重生效)
[双击]     面板=1  开关不变
[左键]     开关 1 -> 0 并可还原
```

新提示串「中键 设置」在 ax 中命中 1 次，旧「双击 设置」为 0。
备份在 `<项目根>/_bak_dec_mb_20261127/`。

### 4. 一个探针坑（写下来免得重犯）

探针最初把 `WM_LBUTTONUP` 当成 `0x0201`，实际 `0x0201` 是 `WM_LBUTTONDOWN`，
`WM_LBUTTONUP` 是 `0x0202`。左键用例一直 FAIL，误以为是新按键分支写错，
最后靠打印常量才定位。投递鼠标消息前先核对 DOWN/UP 常量。

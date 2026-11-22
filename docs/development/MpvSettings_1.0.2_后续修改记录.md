# MpvSettings 1.0.2 后续修改记录

> **版本背景**：`1.0.1` 已发布。本文记录 2026-11-18 在 1.0.1 基础上追加的改动，按 **1.0.2** 归档。
> 1.0.2 新增「NVIDIA RTX 视频增强」（RTX 超分辨率 + RTX Video HDR）一组设置，未改变任何既有设置项语义；
> 已发布用户升级后原有配置照常读取，缺键按出厂默认展示。

## 一、功能改动（1.0.2）

### 1.1 NVIDIA RTX 视频增强（新增，画面页）

新增一个 SettingCard，四项：

| 控件 | 写盘值 | 出厂默认 |
|---|---|---|
| RTX 超分辨率（VSR） | `rtx_vsr=yes\|no` | `yes`（开） |
| VSR 启用上限 | `rtx_vsr_limit=sd\|720p\|1080p\|1440p` | `1080p` |
| RTX Video HDR（SDR→HDR） | `rtx_hdr=yes\|no` | `yes`（开，本次调整） |
| RTX HDR 峰值亮度 | `rtx_hdr_peak=400..2000` | `1000` nit |

四项全部写 `script-opts\MyMPC.conf`，由 `scripts\MyMPC_ui.lua` 在运行时执行。

**默认组合「VSR 开 + HDR 开」（2026-11-18 晚调整）**：
- VSR 是纯画质增强，低分辨率片源放大才触发，无画面翻车风险，默认开；
- RTX Video HDR 原默认关，理由是 SDR 桌面强开会洗白（mpv#17800）。经实测调整：**NVIDIA 驱动侧的
  RTX Video HDR 只对 SDR 源生效，HDR 片源一律跳过**（驱动默认行为，不是 mpv 侧猜的），
  加上 Lua 侧桌面 HDR 判据（`video-target-params/gamma=pq`）兜底，可放心默认开。
  仍要 Windows HDR 已开 + NVIDIA App 侧总开关为开，否则自动跳过。

### 1.2 底层机制（为什么不是 NVAPI）

FrankyRenderer 1.2 的 RTX Video HDR、`MPC Video Renderer`、Chromium/Edge 调的是**同一个 D3D11 视频处理器扩展**：

- VSR：`VideoProcessorSetStreamExtension(GUID d43ce1b3-1f4b-48ac-baee-c3c25375e6f7, {version=1, method=2, enable=1})`
- HDR：`VideoProcessorSetStreamExtension(GUID fdd62bb4-620b-4fd7-9ab3-1e59d0d544b3, {version=4, method=3, enable=1})`

不用 NVAPI、不用 NvIFR、不用额外运行时。mpv 0.40+ 把它包成 `vf=d3d11vpp`：

- VSR：`d3d11vpp=scaling-mode=nvidia:scale=N`
- HDR：`d3d11vpp=nvidia-true-hdr`

所以 mpv 不需要 FrankyRenderer 的 DLL（那是 DirectShow 渲染器，mpv 也挂不上），原生就能用同一套驱动能力。

### 1.3 VSR 阈值（照抄 MPCVR，不在注册表）

用户问「阈值是不是在注册表」——**不是，在源码里硬编码**，注册表只存档位枚举值。
`Aleksoid1978/VideoRenderer` 的 `CD3D11VP::SetSuperRes()`：

```cpp
case SUPERRES_SD:     enable = checkDimension(1024u, 576u);
case SUPERRES_720p:   enable = checkDimension(1280u, 720u);
case SUPERRES_1080p:  enable = checkDimension(2048u, 1088u);  // "a little more than Full HD"
case SUPERRES_1440p:  enable = checkDimension(2560u, 1440u);
```

`checkDimension` 先按长边/短边归一化（`w>=h ? (w<=W && h<=H) : (h<=W && w<=H)`），横竖屏通吃。
本包 Lua 的 `rtx_fit()` 与之逐字等价。用户口中的「≤1080P」对应 `1080p` 档，实际阈值是 **2048×1088**，不是 1920×1080。

### 1.4 Lua 侧动态挂卸（为什么不写死 vf=）

`d3d11vpp` 没有「分辨率上限」选项，写死在 mpv.conf 会对所有片源生效、超阈值也白烧 GPU。
所以 `MyMPC_ui.lua` 在 `file-loaded` / 属性变化时按片源判定后动态 `vf add/remove @MyMPC-rtx:`：

- VSR：源分辨率 ≤ 档位上限 **且** 画面确实被放大（`zoom > 1`）才挂；否则 remove。
  **2026-11-18 改为 MPCVR 策略**：100% 播放不挂、放大窗口才挂（用户实测 MPCVR 里 DVD 100% 时 VSR 关、
  Alt+4 放大到 80% 屏时开）。启用判据（zoom）用 `window-scale`（窗口模式）/ `osd÷源`（全屏模式），
  分母 `width/height`（源尺寸，挂滤镜不被改写）。observe `window-scale`/`fullscreen` 实时重算。
  `display-width`（显示器物理尺寸）已废弃——它会让任何片源强撑到 4K。

  **【关键：滤镜倍率与窗口倍率必须解耦】** mpv 硬机制：`最终画面 = 源 × filter scale × window-scale`。
  中间曾让 `filter scale = window-scale`（Alt+3=2 → filter=2），两个倍率相乘变 4×，且 `osd-width` observe
  形成正反馈滚雪球（scale 2→4→8→8.5、osd 720→2880），就是用户说的"按 Alt+3 放大两次"。
  最终方案（用户选定）：**窗口放大全交给 window-scale，滤镜 scale 固定 `1.001`**（只为向驱动发 VSR
  扩展、不参与画面放大）。实测（`_verify\min_scale_real.py`，真实 DVD 片）：fs=1.001 时 osd 正好 2×、
  VSR=ON、无 failed；fs≥1.01 起 osd 就会略超 2×。代价：VSR 的 AI 超分成分很小，主要是锐化/去噪/
  边缘重建——这是"不动 Alt+数字快捷键体系"换取无二次放大的取舍。
  实测脚本：`_verify\rtx_ws_probe.py`（滚雪球复现）、`_verify\jump_timeline.py`（ms 级跳变，修复后仅 1 次）、
  `_verify\final_e2e.py`（三态，生产配置 PASS）、`_verify\threshold_probe.py`（四档阈值边界全对）。

  **【顺带修复的隐藏 bug】** 生产 `script-opts\MyMPC.conf` 曾被写成 `\r\r\n`（双 CR）行尾——是早前用
  Python `write_text('\r\n'...)` 在 Windows 文本模式同步时把 `\n` 又转成 `\r\n` 所致。后果：每个值带尾随 `\r`，
  mpv 对布尔项报 `Can't convert 'yes\r' to boolean!`、字符串项 `rtx_vsr_limit` 查表失败回落默认，
  **导致用户改档位/开关一直不生效**。已字节级修复为 `\r\n`（备份 `*.bak_crcrlf_*`），并修正验证脚本
  `rtx_verify.py`/`threshold_probe.py` 的写入方式（`newline=''`）。EXE 本身写盘正常，此 bug 纯属手工同步引入。
- HDR：源是 SDR **且** 桌面确为 HDR 才挂；HDR 片源 / SDR 桌面一律 remove。
- 10bit SDR 走 `format=x2bgr10`（强转 nv12 会让 `nvidia-true-hdr` 不激活，mpv#17265）。
- HDR 输出用 `format=max-luma=<峰值>` 标注真实峰值，避免 mpv 默认按 1000 nit 猜导致高光偏暗。
- 换文件先摘旧滤镜，避免重复挂；`end-file` 一起清主滤镜与峰值滤镜。

**桌面 HDR 判据**：实测 HDR 桌面下 mpv 协商出 `video-target-params/gamma=pq`、
`primaries=bt.2020`（DISPLAY1 = `RGB_FULL_G2084_NONE_P2020` @10bit）；SDR 桌面是 `bt.1886/bt.709`。
Lua 用 `target_gamma == "pq" or target_prim == "bt.2020"` 判断，SDR 桌面自动跳过 HDR。
`file-loaded` 后 0.25s 与 1.0s 各判一次，避开 VO 色彩协商晚于文件加载的时序坑。

### 1.5 硬件解码前提（实测结论，与社区说法不同）

社区 `autovsr.lua` 说「必须 `hwdec=d3d11va`，`d3d11va-copy` 不行」。本机实测（mpv 0.42 + RTX 5070）**三种路径都能启用驱动扩展**：

| hwdec | 日志 | 结论 |
|---|---|---|
| `d3d11va` | `NVIDIA RTX Video HDR enabled` / `NVIDIA RTX Super Resolution enabled` | 生效 |
| `auto-copy`（本包默认） | 同上 | 生效 |
| `no`（纯软解） | 同上 | 生效 |

mpv 的 d3d11vpp 会自动把软件帧上传到硬件，所以 Lua **不按 hwdec 拦截**，界面也不再提示「需切 d3d11va」。
生产 `mpv.conf` 的 `hwdec=auto-copy` 保持原样未动——那是为杜比视界/HDR 定的默认值，没必要为 RTX 改。

### 1.6 NVIDIA App 侧硬开关检测（本次新增）

`d3d11vpp` 只是向驱动「请求效果」，最终是否生效还取决于 NVIDIA App 视频页的两个总开关。
setting.ahk 早有 `IsNvidiaVSRAvailable()` 读驱动注册表，但只扫 0000/0001/0002 三个槽位——
多显卡或驱动实例落在高槽号（实测有 0017）时会漏检。本次：

- **setting.ahk**：`IsNvidiaVSRAvailable()` 改为扫全部 0000–0099 四位槽位、按 `DriverDesc` 含 NVIDIA 认卡；
  新增 `IsNvidiaTrueHdrAvailable()` 同法读 HDR 总开关。AHK v1 语法已用 AutoHotkey.exe 编译校验通过。
- **MpvSettings**：新增 `Services/NvidiaAppState.cs`，读同一对注册表键：
  - `_User_Global_VAL_SuperResolution`：`0`=关，`1..4`=质量档，`5`=自动（本机 5070 实测 = 5）
  - `_User_Global_VAL_TrueHDR`：`0`=关，非 0=开（本机实测 = 1）
  - 全槽位扫描 + `DriverDesc` 过滤，与 setting.ahk 判据一致。
- 画面页新增只读行「NVIDIA App 侧开关（检测）」，显示两个总开关的实时状态；
  `WritePlanBuilder` 按实际值给 Warnings：开着不提示，关着才提示「mpv 侧开了也不会生效」，且**不阻止写入**。
- `--default` 路径原本不刷新只读状态会误报「未检测到驱动」，已在 `DefaultProfile.DefaultDraft()` 补读注册表修正。
- 这些状态不参与 diff、不落盘，只是检测与提示。

## 二、版本号（1.0.1 → 1.0.2）

按 1.0.1 记录的约定，改版本共 **5 处**：

| 位置 | 改动 |
|---|---|
| `MpvSettings.csproj` | Version / AssemblyVersion / FileVersion / InformationalVersion → 1.0.2 / 1.0.2.0 |
| `Views/AboutView.xaml` | 「MPV 简单设置 1.0.1」→「MPV 简单设置 1.0.2」 |

标题栏 `· v1.0.2` 由 `MainWindow.xaml.cs` 读程序集 `InformationalVersion` 自动显示，无需改 XAML。

## 三、文件改动清单

**源码**
- `Models/SettingsDraft.cs`：新增 RTX 下拉表（档位/峰值）、`RtxVsr/RtxHdr/RtxVsrLimitIndex/RtxHdrPeakIndex` 属性与反推、重置默认值、`RtxHwdecHint` 只读提示；`HwdecIndex` setter 通知提示刷新。
- `Models/WritePlan.cs`：`ManagedProps` 登记四个新属性（漏登记会让每次保存都当成「改过」）。
- `Services/WritePlanBuilder.cs`：`BuildMyMpcConf()` 写四个 RTX 键；`uiChanged` 纳入新属性；按 NVIDIA App 侧实际开关给 Warnings（不阻止写入）。
- `Services/NvidiaAppState.cs`（新增）：全槽位读 NVIDIA 驱动注册表的 VSR / TrueHDR 总开关。
- `Services/DefaultProfile.cs`：`DefaultDraft()` 补读 NV 开关，修 `--default` 误报。
- `Views/VideoView.xaml`：新增「NVIDIA RTX 视频增强」SettingCard（四项 + 运行前提说明行）。
- `Assets/MyMPC_ui.lua`：新增 ①d RTX 段（阈值表、`rtx_fit`、动态挂卸、桌面 HDR 判据、10bit 格式处理、峰值标注、事件注册）；`o` 默认值同步。
- `MpvSettings.csproj` / `Views/AboutView.xaml`：版本号。

**配置 / 脚本（模板 + 生产）**
- `12.mpv\portable_config\script-opts\MyMPC.conf`、`…\scripts\MyMPC_ui.lua`
- `<MPV配置目录>\` 下同两文件
- 两处 `mpv.conf` 的 `hwdec=auto-copy` **未改**

**发布产物**
- `MpvSettings\publish\MpvSettings.exe` → **1.0.2**，md5 `3a67514421e02ad909afe4a2700d45ae`，453,334 B（版本号仍 1.0.2，修复二次放大 + 行尾 bug）
- `12.mpv\MpvSettings.exe`、生产目录 `MpvSettings.exe`：与 publish 同 md5
- 备份：`_verify\exe_history\MpvSettings_1.0.1_*_before_102_20261118_011547.exe`（三处）
- 源码备份：`<备份目录>/`（移出项目目录，避免被 SDK glob 编译）

## 四、验证

**CLI（`_verify\rtx_verify.py`，40 项全过）**
出厂默认值（含 RTX HDR=yes）/ 四键反推 / 改 VSR 触发整文件重写 / 改 HDR+峰值+档位落盘 / 零改动幂等（字节哈希不变）
/ `--default` 恢复默认（含不误报未检测到驱动）/ 越界值回落 / RTX HDR 前提提示 /
NVIDIA App 侧开关真实注册表读数 + patch 注入模拟「关」出警告 / 生产目录哈希未变。

**端到端（真实 mpv + RTX 5070，`_verify\rtx_e2e.py`）**
- 放大窗口后 VSR 挂上：`NVIDIA RTX Super Resolution enabled`。最终滤镜串 `scale=1.001`（固定，不放大画面；
  画面放大由 window-scale 独立承担，避免二次放大）。真实 DVD 720×480 按 Alt+3 后 osd=1440（正好 2×）。
- 开 RTX HDR + 本机 HDR 常开：`NVIDIA RTX Video HDR enabled` + `NVIDIA RTX Super Resolution enabled`，
  `video-target-params/gamma=pq / primaries=bt.2020`，`max-luma=1200` 标注滤镜确实挂上。
- 生产目录配置直接跑（RTX HDR 默认开）：`VSR RTX-HDR (320x240)`，两条 `enabled` 日志同时出现。
- `d3d11va / auto-copy / no` 三种 hwdec 均实测生效。

**MPCVR 策略三态验证（`_verify\final_e2e.py` + IPC，真实 DVD 片 720×480，生产配置 PASS）**
- 100% 窗口（osd=720×480）：VSR 不挂（对应 NV App 显示关）
- Alt+3（window-scale=2）：挂 `scaling-mode=nvidia:scale=1.001`，osd=1440×960（正好 2×，无二次放大），
  `NVIDIA RTX Super Resolution enabled`
- 缩回 100%：VSR 摘掉，osd 回 720×480
- `_verify\jump_timeline.py` ms 级采样：按 Alt+3 后 osd 序列 `[1440,1440,...]`，跳变仅 1 次
  （修复前为 720→1440→2880 两次跳变，更早还有滚雪球到 8.5）。
- `_verify\threshold_probe.py` 四档阈值边界全对：1920×1080 片在 sd/720p 档 VSR 关、1080p/1440p 档开。

**Lua 语法**：`mpv --no-config --idle --script=<新 Lua> --msg-level=all=debug` 加载无语法错，
`reading options for MyMPC` 正常。

## 五、使用说明 / 已知边界

- **VSR 要生效**：NVIDIA App → 视频 → 开启「超分辨率」；源分辨率不超过所选档位；
  **画面必须以大于 100% 的尺寸显示**（放大窗口 / 全屏 / Alt+4 那种缩放）——与 MPCVR 完全一致：
  100% 原尺寸播放时 NV App 的 VSR 指示灯不亮是正常现象，把窗口放大即亮。
- **RTX HDR 要生效**：Windows HDR 已开 + NVIDIA App → 视频 → 开启「RTX Video HDR」+ 源是 SDR。
  峰值填写要和 NVIDIA App 里的「峰值亮度 (nits)」一致，否则高光偏暗/偏亮。
- 高噪点老片在 RTX HDR 下高光可能「沸腾闪烁」，这是驱动模型特性，FrankyRenderer 也一样。
- FrankyRenderer 另有 mpv 目前没有的能力（白电平探测、原生 HDR10 passthrough、Dolby Vision P7 FEL、
  自动切 Windows HDR），那些属于渲染器级整合，不是 `d3d11vpp` 滤镜能替代的。

---

## 六、补齐：硬件 / NVIDIA App / Windows HDR 不满足时自动禁用（2026-11-18 夜）

之前只弹「警告」，开关仍可开、配置仍写 yes——等于让用户开一个根本不生效的功能。本次补齐真正的自动禁用，并**新增 Windows HDR 直读检测**（此前设置程序完全没有，只靠 Lua 播放时间接判）。

### 6.1 判据（按「静态 / 动态」分开处理，这是核心）

| 前提 | 检测来源 | 不满足时的动作 |
|---|---|---|
| 硬件支持（有 NVIDIA 驱动） | `NvidiaAppState.ReadVsr/ReadHdr` 返回 `-1` | 开关置灰 + **写 `rtx_vsr=no` / `rtx_hdr=no`** |
| NVIDIA App 超分辨率总开关 | 注册表 `_User_Global_VAL_SuperResolution`，`0`=关 | VSR 开关置灰 + **写 `rtx_vsr=no`** |
| NVIDIA App RTX Video HDR 总开关 | 注册表 `_User_Global_VAL_TrueHDR`，`0`=关 | HDR 开关置灰 + **写 `rtx_hdr=no`** |
| Windows HDR | **新增**：CCD API 直读（见 6.2） | HDR 开关置灰 + **仅提示，不写 no**，配置保留 yes |

**为什么 Windows HDR 不写 no**：它是 Win+Alt+B 随时可切的动态状态。若因它写 no，用户开了 HDR 后配置仍是 no，Lua 永远不挂滤镜，还得回来重开开关。现在保留 yes，开 HDR 后 Lua 按 `video-target-params/gamma=pq` 自动生效，无需回来。这项是用户拍板的选择。

前三项是静态前提（不去 NVIDIA App / 装驱动不会自己变好），所以写 no 才是「真禁用」，与界面置灰一致。

### 6.2 Windows HDR 检测：照搬 setting.ahk 的 HdrWin* 函数组

新增 `Services/WindowsHdrState.cs`。不另发明——AHK 里 `HdrWin*` 已在本机验证过，逐行等价移植（AHK 用 `DllCall`+`NumPut` 摆字节，C# 用 P/Invoke+`byte[]`）：

- `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` 枚举活动路径，第 6 参必须 `NULL`（否则返回 87，AHK 踩过）。
- **两套 API 代际**（关键，别精简）：legacy（24H2 前）type=9 / 32 字节，`value` bit1=开启；modern（build≥26100 且 type=15 探测到能力位）36 字节，看末尾 `activeColorMode==2`。24H2 上旧 API 会「查询成功但值不对」，必须按 build 选代际。
- 任一活动显示器开启 HDR 即判定 Windows HDR 已开。检测失败一律当关，不抛异常、不阻止保存。

本机实测：Windows HDR=开、NV VSR=5（自动质量）、NV HDR=1，移植判据与 AHK 一致。

### 6.3 改动落点

- `Services/WindowsHdrState.cs`（新增）：Windows HDR 直读。
- `Models/SettingsDraft.cs`：`WindowsHdrEnabled` 只读状态；`RtxVsrEligible/RtxHdrEligible`（静态前提）；`RtxVsrInteractive/RtxHdrInteractive`（置灰）；`RtxVsrLimitInteractive/RtxHdrPeakInteractive`（下拉置灰）；`RtxVsrEnabledForWrite/RtxHdrEnabledForWrite`（写盘有效值）；`RtxVsrStatusText/RtxHdrStatusText`（动态原因说明）；`ApplyRtxEligibility()` 在反推 / 恢复默认时归零不合格开关。
- `Services/WritePlanBuilder.cs`：`BuildMyMpcConf` 用 `EnabledForWrite` 强制写 no；警告区分「已自动禁用（写 no）」与「Windows HDR 关（仅提示）」；新增 `rtxStale` 判据——盘上残留 yes 但前提不满足时，即使 diff 为空也重写 MyMPC.conf 纠正成 no。
- `Services/DefaultProfile.cs`：`DefaultDraft()` 先读三项环境状态再 `ResetAll()`（顺序要命：反了会把出厂默认误关）。
- `Views/VideoView.xaml`：开关 / 下拉绑定置灰属性；新增两行动态状态说明；更新运行前提文案。
- `00.Core/setting.ahk` 本次未改——VSR/HDR 的 NV App 检测它早已正确（扫全槽位 + `Format("{:04}")`），Windows HDR 检测也已存在（HdrWin*），本次是把同一套判据移植进设置程序。

### 6.4 验证

- `_verify/rtx_autodisable_verify.py`（新增，29 项全过）：前提全开保持 yes；VSR/HDR 各自总开关关只影响对应项、写 no；无驱动两项都写 no；Windows HDR 关保留 yes + Win+Alt+B 提示；草稿可用性/状态文案；盘上残留 yes 被 `rtxStale` 纠正；二次应用字节不变；恢复默认不被误关；生产哈希未变。
- `_verify/rtx_verify.py`（原 40 项，更新旧文案断言后仍全过）。
- 构建：`dotnet build`/`publish -c Release` 0 错误 0 警告。
- 三处 EXE 已同步，md5 `d8abd086884cf984885b8df105319d48`，459,990 字节（publish / 12.mpv / 生产 mpv 目录）。

---

## 七、实测确认：RTX Video HDR 开启时截图保留提亮（2026-11-18）

用户发现 mpv 开启 RTX Video HDR 后截图画面明显提亮，其他播放器做不到。实测验证并确认——**这是真效果，已烘焙进截图文件**。

### 7.1 为什么值得单独验证

快捷键体系里两个截图走不同路径：

| 快捷键 | mpv 命令 | 抓的帧 |
|---|---|---|
| `S`（Shift+S） | `screenshot video` | 纯 vf 滤镜链输出，不含字幕 |
| `s` | `screenshot subtitles` | vf 滤镜链输出 + 字幕叠加 |

RTX HDR 挂在 vf 链（`d3d11vpp:nvidia-true-hdr`），两条路径理论上都能吃到提亮。但 d3d11vpp 是 D3D11 硬件滤镜、输出 10bit PQ 帧，截图读回 CPU 再压成 8bit JPG 的具体行为必须实测，不能靠推理。

### 7.2 方法（同帧开关对照，非目测）

- 测试片源：`<测试视频目录>/<测试片名> - Obvious.4K.HEVC.mkv`，3840×2040 HEVC Main10，**SDR**（bt.709 / bt.1886，无 HDR10 标记）→ 会触发 RTX HDR 的 SDR→HDR 转换。
- 隔离环境：复制一份 `portable_config` 到独立目录、独立 IPC 管道，**不动生产配置**；桌面那张被 PS 编辑过的截图完全没参与分析。
- 同一 mpv 实例暂停在 00:02:57.880（177.88s）同一帧，只切 RTX 滤镜开关：开 → 摘掉 `@MyMPC-rtx` + `@MyMPC-rtx-luma` → 关 → 重新挂回 → 再截一张。
- 环境坐实：`hwdec=d3d11va-copy`，输出协商 `rgb10a2 / BT.2020 / PQ`，vf 两滤镜 enabled，`video-target-params/gamma=pq`。
- 用 `screenshot-to-file <path> video|subtitles` 分别截 `S` / `s` 两组，各 3 张。ffmpeg 解码成 raw gray，Python 统计亮度直方图 + 逐像素差分。

### 7.3 数据（8bit 灰度 0-255）

**`S` 截图（`screenshot video`）：**

| | 平均亮度 | 高光>200 | 过曝≥245 | 暗部<40 |
|---|---|---|---|---|
| RTX HDR 开 | 106.11 | 22.44% | 8.50% | 34.35% |
| RTX HDR 关 | 103.25 | 19.56% | 4.69% | 34.33% |

**`s` 截图（`screenshot subtitles`，含内嵌英文字幕轨）：**

| | 平均亮度 | 高光>200 | 过曝≥245 | 暗部<40 |
|---|---|---|---|---|
| RTX HDR 开 | 106.32 | 22.58% | 8.64% | 34.44% |
| RTX HDR 关 | 103.48 | 19.73% | 4.84% | 34.42% |

逐像素差分：
- `S`：开比关亮的像素占 **55.7%**，平均 +6.0 级，最大 +48 级。
- `s`：开比关亮的像素占 **55.4%**，平均 +6.0 级，最大 +48 级。
- 两组暗部占比几乎不变（34.35% vs 34.33% / 34.44% vs 34.42%）——是**高光/中间调专项提升**，不是整体洗白，黑位守得住。
- 可重复性：每组第 3 张（重新挂回滤镜后）与前一张开态 JPG **字节完全一致**（`S`=901927 B，`s`=944798 B）。

### 7.4 结论

**两种截图都保留提亮，效果一致。** `s` 因为多叠字幕，整体 avg 比 `S` 高约 0.2 级，但提亮幅度（+6 级 / 55%）几乎相同——字幕层没有吞掉滤镜效果。

原因：两条命令都抓 **vf 滤镜链输出**，而 RTX HDR 正挂在 vf 链里，提亮在帧被读回编码成 JPG 之前就已烘焙进去。其他播放器的 HDR 增强多在 vo / presenter 显示层，截图走视频帧路径时绕过了那层，所以抓不到——这就是 mpv 独有该效果的原因。

### 7.5 验证脚本与产物

- `_verify/hdrshot_test.py`（`S` 组）、`_verify/hdrshot_test_s.py`（`s` 组）。
- 产物目录：`<测试截图目录>/`（A/B/C + compare.png）、`<测试截图目录>/`（D/E/F + compare_s.png）。
- 本次只做验证，**未改任何产品代码或 EXE**；EXE 仍是 1.0.2 / md5 `d8abd086884cf984885b8df105319d48`。
- 环境备注：本机 ffmpeg 另有 `<ffmpeg目录>`；本次用的是 TopazVideo 自带 ffmpeg（无 drawtext 滤镜，对比图用 hstack 纯拼）。

---

## 八、HDR 片源截图过曝：根因与测试方法（2026-11-18）

### 8.1 问题

用户反馈：之前调的「HDR 色调修复」4 个值（0.5 / 0.6 / 0.8 / 1.0，AVS 截的参考图在桌面文件夹）是在**非真直通**下测的；切到真直通后这 4 个值截图全过曝。测试片源：`<测试片名> S02E09 ... DV HDR H 265-RAWR.mkv`（HEVC Main10，3832×1600，`smpte2084`/BT.2020，即 HDR10 + DV RPU）。

### 8.2 根因（不是参数没调好，是测试前提不成立）

1. **片源是 HDR，不是 SDR**。真直通 = `tone-mapping=clip` + `target-colorspace-hint=yes`，PQ 码值原样送显示器。此时 spline / contrast 这些色调参数**根本不参与运算**——它们只在 `tone-mapping=spline`（HDR→SDR）路径生效。所以真直通下扫那 4 个值，画面不会有任何区别。
2. **截图抓的是 vf 链帧，没有做 tone mapping**。日志实证：`screenshot video` 拿到的帧被标成 `rgb/bt.709/srgb`（见 `Starting screenshot ... input: ... rgb0 rgb/bt.709/srgb/full`），但内容是 PQ 码值——被当成 SDR 直接编码成 8bit，于是高光过曝发白。
3. **为什么 AVS / 其他播放器截图正常**：它们的截图走 **vo 渲染后的帧**，渲染时已经做过 HDR→SDR tone mapping。mpv 的 `screenshot video/subtitles` 走 vf 帧路径，绕过了 vo 那层——这就是差异来源。

### 8.3 测试方法（正确姿势）

**要测色调修复参数，必须先进 HDR→SDR 模式，否则参数不生效：**

```
tone-mapping=spline
target-colorspace-hint=no   （或删掉这行）
target-trc=bt.1886
```

这三项把 vo 输出目标钉成 SDR，spline / contrast 才会真正作用于画面。在这状态下扫 0.5/0.6/0.8/1.0 才有意义。设置程序里对应「HDR 转 SDR」模式；真直通下色调修复本就该置灰（现有逻辑已如此）。

**同帧对照要求**：用 `pause` + `seek <秒> absolute+exact` 固定同一帧，再切参数截图。不要学桌面那 4 张 AVS 图（时间戳 00.03.271/396/445/855 各不同帧，只能目测不能做像素对比）。

### 8.4 让截图自动转 SDR 的可行办法（实测）

在 HDR10 小片段（从该 DV 原件用 libx265 截 3–13s、保留 PQ/BT.2020 标记）上做同帧对照，用 `screenshot window`（抓 vo 渲染结果，天然带 tone mapping）：

| 方案 | 平均亮度 | 过曝≥245 | 高光>200 |
|---|---|---|---|
| 真直通（clip + PQ 输出） | 90.05 | **0.07%** | **2.87%** |
| 切 SDR + spline | 83.54 | 0.00% | 0.01% |
| 切 SDR + hable | 79.75 | 0.00% | 0.00% |
| 切 SDR + bt.2390 | 91.40 | 0.00% | 1.60% |
| 切 SDR + reinhard | 95.71 | 0.00% | 0.67% |
| 切 SDR + mobius | 89.44 | 0.00% | 0.83% |

真直通截图有过曝像素，切 SDR 目标 + 任一 tone-mapping 算法后过曝归零。**推荐做法**：截图时用 IPC/脚本临时把 vo 目标切到 SDR（`tone-mapping` 选 spline 或 bt.2390），截完恢复真直通——观看仍是真直通 HDR，截图自动是好看的 SDR，两不误。

**走不通的路（实测排除）**：在 vf 链挂 `libplacebo=tonemapping=spline:colorspace=bt709`，滤镜挂载成功但 7 种参数组合截出的 PNG **字节完全相同**（md5 全一致）——mpv 的 `screenshot video` 不消费该滤镜的 tone map 结果。别在这条路上浪费时间。

### 8.5 环境坑

- 9GB DV 原件直接做 `screenshot video` 会偶发在 12 线程 scaling 阶段进程退出（日志停在 `using 12 threads for scaling`）；先把片段截成 HDR10 小文件再测，稳。`screenshot window` 无此问题。
- `screenshot-high-bit-depth=no` 下 JPG/PNG 是 8bit，gray 解码 1 字节/px；统计前确认位深。
- 该文件带 DV RPU，mpv 用基础层 HDR10 解码（video-params gamma=pq / primaries=bt.2020）。

### 8.6 产物

- 验证脚本：`_verify/hdr_screenshot_probe.py`、`hdr_tonemap_solution.py`、`hdr_tonemap_v2.py`、`hdr_window_only.py`。
- HDR10 测试片段：`<测试片目录>/clip_hdr10.mkv`（用 `<ffmpeg目录>` 的 libx265 截）。
- 对比图：`<测试截图目录>/compare3.png`。
- 本次仅验证，**未改产品代码/EXE**；EXE 仍 1.0.2 / md5 `d8abd086884cf984885b8df105319d48`。（注：色调档位后续已定档改动，见第九节。）

---

## 九、HDR 色调修复档位定稿（11-18 量化方案，后作废，2026-11-18 深夜）

### 9.1 最终档位（真直通 PQ 输出下逐档 AHK 实测定稿）

原值 `4/16/39/55` + brightness 5 在真直通下整体偏亮、高档重度过曝（39/55 过曝率 11~12%）、0.5 档就≈AVS 1.0。经多轮 AHK 实时下调（用户双击即测），最终定档：

| 档位 | inverse | contrast | brightness | saturation |
|---|---|---|---|---|
| 关闭/原片 | no | 0 | 0 | 0 |
| 0.5 | yes | **1** | **0** | 0 |
| 0.6 | yes | **4** | **0** | 0 |
| 0.8 | yes | **10** | **0** | 0 |
| 1.0 | yes | **17** | **0** | 0 |

- 用户主观标定：0.5 档≈AVS 0.7（AVS 无 0.7 档，介于 AVS 0.6~0.8 之间）。
- contrast 全部落在不过曝区（扫描实测 ≤18 贴基线 clip≤1%）；brightness 由 5 归零是本次整体降亮度的关键。
- 改动落点：`00.Core/setting.ahk`（写入 `MpvApplyTone` + 回读识别 + 注释表三处）、`Services/WritePlanBuilder.cs` 的 `Tone` 表、`Models/SettingsDraft.cs` 回读识别、4 个验证脚本的档位常量。
- 回读验证：contrast 1/4/10/17 分别识别为 ToneIndex 1/2/3/4，无 Custom 误判。
- 编译 publish 0 错 0 警；feature 全套通过（含直通写 contrast=17）；EXE 三处同步，md5 `2078e35fa32f539693eabbe9a3064fc6`（459,990 B）。
- 注：`stage2_verify.py` 的 baseline 期望表写死出厂默认，但生产 mpv.conf 已被改过（hwdec=auto-copy / contrast=4 / screenshot-format=png），其 5 项失败是"生产配置≠出厂默认"的历史测试设计问题，与本次改动无关（hwdec/截图格式也一起失败可证）。

---

## 十、安装包配置环境恢复出厂默认（2026-11-19 凌晨，收尾）

定档测试全部结束后，把**安装包模板环境**（`<项目根>/12.mpv/`，即 `<安装包脚本>.iss` 第 164 行的打包源）恢复回出厂默认态，确保用户首次安装 / 修复安装拿到的配置是干净的。

### 10.1 执行方式

走设置程序自带的唯一恢复默认入口（不是手工改文件）：

```
MpvSettings.exe --default --config-dir="<12.mpv>\portable_config" --keep-registry
```

- `--default`：`DefaultProfile.Build()` → `SettingsDraft.ResetAll()`，与安装包安装时（iss 第 476 行）同一套逻辑，不另抄键表。
- `--config-dir`：显式锚定安装包模板，绕开本机 App Paths 注册表探测（本机没有 mplayerl.exe 的 App Paths，不能让它回退找错目录）。
- `--keep-registry`：注册表 `HKCU\Software\BassMax\Enabled`（音效开关，出厂=1，本机当前=0）属于**本机运行时状态**，不属于安装包环境；跳过，不动用户本机音效设置。

### 10.2 结果核对

- **HDR 色调修复 = 关闭（默认）**：`inverse-tone-mapping=no` / `contrast=0` / `brightness=0` / `saturation=0`，`tone-mapping=clip`。这四键在 dry-run 计划里**根本没出现**——说明盘上已是出厂值，无需写入。
- 出厂草稿定义复核：`SettingsDraft.SetVideoDefaults()` 中 `ToneIndex = 0`（档位标签 `"关闭（默认）"`）；AHK 侧 `MpvApplyTone(0)` 同为清零态。定档值 `1/4/10/17` 只是用户主动选档时才写入，**不影响出厂默认关闭**。
- `mpv.conf`：恢复前后**逐字节无变化**（diff 空）。
- `script-opts/MyMPC.conf`：仅删掉 9 个空行（EXE 的标准紧凑格式），9 行键值全部不变（`rtx_vsr=yes` / `rtx_hdr=yes` 等出厂开关保留）。
- `cover-art.conf`：内容与出厂一致，未产生写入。
- EXE 未重编、未改动：仍是 1.0.2 / md5 `2078e35fa32f539693eabbe9a3064fc6`（459,990 B），`12.mpv` 与 `publish` 两处一致。

注：`_MPV_阶段2_实测` / `feature_20261117` 等 `_verify` 子目录里的 `contrast=17`、`tone-mapping=spline` 等是**验证夹具**，故意保留作回归基线，不是安装包内容，不参与打包。

---

## 十一、HDR 色调修复档位体系重构定稿（2026-11-19 晚）

### 11.1 背景：量化定档方法整体作废

此前（第八节）的 contrast 1/4/10/17 与 0.4 档（brightness -5）在 2026-11-19 晚被证伪：

- **截图链路不可靠**：mpv 的 shift+s 截图取原始帧、不走 vo 色调映射链，0.5 档与关闭档截图几乎逐像素一致，但人眼差别巨大。此前所有靠截图 YAVG/SSIM 的定档数据、以及跨播放器（MPC-BE/MPCVR）对比数据全部作废。
- **AVS 截图也不可靠**：AVS 0.8 截图看过曝、实际观感不过曝，不再参考 AVS。
- **HDR10 / DV 一度被误判为方向相反**：最终确认是混淆 contrast 与片源元数据所致。

### 11.2 源码结论：提亮主力是 inverse-tone-mapping

翻本机 mpv v0.42.0-94 / libplacebo 源码确认：

- `--inverse-tone-mapping` 由 `vo_gpu_next.c:2961` 传给 libplacebo，本质是解除 `colorspace.c:2016` 的"输出峰值不得超过源峰值"限制，使 spline 进入逆映射分支（`tone_mapping.c:135`），把 PQ 信号朝显示器峰值拉伸——**这就是提亮的唯一主力**。
- `contrast` 只是 equalizer 增量梯度微调；`spline_contrast` 在逆映射下只进 slope 指数 `(1-sc)*0.2`（`tone_mapping.c:573-577`），0.5→0.8 肉眼无区别，**不是**提亮强度旋钮。
- 纯 inverse 下 <测试片名> S02E09 提亮不够、<测试片名> S01E08 已很好——印证提亮幅度由片源元数据 + 显示器目标峰值决定，是设计如此。

### 11.3 定档结果（峰值 2000nits / DisplayHDR 1400 显示器上人眼实测标定）

| 档位 | tone-mapping | inverse-tone-mapping | contrast | brightness | saturation |
|---|---|---|---|---|---|
| 关闭（默认） | clip | no | 0 | 0 | 0 |
| 0.6 | spline | yes | **0**（纯 inverse 基线） | 0 | 0 |
| 0.8 | spline | yes | **6** | 0 | 0 |
| 1.0 | spline | yes | **12** | 0 | 0 |

- **取消 0.5 档**：mpv 默认提亮幅度已较大，0.5 与原片差别太小无意义；AVS 侧 0.5 本就是非常轻微的提亮、可用可不用（AviSynth+ 页的 0.5 按钮保留不动）。
- **HDR10 与杜比视界通用同一套**，不再按片源分支（P5/P8/混合型同档）。
- spline_contrast 保持默认 0.5。

### 11.4 落地范围

- `00.Core/setting.ahk`：档位按钮 6→4（去 0.5/0.4）、MpvApplyTone tier 重映射、回读 / 禁用块 / mCtlList / 点击分支 / 底部注释表全部对齐，GUI 加峰值 2000nits / DisplayHDR 1400 说明。
- 本项目 EXE 源码：`Services/WritePlanBuilder.cs` Tone 表 5→4 档、`Models/SettingsDraft.cs` ToneLabels 与回读、`Views/VideoView.xaml` 说明。
- `MyMPC_ui.lua`：无 HDR/DV 自动识别代码（此前仅调研未实现），无需改动。
- 临时三态对比脚本（tone-3way.lua）与 input.conf 改动已清理还原。
- 打包源 `12.mpv` 仍为出厂关闭档，安装包默认行为不变。

### 11.5 重编译并升版本 1.0.3（2026-11-19 晚，已落地）

- **版本号 1.0.2 → 1.0.3**，改 2 处：`MpvSettings.csproj`（Version/AssemblyVersion/FileVersion/InformationalVersion → 1.0.3 / 1.0.3.0）、`Views/AboutView.xaml`（「MPV 简单设置 1.0.3」）。标题栏 `· v1.0.3` 由 `MainWindow.xaml.cs` 读程序集 `InformationalVersion` 自动显示，无需改 XAML。
- `dotnet build -c Release`：**0 警告 0 错误**；`dotnet publish -c Release -r win-x64 --self-contained false -o publish`：成功。
- 新 EXE `publish\MpvSettings.exe`：FileVersion `1.0.3.0` / ProductVersion `1.0.3` / **459,478 B** / md5 `1161505a02141fadca50940df8fc1355`（旧 1.0.2 为 459,990 B / `2078e35fa32f539693eabbe9a3064fc6`）。
- 三处逐字节一致：`publish\`、`12.mpv\MpvSettings.exe`、生产 `<MPV安装目录>\MpvSettings.exe`（md5 全部 `1161505a...`）。旧 EXE 备份 `<项目根>/_bak_exe_103_20261119_222959\`（publish/12mpv/production 三份）。
- **验证**：新增 `_verify\tone_tiers_103_verify.py`，隔离目录跑 **18/18 全过**——四档写入（ToneIndex 0/1/2/3 → inverse=no/yes/yes/yes、contrast=0/0/6/12，含落盘值）、四档反推（盘上四键 → ToneIndex）、关闭档干净态幂等无改动。打包源 `12.mpv` 的 mpv.conf 仍为出厂关闭档，安装包默认行为不变。

### 11.6 隐藏显示器型号，改用亮度参数（2026-11-19 晚）

- 用户考虑不暴露具体显示器型号，把产品文件里所有「海信 G7 Ultra 27」替换为**「峰值 2000nits / DisplayHDR 1400」**（该机标称峰值 2000nits、DisplayHDR 1400 认证）。替换后全项目 grep「海信 / G7 Ultra」零残留。
- 涉及 8 处：`00.Core/setting.ahk`(3，含 GUI 可见说明)、`Models/SettingsDraft.cs`、`Services/WritePlanBuilder.cs`、`Views/VideoView.xaml`(可见档位说明)、本文档 11.3 标题与 11.4 正文。
- **保持 1.0.3 重编**（仅文案改动，非功能变更）：`dotnet build`/`publish` 0 警告 0 错误。新 EXE md5 `20723e2817eff26c1a5d3e336ad5df9d`（459,990 B）——注意 11.5 中的 `1161505a...` 是文案替换前的 1.0.3 中间产物，**最终交付以本节的 20723e28 为准**。三处（publish / 12.mpv / 生产）逐字节一致，档位回归 18/18 仍全过。
- 注：本机工作日志仍保留「海信 G7 Ultra 27」历史原文（内部记录、不随产品分发），如需一并匿名可再处理。

### 11.7 补充杜比视界 Profile 支持说明（2026-11-19 晚）

- 用户在 `setting.ahk` 色调档位 GUI 文案补一行「支持杜比视界Profile4、5、7、8 (其他Profile未测试)」，要求同步进 EXE。
- 同步到 `Views/VideoView.xaml` 的「色调增强档位」Description（替换原「HDR10 与杜比视界通用」表述，与 AHK 界面文案一致）。注：`setting.ahk:473` 那句「本设置不支持杜比视界的Profile 5」属 **AviSynth+ 色调修复页**（另一功能），与 mpv 档位无关，未动。
- 事实核查：主流 DV Profile 为 **5（单层串流）/ 7（双层蓝光）/ 8（单层兼容 HDR10，含 8.1/8.2/8.4）**，P4 为冷门遗留；Profile 20 是 2023 年 Apple Vision Pro 的 **MV-HEVC 立体 3D** 专用，与「杜比视界第二代(Dolby Vision 2，2025-09 IFA 发布)」无关——DV2 是新图像引擎+元数据、分标准版/Max 的**电视授权等级**，非新 profile 号，且仅电视端支持，不影响 mpv 本地播放识别。
- **保持 1.0.3 重编**（第 3 次，仅文案）：build/publish 0 警告 0 错误。最终 EXE md5 `5751bd52f5dce8641162f78382db5d65`（460,502 B）——**最终交付以本节为准**（11.5 的 1161505a、11.6 的 20723e28 均为历次文案中间产物）。三处逐字节一致，档位回归 18/18 全过。

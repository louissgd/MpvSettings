# MPV 设置 EXE · V1 参数清单与界面布局

日期：2026-11-12
范围：**V1 规格已重新冻结（2026-11-13 01:56，11 项全部拍板，无待定项）**。01:20 追加的两项已定为「方案 B + 单实例合并成一份 `MyMPC_ui.lua`」，见 1.6。**.NET 10 SDK 10.0.404 已装（2026-11-13 20:04）；阶段 ① 静态骨架已于 20:2x 完成并实测通过**（5 页导航 + 单文件 EXE，见 6.2 节）；**阶段 ② 读盘 / 反推 / 搜索 / 重置已完成并实测通过**（见 6.5 节，仍不写盘）；**阶段 ③ 草稿 diff 写盘 + 联动校验已完成并实测通过**（见 6.6 节，只写改过的键，生产配置零污染）。目标是一个独立 EXE，选 MPV 时由「打开主播放器进行自定义设置」按钮拉起；它只是 `setting.exe` 快速设置的一个入口，不接管已有页面。**V1 定为 5 页：播放 / 画面 / 音频 / 截图 / 快捷键**。布局风格仿 FrankyRenderer，但**配色已换为自有墨绿 + 薄荷**（见 3.2，不再取它的 Indigo + Violet），规模控制在 MPC-HC 设置页量级，不向 PotPlayer 的选项爆炸靠拢。

> **2026-11-13 20:07 追加排期**：`setting.ahk` 今日 19:24 已更新——MPV 是只读配置文件的播放器，配置更改时**只给「重启 MPV 后生效」提示、不关闭播放器**（详见第 0 / 5 节）。而「MPV 默认设置 → 备份文件 / 默认模板」**等 EXE 全部设置定稿后放到最后做**（新增阶段 ⑨），因为默认态取决于 EXE 最终键集。阶段 ①–⑧ 不受影响。

---

## 0. 现状核查（已确认的事实）

| 事实 | 证据 |
|---|---|
| 「打开主播放器进行自定义设置」对 MPV 无效是代码层面的必然，不是配置问题 | `00.Core\setting.ahk` 的 `mplayerl:` 标签（2107–2129）：算出了 `selectedPath` 但**从未使用**，最后无条件 `Run, mplayerl.exe`（无参数）；`killProcesses(allplayer)` 的列表只有 `mpc-hc64.exe / mpc-be64.exe / PotPlayerMini64.exe`，不含 `mpv.exe` |
| MPV 的真实配置入口 | 安装根目录 `MediaPlayer\mpv\portable_config\`：`mpv.conf`（播放选项）、`script-opts\hdr-autoswitch.conf`（HDR 脚本）、`input.conf`（10 行按键）、`menu.conf`（右键菜单）。`12.mpv\` 是打包源目录，不是运行目录 |
| setting.exe 已经有一整套 MPV 读写逻辑可复用语义 | `MpvReadKey / MpvWriteKey / MpvGet / MpvSet / MpvOptGet / MpvOptSet`（880–952）、`MpvApplyTone`（1026）、`MpvRefresh`（1066）、`MpvSyncGlobal`（1159）、`MpvAction`（1176）、`MpvRestoreDefaults`（1305）。说明块集中在 2715–2870 行。**已无 `MpvWriteDefaultConf` 函数**：恢复默认改为从 `*_default.conf` 模板整文件覆盖 4 个文件 |
| MPV 的「自带音效链路」不是 setting.exe 的 BassMax DShow 滤镜 | `af=bassmax` 是 mpv **进程内音频滤镜**：`mpv -> af_bassmax -> 声卡`，没有 VB-Cable、没有中继、无额外延迟。参数读 `HKCU\Software\BassMax`，托盘 `bassmax_tray.exe` 每 200 ms 轮询；`Ctrl+b` 用 `af toggle bassmax` 在增强/旁通间切换。与 `audio-spdif` 互斥。**已确认**：`HKCU\Software\BassMax` 是统一注册表，mpv 的 `af_bassmax` 与 MPC-HC / MPC-BE 的 DShow 滤镜都读同一处，EXE 直接写即可，不用按播放器各写一份 |
| 快捷键分两层，且默认键不在任何配置文件里 | `input-bindings` 实测：内置默认 **220 条**（`is_weak=true`，编译时烤进二进制）+ 本包 `input.conf` **10 行**强绑定 = 实际生效 **230 条**。`etc/input.conf` 只是全注释模板。`input.conf` 语法为 `KEY command`（无 `=`、LF 无 BOM），与 `mpv.conf` 完全不同；`setting.exe` 日常不碰它，但「恢复 MPV 默认配置」会用 `input_default.conf` 整文件覆盖（第 4 节第 12 条 / 阶段 ⑨）。详见 1.3 与全表文件 |
| 无封面音频的「大黑框」是 `--force-window` 的空窗口，不是 bug | 手册原文：无视频时窗口**固定 960x540**，且明确「subject to `--geometry`, `--autofit`」。本机截屏实测 **982x596**（含边框）；加 `geometry=640x180` 后变 **662x236**。见 1.4 |
| 双击媒体文件走的是 `mplayerl.exe "%1"`，单实例判断归播放器自己 | `Assoc.ahk` 210–215 行把 `HKCR\MyMPC_Assoc.<ext>\shell\open\command` 写成 `mplayerl.exe "%1"`，**不带任何复用参数**；`mplayerl.exe` 二进制（UTF-16 字符串）只读 `%s\mplayerl.ini` 的 `[Settings] MainPlayer`，另有 `--follow "%s"` 与 `bassmax_tray.exe` 字符串但关联命令不使用。**mpv 全表无单实例选项**，只有 `--input-ipc-server` / `--input-ipc-client`。见 1.5 |
| `setting.ahk` 的 `killProcesses(allplayer)` 与多实例无关 | 它是写注册表/ini **前**关掉播放器，防止播放器退出时回写覆盖（455–456、2764–2765 行注释写明）。多实例行为不归 `setting.ahk` 管，本规格不改它；**2026-11-13 19:24 版本已按「只提示不关播放器」落地**：MPV 配置更改只在状态栏提示「重启 MPV 后生效」（1270–1274、1789–1795），不杀进程。唯一例外是 `DEF:`「恢复为默认设置」仍传 `allPlayerWithMpv`（含 `mpv.exe`），留阶段 ⑨ 复核 |
| MPV 只读配置文件，退出不回写 | 因此无需为 MPV 做「写前关进程」。`MpvRestoreDefaults:`（1305）从 `mpv_default.conf` / `input_default.conf` / `menu_default.conf` / `hdr-autoswitch_default.conf` 四个模板整文件覆盖；模板由打包阶段生成，**源目录 `12.mpv\portable_config\` 里不存在**，需 EXE 定稿后统一生成 → 阶段 ⑨ |
| `osd-msg1` 不支持 ASS，但 Lua 的 `ass-events` overlay 支持 | 五组渲染路径实测：`mp.osd_message` / `show-text` 会把 `{\fs44}` 当文本打印；`create_osd_overlay("ass-events")` + 显式 `res_x/res_y`、`mp.set_osd_ass()` 都能正确渲染混排字号。见 1.6 |
| 真频谱在 V1 做不了 | 命令行传 `--lavfi-complex` 能出帧，但**运行时由 Lua `set_property` 设置会段错误崩溃**（exit 139，0 帧）；条件 profile 里设则自撤销死锁。V1 用 ASS 绘制的轻量柱形装饰代替，真频谱留 V2（需改启动链或走 `vf-metadata`） |
| 同名 IPC pipe 不会拦住第二个实例 | 实测第二个 mpv 打印 `Couldn't create first pipe instance: 拒绝访问 (0x80070005)` 后**仍正常播放到结束**（returncode 0）。所以单实例必须由脚本主动检测并转发，不能指望 mpv 自己拒绝 |

**关于「UI 映射方案.docx」的评判**：技术选型（WPF / Tauri / PySide6）和「卡片 + 大标题 + 灰色副标题」的视觉方向可用；其余三点要砍掉——

- 「画质预设 `scale=ewa_lanczossharp`」「插帧平滑开关」是通用 mpv 模板，你这套用的是 `vo=gpu-next` + libplacebo + 独立 HDR 脚本，`mpv.conf` 里根本没有 `scale`，也没做播放器插帧。照抄就是引入两套不存在的选项，正是选项爆炸的起点。
- 「检测 setting.exe 进程 + 独占打开 mpv.conf + 文件锁」是过度设计，且前提就错了。**setting.exe 是快速设置，新 EXE 只是它的一个按钮入口**；两个程序都要点击才写入，同时打开时以最后一次点击为准，跟其他播放器的设置页一样，不需要接管、互斥或进程检测。长期持锁反而会被杀软盯上。真正需要的是**写前重读 + 只写 diff 键 + 原子替换**（见第 4 节）。
- 「20 个键」这个数量级没问题，但键清单必须从你自己的 `mpv.conf` 长出来，不能从模板长出来。

---

## 1. V1 参数总表（5 页 / 约 28 个选项控件 + 38 行快捷键清单 / 约 31 个 conf 键）

标记：`●` 必做，`○` 可砍（先留位置，做不动就砍）。

### 页 A · 播放（常规）

| | 界面控件 | 类型 | 写入键 | 取值 | 出厂默认 | 反推 / 联动 |
|---|---|---|---|---|---|---|
| ● | 硬件解码 | 下拉 | `hwdec` | `auto-safe` / `auto` / `d3d11va` / `nvdec` / `no` | `auto-safe` | 值不在列表 → 显示「自定义」并置灰，不覆盖 |
| ● | 反交错 | 开关 | `deinterlace` | `yes` / `no` | `yes` | — |
| ● | 单曲循环 | 开关 | `loop-file` | `inf` / `no` | `inf` | 与 setting.exe `Button3` 同键，语义必须一致 |
| ● | 保留播放进度 | 开关 | `save-position-on-quit` | `yes` / `no` | `no` | 与 `Button5` 同键 |
| ● | 字幕轨优先片源默认轨 | 开关 | `subs-match-os-language` | `yes` / `no` | `yes` | 与 `subs-fallback-forced` 是一组，别拆到两页 |
| ● | 无默认轨时不抢 forced 轨 | 开关 | `subs-fallback-forced` | `yes` / `no` | `no` | — |
| — | 外挂字幕优先 | **只读说明，不做控件** | 不写任何键 | mpv 默认 `sub-auto=exact` | 已开启 | 见 1.1；副标题写「同名外挂字幕自动加载并优先于内封字幕」 |

**已砍**：「启动时显示统计面板」（`load-stats-overlay`）——与播放状态统计混淆，低频无价值；`Ctrl+Tab` 手动呼出仍可用，EXE 不写这个键。

### 页 A · 纯音频封面（新增，见 1.2）

| | 界面控件 | 类型 | 写入位置 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| — | 默认显示封面 | **只读说明，不做控件** | 不写键 | `audio-display=embedded-first` + `cover-art-auto=exact` | 已开启 |
| ● | 封面按原始大小显示 | 开关 | `cover-art.conf`: `video-unscaled` | `downscale-big` / `no` | `downscale-big` |
| ● | 封面窗口上限 | 滑块 | `cover-art.conf`: `autofit-larger` | `400x400` ~ `1600x1600`，步长 100（上限 1600 已确认） | **`1200x1200`** |

副标题统一写「仅对纯音频封面生效，不影响视频播放」。关闭「按原始大小」时把 `video-unscaled` 写 `no`，`include` 行与文件保留，下次开启无需重建。

### 页 A · 无封面音频（新增，见 1.4）

| | 界面控件 | 类型 | 写入位置 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| ● | 无封面时显示紧凑信息窗 | 开关 | `script-opts\MyMPC.conf`: `no_art_ui` + `include=~~/audio-info.conf` | 开启 / 关闭 | **开启** |
| ○ | 窗口尺寸 | 只读说明 | `audio-info.conf`: `geometry` | `640x180` | 固定，不做控件；关闭态见 1.6 |

副标题如实写：「显示时间码、曲目信息与进度条；右上柱形为装饰动画，非真实频谱，格式信息显示在柱形下方」。别写成「频谱可视化」，用户实测会期望落空。

**V2 布局（2026-11-13 02:05 用户拍板，实测包已改并截屏）**：左上时间码由 `H*0.27` 缩到 `H*0.155`；歌名由 `H*0.10` 放大到 `H*0.14`，按词折行、最多 2 行、超长末尾加省略号（避免把 `.ape` 单独拆成第二行）；`格式 + 采样率 + 声道` 从左侧移走，改为 `\an9` 右对齐放在柱形正下方；总时长/曲序行不再带格式字段。真实截屏 `MPV无封面音频_信息窗_布局V2.png`。

### 页 A · 播放行为（新增，见 1.5）

| | 界面控件 | 类型 | 写入位置 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| ● | 允许同时运行多个播放器 | 开关 | `script-opts\MyMPC.conf`: `single_instance` | `no` / `yes` | **关闭**（= 双击文件复用当前窗口） |

副标题必须写清语义：「关闭时，双击媒体文件送入当前窗口；直接运行 `mplayerl.exe` 仍可开新窗口」。别写成「进程管理」或「多开」，用户会理解错。

### 页 A · Alt+4 屏幕比例（2026-11-14 22:2x 用户修正）

| | 界面控件 | 类型 | 写入位置 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| ● | 屏幕比例 | 滑块 | `script-opts\fit90.conf` 的 `ratio`（阶段 ⑦ 已落地） | 50% ~ 100%，步长 1% | **90%** |

**语义（2026-11-14 22:2x 用户亲口修正，此前理解有误）**：五个键分成两套基准，**只有 `Alt+4` 按屏幕，其余全按视频分辨率**：

| 键 | 基准 | 行为 |
|---|---|---|
| 双击打开视频 | 视频分辨率 | 默认 **100%**（mpv 原生 `window-scale=1.0`），任何脚本都不得插手 |
| `Alt+&#96;` | 视频分辨率 | 25%（`set window-scale 0.25`） |
| `Alt+1` | 视频分辨率 | 50%（`set window-scale 0.5`） |
| `Alt+2` | 视频分辨率 | 100%（`set window-scale 1.0`） |
| `Alt+3` | 视频分辨率 | 200%（`set window-scale 2.0`） |
| `Alt+4` | **屏幕** | 窗口 = 屏幕可用区域的 N%（等比、不超出），**只在按键那一刻生效** |

⚠ **2026-11-14 21:5x 那一版把「我的原意是自动」理解成「每次打开文件自动套 90%」，是错的**——那会直接推翻「双击视频默认按视频分辨率 100%」这个前提。`auto` 选项已从 `fit90.conf` 与 `fit90.lua` 中删除，`file-loaded` 自动应用整段移除；90 仍是出厂默认，但只对「按一次 `Alt+4`」有意义。详见 6.13。

**UI 落点**：卡片标题「Alt+4 · 按屏幕缩放」，第一行「屏幕比例」+ 滑块（说明写「只在按键时生效，不改变双击打开视频时的默认尺寸」），第二行只读说明「其它窗口键都按视频分辨率」并列出 100% / 25% / 50% / 100% / 200%，第三行只读说明「Alt+4 不生效的情况」（纯音频信息窗、有封面音频），右侧注「比例写入 `script-opts\fit90.conf`」。

### 1.1 「外挂字幕优先」查证结论：mpv 原生支持，且默认已开启

**结论：支持，而且比 MPC-HC 更硬。mpv 没有叫「prefer external subtitles」的开关，这个行为由 `sub-auto`（默认 `exact`）驱动，源码里外部字幕轨是自动选轨的第一顺位。已定（2026-11-12）：既然是原生默认值并已生效，V1 不做开关、不写任何键，只在字幕卡片加一行只读说明。**

实测（本机 `12.mpv\mpv.com v0.42.0`；用 ffmpeg 造 `out.mkv`：内封 srt 标 `default` + `lang=chi`，同名 `out.srt` 为外挂；Lua 在 `file-loaded` 打印 `track-list` 的 `sid / external / default / forced / selected`）：

| 场景 | 实际选中 | 说明 |
|---|---|---|
| 全默认（`sub-auto=exact`） | `sid=2` external | 内封的 `default` + `chi` 全被压过 |
| `--slang=chi,chs,zh` | `sid=2` external | 语言偏好翻不了盘 |
| 内封标 `default+forced` | `sid=2` external | forced 也翻不了盘 |
| 上述 + 现有 `subs-match-os-language=yes` / `subs-fallback-forced=no` | `sid=2` external | 现有两键不影响 |
| `--sub-auto=no` | `sid=1` 内封 | 外挂轨根本不加载 |
| `--autoload-files=no` | `sid=1` 内封 | 连加载带自动选择一起禁掉 |
| `--sub-auto=fuzzy` / `all` | `sid=3` = `out.eng.srt` | 把同目录无关字幕全拉进来，反而选错 |

源码与手册证据（mpv master）：

- `player/loadfile.c` 555–586 行 `compare_track()` 的排序注释：`0a) tid 匹配 → 0b) 非 --external-file → 1) track is external (no_default cancels this) → 1b) 显式传入 → 1c) program ID → 2) 语言列表 → 3a/3b) forced → 3c) default → 3d) os_langs → 5) 轨号`。代码里 `ext1 != ext2` 时直接 `return ext1`，**在语言、default、forced 判断之前就返回**。
- 同文件 727 行 `subs_fallback = (track->is_external && !track->no_default) || ...`：外部轨直接豁免 `--subs-fallback=default`（只选 default 轨）的限制；710 行 `if (!opts->autoload_files && track->is_external) continue;` 对应 `autoload-files=no`。
- `DOCS/man/options.rst` 2913 行：`--sub-auto=<no|exact|fuzzy|all>`，`exact` 默认，含义是「加载与媒体文件名同名字幕 + 可能的语言后缀」。本机 `--list-options` 取值一致，**没有** `--sub-*-prefer-external` 这类开关（`--audio-display=external-first` 管的是封面图，不是字幕）。

**MPC-HC / MPC-BE 对照**：MPC-HC 的「字幕 → 杂项」里有 `Prefer external subtitles over embedded subtitles`，勾上外挂优先、不勾则内封优先，**两种状态都仍加载外挂**。MPC-BE 没有这个开关，只有 Autoload paths + Default track preference，默认优先挑带 `Default` / `Forced` 的内封轨，自动加载的外挂常被压住——这就是它做得不好的地方。mpv 相当于 MPC-HC 的打勾态，且优先级更靠前，完全不受语言/default/forced 干扰。

三条落地约束：

1. 现有 `subs-match-os-language=yes` / `subs-fallback-forced=no` **保留不动**。它们管的是「没有外挂时怎么在内封轨里挑」，与外挂优先不冲突；`setting.exe` 恢复默认说明里的「字幕优先片源默认轨，其次系统语言中文」说的就是这一层。
2. **不写 `fuzzy` / `all`**。实测会把同目录所有字幕（含无关文件）全部加载，还可能选中带语言后缀的那条而非同名主字幕；这正是选项爆炸的入口。
3. **不碰 `autoload-files`**（默认 yes，关了会连带禁掉外部音轨和封面图）；**不给 `slang` 加控件**（一旦设置 `slang`，`subs-match-os-language` 被完全忽略，会破坏现有策略）。`menu.conf` 107 行已有「字幕 → 字幕轨」动态列表，手动切轨路径是通的。

**为什么不做开关**：mpv 没有「外挂仍加载、但内封优先」的原生状态（源码 `no_default` 只对 `--external-files` 强制添加的非目标类型流置位，配置层不暴露），OFF 只能等价于「不自动加载同名外挂字幕」，是个半吊子语义。既然原生默认已满足需求，V1 不提供它；要严格复刻 MPC-HC 两态，V2 得加 Lua（`sub-auto=no` + `file-loaded` 扫目录 + `sub-add <file> auto`），会打破「scripts 只读」边界。由于不写 `sub-auto`，也不存在与 `MpvRestoreDefaults:`（整文件重建、模板无此键）的冲突。

### 1.2 纯音频封面：默认显示 + 原始大小 + 尺寸上限（能做到，纳入 V1）

**结论：能做到，纯配置实现，不写 Lua、不改 mpv、不改启动链。** 代价是 V1 里唯一一处需要「额外配置文件」：`mpv.conf` 加一行 `include=~~/cover-art.conf`，并新建 `portable_config\cover-art.conf` 放一个条件 profile。

「默认显示封面」本身是 mpv 默认，不用写键：`audio-display=embedded-first`（内嵌封面优先，手册原话「对有正常视频轨的文件无影响」）+ `cover-art-auto=exact`（同目录 `cover.jpg` / `folder.jpg` 等自动加载，默认白名单含 `AlbumArt / cover / front / Folder / thumb`）。实测内嵌封面轨是 `type=video image=true albumart=true selected=true`，单独的纯音频文件靠同目录 `cover.png` 也会挂上外部封面轨。

真正要新增的是「按原始大小显示 + 上限」，且**必须只对纯音频生效**：`video-unscaled` / `autofit-larger` 是全局窗口选项，直接写进 `mpv.conf` 会污染视频播放（小分辨率视频不再放大、大视频行为改变）。mpv 的条件自动 profile 正好解决。

`portable_config\cover-art.conf`（EXE 独占管理，完整读写）：

```
# 由 MPV 设置 EXE 管理：纯音频封面显示，勿手工改动段落名
[cover-art]
profile-cond=p["current-tracks/video/image"] == true
profile-restore=copy
video-unscaled=downscale-big
autofit-larger=1200x1200
```

`mpv.conf` 末尾追加一行（**路径必须带 `~~/` 前缀**：不写前缀时 mpv 按工作目录找，实测报 `Failed to open cover.conf`）：

```
include=~~/cover-art.conf
```

四个键的取舍：

| 键 | 作用 | 为什么这么定 |
|---|---|---|
| `profile-cond=p["current-tracks/video/image"] == true` | 只在「当前视频轨其实是封面图」时应用 | 逐条实测 5 个条件：`not p["current-tracks/video"]`、`p["video-params"] == nil` 在**正常视频文件上也会误触发**（文件加载初期属性未初始化）；只有按 `image` 判断能做到音频触发、视频不触发 |
| `profile-restore=copy` | 条件变 false 时自动还原 | **必写**。手册默认 `profile-restore=default` 不恢复；实测「音频 → 视频」播放列表，不写时第二个视频仍残留 `downscale-big`，写了之后 mpv 打印 `Restoring profile: cover-art` 并正确回到 `no` |
| `video-unscaled=downscale-big` | 小封面 1:1 原始像素，大封面等比缩到窗口 | `yes` 在大封面超上限时会**裁剪**（手册原文 "the video is cropped"），不是缩小；`downscale-big` 才是「不放大、只缩小」 |
| `autofit-larger=1200x1200` | 窗口最大尺寸，保持宽高比 | **默认 1200**（2026-11-13 定稿，原 800 偏小；上限 1600 已确认可用）。UI 范围 `400`–`1600`、步长 100——起点定 400 而非 320，是为了让 100 的步长能精确落在 1200 和 1600 上。`--autofit-larger` 是「上限」语义，`--autofit` 是「强制窗口大小」，别用错；也支持百分比如 `60%x60%` |

实测（本机 `mpv.com v0.42.0 --config-dir=... --vo=null --ao=null`；ffmpeg 造 `audio.m4a` 带内嵌 640×480 封面、`nocover.m4a` 无内嵌靠同目录 `cover.png`、`out.mkv` 正常视频）：

| 文件 | profile 生效 | `video-unscaled` |
|---|---|---|
| 内嵌封面的纯音频 | 是 | `downscale-big` |
| 只有外部封面的纯音频 | 是 | `downscale-big` |
| 正常视频 | 否 | `no` |
| 播放列表：音频 → 视频 | 是 → `Restoring profile` | `downscale-big` → `no` |

2026-11-13 定稿复测（把上限从 800 换成 `1200x1200` 后重跑）：内嵌封面 / 外部封面的纯音频都读到 `video-unscaled=downscale-big` + `autofit-larger=1200x1200`；正常视频是 `unscaled=no` 且 `autofit-larger` 为空（全局默认未被污染）；「音频 → 视频」播放列表正确撤销。数值改动不影响条件 profile 逻辑，`_ref\subtest\cfgF`（正例）/ `cfgG`（缺 `profile-restore` 的反例）已同步为 1200。

**三条代价与边界**：① 首次启用会**新建一个 conf 文件**，这是 V1 唯一突破「只写 diff 键」的地方，但 `include=` 本身仍是普通键，`setting.exe` 的 `MpvWriteKey` 会当未知键原样保留；② `MpvRestoreDefaults:` 从 `mpv_default.conf` 整文件覆盖会冲掉 `include=` 行，但 `cover-art.conf` 不会被删，EXE 每次打开检测该行缺失即补回——符合「最后一次点击为准」，不加保护、不弹提示；③ `autofit-larger` 只在初始窗口尺寸计算时起作用，窗口已存在时切歌手感需在本机目视确认，不为它加额外选项。`scripts\*.lua` 依然一行不碰（这里是 conf，不是脚本）。

### 1.3 快捷键查证结论：默认值来自二进制弱绑定，`input.conf` 只存「改过的」

**结论：能查全，而且「有默认值的显示出来、没值的留空」正好对应 mpv 的两层绑定机制。** 默认快捷键**不在任何配置文件里**——`etc/input.conf` 只是全注释的模板，编译 mpv 时去掉 `#` 后**烤进二进制**，以 `is_weak=true` 的弱绑定生效；`portable_config\input.conf` 里的每一行是强绑定（priority 15），同键直接覆盖弱绑定。所以 EXE 打开时：`input.conf` 里有的 = 「自定义」（原样显示），没有但在默认表里的 = 「默认」（显示但不写盘），两者都没有 = 留空。

实测（`12.mpv\mpv.com v0.42.0`，Lua 在 `file-loaded` 读运行时属性 `input-bindings` 导出 TSV）：

| 场景 | 条数 | 构成 |
|---|---|---|
| `--no-config` 纯内置默认 | **220** | `section=default` 194 + 其他 section 26；其中 206 条弱绑定（`owner=nil`），14 条来自内置 OSC 脚本 |
| `--input-conf=本包 input.conf` 实际生效 | **230** | 220 默认 + 本包 10 行强绑定 |
| 差集 = 本包自定义 | **10** | `F5`–`F8` + `Ctrl+b` + `Alt+&#96;` / `Alt+1`–`Alt+4`，全部 `is_weak=false`、priority 15 |

交叉验证：`mpv-player/mpv@v0.42.0/etc/input.conf`（14739 B / 265 行）模板与实测键名、命令逐条一致；`--input-default-bindings` 默认 `yes`，本包 `mpv.conf` 没有关闭它，所以 220 条全部生效。全表按用途分组另存 `MPV快捷键_默认绑定全表.md`。

**「我已经自定义了 5 个」与实际的差异**：`input.conf` 现在是 **10 行**，分两组各 5 个——`F5`–`F8` + `Ctrl+b` 是 HDR / 音效功能键，`Alt+&#96;` + `Alt+1`–`Alt+4` 是窗口缩放键。`setting.ahk` 805、2782 行的说明块仍写着「5 行按键绑定」，是旧版遗留的过时注释（等开始写代码时顺手更新这两处注释，本次只做规划）。EXE 按实际 10 行读取，不按注释。

**三处默认绑定已被覆盖**（EXE 页 E 里要标出来）：

| 键 | 原默认 | 现为 | 影响 |
|---|---|---|---|
| `F8` | `show-text ${playlist}` 显示播放列表 | `script-message hdr-restore` | 播放列表快捷键失效；`menu.conf` 第 9 行「播放列表」仍可打开，页 E 给这行留空供重绑 |
| `Alt+1` | `set window-scale 1` | `set window-scale 0.5` | 语义变了（原 100% → 现 50%），但属于本包有意的重排 |
| `Alt+2` | `set window-scale 2` | `set window-scale 1.0` | 同上（原 200% → 现 100%） |

**默认表怎么进 EXE**：内置一张**静态映射表**（只含页 E 那 38 行的默认键，约 34 条），标注 `DefaultBindingsVersion = mpv 0.42.0`。不采用「启动时跑一遍 `mpv.com --no-config` 导出 `input-bindings`」——那要起一个额外进程、写临时 Lua、拉长打开时间，还徒增杀软面；为 34 条固定值不值得。将来换 mpv 大版本时更新这张表即可，V2 可选加「重新检测默认值」按钮。

**`input.conf` 的三条硬约束**（与 `mpv.conf` 完全不同，混用必出错）：

1. 行格式是 `KEY command`，**没有 `=`**；`KEY=cmd` 这种 `mpv.conf` 写法在这里是非法行。不能用 `MpvWriteKey` 那套逐行替换，必须单独写解析器。
2. 编码是 **UTF-8 无 BOM + LF**（实测 10 行 / 279 字节，`CRLF=0`），而 `mpv.conf` 是 CRLF。写盘必须保持 LF，别复用第 4 节第 2 条的规则。
3. `#` 起始为注释；绑 `#` 键要写 `SHARP`；键名大小写敏感；`ignore` 是解绑命令；单键能覆盖默认，**多键序列不能**（`b-c` 会被内置 `b` 抢走）。命令里含空格的参数要加引号，属性展开写 `${...}`（`show-text ${playlist}` 就是这种）。

**契约要改**：初稿第 4 节第 7 条、页 C 里写的「`input.conf` 一律只读 / 禁止在 EXE 里编辑」**到此作废**。加快捷键页就必须写它。日常路径上 `setting.exe` 不碰 `input.conf`（`MpvWriteKey` 只指向 `mpv.conf` / `hdr-autoswitch.conf`），两份文件不共存写；但 **2026-11-13 19:24 版本新增例外**：`MpvRestoreDefaults:`（1305）「恢复 MPV 默认配置」会用 `input_default.conf` 整文件覆盖 `input.conf`，把 EXE 写入的改键行一起冲掉。这是恢复默认场景、不是并发覆盖 → 归阶段 ⑨ 与默认模板一并处理。具体读写规则见第 4 节第 9 / 12 条。

### 1.4 无封面音频黑框：查证过程（V1 已定方案 B，见 1.6）

**结论（2026-11-13 01:56 已定）：采用方案 B，黑框改为 Pot 风格紧凑信息面板。方案 A 保留为回退方案。真频谱不进 V1，原因与替代做法见 1.6。**

实测环境：ffmpeg 造 `clean/plainsound.flac`（同目录无任何图片，真·无封面）、`clean/withcover.m4a`（内嵌封面）、`clean/video.mkv`；本机 `mpv.exe v0.42.0` + 真实窗口截屏（`_ref\subtest\shots\`）。

| 实测项 | 结果 |
|---|---|
| 无封面音频属性 | `vid=no`、`current-tracks/video=nil`、`video-params=nil`、`audio-params` 有值、`track-list/count=1`；另外两种文件都有视频轨 → 条件可精确区分 |
| 默认窗口 | 982x596，纯黑，无 OSD、无控制条（手册：`--force-window` 固定 960x540） |
| 条件 profile 设 `geometry=640x180` | 窗口变 662x236，**生效**；`profile-restore=copy` 在「音频 → 视频」播放列表里正确撤销（`Restoring profile`），不污染视频 |
| `osd-msg1` 属性展开 | `${time-pos}` / `${duration}` / `${playlist-pos-1}` / `${media-title}` / `${audio-codec-name}` 都正常；`${audio-bitrate}` 在暂停+`ao=null` 时是 `(unavailable)`、`${audio-samplerate}` 是 `0Hz`，实机需 `${?prop:...}` 兜底 |
| `osd-msg1` 里的 ASS 标签 | **不支持**。四组写法实测：单反斜杠 → `(broken escape sequences)`；双反斜杠 → 标签被当字面文本打印；`{\fs38}` / `{\\fs38}` / 带 `\N` 全失败。只有纯文本可用 |
| 可读性补救 | `osd-scale-by-window=no` + `osd-font-size=16` 让文字恢复到可读字号（`shots/plain_bigfont.png`），但仍是一行字，不是 Pot 那种排版 |
| `--cover-art-files=<绝对路径默认图>` | **否决**：显式挂的图会被自动选成主视频轨，正常视频 mkv 也被这张 640x180 的图取代（`vid=2`）。不能用 |
| 条件 profile 里设 `lavfi-complex` | **否决（自撤销死锁）**：条件 `current-tracks/video == nil` 一旦生效就生成视频轨 → 条件变 false → `Restoring profile` → mpv 卡死（10s 超时，0 帧）。生成频谱本身可行（`showcqt` 实测能出帧，`axis=0:text=0` 可去掉红字刻度），但不能放在会被自身改变的条件上 |

**方案 A（纯配置，零脚本）——回退方案，V1 不采用**：新建 `audio-info.conf`，内容为一个条件 profile：

```ini
[no-art-info]
profile-cond=p["current-tracks/video"] == nil and p["audio-params"] ~= nil
profile-restore=copy
geometry=640x180
osd-msg1=<纯文本，${...} 属性展开，${?prop:...} 兜底>
osd-scale-by-window=no
osd-font-size=16
```
`mpv.conf` 末尾加 `include=~~/audio-info.conf`。优点：不动轨道、不自撤销、不写脚本、明天就能开工。缺点：只有一行字；`audio-bitrate` / `audio-samplerate` 要兜底；实测截图里偶发一条 `Panscan: 0.2` 的杂散 OSD，需实机确认来源。

> ⚠ **2026-11-14 修正（阶段 ⑦ 实测推翻两处）**：① 上面那条 `and p["audio-params"] ~= nil` **必须删掉**——`loadfile` 期间 `audio-params` 会短暂变 `nil`，条件抖动触发 `Restoring profile`，把 profile 里的键一起撤销，这正是「单实例转发后窗口回 960x540」的真因；实际落地只用 `p["current-tracks/video"] == nil`。② 纯音频在 mpv 原生行为下**根本不创建窗口**（`--force-window` 也只在有 `--force-window` 参数时建 960x540 空窗），所以 profile 里必须补 `force-window=yes`，否则 `geometry` 无处可施。详见 6.13。

**方案 B（Pot 风格）——V1 采用**：新增 `scripts\MyMPC_ui.lua`，用 `mp.create_osd_overlay("ass-events")` 画大时间码 / 曲序 / 文件名 / 格式采样率声道 / 进度条 / 右上轻量柱形装饰，单实例判断塞进同一个脚本（见 1.5），一份脚本解决两件事。**`lavfi-complex` 真频谱已从方案中删除**（实测运行时设置会段错误），柱形为基于 `time-pos` 的 ASS 动画、非真实频谱。完整落地架构与避坑清单见 1.6。

---

### 1.5 「允许同时运行多个播放器」：是播放器自己的单实例开关，mpv 得靠 IPC + 脚本

**结论：需求语义已定，落点已定，但 mpv 原生做不到——必须写 `mpv.conf` 的 `input-ipc-server` + 新增一个 EXE 独占的 Lua 脚本。默认关闭 = 双击媒体文件复用当前窗口。**

需求边界（用户 2026-11-13 01:20 明确）：

| 场景 | 期望 |
|---|---|
| 播放器正在播放 + **双击 `mplayerl.exe`** | **允许**新实例（现状保持） |
| 播放器正在播放 + **双击其他媒体文件** | **不要**新实例，文件送入当前窗口 |

链路事实：`Assoc.ahk` 210–215 行把扩展名关联成 `mplayerl.exe "%1"`（无复用参数）；`mplayerl.exe` 有源码、不用改、只读 `mplayerl.ini [Settings] MainPlayer`；**判断由播放器自己负责**。`setting.ahk` 不管这件事，本规格不改它。

mpv 侧的能力边界（`--list-options` 全表 + 手册）：

- **没有** `--one-instance` / `--single-instance` / 任何「复用已有窗口」选项。只有 `--input-ipc-server`（Windows 下是 `\\.\pipe\<name>`，前缀可省）和 `--input-ipc-client=fd://N`（FD 继承，不是路径，与本需求无关）。
- 因此标准做法只能是：`mpv.conf` 固定写 `input-ipc-server=MyMPC_mpv`；新启动的实例在脚本里检测同名 pipe，若已有实例 → 用 IPC 发 `loadfile <file> replace`（或 `append-play`）→ 自己 `quit`；若无 → 正常播放。
- 区分两个场景靠「启动时是否带文件参数」：双击媒体文件带 `%1` → 触发复用；双击 `mplayerl.exe` 不带文件 → 不触发，正常开新窗口。这正好对上需求边界。

开关落地：`script-opts\MyMPC.conf` 写 `single_instance=yes/no`。**默认 `yes`**（UI 上「允许同时运行多个播放器」= 关闭）。EXE 只写这个键，脚本启动时读一次；`input-ipc-server` 是固定键，不随开关增删。

**边界变更（2026-11-13 01:56 已接受）**：初稿第 4 节定的是「`scripts\*.lua` 一律只读，不碰」。方案 B 和单实例都要求新增脚本，现正式收紧为：

> **EXE 可以创建并独占管理自己新建的 `scripts\MyMPC_ui.lua` 与 `script-opts\MyMPC.conf`；现有的 `fit90.lua` / `hdr-autoswitch.lua` / `stats.lua` / `tone-ab.lua` 以及 `input.conf` / `menu.conf` 一行不改。**

这是收紧，不是放弃原则——EXE 只碰自己生成的文件，用户手写的脚本永远不动。此变更已拍板通过，1.4 方案 B 与 1.5 单实例一并进 V1。

---

### 1.6 方案 B 定稿：一份 `MyMPC_ui.lua` 管两件事（2026-11-13 01:56 拍板）

**决策**：无封面音频选 **B**，并接受「scripts 只读」边界收紧；B 与单实例合并为**同一份脚本**，一次写完。以下为今晚补做的实测结论与最终架构，明天照此开工。

#### 四层落点

| 层 | 文件 | 内容 | 谁管 |
|---|---|---|---|
| 窗口 | `audio-info.conf` + `mpv.conf` 的 `include=~~/audio-info.conf` | 条件 profile：`force-window=yes` / `geometry=640x180` / `osd-scale-by-window=no` / `profile-restore=copy`。**不放 `osd-msg1`、不放 `lavfi-complex`** | 条件 profile 在窗口创建前求值，Lua 运行时设 `geometry` 实测无效（6.13 修正：`geometry` 只在窗口创建时生效，且必须先 `force-window=yes` 才有窗口） |
| 界面 | `scripts\MyMPC_ui.lua`（新建，EXE 独占） | 职责 1：`ass-events` overlay 画紧凑信息面板；职责 2：单实例 IPC 转发 | EXE 整文件生成，不做 diff |
| 开关 | `script-opts\MyMPC.conf`（新建，EXE 独占） | `single_instance=yes`（默认）/`no`；`no_art_ui=yes`（默认）/`no` | `key=value`，复用逐行替换 |
| 固定键 | `mpv.conf` | `input-ipc-server=MyMPC_mpv`，**不随开关增删** | 只写 diff 键 |

「无封面时显示紧凑信息窗」关闭时：EXE 同步移除 `include=~~/audio-info.conf` 行（保留 `audio-info.conf` 文件），脚本读 `no_art_ui=no` 不画 overlay，回到 mpv 默认 960x540 空窗口。

#### 补做实测（本机 `mpv v0.42.0`，隔离配置 + 真实窗口截屏）

| 实测项 | 结果 |
|---|---|
| 五组 OSD 渲染路径 | `mp.osd_message` / `show-text` → ASS 标签被当纯文本打印；`create_osd_overlay("ass-events")` **不设 `res_x/res_y` → 完全不渲染（纯黑）**；设 `res_x/res_y=640/180` → 大时间码 + 混排字号成功；`mp.set_osd_ass(0,0,...)` → 也成功。**结论：必须显式设 `ov.res_x/ov.res_y`，建议取 `mp.get_property_native("osd-dimensions")`** |
| ASS 事件行分隔 | 同一条 ASS 事件行里写多个 `{\pos}`，**只有第一个生效** → 四行文字挤成一行、20 根柱形被拆到窗口左右两端。改成 `table.concat(段, "\n")` 每段独立事件行后排版完全正确（`shots/deliver2.png`） |
| ASS 颜色 | `\c&HBBGGRR&` 是 **BGR**。写 `\c&HB59AFF&` 出来是粉色；紫色 `#B59AFF` 必须写 `\c&HFF9AB5&` |
| 判定时序 | `file-loaded` 瞬间 `audio-params` 常为 `nil`（但 `aid=1` 已有值），用「track-list 无 video 轨 + `aid ~= "no"`」判定更稳；再加一次 `add_timeout(0.2)` 兜底。三种文件实测：无封面音频 `true`、正常视频 `false`、有封面音频 `false` |
| 窗口尺寸 | 条件 profile 设 `geometry=640x180` → 662x236（含边框），`Applying auto profile`；「音频 → 视频」播放列表 `Restoring profile` 正确撤销 |
| 运行时 `lavfi-complex` | **否决**：Lua `mp.set_property("lavfi-complex", ...)` 直接 **Segmentation fault**（exit 139，0 帧）。命令行传参能出帧，但文件关联是 `mplayerl.exe "%1"`，启动链不改就没法传 |
| 真频谱 | V1 **不做**。右上柱形是 ASS `\p1` 画的基于时间的装饰动画，不读 PCM。V2 若要真频谱，要么改启动链传 `--lavfi-complex`，要么走 `af=lavfi=astats` + `vf-metadata` / `af-metadata` 取包络，另行评估 |
| 同名 IPC pipe | 第二个实例 `Couldn't create first pipe instance: 拒绝访问 (0x80070005)`，**但仍正常播放到结束**（rc=0）。脚本必须主动检测 + 转发 + 自退，不能依赖 mpv 拒绝 |

**最终效果**（实测包真实截屏 `_ref\subtest\shots\deliver2.png`）：662x236 窗口，左侧大号白色时间码 `00:00`，下面三行灰色/白色小字（总时长+曲序+格式 / 文件名 / 格式+采样率+声道），右侧一排 20 根紫色柱形，底部紫色进度条。

#### 2026-11-13 02:1x 补测：Lua 连 named pipe —— **已验证通过**（阶段 ⑦ 无前置阻塞）

| 验证项 | 结果 |
|---|---|
| `io.open("\\\\.\\pipe\\MyMPC_mpv", "wb")` | 可打开、可写，但拿不到 server pid，无法区分「连到自己」 |
| LuaJIT `ffi` + `CreateFileA` | 可打开；`GetNamedPipeServerProcessId` 返回 server pid；`WriteFile` 写 JSON 成功 |
| `mp.get_property("pid")` | 有值，用于与 server pid 对比 |
| 第一个实例（带文件启动） | `server_pid == my_pid` → 判为 self，正常播放 |
| 第二个实例（带文件启动） | 打印 `Couldn't create first pipe instance: 0x80070005`（不影响），`server_pid != my_pid` → 写 `loadfile <file> replace` → 0.15s 后 `quit`；A 日志出现 `Run command: loadfile, flags=64, args=[url="…ape", flags="replace"]` |

**定稿方案**：`mpv.conf` 固定 `input-ipc-server=MyMPC_mpv`；脚本用 `ffi.CreateFileA` 打开管道 → `GetNamedPipeServerProcessId` 与 `mp.get_property_number("pid")` 对比 → 不同则 `WriteFile` 发 `{"command":["loadfile","<path>","replace"]}\n` 并自退，相同则正常播放。启动时 `playlist-count < 1`（双击 `mplayerl.exe` 不带文件）跳过转发，正好对上 1.5 的两个场景边界。**不需要文件锁、不需要外部中转、不改启动链。** 已知副作用：第二个实例闪窗约 0.2s。隔离实测包已补 `scripts\single_instance.lua`，正式交付时合并进 `MyMPC_ui.lua`。

**新遗留问题（02:22 用户实测截图）**：进程确实只剩一个，但**目标窗口恢复成默认 960x540 黑框**，文字也跟着放大。推断：`loadfile replace` 触发 VO 重建，切歌瞬间 `audio-params` 变 nil 使条件 profile 被 `Restoring profile` 撤销，而 `geometry` 只在窗口创建时生效 → 新窗口按默认尺寸建；overlay 取 `osd-dimensions` 作 `res_x/res_y`、字号按 H 比例，所以整块内容变大。当晚未修；对照组脚本 `workspace\_ref\diag_window.py` 已写好但**未运行**（基线 / 去掉 audio-params 条件 / 只用 current-tracks/video / loadfile 后补发 `set_property geometry`），明天对比他人 MPV 后继续。

#### ✅ 2026-11-14 22:0x 结案：上面那两个问题都已修掉（阶段 ⑦）

**推断方向对了一半，但漏了一条更基础的事实。** 对照组实验（`_ref\win_size_probe.py`，六种写法逐一比价，窗口尺寸用 Win32 `GetClientRect` 量，因为纯音频窗口的 `osd-dimensions` 是 0x0）结论：

1. **纯音频在 mpv 原生行为下不创建窗口。** 不是「建了个黑窗」，是真的没有窗口——`input-ipc-server` 能连上、`EnumWindows` 却找不到任何窗口。所以 profile 里必须 `force-window=yes`，否则 `geometry` 无处可施。这条 V1 全程都没记录过。
2. **条件里带 `audio-params` 就是那个老 bug。** `loadfile replace` 期间 `audio-params` 短暂变 `nil` → 条件由 true 变 false → `Restoring profile` 把 `force-window` / `geometry` 一起撤销 → 窗口销毁重建 → 回到 960x540。改成只判 `p["current-tracks/video"] == nil` 后，`loadfile replace` 不再触发 restore，窗口稳定。

**修法（已落地）**：`audio-info.conf` 用 `force-window=yes` + `geometry=640x180` + `profile-restore=copy` + `osd-scale-by-window=no`，条件只写 `current-tracks/video == nil`。真机结果：无封面音频窗口 426x120 物理（= 640x180 @150% DPI）；再双击第二个文件转发后仍是 426x120；切视频恢复并按 90% 适配（1296 高、保持 4:3）；视频 → 音频 → 视频双向都正确。完整数据见 6.13。

**叠加的一条**：`fit90.lua` 的比例已按用户授权外置到 `script-opts\fit90.conf`（`ratio` / `auto`），90 只是出厂默认；改文件重启即生效，实测改成 70 后窗口高度变 1008（屏高 70%）。

---

### 页 B · 画面 / HDR（联动最复杂，必须成组写）

| | 界面控件 | 类型 | 写入键 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| ● | HDR 模式 | 下拉 2 项 | `hdr-autoswitch.conf` 四键联动 | 见下 | 直通显示器 |
| ● | 色调增强档位 | 下拉 5 项 | `inverse-tone-mapping` + `contrast` + `brightness` + `saturation` | 关闭 / 0.5 / 0.6 / 0.8 / 1.0 | 关闭 |
| ● | 视频输出范围 | 下拉 | `video-output-levels` | `full` (PC 0–255) / `limited` (TV 16–235) | `full` |
| ○ | 仅全屏时切换系统 HDR | 开关 | `only_fullscreen` | `yes` / `no` | `no` |
| ● | 着色器缓存 | 只读统计 + 动作按钮 | 不写 conf | 清理 `portable_config\cache\shader_* / icc_*` | — |
| — | 渲染后端 | **只读说明，不做控件** | `vo=gpu-next` / `gpu-api=d3d11` | — | 改这两个最容易把 HDR 链路搞炸 |

HDR 模式联动（照抄 `MpvAction` 1271–1286，一个下拉 = 四次写）：

```
直通显示器 → mode=passthrough, sdr_passthrough=no,  manage_system_hdr=yes, osd_notify=no
HDR 转 SDR → mode=tonemap,     sdr_passthrough=no,  manage_system_hdr=yes, osd_notify=no
```

**行为准则（2026-11-13 用户拍板；旧「SDR 片源也开系统 HDR」是错误语义，已废弃）**：

| 显示器状态 | 片源 | 脚本行为 |
| --- | --- | --- |
| HDR 常开 | 任意 | 完全不介入：不切系统开关、不改 mpv 输出属性，按 `mpv.conf` 原样输出 |
| SDR | SDR | 不介入（SDR→HDR 映射留待后期另说） |
| SDR | HDR | 自动开系统 HDR + 切 PQ 输出（含 HDR 映射），播完 / 退出自动关回 SDR |

实现要点：`sdr_passthrough=no`（两个预设都是 `no`，SDR 片源不再让 `evaluate()` 判成 `want_hdr=true`）；`hdr-autoswitch.lua` 的 `evaluate()` 开头新增 `system_hdr_is_on()` 短路，检测到显示器 HDR 已开就直接 return，`enable_all()` / `apply_pq_output()` 都不会被调用。`manage_system_hdr=yes` + `restore_on_exit=yes` 保留，供第 3 行的自动开 / 自动关使用；`switched` 标志保证用户手动常开的 HDR 不会被脚本误关。副作用：系统 HDR 常开时 F7（`hdr-toggle`）与 HDR 模式下拉不会改变输出，人工对比请用 F5（`tone-ab.lua`）。生产 `portable_config` 与整合包 `12.mpv\portable_config` 两份 conf / lua 已同步；`setting.ahk` 的「直通显示器」按钮、MpvSettings 的反推与成组写盘三处映射同步改为 `no`。

档位联动（照抄 `MpvApplyTone`）：

```
关闭 → inverse=no,  contrast=0,  brightness=0, saturation=0
0.5  → inverse=yes, contrast=4,  brightness=5, saturation=0
0.6  → inverse=yes, contrast=16, brightness=5, saturation=0
0.8  → inverse=yes, contrast=39, brightness=5, saturation=0
1.0  → inverse=yes, contrast=55, brightness=5, saturation=0
```

反推规则：只有组合精确命中上表才显示对应档位；不命中一律显示「自定义」并**禁止写入**（否则会把用户手工标定的 contrast 冲掉）。`F5` 的 A/B 对比属于 `tone-ab.lua`，写进副标题说明即可，不要做成控件。

无 HDR 显示器时：直通项置灰，档位项置灰（与 `MpvRefresh` 1101–1109 行为一致）。

### 页 C · 音频 / 音效链路（本 EXE 的必需项）

| | 界面控件 | 类型 | 写入位置 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| ● | 启用音效链路 | 开关 | `mpv.conf: af` | `bassmax` / 空串 | `bassmax` |
| ● | 音效增强 / 旁通 | 开关 | `HKCU\Software\BassMax\Enabled` | `1` / `0` | `1` |
| ● | 低音 Bass | 滑块 | `...\Bass` | −12.0 ~ +12.0 dB，步长 0.1 | `+2.0` |
| ● | 高音 Treble | 滑块 | `...\Treble` | −12.0 ~ +12.0 dB，步长 0.1 | `+2.0` |
| ● | 驱动 Drive | 滑块 | `...\Drive` | **0.00 ~ 1.00**，步长 0.05 | `1.00` |
| ● | 预设 Preset | 下拉（文字） | `...\Preset` | 0 标准乐 / 1 古典乐 / 2 俱乐部 / 3 流行乐 / 4 爵士乐 / 5 摇滚乐 | `4` 爵士乐 |
| ● | 声道输出 | 下拉 | `mpv.conf: audio-channels` | `auto` / `auto-safe` / `stereo` / `mono` | `auto` |
| ○ | 打开 BassMax 原厂面板 | 动作按钮 | `rundll32 Codecs\bassmax_dshow.ax,BassMaxConfig` | — | 逃生口 |
| — | Ctrl+b 增强/旁通 | **只读说明** | `input.conf` 第 5 行 | `af toggle bassmax` | 改键统一到页 E 快捷键，此处不改 |

**数值控件统一为「滑块 + 数值框」（2026-11-13 20:37 用户拍板）**：替代原来的加减号步进器。鼠标拖动 / 点击轨道快速粗调，聚焦后方向键按 `SmallChange` 精确微调、PageUp/PageDown 按 `LargeChange`、Home/End 到端点，值吸附到 `TickFrequency`。参数范围以 BassMax 源码 `Claw\BACKUP.md` 的注册表参数表为准：`Bass` / `Treble` = −12.0 ~ +12.0 dB（初版骨架误写成 0 ~ 12，已校正）、`Drive` = 0.00 ~ 1.00、`Preset` = 0 ~ 5。这类有明确数值与范围的滑块，不属于第 2 节「需要实时预览才能调的细粒度滑块」。

**预设必须显示文字，不显示裸数字**（用户原话：「数字别人看不懂」）。下拉 6 项按注册表值顺序排列，`SelectedIndex` 即写盘值：`0 标准乐 · 原声` / `1 古典乐 · 轻低音 / 柔高音` / `2 俱乐部 · 重低音` / `3 流行乐 · 人声靠前` / `4 爵士乐 · 均衡（默认）` / `5 摇滚乐 · V 型`。名称来自 `bassmax_core.hpp` 的 `presets()`，效果短说明来自 `bassmax_chrome_ext\common.js`。

**必须写进副标题/提示的硬约束**（漏一条就会出「点了没反应」的 bug）：

1. `af=bassmax` 是**挂载滤镜**，`Enabled` 是**增强还是旁通**，两者不是同一个开关，别合并成一个 Toggle。
2. `HKCU\Software\BassMax` 六键 `Bass / Treble / Drive / Preset / Enabled / TrayIcon` 必须齐全；`ReloadFromRegistry()` 缺任意一个会**整批忽略**（见 `EnsureBassMaxConfig` 752–768）。新 EXE 首次写入前先补齐 `TrayIcon=1` 等缺项。
3. 注册表改动由托盘 200 ms 轮询生效，**改完无需重启播放器**；但 `af` 改动需要重启 mpv。
4. 检测到 `audio-spdif` 非空时，音效开关置灰并提示「比特流直通与 BassMax 互斥」。
5. 多声道不再被下混：`af_bassmax` 按 chmap 成对处理，**仅多声道**时 `Drive` 内部上限压到 `0.6`（`BM_MULTI_DRIVE_MAX`），立体声仍按注册表原值。注册表 `Drive` 本身是 `0.00 ~ 1.00`（`Claw\BACKUP.md` 参数表），**UI 上限就是 1.00，不是 2.0**；`0.6` 是滤镜内部的多声道钳位，别拿它当 UI 上限。UI 上不要写「音效最佳场景是立体声」这种过时说明。
6. `setting.exe` 的 `Button1` 写 `af`，关时写的是**空值行 `af=`** 而不是删行。新 EXE 必须保持一致，否则两个程序的反推会对不上。
7. `input.conf` 强制读取且格式与 `mpv.conf` 不同，**不能合并**，改键统一走页 E（见 1.3 / 第 4 节第 9 条）；`hdr-autoswitch.conf` 首行注释是 BOM 保护，不能删。
8. **统一注册表**：`HKCU\Software\BassMax` 是 BassMax 全系滤镜（mpv 的 `af_bassmax`、MPC-HC / MPC-BE 的 DShow 滤镜、原厂配置面板）共用的同一份参数。EXE 用原生控件直接写这里，一次写入对所有播放器同时生效；副标题注明「此处修改对所有播放器的 BassMax 生效」即可，不需要按播放器分支，也不需要任何同步逻辑。

### 页 D · 截图 / 系统

| | 界面控件 | 类型 | 写入键 | 取值 | 出厂默认 |
|---|---|---|---|---|---|
| ● | 截图格式 | 下拉 | `screenshot-format` | `jpg` / `png` / `webp` / `avif` | `jpg` |
| ● | 截图保存位置 | 下拉 + 自定义 | `screenshot-dir` | 桌面 / 图片文件夹 / mpv 目录 / 自填 | 安装时写入的绝对路径 |
| ● | 高位深截图 | 开关 | `screenshot-high-bit-depth` | `yes` / `no` | `no` |
| ● | 动作区 | 按钮组 | — | 恢复出厂默认 / 重置本页 / 打开 mpv.conf / 打开配置目录 | — |

`screenshot-dir` 规则：正斜杠 + 结尾斜杠（`MpvNormPath`），必须是绝对路径（mpv 不展开 `~~desktop/`）；「恢复默认」只把现有值原样带走，绝不重新生成，否则会覆盖 OneDrive / 重定向桌面。`screenshot-template` 带时间码，Windows 下不能用 `%P`，V1 不做控件。

**已砍**：「标记色彩空间」（`screenshot-tag-colorspace`）——不做控件、不写键，保持出厂 `no`。

### 页 E · 快捷键（新增，见 1.3）

**一页固定清单，不是一个全量绑定编辑器。** 5 张卡片 / 38 行，每行「动作名 + 灰色副标题（mpv 命令）+ 右侧热键框」。热键框三种状态：**有默认键**直接显示（弱色，标「默认」）、**本包已自定义**显示并标紫色「自定义」、**没有任何绑定**留空显示占位文字「未绑定 · 点击设置」。改键用键盘捕获控件（点击后按组合键录入，Esc 取消），不做文本手输、不做键序列、不做多命令。

| 卡片 | 动作 | mpv 命令 | 默认键 / 当前值 |
|---|---|---|---|
| **E1 播放控制** | 播放 / 暂停 | `cycle pause` | `SPACE`（另有 `p`） |
| | 停止 / 退出 | `quit` | `q` |
| | 上一个文件 | `playlist-prev` | `<` |
| | 下一个文件 | `playlist-next` | `>` |
| | 快退 5 秒 | `seek -5` | `LEFT` |
| | 快进 5 秒 | `seek 5` | `RIGHT` |
| | 快退 60 秒 | `seek -60` | `DOWN` |
| | 快进 60 秒 | `seek 60` | `UP` |
| | 逐帧前进 | `frame-step` | `.` |
| **E2 画面 / 截图** | 全屏切换 | `cycle fullscreen` | `f` |
| | 退出全屏 | `set fullscreen no` | `ESC` |
| | 截图（含字幕） | `screenshot` | `s` |
| | 截图（不含字幕） | `screenshot video` | `S` |
| | 截图当前窗口 | `screenshot window` | `Ctrl+s` ⚠ |
| | 显示进度 | `show-progress` | `o` |
| | 统计信息 | `script-binding stats/display-stats` | `i` |
| **E3 音轨 / 字幕 / 音量** | 切换音轨 | `cycle audio` | `SHARP` |
| | 切换字幕轨 | `cycle sub` | `j` |
| | 字幕显示开关 | `cycle sub-visibility` | `v` |
| | 字幕延迟 −0.1s | `add sub-delay -0.1` | `z` |
| | 字幕延迟 +0.1s | `add sub-delay 0.1` | `x` |
| | 音量 −2 | `add volume -2` | `9` |
| | 音量 +2 | `add volume 2` | `0` |
| | 静音 | `cycle mute` | `m` |
| **E4 本包专属**（10 行全部已有值，标「自定义」） | 色调增强 A/B 对比 | `script-message tone-ab` | `F5` |
| | 查看 HDR 状态 | `script-message hdr-status` | `F6` |
| | HDR 直通 / 色调映射 | `script-message hdr-toggle` | `F7` |
| | 恢复系统 HDR | `script-message hdr-restore` | `F8` |
| | BassMax 增强 / 旁通 | `af toggle bassmax` | `Ctrl+b` |
| | 窗口 25%（视频分辨率） | `set window-scale 0.25` | `Alt+&#96;`（反引号键） |
| | 窗口 50%（视频分辨率） | `set window-scale 0.5` | `Alt+1` |
| | 窗口 100%（视频分辨率） | `set window-scale 1.0` | `Alt+2` |
| | 窗口 200%（视频分辨率） | `set window-scale 2.0` | `Alt+3` |
| | 窗口缩放到屏幕比例 | `script-message mpv-fit90` | `Alt+4` |
| **E5 留空可自设**（4 行，当前无绑定） | 显示播放列表 | `show-text ${playlist}` | **空**（原默认 `F8` 已被「恢复系统 HDR」占用） |
| | 停止播放（保留列表） | `stop keep-playlist` | **空**（mpv 无默认绑定） |
| | 快进 10 秒 | `seek 10` | **空**（默认只有 5 / 60 秒） |
| | 快退 10 秒 | `seek -10` | **空**（默认只有 5 / 60 秒） |

> 前四行与 `Alt+4` **基准不同，必须分开写**：`window-scale` 是「视频分辨率的倍率」，所以 `Alt+&#96;` / `Alt+1` / `Alt+2` / `Alt+3` = 视频的 25% / 50% / 100% / 200%（双击打开视频的默认值也是视频 100%）；只有 `Alt+4` 走 `mpv-fit90`，按**屏幕可用区域**的 N% 缩放，且只在按键时生效。比例控件在页 A「Alt+4 屏幕比例」卡片，别把 90% 写进动作名（详见页 A 同名小节）。

⚠ 两处副标题必须照抄，都是既有资料里已经踩过的坑：

- 「截图当前窗口 `Ctrl+s`」→「会连同字幕与 OSD 一起截入，并按窗口尺寸缩放；一般请用 `s`」（`setting.ahk` 2896 说明块原话）。
- 「显示播放列表」→「原默认 `F8` 已被本包占用；右键菜单「播放列表」仍可打开」（`menu.conf` 第 9 行）。
- 「切换音轨」的键显示为 `#`，但写进 `input.conf` 必须是 `SHARP`（`#` 起始会被当注释行）——UI 显示与写盘值要做一次转换，别直接把显示值拼进文件。

**三态语义（这是整页最容易做错的地方）**：

1. 「默认」态**不写任何行**。`input.conf` 里没有这行，mpv 靠编译进二进制的弱绑定生效。若把 38 行全写进去，会把 `input.conf` 从 10 行撑成 48 行，还会把弱绑定固化成强绑定，将来 mpv 升级改默认就跟随不了。
2. 用户改键 → 才写一行 `KEY cmd`。**原默认键仍然有效**（强绑定只是叠加，不会撤销弱绑定），副标题提示「原键仍可用；如需彻底禁用需额外写 `ignore`」。V1 不提供解绑操作，避免把简单页面做成编辑器。
3. 用户清空一个「自定义」行 → **删掉该行**，语义是「恢复默认」，不是「禁用该键」。清空「默认」态或「留空」态 → 无操作，不写文件。

**冲突校验**：两个动作设成同一个键 → 标红并阻止保存；新键撞上内置默认键（例如把「全屏」设成 `s`）→ 黄色警告「将覆盖默认的截图快捷键」但允许保存，这正是 mpv 强绑定覆盖弱绑定的预期行为。键名大小写敏感（`s` ≠ `S`、`z` ≠ `Z`），校验时不折叠大小写。

> 阶段 ⑥ 落地时补了一条限定：**只有本次真正改动过冲突中的某一行才阻断**。生产 `input.conf` 里 `Alt+1` / `Alt+2` 本来就被本包重排过（50% / 100%），属于历史遗留同键；若「扫到同键就拦」，用户改任何无关的键都会被拖住。详见 6.12。

**明确不做**（防选项爆炸的边界）：全量 220 条绑定表、自定义动作命令输入、键序列 `a-b-c`、分号多命令、`{section}` 绑定、`ignore` 解绑、鼠标/滚轮绑定、导入导出配置、全局搜索。完整默认表另存 `MPV快捷键_默认绑定全表.md` 供查阅，不塞进 UI。

**唯一例外是「其他绑定」只读卡片**（阶段 ⑥ 补，见 6.12）：盘上已有、但不属于上表 38 行、且能明确解析的绑定，页面最底部按「键名 + 命令」列出，无编辑入口、无任何写盘路径，只为让「其余行原样保留」这句话对用户可见。没有这类行时整张卡片不出现。

---

## 2. V2 候选（先留架构，不做 UI）

`scale / dscale / cscale` 预设、`interpolation + tscale`（播放器插帧，与你的 RIFE 流水线不是一回事）、`sub-font / sub-scale / sub-border-size / sub-color`、`volume / volume-max`、`osc-layout`、`keep-open`、`image-display-duration`、`audio-display` 内嵌/外置封面优先级、`cover-art-auto` / `cover-art-whitelist` 细调、`audio-spdif`、`gpu-shader-cache=no`、`deband`、`tone-mapping` 曲线与 `tone-mapping-spline-contrast` 细调、`target-trc`、`hdr-compute-peak`、窗口行为（`border / ontop / window-scale`）、主题/强调色切换、多语言。`geometry` / `autofit` 已被 1.2 / 1.4 占用为固定值，**不再作为可选项开放**。**真实音频频谱**（`lavfi-complex` + `showcqt` / `vf-metadata` 包络）列为 V2 候选：V1 右上柱形只是 ASS 装饰动画，不读 PCM。

**明确不做**（这是「不变成 PotPlayer」的边界）：完整 libplacebo 参数、着色器链编辑、**全量按键绑定编辑器**（V1 只做页 E 的 38 行固定清单）、menu.conf 编辑器、逐文件 profile、内置统计图表、渲染器性能曲线、任何需要实时预览才能调的细粒度滑块、BassMax 原厂面板的全部高级参数。

### 2.1 VapourSynth 查证结论：API v4 → 不做，也不预留

**结论：这个 mpv 用的是 VapourSynth API v4（VSScript4 / VapourSynth R4），不是 V3。按既定规则，V1 不纳入 VapourSynth，也不预留任何对话框。**

证据链（本机实测 + 官方源码 + changelog 三方一致）：

| 来源 | 结果 |
|---|---|
| `12.mpv\mpv.com --version`（实测） | `mpv v0.42.0-38-gbcde6b93c-dirty`，2026-11-11 构建；`--msg-level=all=v --version` 的编译参数含 `-Dvapoursynth=enabled`、特性列表含 `vapoursynth` |
| `mpv.com --vf=help` | 有 `vapoursynth  VapourSynth bridge`；`--vf=vapoursynth=help` 列出 4 个子选项：`file`、`buffered-frames`(1–9999, 默认 4)、`concurrent-frames`(auto, 1–99)、`user-data` |
| `mpv/master: video/filter/vf_vapoursynth.c` | 第 25 行 `#include <VSScript4.h>`；初始化调 `getVSScriptAPI(VSSCRIPT_API_VERSION)`（该宏来自 VSScript4.h，major = 4）与 `getVSAPI(VAPOURSYNTH_API_VERSION)`；还调用 API4 才有的 `getVSScriptAPILastError` |
| mpv 0.39.0 changelog | `vf_vapoursynth: upgrade to VapourSynth API v4`。**分界：mpv ≤ 0.38 = API3（VSScript.h），≥ 0.39 = API4，不再兼容 V3 的 VSScript.dll** |
| 本机 exe 二进制 | 含 `VSScript.dll`、`VSSCRIPT_PATH`、`getVSScriptAPI`、`getVSScriptAPILastError` 字符串，与上述源码一致 |

**两条得说清的实情**：

1. 「V4 太新、插件没跟上」这个前提在 2026 年已经不成立。VapourSynth R73/R75 的 core 是 API R4.1，MPV_lazy 的 `k7sfunc` 明确要求 API 4+，RIFE / MVTools / 超分那一整套 `.vpy` 都是 API4。但这**不改变决定**：要回到 V3 就得把 mpv 降到 ≤ 0.38，代价是丢掉 `gpu-next` / libplacebo v7.391 这一整套和你现有的 HDR 脚本，不值得。所以是「本整合包的 VapourSynth 整体不做」，不是「等插件成熟再做」。
2. 你设想的两个对话框里，第一个在技术上根本不是 mpv 选项。源码 `drv_vss_init()`（819–828 行）只认**环境变量 `VSSCRIPT_PATH`** 指向 `VSScript.dll`，未设置时按 `VSScript.dll` 在 PATH 里找；没有 `--vapoursynth-dll` 这种可以写进 `mpv.conf` 的键。第二个才是 `vf=vapoursynth=file=<绝对路径>.vpy`。也就是说要指定 VapourSynth 地址得改启动链的环境变量，塞不进一个只读写 conf 的设置 EXE。

另外两条客观限制：`12.mpv\` 里没有 `VSScript.dll`，也没有 VapourSynth 运行时，真要用得另装 R4+；`vf=vapoursynth` 是**流式桥**（帧窗口默认 4、每次 seek 重载脚本、不支持随机访问），mpv 手册原文警告「除非你对 VapourSynth 有专家级了解、且能自己修 wrapper 的 bug，否则别用」。给爱折腾的人留门可以，但放进 V1 只会招来一堆「点了没反应」的反馈。

将来若改主意，要动的是：启动链设 `VSSCRIPT_PATH` → `mpv.conf` 加 `vf=vapoursynth=file=...`（可选 `buffered-frames` / `concurrent-frames` / `user-data`）。**记录备查，本版不实现。**

---

## 3. 界面布局（仿 FrankyRenderer，但砍掉多余层级）

### 3.1 结构

```
┌──────────────────────────────────────────────────────────┐
│ MPV 设置                                            ×     │  标题栏 34px
├────┬─────────────────────────────────────────────────────┤
│    │  播放                         [配置: mpv.conf]        │  页面标题 + 顶部工具行
│ 图 │                              [搜索设置项] [全部重置] │
│ 标 │  ┌ 播放 · 画面 · 音频 · 截图 · 快捷键 ┐ [重置页面]   │  可选二级标签（见 3.4）
│ 导 │  └──────────────────────────┘                        │
│ 航 │  ┌─ 卡片：播放 ───────────────────────────────┐     │
│ 56 │  │ 硬件解码        灰色副标题        [下拉]    │     │  行高 70px
│ px │  │ 单曲循环        灰色副标题        [开关]    │     │
│    │  └────────────────────────────────────────────┘     │
│    │  ┌─ 卡片：字幕 ───────────────────────────────┐     │
│    │  └────────────────────────────────────────────┘     │
│    │  ┌─ 卡片：纯音频封面 ─────────────────────────┐     │
│    │  │ 原始大小        灰色副标题        [开关]    │     │
│    │  │ 窗口上限        灰色副标题        [步进]    │     │
│    │  └────────────────────────────────────────────┘     │
│    │                              [ 确定 ] [ 取消 ]      │  底部右对齐
└────┴──────────────────────────────────────────────────────┘
```

- 窗口约 **1000 × 680**（Franky 内容区上限 960 × 620 @96dpi，行高 70px，侧栏常量 260px 是**展开态**；截图里是折叠态窄图标条）。V1 有 5 页，**固定 56px 窄图标条**，不做可展开侧栏——内容少，展开 260px 只会留大片空白。
- 左侧 5 个图标 + 1 个分隔 + 底部 1 个「关于」：播放 / 画面 / 音频 / 截图 / 快捷键。当前项用紫色块 + 左侧 3px 竖条（截图里的齿轮高亮样式）。
- 快捷键页是 5 张卡片的**平面长列表**，行高压到 52px（比其它页的 70px 矮，否则 38 行要滚很久），热键框固定 160px 宽右对齐；「默认 / 自定义 / 未绑定」用文字色 + 小徽标区分，不新增颜色。卡片可折叠但默认全展开，不做页内二级标签。
- 顶部工具行：页面大标题（粗体 24px）、右侧只读「配置：mpv.conf」标签、搜索框、「全部重置」「重置页面」。**砍掉「画质：自定义」下拉**——V1 没有画质档位，放一个不能改的下拉是纯装饰。
- 卡片：圆角 8px，标题栏带左侧紫色竖条；每行固定「标题 14px / 副标题 12px 灰字 / 右侧控件」三段式。这是截图里最值得抄的一点，副标题承担了「不让用户去查文档」的职责。
- 播放页三张卡片：「播放」放硬解 / 反交错 / 单曲循环 / 进度；「字幕」放 2 个开关 + 1 行**只读说明**（外挂字幕优先，灰色副标题「同名外挂字幕自动加载并优先于内封字幕」）；「纯音频封面」放 2 个控件 + 1 行只读说明，副标题统一写「仅对纯音频封面生效，不影响视频播放」。只读说明行右侧不放控件，用弱色文字，避免看起来像能点的开关。三张卡片合计会超出 620px 内容区，靠已有的滚动条处理，不为它压缩行高。
- 底部：确定（写盘并关闭）/ 取消（丢弃草稿并关闭）。Franky 能做到「OK 保留、Cancel 回滚」是因为它编辑 private draft；**新 EXE 直接用同一套草稿模型**（见 3.3）。不加「应用」按钮：mpv 多数选项重启才生效，加「应用」会给人「已经生效」的错觉。
- 右侧滚动条 + 卡片内分隔线，与截图一致。

### 3.2 配色（墨绿 + 薄荷，自有配色）

**已废弃**初稿「直接取 `src/UI/MenuTheme.h` 的 Indigo + Violet」的做法——一眼就能看出是照抄 FrankyRenderer。现改为自有墨绿系：底色用「近黑墨绿」做四级明度层次（页面 < 侧栏 < 卡片 < 卡片头），强调色用薄荷绿，与紫/靛蓝系彻底区分。

| 用途 | 变量 | 色值 |
|---|---|---|
| 窗口底 / 页面底 | `AppBrush` | `#131F11` |
| 侧栏 / 标题栏 / 底栏 | `SidebarBrush` | `#1B2918` |
| 卡片体 | `CardBrush` | `#243723` |
| 卡片头 | `CardHeaderBrush` | `#2E462E` |
| 边框 / 行分隔线 | `BorderBrush` | `#435A40` |
| 输入框 / 徽标底 | `InputBrush` | `#1B2C19` |
| 主文字 | `TextBrush` | `#FFFFFF` |
| 副标题 | `SubTextBrush` | `#D3E6D3` |
| 弱文字 / 说明 | `WeakTextBrush` | `#AFC4B3` |
| 强调色 `accent`（选中、开关、滑块、焦点条） | `AccentBrush` | `#A3F6BE` |
| 强调色悬停 | `AccentHoverBrush` | `#CCFFDF` |
| 侧栏 / 下拉选中底 | `ActiveBgBrush` | `#37543E` |
| 开关关闭态轨道 | `ToggleOffBrush` | `#4B6444` |
| 滚动条滑块 / 悬停 | `ScrollThumbBrush` / `ScrollThumbHoverBrush` | `#435A40` / `#5E7F57` |
| 提示条 标题 / 底 / 边 / 正文 | `WarnBrush` / `WarnBgBrush` / `WarnBorderBrush` / `WarnTextBrush` | `#C2FF72` / `#1A350E` / `#468921` / `#C9F0E6` |
| 阻止写入（红）/ 底 | `DangerBrush` / `DangerBgBrush` | `#FF9A9A` / `#3A2626` |
| 整窗遮罩（90% 不透明） | `OverlayBrush` | `#E6131F11` |

**规范：所有界面色只允许从 `Themes\Theme.xaml` 顶部的这组语义画刷取，XAML 里不得再出现字面十六进制**（包括滚动条、遮罩、关闭按钮悬停、结果窗）。单一强调色派生其余状态（`accentHover` 提亮、`activeBg` 用中间绿），保持「不花」。V1 不做主题切换，只做这一套暗色。

### 3.3 交互契约

1. 打开时对 `mpv.conf` / `hdr-autoswitch.conf` / `cover-art.conf` / `input.conf` / `HKCU\Software\BassMax` 做**快照**，并检查 `include=~~/cover-art.conf` 是否存在；所有控件编辑内存草稿。
2. 「确定」= 对比草稿与快照 → 只写变化的键 → 原子替换文件。`mpv.conf` 严格只写 diff；`cover-art.conf` 是 EXE 独占的 5 行固定结构，可整文件重写。「取消」= 什么都不写。
3. 打开时若 `mpv.exe` 或 `mpv.conf` 不存在，整窗禁用控件并显示原因（照抄 `MpvUsable()` 逻辑）。
4. 检测到值不在任何预设内（尤其 HDR 档位），显示「自定义」并置灰该控件，**绝不静默覆盖**。
5. 搜索框只做行过滤，V1 成本极低但为后续扩展留口；不做跳转高亮动画。

### 3.4 关于二级标签页

截图里的「输出 / 背景 / 主题」是**页内再分层**。V1 常规页只有 1–3 张卡片；快捷键页虽有 5 张卡片，但都是同一层级的动作清单，只是分组容器，**同样不需要二级标签**。等 V2 参数涨上来再加。现在加就是空 tab，纯噪声。

---

## 4. 写盘契约（与 setting.exe 并存）

**已定：不接管。** `setting.exe` 是快速设置，新 EXE 只是它的一个按钮入口；两处都要点击才写入，同时打开时**以最后一次点击为准**，跟其他播放器设置页一样。不做进程检测、不做互斥、不改旧页为只读、不加文件锁。

两个程序读写同一份 `mpv.conf`，按下面做就行：

1. **只写 diff 键，逐行替换**：保留注释行、空行、未知键、原有键顺序。不重写整个文件（docx 里「精准替换」这一条是对的）。这一条才是「最后一次点击为准」不互相覆盖的关键。
2. **编码 UTF-8 无 BOM + CRLF**。`hdr-autoswitch.conf` 首行注释必须保留（旧版 AHK 写 BOM 时，mpv 的 `read_options` 会把带 BOM 的第一行当 unknown key 丢掉）。
3. **写前重新读盘**（保证写的是最新内容，而不是打开窗口时的旧快照），写时「临时文件 + `MoveFileEx(REPLACE_EXISTING)`」原子替换，全程不长期持锁。
4. **联动键成组写**：HDR 模式 4 键、档位 4 键，要么全写要么全不写；中途失败回滚到备份副本。
5. 值规则与 `MpvWriteKey` 一致：找不到键就追加到末尾；`af` 关闭写 `af=` 空值行而非删行；路径正斜杠 + 尾斜杠。
6. 安装根目录从 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\mplayerl.exe\Path` 读（与 setting.ahk 第 19–24 行一致），**不要硬编码 `<项目根>`**。
7. `menu.conf` 一律只读，不碰。**现有 4 个脚本**（`fit90.lua` / `hdr-autoswitch.lua` / `stats.lua` / `tone-ab.lua`）只读；EXE 可新建并独占管理自己的脚本（已拍板，见第 11 条与 1.6）。`input.conf` **改为页 E 独占写**，规则见第 9 条；`setting.exe` 日常不写这份文件，不存在并发覆盖问题。**例外**：`MpvRestoreDefaults:` 会用 `input_default.conf` 整文件覆盖 `input.conf`，属「恢复默认」场景（第 12 条 / 阶段 ⑨），不是并发写。
8. **封面功能是例外之一**：`cover-art.conf` 由 EXE 独占创建 / 读写（内容见 1.2），`mpv.conf` 只在末尾维护一行 `include=~~/cover-art.conf`。这行是普通键，`setting.exe` 的逐行替换会当未知键原样保留；`MpvRestoreDefaults:` 整文件重建会冲掉它，但 `cover-art.conf` 文件仍在——EXE 每次打开检测该行缺失就补回，不做进程检测、不加文件锁、不弹提示。
9. **`input.conf` 是例外之二，也是唯一一处「非 `key=value` 格式」的写盘**（详见 1.3）：
   - 语法 `KEY command`，**没有 `=` 号**；UTF-8 无 BOM + **LF**（不是 CRLF）；`#` 为注释，`SHARP` 才是 `#` 键，`ignore` 是解绑命令。必须由独立解析器处理，不能复用 `MpvWriteKey`。
   - **只写用户改动过的行**：默认态不写、留空态不写、清空自定义行 = 删行（恢复默认）。定位按「动作 ID 对应的规范命令串」精确匹配，数值写法差异（如 `1.0` vs `1`）先归一化再比对。
   - **其余行一律原样保留**：不在 38 行清单里的自定义绑定、注释、`{section}` 行、键序列、分号多命令、OSC 相关绑定。识别不了就放进「其他绑定」只读区展示，绝不擅自改写或删除。
   - 写前重读 + 临时文件原子替换（沿用第 3 条）；写后行数应约等于「原有未知行 + 用户改动数」。
   - 不做 `ignore` 解绑、不做键序列、不做多命令输入——这三条会立刻把简单页面变成编辑器，属于 1.3 明确划掉的边界。

`setting.exe` 的「MPV专属设置」页保持现状，一个字都不用改；快速设置页那 4 个总开关也照旧。10. **无封面音频是例外之三**（见 1.6）：`audio-info.conf` 由 EXE 独占创建 / 读写，`mpv.conf` 末尾维护一行 `include=~~/audio-info.conf`（开关关闭时移除该行、保留文件）。与 `cover-art.conf` 同理，`MpvRestoreDefaults:` 冲掉 include 行时，EXE 每次打开按开关状态补回。条件 profile **必须带 `profile-restore=copy`**，否则从音频切到视频会残留 640x180 小窗口。profile 里只放窗口尺寸，不放 `osd-msg1` / `lavfi-complex`。
11. **已拍板通过（脚本方案，见 1.6）**：`mpv.conf` 固定写 `input-ipc-server=MyMPC_mpv`；`script-opts\MyMPC.conf` 与 `scripts\MyMPC_ui.lua` 由 EXE 独占创建 / 读写；现有 4 个脚本与 `input.conf` / `menu.conf` 一行不改。`script-opts` 是 `key=value` 格式、UTF-8 无 BOM，可复用 `MpvWriteKey` 的逐行替换逻辑；Lua 文件整文件生成，不做 diff。**边界收紧为「EXE 只管自己新建的文件，用户手写脚本永远不动」**。
12. **备份 / 默认模板（阶段 ⑨，最后做）**：`MpvRestoreDefaults:`（1305）从 `*_default.conf` 整文件覆盖 4 个配置；桌面「导出 / 导入播放器设置」目前只覆盖 MPC-HC / MPC-BE / PotPlayer，**不含 MPV**。这两处都要等 EXE 键集定稿后统一做：把 `include=~~/cover-art.conf`、`include=~~/audio-info.conf`、`input-ipc-server=MyMPC_mpv`、`script-opts\MyMPC.conf`、封面上限、页 E 自定义键行纳入默认模板与备份清单，并复核 `DEF:` 是否仍需 `allPlayerWithMpv`（MPV 只读配置、不回写，理论上只提示即可）。阶段 ①–⑧ 不碰这些。

唯一要验证的是：两个窗口先后写不同的键，先写的那个不会被后写的整文件覆盖——第 1 条已保证；以及「恢复默认」后新 EXE 能自动补回两条 `include=` 行。**「恢复 MPV 默认配置」会从 4 个 `*_default.conf` 模板整文件覆盖 `mpv.conf / input.conf / menu.conf / hdr-autoswitch.conf`**：`input.conf` 里的自定义键与 EXE 写入的改键行会一起被冲掉，`mpv.conf` 的 `include=` / `input-ipc-server` 也会丢。这不是并发问题，属默认模板内容问题 → 归阶段 ⑨ 统一处理（EXE 每次打开按开关状态补回 include 行的机制仍然有效）。

---

## 5. 集成点（改 `setting.ahk` 的位置）

新 EXE 的定位就是 `setting.exe` 快速设置里的一个按钮入口，地位与 MPC-HC / MPC-BE / PotPlayer 各自的设置页相同：点开 → 改 → 写盘 → 生效。**除此之外不做任何集成动作**：不接管旧页、不加进程检测、不加锁。

`mplayerl:` 标签已经算好了 `selectedPath`，只是没用。补一个分支即可：

```ahk
if (InStr(selectedPath, "mpv", false)) {
    Run, MpvSettings.exe          ; 新 EXE
    SB_SetText("已打开 MPV 设置", 2)
    return
}
```

放在 `Run, mplayerl.exe` 之前。

**2026-11-13 19:24 用户已按此更新 `setting.ahk`**：MPV 只读配置文件、退出不回写，配置更改只 `Process Exist` + 状态栏提示「重启 MPV 后生效」，**不关闭播放器**（`MpvAction` 1270–1274、总开关 `MpvSyncGlobal` 1789–1795）；`killProcesses(allplayer)` 仍不含 `mpv.exe`。音效参数（`HKCU\Software\BassMax`）改完即时生效，`af` 与其余 `mpv.conf` 项重启 mpv 才生效。唯一例外是 `DEF:` 恢复默认仍传 `allPlayerWithMpv`，是否收敛为「只提示」留阶段 ⑨ 复核。

---

## 6. 技术选型（已定：C# + WPF）

**C# + WPF（.NET 10，单文件 publish）**。理由：XAML 能 1:1 还原截图的圆角卡片和紫色高亮；无 WebView2 运行时依赖；单文件 EXE 十来 MB，杀软误报率远低于 PyInstaller 打出来的 30MB+ 包。Tauri 要拉 Rust 工具链，PySide6 打包误报高，AHK v1 原生 Gui 做不出这个质感（硬做要自绘 GDI+，成本最高）。Franky 本身是手写 Direct2D/DirectWrite，**只抄它的视觉规范和「草稿 + 事务」模型，别抄它的实现**。

**版本已定：.NET 10 LTS / SDK 10.0.404**（2026-11-13 20:04 拍板）。查证结论：`.NET 8` 已于 **2026-11-10 EOL**（末代补丁 8.0.34），不再收安全更新；下载页把 `.NET 11`（同日 GA、STS）标「推荐」只代表最新 GA 版本，不是技术建议；`.NET 10` 是 LTS，与 11 的终止支持日期同为 **2028-11-14**，但更新策略保守、WPF 工具链适配最稳，且已发布一年、目标机运行时普及率高。本机实测 `dotnet --list-sdks` = `8.0.428` + `10.0.404`、`dotnet --version` = `10.0.404`；本机已有 `Microsoft.WindowsDesktop.App 10.0.8`，framework-dependent EXE 直接可跑。**8.0.428 保留不卸**（多 SDK 可共存，用 `global.json` 锁版本即可），但别拿它新建项目；更别卸 8 的运行时，机器上还有别的软件依赖多个运行时版本。

**项目配置（阶段 ① 建项目时照抄）**：

```xml
<!-- .csproj -->
<TargetFramework>net10.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<RollForward>Minor</RollForward>
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>false</SelfContained>
```

```json
// global.json（项目根目录，锁 SDK，防止多版本共存时挑错）
{ "sdk": { "version": "10.0.404", "rollForward": "latestFeature" } }
```

`<RollForward>Minor</RollForward>` 是默认值，显式写出便于审查：只接受 `10.0.x` 任意补丁，**不跨大版本**。别设 `Major`——让别人装了 .NET 11 也能跑听着省事，但 WPF 跨大版本行为不可预期；版本不匹配就该明确报错，分发说明里写死装 10 更干净（见 6.1）。

**发布**：`dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained false -c Release`。framework-dependent 单文件约十来 MB，对方机器需要 .NET 10 桌面运行时。只有确认分发对象装不了运行时时，才改 `--self-contained true`（EXE 约 60–90MB，自解压单文件更易被杀软误报，用户明确在意这点，**不作默认方案**）。

**阶段 ① 已完成（2026-11-13 20:2x）**：建好 WPF 项目与 5 页静态骨架（主题 + 卡片 + 控件，**不接任何读写逻辑**），编译 0 警告 0 错误，真实窗口逐页点击验证导航与渲染通过，单文件 EXE 已发布，详见 6.2。**阶段 ② 已完成**（读盘 + 反推 + 搜索 / 重置，详见 6.5），仍是只读阶段。**阶段 ③ 已完成**（草稿 diff 写盘 + 联动校验，详见 6.6），首次真正落盘。**阶段 ④ 已完成**（音效链路 `af` + BassMax 注册表六键双写，详见 6.7），首次写注册表。**阶段 ⑤ 已完成**（纯音频封面 `cover-art.conf` + `include` 行，详见 6.11），首次整文件新建。**阶段 ⑥ 已完成**（`input.conf` 行级改键 + 冲突分级，详见 6.12），首次写「非键值对」格式的配置文件。**阶段 ⑦ 已完成**（`audio-info.conf` + `MyMPC_ui.lua` + `MyMPC.conf` + `fit90.conf` + 单实例 + 窗口 960x540 老 bug + **转发后抢回前台**，详见 6.13），首次落地多文件联动接线并破例修改既有脚本 `fit90.lua`。**阶段 ⑧ 已完成**（`setting.exe` 按钮接入 + 主按钮对 MPV 的分支 + 右键「选项」+ 改名「MPV 简单设置」+ DWM 圆角，详见 6.14），首次让配置工具与播放器互相拉起。下一步只剩阶段 ⑨：默认模板与备份。

阶段拆分：① 静态骨架（5 页 + 卡片 + 主题，无逻辑）→ ② 读盘 + 反推 + 搜索/重置 → ③ 草稿 diff 写盘 + 联动校验 → ④ 音效链路（conf + 注册表双写）→ ⑤ 纯音频封面（独立 conf + include 行 + 条件 profile）→ ⑥ 快捷键页（静态默认表 + `KEY command` 解析器 + 键盘捕获控件 + 冲突校验）**已完成** → ⑦ **方案 B + 单实例**：`audio-info.conf`（只管窗口）+ `MyMPC_ui.lua`（ASS overlay + IPC 转发）+ `MyMPC.conf` + `input-ipc-server` 固定键 → ⑧ 集成 `mplayerl:` 分支 + 验证两个窗口交替写入互不覆盖（只验，不写互斥逻辑）**已完成，另加了右键「选项」与改名，详见 6.14** → ⑨ **已完成**：默认值内嵌进 EXE、由 `--default` 直接写（见 6.15），备份 / 导入导出在 ⑩b 落地（见 6.17–6.18），不再要四个 `*_default.conf` 模板 → ⑩a **已完成**：af=bassmax 常驻 + 左下角重启播放器按钮（见 6.17） → ⑩b **已完成**：自定义文件夹导入导出 + 关于页入口 + 主题弹窗 + 页脚按钮对齐（见 6.18）。

**阶段 ①–⑩c 全部完成，2026-11-15 23:2x 定版 `1.0.0`（图标 / 路径探测 / 界面文案 / 安装包模板，见 6.19）；⑩d 于 2026-11-16 00:0x 完成工程目录整理与 `<安装包脚本>.iss` 的 MPV 安装接入（见 6.20），EXE 本体未再改动。** 阶段 ⑧ 除「按钮集成 + 两窗口交替写」外，还按用户 2026-11-15 00:0x 追加的四件事一起做掉：改名「MPV 简单设置」、三处 UI 修正（工具行两列 / 页脚两列 / DWM 圆角）、MPV 右键加「选项」并换成可配色的 OSD 菜单、主按钮在选中 MPV 时直接开设置程序（详见 6.14）。`MpvSettings.exe` 停在 **416,982 字节 / `1.0.0`**（⑩c：专属图标 + App Paths 探测收紧 + 界面文案精简 + 12.mpv 模板同步清理；md5 `10217cf2…`）。 阶段 ⑦ 的两个前置遗留（Lua 连 named pipe、转发后窗口恢复 960x540）**都已结案**：管道方案 2026-11-13 02:1x 跑通，窗口问题的两条根因（纯音频不建窗需 `force-window=yes`、条件带 `audio-params` 会抖动触发 `Restoring profile`）2026-11-14 22:0x 定位并修复，真机与生产双验证通过。「窗口自动适配」比例按用户授权外置到 `script-opts\fit90.conf`（`fit90.lua` 破例修改，90 仅为出厂默认）；**2026-11-14 22:2x 用户修正语义后，该比例只作用于 `Alt+4` 这一次按键，不再随打开文件自动应用**。**阶段 ⑨ 明确排在 ①–⑧ 之后**：默认模板 / 备份清单依赖 EXE 最终键集，提前做必然返工；`setting.ahk` 今日已改的「只提示不关播放器」不需要 EXE 配合，已可独立生效。

### 6.1 分发：别人打不开时让他装什么

**结论：`.NET 桌面运行时 10.0.x` → Windows → `x64` → 「安装程序」列**（入口 `dotnet.microsoft.com/download/dotnet/10.0`）。

| 别装 | 原因 |
|---|---|
| ASP.NET Core 运行时 | 给 Web/服务器程序用的，与这个 WPF EXE 无关 |
| 单独的「.NET 运行时」 | 桌面运行时**已包含** .NET 运行时，微软页面写明「无需单独安装」 |
| 「二进制文件」列的 zip | 要手动配环境变量，普通用户装不上 |
| .NET 11（缺运行时弹窗的默认下载链接） | `RollForward=Minor` **不跨大版本**，装 11 照样打不开 |

- 架构取 `x64`（EXE 为 `win-x64`）；只有 ARM64 Windows 设备换 `Arm64`，`x86` 基本用不上。
- 版本不必死守某个补丁：framework-dependent 只认大版本 `10`，任意 `10.0.x` 都能跑（本机 10.0.8 即证据）。给通用 10.0 页面，别给会过期的具体补丁直链。
- 安装程序要**管理员权限**；公司/受限机器装不了时，只能改发 `--self-contained true` 版本并接受体积与误报代价。

**可直接贴进对外分发说明 / EXE 缺运行时提示**：

> 若启动时提示「必须安装 .NET」，请到 `dotnet.microsoft.com/download/dotnet/10.0` 下载 **.NET 桌面运行时 10.0.x → Windows x64 安装程序**，安装完成后重新打开即可。不要安装 ASP.NET Core 运行时，也不要安装 .NET 11。

### 6.2 阶段 ① 交付（2026-11-13 20:2x，已完成）

**项目**：`<项目根>/MpvSettings//`（独立源码目录，未放进 `00.Core`，不参与安装包）。`global.json` 锁 `10.0.404`；`MpvSettings.csproj` = `net10.0-windows` + `UseWPF` + `RollForward=Minor` + `PublishSingleFile` + `SelfContained=false`。

| 文件 | 作用 |
|---|---|
| `Themes\Theme.xaml` | 3.2 色板 + 卡片 / 三段式行 / 开关 / 下拉 / 按钮 / 导航 / 徽标全套样式 |
| `Controls\OptionRow.cs` / `SettingCard.cs` / `ValueSlider.xaml` | 复用控件；`OptionRow.IsNote` 表示只读说明行（弱色文字、右侧无控件）；`ValueSlider` = 滑块 + 数值框，鼠标粗调 / 方向键精调（20:37 起替代 `NumericStepper`，旧控件已删除） |
| `MainWindow.xaml` | 自绘 34px 标题栏、56px 图标导航、顶部工具行、滚动内容区、底部确定/取消 |
| `Views\*.xaml` × 6 | 播放 / 画面 / 音频 / 截图 / 快捷键 / 关于 |
| `Models\ShortcutData.cs` | 页 E 的 38 行静态清单（E1 9 + E2 7 + E3 8 + E4 10 + E5 4） |

**边界**：控件显示的全是规格里的出厂默认值，硬编码静态占位；不读 `mpv.conf` / 注册表、不写任何文件、不启动播放器。搜索框、全部重置、重置页面、确定按钮均 `IsEnabled=False` 并标注所属阶段；取消与标题栏关闭可用。数值滑块只做 UI 内部数值变化，不接草稿。

**验证**：`dotnet build -c Release` → **0 警告 / 0 错误**；`dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained false -o publish` → `publish\MpvSettings.exe` **210 KB**（framework-dependent，本机 10.0.8 运行时直接可跑）。真实窗口逐页点击实测：5 个导航项 + 关于都能切换，页面标题 / 配置标签 / 卡片内容同步更新，无文字重叠、裁切或黑块。截屏：`MPV设置EXE_阶段1_播放页.png`、`MPV设置EXE_阶段1_音频页.png`、`MPV设置EXE_阶段1_快捷键页.png`。

> 实测踩坑：150% DPI 下用 `mouse_event` 模拟点击，Python 脚本必须先 `SetProcessDpiAwareness(2)`，否则屏幕坐标被系统二次缩放、点击落到窗口外（表现为「截图哈希变了但页面没换」）。这是验证脚本的问题，不是 EXE 的问题。

### 6.3 阶段 ① 审阅修订（2026-11-13 20:37–20:4x）

用户看完截屏提了两处，并给出 BassMax 源码目录 `<项目根>/bassmax`，据此核对了预设名与参数范围。

| # | 用户意见 | 落地 |
|---|---|---|
| 1 | 音效驱动上限是 1.0 不是 2.0 | `Drive` 改 `Maximum=1`；按源码 `BACKUP.md` 参数表顺带校正 `Bass/Treble` 由 0~12 改为 **−12~+12** |
| 2 | 加减号改成滑块，鼠标快速拖动 + 键盘精确控制 | 新建 `Controls\ValueSlider.xaml(.cs)` 与 `Theme.xaml` 的 `DarkSlider` 样式；替换音效 3 项 + 封面窗口上限 + 新增的比例项，删除 `NumericStepper.*` |
| 3 | 音效预设给文字，数字别人看不懂 | 下拉 6 项改为 `标准乐 · 原声` … `摇滚乐 · V 型`（`presets()` 名称 + Chrome 扩展短说明），默认选中 4 爵士乐；顺序 = 写盘值 0~5 |
| 4 | 「窗口适配 90%」本意是「自动」、对齐其它播放器，90% 只是默认值但允许更改 | 页 E 动作名改「窗口自动适配」；页 A 新增「窗口自动适配」卡片 + 比例滑块（默认 90%，50~100 步长 1）。**2026-11-14 22:2x 更正**：不是「打开文件自动」，只有 `Alt+4` 按屏幕比例；卡片与动作名随之改为「Alt+4 · 按屏幕缩放」 |

**滑块交互实测**（`MPV设置EXE_阶段1修订_滑块交互实测.png`）：Drive `1.00` → 鼠标点击轨道 30% 处 `0.30` → 方向键右 ×3（每次 0.05）`0.45` → PageUp（0.10）`0.55`。第一版方向键无效，原因是点击轨道时 WPF 不把键盘焦点交给 Slider；已在 `ValueSlider` 的 `PreviewMouseDown` 显式 `Focus()`，并自行接管 `PreviewKeyDown`（设 `Handled`，避免与 Slider 原生处理叠加成双倍步进）。

**验证**：修订后 `dotnet build -c Release` 仍 **0 警告 / 0 错误**，重新 `dotnet publish` 出单文件 EXE。截屏 `MPV设置EXE_阶段1修订_音频页.png`、`MPV设置EXE_阶段1修订_播放页_底部.png`、`MPV设置EXE_阶段1修订_快捷键E4.png`。

⚠ **遗留**：页 A 的「自动适配比例」目前只是静态占位（阶段 ① 不接读写）；`fit90.lua` 硬编码 `0.9` 且与「一行不改」边界冲突，阶段 ⑦ 需定夺比例如何真正生效（详见页 A「窗口自动适配」）。控件数由约 27 → 约 28；该比例的 `script-opt` 写入位置未定，暂未计入 conf 键数。

> ✅ **2026-11-14 已落地**：比例写入 `script-opts\fit90.conf` 的 `ratio`（EXE 独占），`fit90.lua` 改为读该文件（用户已授权破例），控件仍是 50–100 步长 1、默认 90。conf 键数 +1（`ratio`）。**同日 22:2x 再修正**：`auto` 键取消，比例只在按 `Alt+4` 时使用。详见 6.13。

**下一步**：阶段 ② 读盘 + 反推 + 搜索/重置；`ShortcutData` 的静态清单在阶段 ⑥ 换成「内置弱绑定静态表 + `input.conf` 强绑定」。

### 6.4 阶段 ① 补充修订：侧栏图标配色（2026-11-13 20:53）

**用户反馈**：侧栏未选中图标是黑色，在 `#16192B` 深色背景上几乎看不见，应改白色或其它颜色（用户截屏 `PixPin_2026-11-13_20-51-47.png`）。

**根因**：`Theme.xaml` 的 `NavRadio` 样式**没有给 `Foreground` 设默认值**，只有 `IsChecked=True` 触发器把它设成紫色。`MainWindow.xaml` 里 6 个图标 `Path` 的 `Fill` / `Stroke` 都绑定到 `RadioButton.Foreground`；自绘 `ControlTemplate` 不会继承系统前景色，未选中态就回退到 WPF 默认黑色。选中项恰好有紫色所以一直正常，问题只在未选中态。

**修法**（只改 `Themes\Theme.xaml` 一处样式，不动任何图标 Path）：

| 状态 | Foreground | 效果 |
|---|---|---|
| 未选中（默认） | `SubTextBrush` `#C0C5E2` | 浅灰白，清晰可见 |
| 悬停 | `TextBrush` `#F3F3FF` | 纯白 + 背景块高亮 |
| 选中 | `AccentBrush` `#B59AFF` | 紫色 + 紫色左竖条 + 圆角背景块（保持原样） |

触发器声明顺序为 `IsMouseOver` → `IsChecked`，后者覆盖前者；因此「选中项同时被鼠标悬停」仍保持紫色，不会出现层级倒挂。**规范**：以后在 `MainWindow.xaml` 新增任何导航图标，只要沿用 `Fill` / `Stroke` 绑 `Foreground` 的写法即可自动获得三态配色，不需要逐项设颜色。

**验证**：`dotnet build -c Release` 0 警告 / 0 错误；真实窗口截取三态（`MPV设置EXE_阶段1修订_侧栏图标三态.png`）与改前改后对比（`MPV设置EXE_阶段1修订_侧栏图标_前后对比.png`）——5 个未选中图标（画面 / 音频 / 截图 / 快捷键 / 关于）全部由黑变浅白，底部「关于」信息圆圈同样修复，播放三角选中态仍是紫色；悬停变纯白并带背景高亮。重新 `dotnet publish` 出单文件 EXE。

---

### 6.5 阶段 ② 交付（2026-11-13 21:1x，已完成）

**范围**：读盘 + 反推 + 搜索 / 重置 + 草稿模型。**仍不写任何配置文件**——确定按钮继续禁用并标注「阶段 ③ 接入草稿 diff 写盘」，注册表只读，`input.conf` 只读，`scripts\*.lua` 一行未动。

#### 新增 / 修改文件

| 文件 | 作用 |
|---|---|
| `Services\ConfigLocator.cs` | 配置目录定位、可用性判定（`MpvUsable()` 等价）、各文件路径 |
| `Models\ConfigSnapshot.cs` | `ConfFile`（逐行 `key=value` 解析 + 原始行 / BOM / CRLF 保留）、`BassMaxSnapshot`（六键 + 齐全性）、`ConfigSnapshot`（一次快照全部配置源） |
| `Models\SettingsDraft.cs` | `ObservableObject` + 草稿的 45 个属性（约 28 个可编辑控件 + 下拉选项 + 只读状态）、`LoadFrom()` 反推、`ResetAll()` / `ResetPage()` |
| `Models\Converters.cs` | `InverseBoolConverter`（自定义态置灰）、`BoolToVisibilityConverter` |
| `MainWindow.xaml(.cs)` | `DataContext = 草稿`；搜索过滤、重置、页 D 只读动作、配置源 / 快照状态行、不可用覆盖层；切页 `ScrollToTop()` |
| `Views\*.xaml` × 5 | 播放 / 画面 / 音频 / 截图 / 关于改为 TwoWay 绑定；页 D 四个动作按钮启用；音频页新增「注册表状态」只读行 |
| `App.xaml(.cs)` | 去掉 `StartupUri`，手动建窗以支持 `--dump-only` 无窗口验证 |
| `Themes\Theme.xaml` | 注册两个转换器；`DarkTextBox` 补 `IsEnabled=False → Opacity 0.45` |
| `_ref\stage2_verify.py` / `_ref\stage2_ui_verify.py` | 反推断言 + 真实窗口交互验证脚本 |

#### 配置定位（不硬编码路径）

按 `setting.ahk` 第 19 行读 `HKLM\SOFTWARE\...\App Paths\mplayerl.exe` 的 `Path`（显式 64 位视图），配置在 `<root>\MediaPlayer\mpv\portable_config`。本机实测 `Path = <安装根>`，其 `mpv.conf` 与源码包 `12.mpv\portable_config\mpv.conf` 内容一致。优先级：`--config-dir=<dir>` → App Paths → 从 EXE 目录向上找 `12.mpv\portable_config`（开发包回退）。可用性 = `mpv.exe` 存在 **且** `mpv.conf` 存在；不满足则整窗盖一层禁用遮罩并显示原因。窗口底部与关于页都显示来源和完整路径，避免「以为在改 A、实际在读 B」。

⚠ **实测踩坑**：`--config-dir` 常指向一个只有配置的副本目录，本身没有 `mpv.exe`。第一版误把安装根目录设成副本的父目录，导致误判「未安装 MPV」并弹出禁用遮罩。已改为 **`mpv.exe` 候选始终包含 App Paths 的安装根目录**，`--config-dir` 只覆盖配置目录。

#### 反推规则（已落地）

| 来源 | 反推要点 |
|---|---|
| `mpv.conf` | 缺键 → **本包出厂默认**（不是 mpv 原生默认）；`af=` 空值行按「关闭」而非缺键；`audio-spdif` 非空 → 音效开关置灰并提示互斥；`screenshot-dir` 与桌面 / 图片 / mpv 目录比对，命中 0/1/2，其余为「自填路径」并启用路径框 |
| `script-opts\hdr-autoswitch.conf` | HDR 模式要求 `mode` + `sdr_passthrough` + `manage_system_hdr` + `osd_notify` **四键整组精确命中**，否则「自定义」；`only_fullscreen` 单独反推 |
| `mpv.conf` 四键组合 | 色调档位要求 `inverse-tone-mapping` / `contrast` / `brightness` / `saturation` 全命中（`contrast` 4/16/39/55），否则「自定义」并置灰，**禁止写入**以免冲掉手工标定 |
| 下拉通用 | 值不在预设内 → 动态追加一项「自定义（当前值：xxx）· 不覆盖」并选中，`IsEnabled = !Custom` |
| `cover-art.conf` | `video-unscaled` / `autofit-larger`（解析 `1200x1200` 取首数、吸附到 100 步长、钳到 400–1600）；文件不存在按出厂默认 |
| `script-opts\MyMPC.conf` | `no_art_ui=no` 才算用户关闭；`single_instance=no` 才反推为「允许多开」；`fit_ratio` 存在则读，否则 90 |
| `HKCU\Software\BassMax` | 六键读取并计算齐全性，缺项在「注册表状态」只读行显示；数值钳到控件范围 |

**两处语义决策（写清楚，免得以后扯皮）**：

1. **EXE 独占文件尚未创建时按出厂默认展示**。当前生产目录里 `cover-art.conf` / `audio-info.conf` / `script-opts\MyMPC.conf` 都不存在、`mpv.conf` 也没有两条 `include=` 行 —— 这是「阶段 ⑤ / ⑦ 还没落地」，不是用户把开关关了。所以只有显式写了 `no_art_ui=no` 或 `single_instance=no` 才反推为关闭；`include=` 行缺失由阶段 ③ / ⑦ 按开关状态补回，遵循第 4 节第 8 / 10 条。
2. **重置 = 恢复出厂默认，不是撤销本次修改**。「全部重置」全部页、「重置页面」只当前页；「取消」才是丢弃整个草稿并关闭。三者的 ToolTip 已写明。重置不清掉只读状态（配置源、注册表齐全性、比特流直通检测）。

#### 交互

- **搜索**：顶部输入框（带水印）按 `标题 + 副标题` 不区分大小写过滤当前页的 `OptionRow`，整张卡片所有行都不命中时连卡片一起隐藏；切页后重跑过滤。页 E / 关于页没有 `OptionRow`，搜索框自动置灰（1.3 明确不做页内 / 全量搜索）。**切页时 `ScrollViewer.ScrollToTop()`** —— 五个页面共用同一个滚动容器，不复位会把上一页的滚动位置带过去（实测从播放页底部切到快捷键页会停在 E3）。
- **重置**：顶部两个按钮 + 页 D 的「恢复默认 / 重置本页」都只改草稿。
- **页 D 动作**：`mpv.conf` / `配置目录` 用系统默认程序只读打开，不写盘。
- **不可用态**：半透明遮罩 + 原因文字（区分「未安装 MPV」与「缺少 mpv.conf」）+ `--config-dir` 提示，控件同步禁用。

#### 验证

- `dotnet build -c Release` **0 警告 / 0 错误**（版本号升到 `0.2.0-stage2`）。
- `_ref\stage2_verify.py`：**45 个草稿属性逐项断言**，两组配置 —— 基线（App Paths 真实配置）与变异配置（改 15 个键、新建 3 个 EXE 独占文件、`audio-spdif=auto`、非预设 `hwdec=auto-copy`、档位 0.8 组合、自填截图路径）。全部通过；**生产配置目录与变异目录的文件哈希在启动前后完全一致**，证明阶段 ② 零写盘。
- `_ref\stage2_ui_verify.py`：真实窗口逐页截屏 —— 自定义态显示「自定义（当前值：auto-copy）· 不覆盖」并置灰；HDR 转 SDR / 0.8 / TV 16–235 / 仅全屏开启；音效链路关闭且置灰、注册表 `6 / 6 齐全`；封面 800 × 800、比例 75%、允许多开开启；搜索 `hwdec` 只剩一行、搜 `zzzz` 全隐藏、清空恢复；变异态点「重置页面」回到 `auto-safe（推荐）` 与出厂开关；`--config-dir` 指向不存在目录时遮罩显示「缺少 mpv.conf」。截屏前缀 `MPV设置EXE_阶段2_*`。
- 关于页显示 `App Paths 注册表 · <MPV配置目录>/...`、快照行数、注册表状态、比特流直通状态。

#### 验证用命令行开关（不是 UI 功能）

| 开关 | 作用 |
|---|---|
| `--config-dir=<dir>` | 显式指定 portable_config（变异配置实测 / 分发排错）；不带时走 App Paths |
| `--dump-draft=<json>` | 把反推后的整个草稿导出成 JSON 便于断言。**不带该参数时 EXE 一个字节都不写**；带时也只写用户显式指定的调试文件，绝不碰配置目录 |
| `--dump-only` | 只做「读盘 → 反推 → 导出」，不弹窗口（`App.xaml.cs` 手动建窗后直接 `Shutdown`） |

#### 留给阶段 ④ 及之后的接口

阶段 ③ 的 `WritePlanBuilder.Build(draft, baseline, snapshot)` 已把「草稿 diff → 按文件分组的键变更」独立出来，后续阶段只需往计划里追加目标（注册表、`cover-art.conf`、`MyMPC.conf`、`input.conf`），`ConfWriter` 的原子替换 / 事务回滚可直接复用（`input.conf` 的 `KEY command` 格式需要独立解析器，仍归阶段 ⑥）。仍未接的：注册表写入（④）、封面独立 conf + `include` 行（⑤）、快捷键编辑（⑥）、`MyMPC_ui.lua` 与单实例（⑦）、页 A 比例的真正生效（`fit90.lua` 硬编码 `0.9`，阶段 ⑦ 定夺）、默认模板 / 备份（⑨）。

---

### 6.6 阶段 ③ 交付（2026-11-13 21:3x，已完成）

**范围**：草稿 diff 写盘 + 联动校验。**首次真正落盘**，但严格限定在两份 `key=value` 文件：`mpv.conf` 与 `script-opts\hdr-autoswitch.conf`。`input.conf` / `menu.conf` / 四个现有 Lua 脚本 / 注册表 / 生产配置目录一行未动。

#### 写盘键集（本阶段）

| 页 | 文件 | 键 |
|---|---|---|
| A 播放 | `mpv.conf` | `hwdec` / `deinterlace` / `loop-file` / `save-position-on-quit` / `subs-match-os-language` / `subs-fallback-forced` |
| B 画面 | `mpv.conf` | 档位四键 `inverse-tone-mapping` + `contrast` + `brightness` + `saturation`；`video-output-levels` |
| B 画面 | `hdr-autoswitch.conf` | HDR 模式四键 `mode` + `sdr_passthrough` + `manage_system_hdr` + `osd_notify`；`only_fullscreen` |
| C 音频 | `mpv.conf` | `audio-channels`（`af` 与注册表六键归 ④） |
| D 截图 | `mpv.conf` | `screenshot-format` / `screenshot-dir` / `screenshot-high-bit-depth` |

**明确不写**：`af` 与 `HKCU\Software\BassMax` 六键 → ④（已完成）；`input.conf` → ⑥；`MyMPC.conf` / `audio-info.conf` / `input-ipc-server` / `MyMPC_ui.lua` → ⑦。用户改了这些控件时，结果窗口首屏会用提示条列出「本次有 N 类改动属于后续阶段，本次未写入」，避免误以为已经生效。**不含 `include=` 行**：文件还没由 ⑤ / ⑦ 创建时单独写 include 会让 mpv 启动报 `Failed to open cover.conf`。

#### 新增 / 修改文件

| 文件 | 作用 |
|---|---|
| `Models\WritePlan.cs` | `WritePlan` / `FilePlan` / `KeyChange` / `WriteResult`；`DraftBaseline`（打开时的草稿副本）；`DraftPatch`（CLI 验证用反射覆盖草稿） |
| `Services\WritePlanBuilder.cs` | 草稿 diff → 写盘计划；联动组表、自定义态保护、索引 / 路径校验、后续阶段延后清单 |
| `Services\ConfWriter.cs` | 写前重读、逐行替换、UTF-8 无 BOM + 原换行风格、临时文件 + `MoveFileEx(REPLACE_EXISTING\|WRITE_THROUGH)` 原子替换、多文件事务回滚 |
| `ApplyResultWindow.xaml(.cs)` | 自绘结果对话框：写入明细（含成组标记 / 前后值）、跳过提示、延后提示条、阻止项 |
| `MainWindow.xaml(.cs)` | 确定按钮启用；`BuildPlan()` / `ApplyDraft()` / `RunCli()`；不可用态禁用确定；标题 / 状态栏改为阶段 ③ 文案 |
| `App.xaml.cs` | 无窗口 CLI 分流（`--dump-only` / `--plan-only` / `--apply-only`） |
| `Models\SettingsDraft.cs` | 下拉值表由 `private` 改 `internal`，供 `WritePlanBuilder` 复用同一份映射，避免反推与写盘漂移 |
| `MpvSettings.csproj` | `Version` → `0.3.0-stage3` |

#### 实现要点（对齐第 4 节写盘契约）

1. **diff 基准是「打开时的草稿」而不是「打开时的文件」**：`DraftBaseline.Capture()` 在反推完成后立即记下 34 个可编辑属性；点确定只把变化的属性翻译成键。没改过的键一律不进计划，因此 `setting.exe` 刚写的键不会被旧值回冲。
2. **写前重读**：`ConfWriter` 执行时对每个目标文件重新 `ConfFile.Load()`，以磁盘最新文本为基底逐行替换。验证 4 在进程启动后外部改写 `tone-mapping` 并新增未知键，写盘后两者都保留 —— 证明不是整文件回写旧快照。
3. **逐行替换**：注释行、空行、未知键、原有键顺序、CRLF、无 BOM 全部原样保留；键缺失追加到文件末尾（末尾空行之前）；**同名键的多行全部同步替换**（`MpvReadKey` 读第一行、mpv 取最后一行，留一半旧值会让两个程序反推不一致）；行内 `# ...` 备注跟随键一起保留（注意 mpv 把行内 `#` 当值的一部分，这种值会被反推成「自定义」并受下条保护）。
4. **联动键成组**：HDR 模式 4 键、色调档位 4 键各作为一个 `Group`，要么全写要么全不写；同一文件的所有改动一次性构造文本、一次性原子替换，天然满足「组不半写」。
5. **自定义态绝不覆盖**：打开时或当前草稿为 `HwdecCustom` / `HdrModeCustom` / `ToneCustom` / `VideoLevelsCustom` / `AudioChannelsCustom` / `ShotFormatCustom` 时，跳过该键并给警告。「全部重置」会清掉 Custom 标志，所以这道校验必须在写盘前再做一次，不能只靠控件置灰。
6. **校验拦截**：下拉索引越界、自填截图路径非绝对或为空、目标 conf 不存在、配置不可用 → `Blockers`，**一个字节都不写**，主窗口保持打开可修正后重试。
7. **事务回滚**：所有文件的新文本先在内存构造完，再依次原子替换；任一文件失败（含验证注入 `--apply-fail-after=1`）按逆序用原字节恢复，清理全部 `.tmp`。文本与磁盘完全一致时连临时文件都不建，做到「零改动零写盘」。
8. **截图路径**：`screenshot-dir` 走 `Environment.GetFolderPath`（兼容 OneDrive 重定向桌面），统一正斜杠 + 结尾斜杠；相对路径直接拦截。

#### 交互

- **确定**：构造计划 → 校验 → 原子写盘 → 结果对话框（写入明细 / 成组标记 / 前后值 / 跳过 / 延后 / 阻止）→ 成功才关闭主窗口；校验被拦或写盘失败时窗口保留，改完可重试。
- **取消**：直接关闭，什么都不写。
- 结果对话框首屏固定显示「后续阶段未写入」提示条，底部注明「多数 mpv 选项需重启播放器后生效；本窗口不会关闭播放器」。

#### 验证用命令行开关（不是 UI 功能）

| 开关 | 作用 |
|---|---|
| `--config-dir=<dir>` | 显式指定 portable_config，所有写盘验证都在副本目录里跑 |
| `--apply-patch=<json>` | 反射覆盖草稿属性，模拟「用户在界面上改了这些控件」 |
| `--patch-ui=<json>` | 同上，但窗口正常显示，用于真实窗口截屏断言 |
| `--plan-only --dump-plan=<json>` | 只导出写盘计划，不写盘 |
| `--apply-only --dump-result=<json>` | 无窗口执行写盘并导出结果 |
| `--apply-delay-ms=<n>` | 写盘前延迟，供外部脚本在启动后改盘以验证「写前重读」 |
| `--apply-fail-after=<n>` | 成功写完第 N 个文件后注入失败，验证事务回滚 |
| `--dump-only --dump-draft=<json>` | 阶段 ② 遗留的只读导出，保持原行为 |

#### 验证结果

- `dotnet build -c Release` **0 警告 / 0 错误**；`dotnet publish -r win-x64 -p:PublishSingleFile=true --self-contained false -c Release` 出 `publish\MpvSettings.exe` **278,756 字节**（阶段 ② 为 255,716）。
- `_ref\stage3_verify.py`：**10 组 / 70+ 条断言全部通过**，且用 `bin\Release` 与 `publish` 两个 EXE 各跑一遍。覆盖：diff 最小性（只改 1 行）、注释 / 空行 / 未知键 / 顺序 / CRLF / 无 BOM、HDR 四键 + 档位四键成组、两文件事务、非预设值（含重置清标志）不覆盖、写前重读、失败注入回滚后哈希完全一致且无残留 tmp、缺键追加、重复键同步、幂等零写盘、非法路径拦截、后续阶段键只提示、**生产配置目录 58 个文件哈希前后完全一致**。
- `_ref\stage3_ui_verify.py`：真实窗口三组截屏 —— ① 12 键 / 2 文件写入后的结果对话框（含成组标记、追加提示、3 类延后提示条）；② 无改动点确定显示「无需写入」；③ 相对路径被拦显示「校验未通过」且主窗口保持打开。截屏前缀 `MPV设置EXE_阶段3_*`。写盘后实读 `deinterlace=no` / `contrast=39` / `mode=tonemap` / `screenshot-dir=<图片目录>/` 全部命中。

#### 已知边界

- `KeysWritten` 只统计文本真正发生变化的文件；计划里有键但磁盘值已一致时，返回「零改动零写盘」。
- 结果对话框的明细区自带滚动（最多约 8 行首屏可见），其余内容由外层滚动查看；延后项已在上部提示条重复一次。
- 行内 `#` 在 mpv 里算值不算注释，带这种写法的值会显示为「自定义」并被保护，不会被 EXE 规范化；这是刻意行为。
- 阶段 ⑧ 要验证的「两窗口交替写不同键互不覆盖」尚未做，但第 2 条（写前重读）+ 第 1 条（只写 diff）已在单进程层面实证。

---

### 6.7 阶段 ④ 交付（2026-11-13 22:3x，已完成）

**范围**：音效链路双写 —— `mpv.conf` 的 `af` + `HKCU\Software\BassMax` 六键。**首次写注册表**。`cover-art.conf` / `include` 行（⑤）、`input.conf`（⑥）、`MyMPC.conf` 与 Lua（⑦）仍未动。

#### 生效时机（用户 22:24 确认，务必不要写错）

| 改动 | 生效 | 依据 |
|---|---|---|
| `Bass` / `Treble` / `Drive` / `Preset` / `Enabled` | **实时，≤200 ms** | `af_bassmax.c` 在音频回调里每 200 ms 调 `bm_registry()` 重新取参数；托盘也是 200 ms 轮询 |
| `af` 挂载开关（`af=bassmax` ↔ `af=`） | **重启 mpv** | `af` 是 mpv.conf 启动选项 |
| 其余所有键 | **重启 mpv** | mpv 只在启动时读配置 |
| `TrayIcon` | 立即，仅影响托盘图标 | 与声音无关 |

**结论**：注册表六键里除 `TrayIcon` 外都是实时的；`af` 反而不实时。页 C 已加「生效时机」只读行，结果窗口底部也写明。

#### `af` 是滤镜链，不是布尔开关

- 开：链上已有 `bassmax`（含 `@label:bassmax`）就不动；否则追加到链尾。
- 关：只摘掉 `bassmax` 那一节；链被删空时写**空值行 `af=`**（与 `setting.ahk` 的 `Button1` 一致，不删行）。
- 切分按逗号，但忽略 `[]` 内的逗号（`lavfi=[volume=1.5]` 不会被拆坏）。
- 基于**写前重读**到的最新链条合并（`KeyChange.Transform`），因此别的程序同时加的滤镜不会被整行覆盖。
- 检测到 `audio-spdif` 非空时**不写 `af`**，只给提示「比特流直通与 BassMax 互斥」。

#### 注册表写入契约

| 项 | 规则 |
|---|---|
| 键名 | `Bass` / `Treble` / `Drive` / `Preset` / `Enabled` / `TrayIcon` |
| 格式 | 照抄 `bassmax_settings.cpp` 的 `SaveTo`：`Bass`/`Treble` `%.1f`、`Drive` `%.2f`、其余 `%d` |
| 类型 | 缺键补 **REG_SZ**；原来是 REG_DWORD 的整数继续写 DWORD；DWORD 遇上 0.1 步长的小数时改回 REG_SZ（DWORD 会截断掉 0.5） |
| diff | 按**数值**比较 —— `"2"` 与 `"2.0"` 是同一个调音，不重写 |
| 补齐 | 六键缺任意一个就按草稿值补，明细标注「（补齐缺失键）」。DShow 的 `ReadAll` 缺一项会整批忽略，mpv 的 `bm_registry` 是逐键宽容，两种都照顾到 |
| `TrayIcon` | 没有对应控件，**只在缺失时补 1**，不改用户设的值 |
| 事务 | 注册表**先写**（可逐项还原），文件后写；文件失败则逆序还原文件 + 还原注册表。值相同连 `SetValue` 都不调 |

#### 实现要点

- `Services\BassMaxStore.cs`（新）：键路径、官方格式、数值比较、`ReadRaw`（DWORD→十进制）、`RegistryUndo`（记原值 / 原类型，逆序还原）、`RegistryWriter.TryApply`。
- `Services\AfChain.cs`（新）：括号感知的链切分 + 增删 `bassmax`。
- `Models\ConfigSnapshot.cs`：`BassMaxSnapshot` 改用 `BassMaxStore`，修掉「REG_DWORD 被 `as string` 读成 null 从而误报缺项」的潜在 bug，并记录原类型。
- `Models\WritePlan.cs`：新增 `RegChange` / `RegistryPlan` / `KeyChange.Transform`（`[JsonIgnore]`）/ `KeyChange.DisplayTo`（空值行显示 `<空值行>`）；`WritePlan.Registry`、`RegistryCount`、摘要拼成「N 个文件 / M 个键 · 注册表 X 项」；`WriteResult.RegistryWritten`。
- `Services\WritePlanBuilder.cs`：`af` 与六键进计划，音效从「延后项」移除；`audio-spdif` 互斥改为警告跳过。
- `Services\ConfWriter.cs`：注册表先行 + 失败还原；逐键跳过「磁盘上已经是目标值」的项（含 `af` 合并后无变化），`KeysWritten` 更准。
- `Services\ConfigLocator.cs`：`BassMaxPanel` 解析 `Codecs\bassmax_dshow.ax`（生产 `<root>\Codecs`，开发包 `<root>\00.Core`），找不到就禁用逃生口按钮。
- `Views\AudioView.xaml(.cs)`：启用「打开 BassMax 原厂面板」（`rundll32 <ax>,BassMaxConfig`），更新四行说明，新增「生效时机」只读行，注册表状态改为「缺项…（确定时自动补齐）」。
- 版本 `0.4.0-stage4`；`AboutView` / 主窗口页脚 `阶段 ④`。

#### 验证开关（新增）

| 开关 | 用途 |
|---|---|
| 环境变量 `MPVSETTINGS_BASSMAX_KEY` | 把注册表键路径改到测试键，**自动化验证绝不碰真实调音值**；默认 `Software\BassMax` |

#### 验证结果

- `dotnet build -c Release` **0 警告 / 0 错误**；publish 单文件 EXE **288,484 字节**（阶段 ③ 为 278,756）。
- `_ref\stage4_verify.py`：**13 组 / 60+ 条断言全部通过**，`bin\Release` 与 `publish` 两个 EXE 各跑一遍。覆盖：`af` 关闭写空值行且不删行、开启、链保序（`bassmax,scaletempo2` → `scaletempo2`）、链尾追加、`lavfi=[...]` 方括号不拆、值已一致零写盘、`audio-spdif` 互斥、注册表只写改过的项、官方格式（`4.5` / `0.30` / `-3.4` / `0`）、`"2"`≈`"2.0"` 不重写、缺 `TrayIcon` 补齐、缺多键按出厂默认补齐、DWORD 整数保留 / 小数转 SZ、文件失败注入后**注册表一并回滚**、幂等零写盘、多源同批摘要、**生产配置目录哈希不变 + 生产 `HKCU\Software\BassMax` 六键值不变**。
- `_ref\stage4_ui_verify.py`：真实窗口三组 —— ① 音频页（+4.5 / −2.0 / 0.65 / 古典乐）点确定后结果窗显示 5 项注册表 + 1 键 `af`，实读 `Bass=4.5` / `Enabled=0` / `af=` 命中；② 缺 `TrayIcon` 时页面显示「缺项」且点确定后补齐、六键齐全；③ 生产注册表六键前后一致。截屏前缀 `MPV设置EXE_阶段4_*`。
- `_ref\stage3_verify.py` 已同步：注册表指到测试键；第 9 组断言改为「配置文件无键可写 + 注册表改 2 项 + 两条延后提示（封面 / 阶段⑦）」，全部通过。
- 生产配置只读跑 `--plan-only`：`Files: []`、`Registry: 0`、`Blockers: []` —— 打开即零改动，不会误写。

#### 已知边界

- 主窗口打开期间若外部改了注册表，反推值不会自动刷新（与配置文件同策略：以打开时快照为准，写盘时按 diff 只覆盖用户改过的项）。
- 结果窗口只在写入成功时自动关闭；注册表写失败会保留窗口，且文件一个字节都没动。

#### 6.7.1 补丁：「应用」按钮（2026-11-13 22:45）

底部按钮区改为 **`应用` · `确定` · `取消`**（`应用` 为 `GhostButton`，`确定` 为主色）。

| 按钮 | 成功时 | 被拦 / 失败时 |
|---|---|---|
| 应用 | 写入后**保持窗口打开**，结果窗右下按钮显示「继续编辑」，底部提示「已应用，设置窗口保持打开…」 | 都保持窗口打开，底部提示「本次未写入任何内容…」，按钮显示「返回修改」 |
| 确定 | 写入后关闭窗口（原行为不变） | 保持窗口打开，可修正后重试 |
| 取消 | 直接关闭，不写任何文件（原行为不变） | — |

**应用后基线刷新（关键）**：成功后重新 `new ConfigSnapshot(_locator)` 读盘 → `SettingsDraft.RefreshReadOnly()` 只刷新只读状态文本（配置源、快照时间、`audio-spdif` 互斥、注册表齐全性）→ `_baseline = DraftBaseline.Capture(_draft)`。**不调 `LoadFrom`**，界面上的编辑值一律不动。因此同一批改动不会被第二次提交：再点应用会走「无需写入」，不会重复写盘或反复弹注册表警告。

实现：`MainWindow.WriteAndExit(bool closeOnSuccess)` 统一两条路径；`_snapshot` / `_baseline` 去掉 `readonly`；`ApplyResultWindow(result, plan, closeOnSuccess)` 多一个模式参数控制底部文案与按钮；配置不可用时 `ApplyBtn` 与 `OkBtn` 一起禁用。版本 `0.4.1-stage4`，publish 单文件 **288,996 字节**。

验证 `_ref\stage4_apply_ui_verify.py`（build 版与 publish 版各一遍，**ALL PASS**）：

- A 点应用 → 结果窗出现、主窗口**未退出**、`deinterlace=no` / `save-position-on-quit=yes` / `af=` 已落盘、注册表 `Bass=4.5`；
- B 再点应用 → 标题变「无需写入」，主窗口仍在；另起进程 `--plan-only` 复核 `Files + Registry = 0`，证明基线确实推进了；
- C 非法截图路径 + 点应用 → 主窗保持打开，配置文件 mtime 全部不变；
- D 点确定 → 结果窗关闭后主窗口也随之关闭。

#### 6.7.2 外观收尾：去掉顶部系统细条 + 滚动条主题化（2026-11-13 22:55）

**顶部那条浅灰细条不是布局元素**，是 `WindowStyle="None"` + `ResizeMode="CanResize"` 时 WPF 保留的非客户区：DWM 把它画成约 7 px 高、颜色 `#4A5459` 的条（截图逐行采样确认 y=0..9 全是这个色，而标题栏是 `#16192B`）。内容和样式都盖不住它。

修法：两个窗口都挂 `WindowChrome`：

```xml
<shell:WindowChrome CaptionHeight="0" ResizeBorderThickness="6"
                    GlassFrameThickness="0" CornerRadius="0" UseAeroCaptionButtons="False" />
```

`GlassFrameThickness=0` 让 DWM 不再画边框；`ResizeBorderThickness` 继续提供四边 / 四角拉伸命中区；`CaptionHeight=0` 保证标题栏仍由自绘的 `DragMove` 拖动（不引入系统标题栏行为）。

**连带必须处理的坑**：`ResizeBorderThickness=6` 会吃掉窗口最右侧 6 逻辑像素的鼠标命中（`WM_NCHITTEST` 返回 `HTRIGHT`）。滚动条原本贴着右边缘，10 px 宽里绝大部分落在这条拉伸带内 —— **表现为滚动条点不动、拖不动**（自动化验证里按下 Thumb 颜色不变、拖动后画面零差异，才定位到这一点）。修法：`PageScroll` 加 `Margin="0,0,8,0"`，让滚动条整体退到拉伸带以外。`ApplyResultWindow` 的 `ResizeBorderThickness=0`，无需处理。

**滚动条**（`Themes\Theme.xaml`，隐式样式，全局生效）：

| 项 | 值 |
|---|---|
| 宽度 | 10 px（水平方向高 10），无上下箭头按钮 |
| 轨道 | 透明，露出内容区底色 |
| 滑块 | 圆角 3、`ScrollThumbBrush #435A40`，悬停 `#5E7F57`，拖动时换强调色 `AccentBrush` |
| 翻页 | 两端 RepeatButton 透明模板，绑 `PageUp/PageDown`、`PageLeft/PageRight`，保留点击翻页与滚轮 |

验证数据（build 与 publish 版一致）：

- 窗口顶部 `y=0..9` 采样由 `(74,84,89)` 变为 `(22,25,43)`，主窗口与结果窗口都干净；
- 客户区 `1500×1020 @ 150% DPI` = 正好 `1000×680` 逻辑，比改前多回被边框吃掉的 16×8 px；
- `WM_NCHITTEST` 实测：左边 10 / 右边 11 / 底边 15 / 右下角 17 / 标题栏与内容区 1（HTCLIENT，靠 `DragMove`）；
- 滚动条悬停色 `(91,99,144)`、按下 `(181,154,255)`，拖动后内容确实滚动（截图差异区域非空）；
- `stage3_ui_verify.py` / `stage4_ui_verify.py` / `stage4_apply_ui_verify.py` 全部无 FAIL；`stage3_verify.py` / `stage4_verify.py` 断言全过。

版本 `0.4.2-stage4`，publish 单文件 **291,556 字节**。

**回归脚本隔离补丁（本次踩到）**：`stage3_ui_verify.py` 是阶段 ③ 写的，当时音效还不写盘，所以它直接打真实注册表。阶段 ④ 之后它的 A 组 `Bass=3.5` 会真的落到 `HKCU\Software\BassMax` —— 本次回归确实把用户 `Bass` 从 `2.0` 改成了 `3.5`，已手工改回 `2.0`（其余五键未动，类型仍是 REG_SZ）。脚本已补 `MPVSETTINGS_BASSMAX_KEY=Software\BassMax_MpvSettingsStage3UiTest`、`prepare()` 建六键、结束时删测试键；复跑后生产六键前后一致、测试键无残留。

**教训**：每次给 EXE 增加一类新的写入目标（文件 / 注册表 / 新 conf），都要把**所有历史 UI 脚本**过一遍，确认它们的测试数据落在测试目标上，而不只是改当期那个脚本。



---

### 6.8 默认配色改为自有墨绿系（2026-11-13 23:0x）

用户自己调了一版绿色，指出「避免一看就是照抄的 FrankyRenderer」，要求把默认配色换成这一版。做法不是凭眼估色，而是用 Pillow 对参考图（`1500×1020`，与 150% DPI 下的客户区物理像素 1:1）逐像素采样 + 垂直/水平扫描定位卡片与分隔线，再按采样值落到画刷：

- 主色完全对齐：`#131F11`（页面）/ `#1B2918`（侧栏、标题栏、底栏）/ `#243723`（卡片）/ `#2E462E`（卡片头）/ `#435A40`（边框、分隔线、滚动条滑块）/ `#1B2C19`（输入框、徽标底）/ `#A3F6BE`（强调色）/ `#37543E`（侧栏选中）/ `#4B6444`（开关关闭态轨道）。
- 文字由紫白改为纯白 + 浅绿灰：`#FFFFFF` / `#D3E6D3` / `#AFC4B3`（采样自参考图说明文字与状态栏文字）。
- 派生色由主色推出：`AccentHoverBrush #CCFFDF`（薄荷提亮）、`ScrollThumbHoverBrush #5E7F57`。

**顺带做的一次收口**：原来散落在 `MainWindow.xaml`（关闭按钮悬停 `#3A2A3A` / `#FF9A9A`、遮罩 `#E6101321`）、`Theme.xaml` 滚动条（`#394060` / `#5B6390`）、`ApplyResultWindow.xaml`（提示条 `#2A2538` / `#6B5A86` / `#E7C98A`、小标题 `#B59AFF`、阻止写入 `#FF9A9A`）里的**字面颜色全部抽成语义画刷**：`WarnBrush / WarnBgBrush / WarnBorderBrush`（琥珀提示）、`DangerBrush / DangerBgBrush`（红色阻止写入 + 关闭按钮悬停）、`ScrollThumbBrush / ScrollThumbHoverBrush`、`OverlayBrush`。现在全工程 XAML 只剩 `Theme.xaml` 顶部 20 行有十六进制，改主题只动这一处。

验证：`bin\Release` 真实窗口逐页截屏（播放 / 画面 / 音频 / 截图，含结果窗），与参考图同坐标像素比对，`#131F11` / `#2E462E` / `#243723` / `#A3F6BE` / `#4B6444` 五处完全一致；无残留紫色元素。回归用 `--config-dir=<临时副本>` + `MPVSETTINGS_BASSMAX_KEY=Software\BassMaxColorTest` 跑一次「应用」，结果窗配色统一，**生产配置目录哈希 `2048da41…` 与生产 `HKCU\Software\BassMax` 六键（Bass 2.0 / Treble 2.0 / Drive 1.00 / Preset 4 / Enabled 1 / TrayIcon 1）前后完全一致**，测试键与临时副本已删除。build 0 警告 0 错误，版本 `0.4.3-stage4`，publish 单文件 **292,068 字节**。仅改 4 个 XAML 文件，`.cs` 与逻辑零改动。

### 6.9 配色收口补丁：快捷键页状态色 + 全工程去字面色（2026-11-13 23:2x）

用户换完主题后截图指出「快捷键颜色没改」——右侧键位徽标里的「自定义 / 默认 / 未绑定」仍是旧的紫 `#B59AFF` 与旧浅灰 `#959EBE`。

**为什么上一轮漏了**：这三个画刷不在 XAML 里，而在 `Models\ShortcutData.cs` 顶部：`Color.FromRgb(0x95,0x9E,0xBE)`（默认）、`Color.FromRgb(0xB5,0x9A,0xFF)`（自定义，即旧强调色）、`Color.FromRgb(0x6B,0x73,0x91)`（未绑定）。上一轮的检查手段是 `grep '#[0-9A-Fa-f]{6}'`，**`FromRgb` 里没有 `#`，所以扫不到**。

**教训**：查「有没有漏改的颜色」不能只扫 XAML 十六进制，必须同时扫 `FromRgb` / `FromArgb` / `new SolidColorBrush` / `Brushes.`：

```
grep -rn "FromRgb\|FromArgb\|Brushes\.\|new SolidColorBrush" --include=*.cs .
```

**修法**（把状态色从数据层搬到视图层，颜色回到主题）：

- `Models\ShortcutData.cs`：删掉 `StateBrush` 属性与三个画刷，`ShortcutRow` 只留文本字段（`Action` / `Command` / `KeyText` / `State`）。
- `Views\ShortcutsView.xaml`：新增局部样式 `ShortcutStateText`，用 `DataTrigger` 按 `State` 取色 —— 默认 `WeakTextBrush #AFC4B3`、`自定义` `AccentBrush #A3F6BE`、`未绑定` 新增的 `MutedTextBrush #7F9285`（比 WeakText 再暗一档，表达「有字但不重要」）。键位文字与右侧状态标签共用这一个样式。
- `Themes\Theme.xaml`：新增 `MutedTextBrush`。

**验证**：真实窗口截屏按像素采样 —— 自定义行键位 `#A3F6BE`（薄荷）、状态标签 `#8FD8A5`（薄荷 × 0.85 不透明度）、未绑定行 `#7F9285`，全页无紫系像素（唯一命中的蓝色是 `MPV 设置` 标题文字的 ClearType 次像素边缘，不是设计色）。

**顺带回答「琥珀提示条从哪点出来」**（配色后于 6.10 改为同系浅绿）：它只出现在写入结果窗顶部，触发条件是本次改动落在**后续阶段**的键上，此时 `WritePlan.Deferred` 非空：

| 改动位置 | 提示文案 |
|---|---|
| 播放页的无封面信息窗 / Alt+4 屏幕比例 / 允许多运行 | 无封面信息窗 / Alt+4 屏幕比例 / 单实例 → 阶段 ⑦ |

（「纯音频封面」那一条已于阶段 ⑤ 移除，现在会真正写盘，见 6.11。）

只改当前阶段的键（如反交错、音效）不会出现这条。它和「阻止写入」是两种状态：后者是校验失败（`Blockers` 非空，红色）整批不写；提示条只是告知「这些键本次没写」，其余键照常写入。实测截屏见 `MpvSettings\预览_写入结果_未写入提示条.png`。

版本 `0.4.4-stage4`，publish 单文件 **292,068 字节**（只改 XAML + 一个 Model 字段，体积未变）。build 0 警告 0 错误。

### 6.10 提示条改为同系浅绿（2026-11-13 23:3x，`0.4.5-stage4`）

用户觉得 `#2B2A18` 的琥珀提示条在墨绿界面里「不搭」，自己调了一版绿色并直接给了效果图，要求按图改。

**取色**（Pillow 逐像素，同 6.8 的做法）：参考图 873×98 是提示条那块区域的物理像素裁剪，外圈 `#131F11` 恰好等于窗口底 `AppBrush`，说明它就是叠在页面底上的。

| 位置 | 取到 | 用在哪 |
|---|---|---|
| 外圈 | `#131F11` | 窗口底，非提示条自身 |
| 边框（1 px） | `#468921` | `WarnBorderBrush` |
| 底色 | `#1A350E` | `WarnBgBrush` |
| 标题文字 | `#C2FF72` | `WarnBrush` |
| 正文文字 | `#C9F0E6` | 新增 `WarnTextBrush` |

**一处不是照搬**：原正文用的是 `SubTextBrush #D3E6D3`（灰绿），压在 `#1A350E` 上发闷、对比只有 8:1 左右却看着糊；按图换成 `#C9F0E6`（偏青白），并单独提成 `WarnTextBrush`，只在提示条正文用。结果窗其它区域仍是 `SubTextBrush`，不影响。

**为什么标题用黄绿而不是薄荷**：提示是「需注意」而非「已生效」，黄绿 `#C2FF72` 与强调薄荷 `#A3F6BE` 拉开色相，扫一眼能区分「这是提示」和「这是选中」。红色 `DangerBrush #FF9A9A` 仍专用于「阻止写入」——那是校验失败、整批不写，语义完全不同，不跟着改绿。

**顺带同步**：结果窗下方「跳过 / 提示」小标题、以及「本阶段未写入（按排期后续接入）」也走 `WarnBrush`，一并变绿，不再是紫/琥珀混着的状态。

**验证**：临时配置副本 + `Software\\BassMaxTipTest` 测试键，走「播放页滚到底 → 打开始终运行多实例 → 应用」复现提示条，`PrintWindow` 抓窗后按像素比对——底色 `#1A350E`、边框 `#468921`、标题 `#C2FF72`、正文 `#C9F0E6`，**与参考图四种颜色完全一致**。生产配置哈希 `2048da41…` 与 `HKCU\\Software\\BassMax` 六键前后不变，测试键与副本已删。build 0 警告 0 错误，publish 单文件 **292,068 字节**（只改颜色，体积未变）。

### 6.11 阶段 ⑤：纯音频封面写入（`0.5.0-stage5`）

把页 A「纯音频封面」从「只提示未写入」改成真正写盘。这是 V1 里唯一一处**新建文件 + 整文件重写**，写盘契约的三条原则在这里都必须破例，所以单独记清楚。

**新增的两个写盘能力**

| 能力 | 位置 | 为什么必须 |
|---|---|---|
| `FilePlan.NewContent` / `IsWholeFile` / `IsCreate` | `Models\WritePlan.cs` | `cover-art.conf` 是带 `[cover-art]` 段的条件 profile，逐键替换没有意义；而且首次启用时文件不存在，阶段 ③ 的执行器遇到不存在的文件直接抛异常 |
| `KeyChange.AppendOnly` | 同上 + `ConfWriter.ReplaceKey` | `include=` 是**可重复键**。`RemoveKey` 式的逐行替换会把用户自己的 `include=~~/MyMPC.conf` 一起改写成 `include=~~/cover-art.conf` —— 这是会毁配置的坑 |
| 整文件新建的回滚 | `ConfWriter` 回滚段 | 原文件不存在时 `Original` 为 null，回滚必须**删掉**新文件，而不是写回零字节 |

**触发条件（关键设计）**

```csharp
bool coverChanged = baseline.Changed(d, "CoverOriginal", "CoverMax");
bool coverMissing = !s.CoverArt.Exists || !m.HasInclude("cover-art.conf");
bool otherChanges = plan.KeyCount > 0 || plan.Registry.HasChanges;
if (coverChanged || (coverMissing && otherChanges)) { ...写 cover-art.conf + 补 include... }
```

1.2 节原话是「EXE 每次打开检测该行缺失即补回」。真按「打开就写」做，会打破「零改动零写盘」——用户只是打开看一眼，磁盘就被改了。所以折中成：**缺就补，但要等到本次确实有别的改动要落盘时才补**。代价是「只打开不改」时封面仍未接入，所以卡片上新增第四行「配置接入状态」把这件事显式说出来，而不是让用户猜。

`otherChanges` 必须放在所有其它键算完之后判断，所以这一节在 `Build()` 里排在页 D 之后、`Deferred` 之前。

**写出的内容**（与 1.2 完全一致，6 行，CRLF，无 BOM，沿用已有文件的换行风格）

```
# 由 MPV 设置 EXE 管理：纯音频封面显示，勿手工改动段落名
[cover-art]
profile-cond=p["current-tracks/video/image"] == true
profile-restore=copy
video-unscaled=downscale-big     # 关闭「原始大小」时写 no
autofit-larger=1200x1200         # = UI 的上限值，x 前后同一个数
```

`CoverMax` 越出 400–1600 直接 `Blockers`（不悄悄钳）；不在 100 的整数倍上（只有 CLI 能造成）吸附并给 Warning。

**执行顺序**：`ConfWriter` 按 `IsWholeFile` 排序，整文件写的排在前面 —— 先把被 include 的文件建好，再往 `mpv.conf` 加 include 行，任何中间状态都不会指向不存在的文件。

**结果窗文案**（用户截图确认）

```
已写入 2 个文件（1 个整文件重写 · 1 个键）
  cover-art.conf · 新建文件（6 行 · 封面条件 profile）
  mpv.conf · 追加 include=~~/cover-art.conf
```

`include` 只有一行，不是联动组，所以去掉了「成组」标记；它也是天然可缺的键（0 行合法），所以不再进「跳过 / 提示」区 —— 否则用户会以为出了问题。

**验证**

脚本 `_ref\stage5_verify.py`，10 组约 50 个断言全过：新建/整文件重写/不重复追加 include/**已有其它 include 不被冲掉**/零改动不建文件/只改别的键顺带补/连改两次不写盘/上限越界阻断/回滚删除新文件/生产未触碰。生产配置目录 80 个文件哈希与 `HKCU\Software\BassMax` 前后不变。

真机用程序生成的配置跑 `mpv.com --config-dir=... --vo=null`：

| 样本 | `current-tracks/video/image` | `video-unscaled` | `autofit-larger` |
|---|---|---|---|
| `audio.m4a`（内嵌封面） | yes | `downscale-big` | `1000x1000` |
| `nocover.m4a`（靠同目录 cover.png） | yes | `downscale-big` | `1000x1000` |
| `out.mkv`（正常 h264 视频） | no | `no` | 空（全局默认未被污染） |
| 同会话 音频 → 视频 | yes → no | `downscale-big` → `no`（打印 `Restoring profile: cover-art`） | 同上 |

**一个踩过的坑**：`_ref\subtest\clean\video.mkv` 这个样本的视频轨**本身带 image 标记**，第一次拿它当「正常视频」测，得到 `unscaled=downscale-big`，差点误判成配置出错。正确样本是 `out.mkv` / `forced.mkv`（`img=no`）。以后验「视频不受影响」必须同时打印 `current-tracks/video/image`，只看文件名会翻车。

另外 `mpv.conf` 里的 `loop-file=inf` 会让 `--frames=1` 卡在循环里，命令行验证要显式加 `--loop-file=no --length=0.4`。

### 6.12 阶段 ⑥：input.conf 行级改键（`0.6.1-stage6`）

把页 E 从「静态清单展示」改成真正写盘，唯一目标文件是 `input.conf`。这个文件不是「一堆键值对」，而是「一行一条绑定」，所以它的写盘语义不是「改值」而是**写不写这一行**——三态、冲突、删行都由此派生，单独记清楚。

**四条硬约束（1.3 节查证，全部落在 `ShortcutKeys`）**

| 约束 | 表现 | 不遵守的后果 |
|---|---|---|
| 语法是 `KEY command`，**没有 `=`** | `SPACE cycle pause` | 写成 `SPACE=cycle pause` 后整行被当成未知键名忽略，绑定静默失效 |
| UTF-8 无 BOM + **LF** | 生产 10 行实测全 LF | 而 `mpv.conf` 是 CRLF。**两个文件换行风格不同**，写盘时各按各的，不能共用一套 |
| `#` 起即注释 → `#` 键必须写 `SHARP` | `SHARP cycle audio` | 写 `# cycle audio` 会变成注释，音轨切换键凭空消失 |
| 键名大小写敏感 | `s` 截图含字幕 / `S` 截图不含字幕 | 归一化时折叠大小写会把两个不同动作并成一个 |

**三态语义（本阶段最容易搞混的一处）**

| 盘上状态 | UI 显示 | 用户操作后 | 写盘动作 |
|---|---|---|---|
| 不写这一行 | 默认 `SPACE` 等 | 改成别的键 | 追加一行 |
| 不写这一行 | 默认 | 改回默认键 | **不写**（保持不写，不追加同值行） |
| 有一行 | 该键 | 改成别的键 | 原地替换该行键名 |
| 有一行 | 该键 | 点 ✕ 清空 | **删行**（不是写空值） |
| 不写这一行 | 未绑定 | 设一个键 | 追加一行 |

关键点：**清空 = 删行 = 恢复默认**，不是「禁用」。mpv 没有「禁用某条绑定」的写法（内置弱绑定删不掉），所以 UI 上 ✕ 的语义只能如实是「回到默认」。`SetKey` 里那段特判就是干这个的：

```csharp
if (!Present && HasDefault &&
    string.Equals(ShortcutKeys.Canonical(k), ShortcutKeys.Canonical(DefaultKey), StringComparison.Ordinal))
{ Key = k; Present = false; return; }   // 改回默认键 = 不写这一行
```

**命令归一化**

`NormCommand`：首词小写 + 纯数字 token 统一成最短写法（`1.0`→`1`、`0.50`→`0.5`）+ 空白折叠成一个空格。`Canonical`：修饰键按固定顺序排序（`Ctrl+Alt+s` ≡ `Alt+Ctrl+s`）、别名统一（`control`/`strg`→`Ctrl`、`super`/`win`→`Meta`）、`#`→`SHARP`。

不是洁癖——生产 `input.conf` 里就是 `Alt+1 set window-scale 0.5`，而内置默认是 `Alt+1 set window-scale 1.0`。用户如果把默认 `1.0` 敲回去，不归一化就会判成「不同命令」再追加一行，同一键出现两条强绑定，mpv 按先后取一条，用户看到的生效结果和 UI 显示对不上。

**定位规则（`Services\InputConfWriter.Build`）**

1. `TryParseLine` 只接受能明确解析的行；`#` / `;` / `[` 开头、含 `;` 的多命令串、含 `-` 的键序列（`a-b`）**一律不猜**，原样保留。
2. 按归一化命令串匹配。命中多行 → 全部改掉并合并成一行（历史重复行顺手收敛）。
3. 无命中且需写入 → 末尾追加；文件不存在则新建，并补尾换行。
4. 沿用原文件换行风格（新建时用 LF）；新旧文本相同连临时文件都不建。
5. 别人的绑定、注释、`[section]` 逐字不动——这条由第 5 组验证专门盯着。

**冲突校验分级**

| 情况 | 级别 | 结果 |
|---|---|---|
| 两行都写，且键名相同（强 × 强） | `Blockers` | 阻断整批，一条都不写 |
| 自定义键撞上内置默认键，没被本包覆盖（强 × 弱） | `Warnings` | 允许保存，结果窗列出「原默认 X 已被占用」 |
| 命令数值写法不同（`0.5` vs `0.50`） | 不算冲突 | 归一化后同一行 |

**关键设计：只有本次真动过冲突中的某一行才阻断。** 生产环境里 `Alt+1` / `Alt+2` 本来就被本包重排过（`Alt+1`=50% 而非默认 100%），属于「历史遗留同键」。若按「扫到同键就拦」实现，用户改任何无关的键都会被这两行拖住、写入永远失败。所以校验拿的是**本次 diff 涉及的行**，没动过的冲突只在结果窗提一句，不阻断。

**其他绑定：只读卡片（写盘契约第 9 条的后半句）**

第 9 条写的是「识别不了就放进『其他绑定』只读区展示，绝不擅自改写或删除」。第一版只做了后半句（原样保留），没做展示——用户看得见的只有 38 行，盘上多出来的绑定成了黑盒。补一张卡片收口：

- 收集规则：`TryParseLine` 能解析、且命令不在 38 行清单里的行。注释 / `[section]` / 键序列 / 分号多命令**连收集都不做**，它们本来就逐字保留。
- 无编辑入口，只有「键名 + 命令」两列；卡片标题带行数（`其他绑定（只读 · N 行）`），没有这类行时整张卡片不显示。
- 位置在 E5 之后、页面最底部，不影响上面 38 行的坐标与滚动位置。

**新增 / 修改文件**

| 文件 | 作用 |
|---|---|
| `Models\ShortcutCatalog.cs`（新，替代已删的 `ShortcutData.cs`） | 38 行固定清单 5 组（E1 9 / E2 7 / E3 8 / E4 10 / E5 4），每行 `Id`/`Action`/`Command`/`DefaultKey`/`ShipLine`/`Note` + `ShortcutKeys` 归一化 |
| `Models\ShortcutDraft.cs`（新） | `ShortcutRow`（`KeyText` / `CanClear` / `ConflictText` / `HintText` / `Snapshot`）+ `ShortcutDraft`（`LoadFrom` / `ApplyPatch` / `RefreshChecks` / `Others` 只读收集） |
| `Services\InputConfWriter.cs`（新） | 纯函数 `Build(path, edits, diskBytes)`，只算字节不碰盘 |
| `Services\ConfWriter.cs`（改） | `FilePlan.InputEdits` 分支：写前重读、原子替换、进同一个事务（失败逆序回滚） |
| `Models\WritePlan.cs`（改） | `InputEdit{Command, Action, OldKey, NewKey, IsDelete}`、`InputEditCount` 计入 `HasChanges`、`DraftPatch.Shortcuts` 支持 CLI 改键 |
| `Services\WritePlanBuilder.cs`（改） | 页 E 生成段按 `baseline.Shortcut(id)` 与当前 `Present/Key` 做 diff + 冲突 / 覆盖分级 |
| `Controls\HotkeyNames.cs`（新） | `Key` + `ModifierKeys` → mpv 键名（`Shift+S`→`S`、`Space`→`SPACE`、`Escape`→`ESC`、`OemTilde`→`` ` ``） |
| `Views\ShortcutsView.xaml(.cs)`（重写） | 键位框 + ✕ + 三态配色；`PreviewKeyDown` 全程 `e.Handled = true`；末尾「其他绑定」只读卡片 |

**WPF 键捕获的一个必须动作**：`PreviewKeyDown` 里无条件 `e.Handled = true`。不吞键的话空格会触发 Button 点击（键位框旁边就是「确定」）、`Alt+F4` 会直接关窗。`Key.System` 要取 `e.SystemKey` 才拿得到 Alt 组合的主键，Esc 单独留给「取消录入」。

**验证**

`_ref\stage6_verify.py`，13 组 57 条断言，`bin\Release` 与 `publish` 两个 EXE 各跑一遍全过：反推三态（E1/E2/E3 默认、E4 10 行自定义、E5 未绑定）、默认态改键追加、自定义态原地替换、清空删行、未知行逐字保留、`SHARP`↔`#`、`0.50` 认同一行、零改动不写盘、强×强阻断、强覆盖弱警告、事务回滚字节级还原、文件不存在时新建（无 BOM + LF）、生产 81 文件哈希与 `HKCU\Software\BassMax` 六键不变。

`_ref\stage6_ui_verify.py` 真实窗口五组：A 点键位框录 `Ctrl+P` → 确定 → 落盘；B 点 ✕ → 确定 → 该行被删；C 两行动作同键 → 拦截窗；D 滚到 E5 看「未绑定」态；E 盘上多两行别人的绑定 → 只读卡片列出、点确定后文件字节不变。8 张预览图存 `MpvSettings\预览_阶段6_*.png`。

build **0 警告 0 错误**；publish 单文件 **320,740 字节**（`0.5.0-stage5` 的 296,164 涨 24,576）。

**四个踩过的坑**

1. UI 脚本 B 组「清空不生效」：不是逻辑错，是坐标错。「自定义」行比「默认」行多一个 ✕，整组右对齐导致键位框左移 28 px，点 843 落到 ✕ 外面。坐标改为 `KEYBOX_X=800, CLEAR_X=920` 后通过。**改行内元素数量就要重算整组坐标，别复用旧值。**
2. A 组突然全挂、截屏全是资源管理器：`SetForegroundWindow` 被前台锁吞掉，窗口在后台收不到点击（诊断输出显示 t=3.0 时前台变成「微信」）。加 `focus()`：`ShowWindow(SW_RESTORE)` + 点标题栏 + `GetForegroundWindow()` 校验，最多 6 次。
3. 顺带发现滚轮方向写反（`wheel(1)` 是向上滚，D 组停在 E1 没到 E5），改 `wheel(-1)`（`mouse_event(0x0800, …, -120, …)`）。另外 `focus()` 校验失败时 `hotkey()` 直接抛异常拒绝发键——否则 `Ctrl+P` 会打进别的程序。
4. 加了「其他绑定」卡片后，页面变高，原来 30 次滚轮到底的 D 组只滚到 E4，截屏里根本看不到 E5。改成 80 次并留余量。**页面高度变了，所有「滚到底」类断言都要重新确认落点。**

### 6.13 阶段 ⑦：无封面信息窗 / Alt+4 屏幕比例 / 单实例（`0.7.0-stage7`）

把页 A 剩下三张卡片（无封面音频 / Alt+4 屏幕比例 / 播放行为）接到盘上，并**同时修掉「单实例转发后窗口回 960x540」这个从 2026-11-13 挂到现在的未结项**。本阶段的特殊之处：它是第一个既写文件又改用户旧脚本的批次，所以「谁写谁」的边界要写清楚。

#### 五个落点（EXE 独占，全部整文件生成）

| 文件 | 大小 / 行数 / 换行 | 内容 | 写入条件 |
|---|---|---|---|
| `audio-info.conf` | 222 B · 7 行 · CRLF | `[no-art]` 条件 profile：`force-window=yes` / `geometry=640x180` / `osd-scale-by-window=no` / `profile-restore=copy` | 开关打开时建；关掉时**保留文件**，只摘 include 行 |
| `script-opts\MyMPC.conf` | 137 B · 4 行 · CRLF | `no_art_ui` / `single_instance`（UI 是「允许多开」，脚本值相反）/ `pipe_name=MyMPC_mpv` | 有改动或接线缺件时 |
| `script-opts\fit90.conf` | 163 B · 3 行 · CRLF | `ratio=<50..100>`（**无 `auto`**：只有 `Alt+4` 用得到它） | 同上 |
| `scripts\MyMPC_ui.lua` | 16,292 B · 383 行 · **LF** | 嵌入资源 `Assets\MyMPC_ui.lua` 原样落地（信息窗 overlay + 单实例转发 + 转发后抢前台） | 同上；内容与源文件相同则不重写 |
| `mpv.conf` 两行 | +2 行（712 B） | `include=~~/audio-info.conf`、`input-ipc-server=MyMPC_mpv` | include 按开关增删；`input-ipc-server` **只在缺失时追加，永不删** |

`fit90.lua` 是**唯一被破例修改的既有脚本**（用户 2026-11-14 明确授权「`MyMPC\MediaPlayer\mpv` 下所有文件都还不是最终版，以本设置文件为准」）。其余 `hdr-autoswitch.lua` / `stats.lua` / `tone-ab.lua` / `menu.conf` 一个字节没动。

#### 比例外置：`fit90.lua` 从「写死 0.9」改成「读 fit90.conf」

原来 `scale = math.min(dw * 0.9 / vw, dh * 0.9 / vh)`，UI 上的滑块纯属摆设。现在：

```lua
local o = { ratio = 90 }             -- 只有比例，没有 auto
options.read_options(o, "fit90")     -- 读 script-opts/fit90.conf
...
local scale = math.min(dw * ratio / vw, dh * ratio / vh)   -- ratio 来自文件
mp.set_property_number("window-scale", scale)
...
mp.register_script_message("mpv-fit90", fit)   -- 只有 Alt+4 会调到这里
```

**2026-11-14 22:2x 修正**：上一版加的 `auto` / `file-loaded` 自动应用已整段删除，`fit90.conf` 里的 `auto` 键同步取消。原因是它违反了「双击打开视频默认按视频分辨率 100%」——实测同一台机器、同一个 `out.mkv`（320x240）：旧版打开即被拉到 **1296 高**（屏高 90%），新版默认 **160 高**（视频 100%，mpv 原生），按一次 `Alt+4` 才变 1296。老的 `auto=yes` 会在下次保存时被整文件重写清掉（`stage7_verify.py` 2.13 有断言）。

两种不生效的情况，都是「算了也没用」：

| 跳过条件 | 判据 | 为什么 |
|---|---|---|
| 纯音频 / 无画面 | `dwidth` 或 `dheight` ≤ 0 | `window-scale` 是「视频分辨率的倍率」，没有画面就没有倍率可乘；信息窗（`geometry=640x180`）按了只提示「只对视频生效」，不会被撑开 |
| 封面图 | `current-tracks/video/image == true` | 封面走 `cover-art.conf` 的 `video-unscaled=downscale-big`（**只缩不放**），窗口撑大后封面纹丝不动、只会多出一圈黑边。实测 800x600 的封面按 `Alt+4` 后窗口从 800x600 变 1296x1296，封面还是 800x600——用户当场指出「封面并没有变大」，**这才叫无意义** |

> ⚠ **2026-11-14 22:4x 第二次修正**：22:3x 那版把封面从跳过名单里拿掉了，理由是「用户主动按键就该生效」。错在只量了**窗口尺寸**（1296）就宣布通过，没看**画面内容**——封面根本没放大。`D` 组断言因此从「窗口高 ≈ 屏高 90%」改成「按 `Alt+4` 前后窗口尺寸完全相等」，`P5.2` 同。**量窗口不等于量效果：涉及缩放，必须同时看画面里那东西有没有变。**

#### 老 bug 结案：两条事实叠在一起

| # | 事实 | 怎么发现的 |
|---|---|---|
| 1 | **纯音频在 mpv 原生行为下根本不创建窗口** | IPC 连得上、`EnumWindows` 找不到任何窗口、`osd-dimensions` 是 0x0。不是「黑窗」，是没窗。profile 必须写 `force-window=yes` |
| 2 | **`profile-cond` 里带 `audio-params` 就是抖动源** | `loadfile replace` 期间 `audio-params` 短暂变 `nil` → 条件 true→false → `Restoring profile` 把 `force-window` / `geometry` 一起撤销 → 窗口销毁重建 → 960x540 |

修法：条件只写 `p["current-tracks/video"] == nil`，profile 内补 `force-window=yes`。真机数据（150% DPI，故 640x180 逻辑 = 426x120 物理）：

| 场景 | 结果 |
|---|---|
| 双击无封面音频 | 426x120，`geometry=640x180`，`osd-dimensions.w=640` |
| 再双击一个文件（单实例转发） | 第二实例 0.15s 自退；**窗口仍 426x120，`geometry` 未被 restore** |
| 同窗口切视频（**不按任何键**） | `geometry` 清空，窗口 **160 高**（视频 320x240 的 100%，mpv 原生）、仍 4:3 |
| 同窗口切视频 + 按一次 `Alt+4` | 窗口 **1296 高**（= 屏高 90%）、仍 4:3 |
| 再切回音频 | 回到 426x120（双向都正确） |
| 有封面音频 | 窗口 **800x600**（封面自身尺寸）；按 `Alt+4` **尺寸完全不变**，只提示「不适用于封面」 |

#### 单实例

`mpv.conf` 固定 `input-ipc-server=MyMPC_mpv`（不随开关增删，否则关开关时管道消失、转发无从发起）。`MyMPC_ui.lua` 里 `ffi.CreateFileA("\\.\pipe\MyMPC_mpv")` → `GetNamedPipeServerProcessId` 与 `mp.get_property_number("pid")` 比对：相同 = 自己是宿主，正常播放；不同 = 发消息后自退。`playlist-count < 1`（直接双击 `mplayerl.exe` 不带文件）跳过转发。**不需要文件锁、不改启动链。**

#### 转发后窗口必须回到前台（2026-11-14 22:5x 用户报的第二个 bug）

**现象**：单实例第二次双击媒体文件，窗口尺寸正确了，但**焦点仍在后台**（第一次双击正常在前台）。

**根因是 Windows 的前台锁定（foreground lock），不是 mpv 的 bug**。`SetForegroundWindow` 只允许「当前就是前台」或「由前台进程拉起」的进程调用，否则被静默拒绝（任务栏图标闪烁）。链路里两次都撞上它：第二实例在后台起窗、自己被拒；第一实例只是被动换了个文件，mpv 不会主动抢焦点，也不会替脚本抢。

**修法三件事，缺一不可**：

| # | 谁做 | 做什么 | 为什么 |
|---|---|---|---|
| 1 | 第二实例（转发前） | `AllowSetForegroundWindow(第一实例 pid)` | 把「可以设置前台窗口」的权限**转交**给宿主的进程。第二实例是双击文件时由 explorer 拉起的，带着前台权限；不转交，宿主怎么调都白调 |
| 2 | 第二实例（转发方式） | 改发 `{"command":["script-message","MyMPC-ui-forward","<path>"]}`，不再直接发 `loadfile` | 直接发 `loadfile` 时对方无从判断「这是别人转发来的，要抢前台」还是「我自己的播放列表在走」 |
| 3 | 第一实例（收到消息） | `window-minimized=false` → `loadfile replace` → 三级降级抢焦点 | 最小化时必须先恢复；`loadfile` 后窗口句柄可能变，所以 `file-loaded` 里再用新句柄补一次 |

抢焦点三级降级（`focus_window()`，返回 `direct` / `attach` / `alt` / `false`，日志能看到命中哪一级）：

```
① SetForegroundWindow          → 权限已转交，正常情况这一级就成（实测三次全是 direct）
② AttachThreadInput + 再试一次 → 借当前前台窗口的输入队列，跳过检查
③ keybd_event(Alt) 按下抬起    → 让系统认为有用户输入，解除前台锁定（最后手段，有副作用，别当前两级的替代）
```

窗口句柄直接取 `mp.get_property_native("window-id")`——**在 Windows 上它就是 HWND**（实测 `0x160628` = 1443368，与 `EnumWindows` 找到的一致），不必自己枚举窗口。取到后 `IsIconic` 判最小化 → `ShowWindow(SW_RESTORE)`，再 `SetWindowPos(TOPMOST/NOTOPMOST)` + `BringWindowToTop` 把 Z 序顶到最前。

真机验证（`_ref\stage7_focus_probe.py`，5 组 16 条）：

| 场景 | 结果 |
|---|---|
| 第一实例点窗口 → 前台 | 通过 |
| 点记事本抢走焦点（mpv 在后台） | 通过 |
| 后台状态双击视频 → 转发 | 第二实例退出、标题变 `out.mkv`、**第一实例回到前台**、未最小化 |
| 再一次（后台 → 转发音频） | 标题变 `plainsound.flac`、**再次回到前台** |
| 第一实例最小化时转发 | **自动恢复 + 回到前台 + 文件已换** |

mpv 日志：`received forward -> ...` 紧跟 `focus(after-loadfile) -> direct`，三次全部 `direct`，没有用到 `attach` / `alt`。**注意 `--log-file` 必须写成 `--log-file=xxx`**，mpv 只认等号形式，空格分隔会 `Fatal error` 直接退出（这个坑让探针第一版「窗口没起来」）。

#### 写盘规则（复用 ③ 的事务与「写前重读」）

- **触发条件**：`uiChanged`（三个控件任一改动）**或** `wiringMissing && 本次确有其它改动`。与封面的 `coverChanged || (coverMissing && otherChanges)` 同一套思路——不允许「打开看一眼就写盘」。
- **整文件「内容相同不重写」**：`Same()` 先做换行与尾空行归一化再比字符串，外部编辑器另存成 CRLF 不会触发无意义重写。
- **include 增删**：开启且缺 → `Append + AppendOnly`（只加自己这行）；关闭且已有 → `MatchValue="~~/audio-info.conf" + Remove=true`，只删值精确匹配的那一行，`cover-art` 与用户自己的 include 不受影响。
- **比例校验**：`50–100` 之外 `Blockers`；非整数 `Warnings` 提示取整。
- **事务**：四个文件 + 两行键在同一批里，任一失败整体逆序回滚，**新建的文件回滚时删除**（沿用阶段 ⑤ 的语义）。
- **只读状态行**：`NoArtConfStatus`（已接入 / 缺 include / 缺文件 / 尚未创建）与 `ScriptConfStatus`（缺哪些：`MyMPC_ui.lua` / `MyMPC.conf` / `fit90.conf` / `input-ipc-server`）在 `RefreshReadOnly` 里算，让「为什么没生效」在界面上可读。

#### 生产部署：文件是 EXE 自己写的，不是我抄的

用户已授权直接落生产，但为了证明「EXE 写出来的就是跑得起来的那份」，走 EXE 的 CLI 写盘而不是手工复制：

```
MpvSettings.exe --config-dir=<生产> --apply-only --apply-patch={FitRatio:92}
  → 已写入 5 个文件（4 个整文件重写 · 2 个键）
MpvSettings.exe --config-dir=<生产> --apply-only --apply-patch={FitRatio:90}
  → 已写入 1 个文件（script-opts\fit90.conf · 整文件重写）
```

第一趟用 92 触发完整接线（四个文件 + 两条键），第二趟把比例改回出厂 90，顺带验证「只改比例只动一个文件」。落盘字节核对：`audio-info.conf` 7 个 CRLF / 0 个裸 LF / 无 BOM；两个 conf 同；`MyMPC_ui.lua` 0 个 CRLF（纯 LF）、282 行（当时版本；现为 16,292 B / 383 行）。改前整目录备份在 `mpv\portable_config.bak_stage7_20261114_220403\`（9 个文件）。

**2026-11-14 22:3x 语义修正后的第二次部署**（同一通道，91 → 90）：只重写 `script-opts\fit90.conf`，明细写「整文件重写（3 行 · Alt+4 屏幕比例）」，落盘 163 B / 3 行，`auto=` 已不存在；`fit90.lua` 不由 EXE 生成（它是随安装包分发的脚本），直接覆盖成 2,245 B / 61 行 LF 版，并同步到 `12.mpv\portable_config\scripts\fit90.lua`。

#### 验证

| 脚本 | 覆盖 | 结果 |
|---|---|---|
| `_ref\stage7_verify.py` | 12 组 **57 条**断言：反推出厂默认 / 首次四文件接线 / 只改比例只动一个文件 / 关开关摘 include 保文件 / 零改动零写盘 / 二次保存幂等 / 别人的 include 不被冲 / 比例越界阻断 / 事务回滚（新建文件被删）/ 编码（conf CRLF、lua LF、无 BOM）/ lua 与源文件行数一致 / **老的 `auto=yes` 被清掉** / 生产目录哈希与注册表六键不变 | 全过 |
| `_ref\stage7_mpv_probe.py` | 隔离配置 6 组 **19 条**：信息窗尺寸 / `loadfile` 不回退 / **切视频默认 160 高（视频 100%）+ 按 `Alt+4` 变 1296** / **封面按 `Alt+4` 前后尺寸完全相等（800x600）** / 单实例转发 / 多开 | 全过 |
| `_ref\stage7_prod_probe.py` | **生产配置**（不加 `--config-dir`）9 组 **28 条**：落点齐全 + `fit90.conf` 无 `auto` / 信息窗 / 转发不回退 / **切视频默认不动、`Alt+4` 才 90%** / **封面按 `Alt+4` 尺寸不变** / 日志无错 / P7「把 `fit90.conf` 改成 70 → 按 `Alt+4` 窗口 1008 高（屏高 70%）」证明比例真的从文件读 | 全过 |
| `_ref\stage7_ui_verify.py` | 真实窗口 7 组 **29 条**：三张卡片渲染 + 状态行文案 / 只改比例只写一个文件（且 `auto` 不回来）/ 开「允许多开」写 `single_instance=no` / 关信息窗摘 include 保文件 / 空配置首次补齐（先断言起点确实缺件）/ 重开看状态行 / 零改动点确定零写盘 | 全过 |
| `_ref\stage7_focus_probe.py` | **生产配置** 5 组 **16 条**：前台基线 / 记事本抢走焦点 / 后台双击视频转发后回前台 / 再一次（音频）/ **最小化时转发自动恢复**；并校验 `focus -> direct` 未降级到 `Alt` 模拟 | 全过 |

`bin\Release` 与 `publish` 两个 EXE 各跑一遍。截图存 `MpvSettings\`：`预览_阶段7生产_无封面信息窗.png`（时间码 00:02 / `plainsound.flac` / `flac 44100 Hz 1 ch` / 紫色柱形 / 进度条）、`预览_阶段7生产_Alt4屏幕90.png`、`预览_阶段7生产_有封面音频.png`、`预览_阶段7生产_比例70.png`，以及 UI 侧 `预览_阶段7_改比例.png`（新卡片全文）、`预览_阶段7_播放页_*.png`、`预览_阶段7_空配置状态.png`、`预览_阶段7_写入结果_首次补全.png`、`预览_阶段7_写入后状态*.png`。旧的 `预览_阶段7生产_切视频自动适配90.png` 是修正前的产物，保留作对照。

build **0 警告 0 错误**；publish 单文件 **339,172 字节**（`0.6.1-stage6` 的 320,740 涨 18,432，来自内嵌的 11 KB Lua）。

#### 新增 / 修改文件

| 文件 | 作用 |
|---|---|
| `Assets\MyMPC_ui.lua`（新，383 行 / 16,292 B） | 一份脚本干三件事：`ass-events` overlay 信息窗 + 单实例转发 + 转发后把窗口抢回前台（`AllowSetForegroundWindow` 转交 + 三级降级 `SetForegroundWindow`）。以 `EmbeddedResource` 嵌进 EXE（`LogicalName` 写死），`csproj` 加 4 行 |
| `Services\WritePlanBuilder.cs`（改） | 阶段 ⑦ 生成段（三开关 diff + 接线缺件兜底）+ `BuildAudioInfo/BuildMyMpcConf/BuildFit90/LuaSource` + `Same/Norm` + 局部 `AddWhole` |
| `Models\WritePlan.cs`（改） | `KeyChange.Remove` / `MatchValue`（按 key+value 删行）、`FilePlan.WholeFileKind`（明细文案「无封面窗口 profile」等） |
| `Services\ConfWriter.cs`（改） | 删行分支 + `RemoveLines(key, matchValue)` 从后往前删 |
| `Services\ConfigLocator.cs`（改） | `Fit90Conf` / `MyMpcLua` 两个路径 |
| `Models\ConfigSnapshot.cs`（改） | `Fit90` 快照 + `LuaText` / `LuaExists` |
| `Models\SettingsDraft.cs`（改） | `NoArtUi` / `FitRatio` / `AllowMultiInstance` 反推（比例兼容老配置的 `fit_ratio`）+ 两条状态行 |
| `Views\PlaybackView.xaml` / `MainWindow` / `AboutView` | 三张卡片加状态行、页脚与标题栏改阶段 ⑦、关于页补五个新文件说明 |

#### 五个踩过的坑

1. **`subtest\clean\video.mkv` 的视频轨带 `image` 标记**（阶段 ⑤ 已踩过一次），被 `/image` 规则跳过适配，C 组一度以为适配失效。测试视频只能用 `subtest\out.mkv`。**同一个坑不能踩第三次：任何涉及视频轨的测试，先用 `ffprobe` 确认不是图。**
2. 纯音频窗口的 `osd-dimensions` 是 **0x0**，拿它量窗口永远量不到；改用 Win32 `GetClientRect`。
3. 截到桌面壁纸：进程非 DPI 感知，`GetWindowRect` 给虚拟坐标、`ImageGrab` 要物理像素；按 `GetDpiForWindow` 换算（本机 1.5）。
4. **`mpv.conf` 是 CRLF，用 `replace("include=...\n")` 摘行摘不掉**，导致「空配置首次补齐」用例在起点其实没缺件——断言全绿但全是假通过。给测试加了 `E0` 三条「起点确实缺」的前置断言才暴露。**做「修复前/后」对比时，先断言起点状态，否则测的是同一份文件两次。**
5. 生产部署后，`stage7_verify.py` 的 `fresh()` 夹具仍复制生产 `mpv.conf`，于是「首次接入」变成「已接入」的假通过（12.3 也跟着挂了）。改为默认按行滤掉 `include=~~/audio-info.conf` 与 `input-ipc-server`，需要已接线现场时传 `wired=True`。**生产状态一变，所有「复制生产文件当夹具」的测试都要复检前提。**

#### 第六个坑（语义层面，2026-11-14 22:2x）

6. **「我的原意是自动」不等于「每次打开文件自动应用」**。上一版据此写了 `auto=yes` + `file-loaded` 0.1s 应用，看起来「自动适配」很合理，实际把「双击视频默认视频分辨率 100%」这个前提推翻了——用户一句「只有 `Alt+4` 是针对屏幕大小的百分比，意思是按快捷键直接把窗口大小设成屏幕的百分比，不是默认播放视频的大小」才暴露。**涉及既有快捷键的语义，先问「按一下变成什么」和「不按是什么」，不要从形容词反推实现。** 这一条改了脚本、配置键、UI 文案与四套测试，成本远高于多问一句。

#### 后续

阶段 ⑧：`setting.exe` 的按钮集成（打开本 EXE、`Process Exist` 判定）——**已完成，见 6.14**；阶段 ⑨：默认模板与备份——`MpvRestoreDefaults:` 会连带冲掉 `input.conf` 改键、两条 `include=` 与 `input-ipc-server`，届时必须把 `script-opts\MyMPC.conf` / `fit90.conf` / `audio-info.conf` / `MyMPC_ui.lua` 一并纳入默认模板与备份清单。

安装包模板 `12.mpv\portable_config\` 本阶段只同步了 **`scripts\fit90.lua`**（它是被改动的既有脚本，留着旧版会让重装后的 mpv 无视 `fit90.conf`、退回写死 0.9）；`audio-info.conf` / `script-opts\MyMPC.conf` / `fit90.conf` / `MyMPC_ui.lua` 与 `mpv.conf` 的两行**不预置**，保持阶段 ⑤ 以来「默认模板统一归阶段 ⑨」的边界——缺件时 EXE 首次保存会自动补齐，且状态行会写清缺了什么。

---

### 6.14 阶段 ⑧b：改名为「MPV 简单设置」+ 三处 UI 修正 + 右键「选项」（`0.8.1-stage8`）

用户一次给出四件事，原话：「我原意是用 打开主播放器进行自定义设置 在选中 MPV 时打开设置程序 不过现在新增的按钮也保留 / 设置程序的 UI 出现了错误：重置页面的框不完整、最外框的四角是尖锐的直角 改成圆角 / MPV 的右键增加「选项」能打开设置程序 右键菜单最好能和设置程序匹配颜色就更好了 / 设置程序更名为 MPV 简单设置 现在并没有全部设置项 也没有高级页 所以改个名字就行了」。

#### 四件事的落点

| # | 需求 | 落点 | 不改的 |
|---|---|---|---|
| 1 | 主按钮在选中 MPV 时直接开设置程序 | `setting.ahk` 的 `mplayerl:` 分支判 `InStr(selectedPath, "mpv")` → `MpvOpenSettingsProgram()` | MPV 专属页原有的「打开 MPV 简单设置」按钮保留，两处共用同一个函数 |
| 2 | 「重置页面」右边框缺失 + 窗口直角 | `MainWindow.xaml` 工具行两列化 + 页脚两列化；新增 `Services\WindowCorners.cs`（DWM 圆角） | 页面内容与配色一行未动 |
| 3 | 右键「选项」+ 配色匹配 | `menu.conf` 顶层加「选项(&O)」→ `scripts\mpv-settings.lua`；`load-context-menu=yes` + `script-opts\context_menu.conf` + `mpv.conf` 四条 `osd-*` | 菜单里既有条目的标题、顺序、快捷键提示全不动 |
| 4 | 改名为「MPV 简单设置」 | 标题栏、关于页、不可用遮罩、`setting.ahk` 按钮文案、EXE 的 `AssemblyVersion` | 文件名仍叫 `MpvSettings.exe`（改名会让已部署的 `setting.ahk` 找不到它） |

#### 改名只改显示名，不动文件名

设置程序实际只有 5 页常用项、没有高级页，叫「MPV 设置」名不副实。改的是给人看的地方：

- `Title="MPV 简单设置"`、标题栏文本同步；副标题「· 阶段 ⑧ 设置工具接入」→「· 常用项」。
- 关于页卡片标题、版本行 `MpvSettings 0.8.1 · 常用项版`，并补一段说明改名理由（「常用项版」而不是「精简版」，因为后续会补页，只是没有高级页）。
- `ApplyResultWindow` 与 `MainWindow` 的不可用遮罩（「MPV 简单设置不可用」）。
- `setting.ahk` 的按钮文字「打开 MPV 设置程序」→「打开 MPV 简单设置」，副文本改「常用项」。
- `csproj` 的 `<Version>` 保持 `0.8.1-stage8`，**文件名 `MpvSettings.exe` 不动**——`setting.ahk` 有 4 个候选路径硬指这个名字，改名就要连带改安装包脚本与 `12.mpv` 模板，收益为零。

#### 三处 UI 修正

**① 「重置页面」右边框缺失。** 顶部工具行原本是三列 `Auto / * / Auto`：右栏的按钮组因为里面有文字按钮，最小宽度比中栏剩余宽度还大，Grid 被撑出容器，最右那个按钮只丢掉右边**一条**边框（其它三条都在，所以看着像描边画歪了）。改成两列 `* / Auto`，左栏吃剩余宽度，配置路径文字加 `MaxWidth="240" TextTrimming="CharacterEllipsis"`；搜索框 170 → 150 让出宽度。渲染图实测右边距 37px = 24（页面 padding）+ 约 6（拉伸带）+ 13 余量，四边完整。

**② 页脚文字被「确定」按钮压住。** 同格叠了两个 `StackPanel`：左边状态文字换行后向右溢出，直接钻到按钮底下。改成两列 `* / Auto`，文案同时缩短到 2 行以内。

**③ 窗口四角是直角。** WPF 的 `WindowStyle=None` 窗口由我们自己画，WindowChrome 的 `CornerRadius` 只影响客户区内容、**不裁窗口外形**，所以一直是尖角。改用 DWM 原生圆角：

```csharp
// Services\WindowCorners.cs
private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
private const int DWMWCP_ROUND      = 2;   // ≈8px，主窗口
private const int DWMWCP_ROUNDSMALL = 3;   // ≈4px，结果对话框
DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
```

挂在 `Window_SourceInitialized`（那时 `hwnd` 才存在）。Win11 生效、Win10 静默失败（返回非 0，不抛异常，界面退回直角），**最大化时 DWM 自动取消圆角**，不需要我们管。实测 `corner_pref` 旧版 **0** → 新版 **2**（`stage8b_corner_probe.py` 直接读 DWM 属性，不靠肉眼）。

#### 右键「选项」：一条从 conf 到进程的链路

```
menu.conf 第 150 行  选项(&O)⇥script-message open-settings
        ↓  （context_menu.lua 点一下即执行，因为这条只有命令、没有状态列）
script-message open-settings
        ↓
scripts\mpv-settings.lua（2,178 B）
        ↓  expand-path ~~/ → 拼候选 → run
MpvSettings.exe --config-dir=<生产 portable_config>
```

`mpv-settings.lua` 三个要点：

1. **`run` 不展开 `~~/`**，路径解析必须在 Lua 里做：`mp.command_native({"expand-path", "~~/"})`，并补尾部 `/`。传出去的目录还要 `gsub("[/\\]+$", "")` 去掉结尾分隔符，否则 `GetFileName/GetParent` 在 C# 侧会别扭。
2. **EXE 候选三处**（`../MpvSettings.exe`、`MpvSettings.exe`、`../MpvSettings/MpvSettings.exe`），逐个 `utils.file_info` 试，命中就 `run`；找不到只 `show-text` 提示，不报错打断播放。
3. **菜单条目「只有命令、没有状态列」时点一下就执行**——`context_menu.lua` 走的是 `item.data.cmd` 分支直接 `mp.command` + `close()`，所以这一行不需要 `hidden=` / `disabled=`，也不会先弹二级菜单。

#### 为什么必须把右键菜单换成 OSD 版

mpv 有两条右键菜单路径：系统原生 Win32 弹出菜单（`context-menu` 命令 → `VOCTRL_SHOW_MENU`）与 Lua OSD 菜单（`context_menu.lua`）。前者是真正的 Win32 `TrackPopupMenu`，**配色由 Windows 主题决定，mpv 侧没有任何选项能改**——实测把 `osd-*` 改成墨绿系后原生菜单依旧是系统白/深灰。所以：

```
mpv.conf:  load-context-menu=yes     # 默认 no，即用原生菜单
```

切过去之后，配色分散在两个文件：

| 元素 | 来源 |
|---|---|
| 菜单底色 | **不在** `context_menu.conf` 里，取 `--osd-outline-color`（`border-style=background-box` 时取 `--osd-back-color`） |
| 文字色 | `--osd-color` |
| 当前项文字 / 底色 | `focused_color` / `focused_back_color` |
| 描边粗细 / 颜色 | `menu_outline_size` / `menu_outline_color` |
| 圆角 / 内边距 / 字号 | `corner_radius=8` / `padding_x=10` `padding_y=6` / `font_size=14` |

落在 `script-opts\context_menu.conf`（792 B）里的值全部对齐设置程序的主题（`#1B2918` 底 / `#435A40` 边 / `#A3F6BE` 薄荷高亮 / `#131F11` 高亮文字 / `#7F9285` 禁用），`background_alpha=0` 不透明。`mpv.conf` 追加 10 行（41–52 行）：

```
osd-color=#FFF0F7F0
osd-outline-color=#FF1B2918
osd-selected-color=#FFA3F6BE
osd-selected-outline-color=#FF131F11

# 右键菜单改用 mpv 自带的 OSD 菜单（Lua），不用系统原生菜单：
# 原生菜单是 Win32 弹出菜单，配色由 Windows 主题决定，mpv 改不了；
# OSD 菜单才能按设置程序配色走（见 script-opts/context_menu.conf）。
# 想换回原生菜单：把这里改成 no。
load-context-menu=yes
```

**代价要说清**：OSD 菜单是 mpv 自己画的，鼠标悬停高亮、二级菜单展开动画都比原生菜单「软」；原生菜单里带系统勾选标记的条目（如「始终置顶」）在 OSD 版里改用颜色区分。想要系统观感就把 `load-context-menu` 改回 `no`，代价是配色退回系统主题。

#### `setting.ahk` 的两处入口收敛成一个函数

原来 `MpvOpenSettings:` 标签里堆了「找 EXE / 判已运行 / 拉起」三段逻辑，主按钮那条路又要复制一遍。现在抽成顶层函数 `MpvOpenSettingsProgram()`（`setting.ahk` 第 936 行），两处都只调它：

- **MPV 专属页按钮** → `MpvOpenSettingsProgram()`（第 2222 行附近）
- **主按钮 · 选中 MPV** → `mplayerl:` 分支里 `if InStr(selectedPath, "mpv")` 命中就走同一个函数并 `return`，**不再 `Run, mplayerl.exe`**（这正是用户说的「原意」）

函数里三件事，行为与阶段 ⑧ 一致，只多了一条传参：

```ahk
if (WinExist("ahk_exe MpvSettings.exe")) { WinActivate ... return true }   ; 已在运行就切过去
mCfg := A_WorkingDir . "\MediaPlayer\mpv\portable_config"
if (FileExist(mCfg . "\mpv.conf"))
    Run, "%mExe%" --config-dir="%mCfg%"
else
    Run, "%mExe%"
```

`--config-dir` 是这轮补的：MPV 是选装组件，用户可能把 `MediaPlayer\mpv` 整体搬走，靠注册表 App Paths 找配置会落空；显式传目录后，设置程序读的就是「跟这个 mpv 同级的那份配置」。`MpvSettingsExe()` 的候选路径保持 4 个（`MediaPlayer\mpv\` / `MediaPlayer\mpv\MpvSettings\` / `00.Core\` / 安装根）。顺手把第 1436 行那段「设置程序自己会读 App Paths，所以不用传参数」的过期注释改掉了——**注释和实现对不上比没注释更坏**。

#### 验证

| 脚本 | 断言 | 覆盖 |
|---|---|---|
| `_ref\stage8b_open_settings.py` | **12 条全过** | `menu.conf` 里「选项」的命令串正确；`mpv-settings.lua` 能被加载；发 `script-message open-settings` 后真的拉起 `MpvSettings.exe`（pid 19056），mpv `-v` 日志里 `run` 的实参是 `--config-dir=<MPV配置目录>` |
| `_ref\stage8b_ahk_probe.py` | **7 条全过** | 新按钮「打开 MPV 简单设置」存在、旧名消失、主按钮文字未变；主播放器=MPV 时点主按钮 → 拉起 `MpvSettings.exe` 且**没有**拉起 `mplayerl.exe`；MPV 页按钮同样能拉起 |
| `_ref\stage8b_corner_probe.py` | 新旧对照 | `corner_pref` 旧 **0** → 新 **2**；结果对话框 `3` |
| 渲染图 | 3 张（工具行 / 页脚 / 整窗） | 重置页面按钮四边完整（右边距 37px）、页脚 2 行不被压、圆角可见 |
| 写盘回归 | 1 组 | 加了 4 条 `osd-*` + `load-context-menu` 后再改 `loop-file`，只动那一行（`inf → no`），`KeysWritten=0`、`FilesWritten=1`，新键一条未丢 |

`stage8b_ahk_probe.py` 用 `BM_CLICK` 点按钮、不依赖屏幕合成；`stage8b_open_settings.py` 断言「进程存在 + 日志里的命令行」，也不依赖截图。

#### 踩坑：远程 / 锁屏会话里抓不到 WPF 窗口内容

第一次想用 `ImageGrab.grab()` 截设置程序窗口对像素，结果整窗位置**只有桌面壁纸**——连记事本都截不到（`PrintWindow`、`BitBlt` 同样全黑）。原因是这类会话里没有活动的显示合成，窗口内容不进屏幕缓冲。三条尝试全废掉后改走 app 内渲染：

```
MpvSettings.exe --shot=out.png [--shot-page=audio]
```

`Loaded` 之后挂 400ms 的 `DispatcherTimer` 再 `RenderTargetBitmap.Render(this)` + `PngBitmapEncoder`。为什么不是 `Loaded` 当场渲染、也不是 `DispatcherPriority.ContextIdle`：两种都只拿到纯白/半成品，400ms 才等到首帧画完。`--shot=` 是**验证用钩子，不参与任何业务逻辑**，随 EXE 一起发出去（多 20 行代码，无副作用）；真嫌碍事可以靠条件编译去掉，V1 先留着。

顺带两个坑：① 旧版窗口标题是「MPV 设置」（没有「简单」），`FindWindowW` 按新标题找旧版永远找不到——做新旧对照时要按各自的标题各探一次；② `wmic` 在本机被安全策略拦，进程断言改用 `tasklist /FI "IMAGENAME eq MpvSettings.exe" /NH`（Git Bash 下要写 `//FI` 否则被当成路径）。

#### 新增 / 修改文件

| 文件 | 说明 |
|---|---|
| `MpvSettings\Services\WindowCorners.cs`（新） | DWM 圆角，`small` 参数给结果对话框用 |
| `MpvSettings\MainWindow.xaml` / `.cs` | 标题栏改名、工具行两列、页脚两列、`PageMeta` 去「配置：」前缀、`Window_SourceInitialized` 挂圆角、`--shot=` 钩子 |
| `MpvSettings\ApplyResultWindow.xaml` / `.cs` | 同样挂圆角（`small: true`） |
| `MpvSettings\Views\AboutView.xaml` | 改名说明、版本 `0.8.1 · 常用项版`、「V1 范围」改成三个入口、「后续阶段」改 ⑧ 当前 + ⑨ 默认模板 |
| `MpvSettings\MpvSettings.csproj` | `<Version>0.8.1-stage8</Version>` |
| `portable_config\scripts\mpv-settings.lua`（新，2,178 B） | `expand-path` + 三候选 + `run ... --config-dir` |
| `portable_config\script-opts\context_menu.conf`（新，792 B） | OSD 菜单外观 |
| `portable_config\menu.conf` | 第 150 行插「选项(&O)」+ 制表符 + `script-message open-settings` |
| `portable_config\mpv.conf` | 第 41–52 行：4 条 `osd-*` + 注释 + `load-context-menu=yes` |
| `00.Core\setting.ahk` | 按钮改名、`MpvOpenSettingsProgram()` 抽取、`mplayerl:` 分支、过期注释更正 |
| `12.mpv\`（安装包模板） | 上述 4 个 mpv 侧文件 + `MpvSettings.exe` 全部同步 |

部署：`MyMPC\MediaPlayer\mpv\MpvSettings.exe`（350,948 B）、`12.mpv\MpvSettings.exe`（同）、`00.Core\setting.exe` 与 `MyMPC\setting.exe`（各 1,427,456 B）。备份：`portable_config.bak_stage8b_20261115_000233`、`MpvSettings.exe.bak_20261115_000233`、`setting.exe.bak_20261115_0011`（两处）。

publish 从 348,388 → **350,948 字节**（+2,560，全部是圆角 / 截图钩子 / 改名文案）。

`setting.ahk` 的编译命令（**`/base` 不是 `/bin`；`/icon` 不能漏**）：

```sh
MSYS2_ARG_CONV_EXCL='*' MSYS_NO_PATHCONV=1 \
"<AutoHotkey目录>/Compiler/Ahk2Exe.exe" \
  /in  "<项目根>/00.Core/setting.ahk" \
  /out "<项目根>/00.Core/setting.exe" \
  /base "<AutoHotkey目录>\v1.1.37.02\AutoHotkeyU64.exe" \
  /icon "<项目根>/00.Core/ico.ico" /silent
```

`MSYS2_ARG_CONV_EXCL='*' MSYS_NO_PATHCONV=1` 必须写，否则 Git Bash 把 `/in` `/out` `/base` 当路径改写，编译器收不到参数。**漏 `/icon` 会少 30,208 字节**（1,397,248 vs 1,427,456），图标退回 AutoHotkey 默认——体积一变就说明漏了。版本信息不从命令行传，靠脚本头 5 行 `;@Ahk2Exe-Set*` 指令。可复现性实测：同一份脚本连编两次，md5 完全一致（`c42ce461…`），所以**改注释不会改二进制**，容器变更只改注释时不必重新部署。

#### 6.14.1 补测（2026-11-15 00:2x）：右键菜单能不能脱离窗口尺寸

用户贴了两张图问「MPV 的右键菜单和窗口大小强绑定吗，有没有像其他播放器那样固定大小」。查源码 + 实测三条结论：

**① 尺寸可以解绑，位置不行。** OSD 菜单的坐标系就是窗口本身（`context_menu.lua` 的 `get_scaled_osd_dimensions()` 直接取 `osd-dimensions`，`height = min(内容高度, osd_h)`），所以它**永远画在视频表面内**，窗口多矮菜单最多多高，超出的项靠 `▴/▾` 翻页。字号则由 `osd-scale-by-window` 决定：`yes`（默认）按 `窗口高/720` 线性缩放，`no` 固定像素。`audio-info.conf` 的 `[no-art]` 里本来就写了 `osd-scale-by-window=no`（否则 180px 高的窗口里 OSD 会小到 3px），代价是菜单在这个矮窗口里相对偏大、被裁掉几项。

**② 想「固定大小 + 能伸到窗口外」只有一条路：原生菜单。** 实测（`_ref\stage8c_menu_native.py`，644×180 的播放器窗口）：

| 组 | 条件 | 结果 |
|---|---|---|
| A | `load-context-menu=no` → `context-menu` | 弹出真正的 Win32 窗口（类名 `#32768`），**223×452**，高度是播放器窗口的 2.5 倍；无父窗口 → 完全独立 |
| B | `load-context-menu=yes` → `script-message-to context_menu open` | **没有任何 Win32 弹窗** → 确认是画在视频表面上的 |
| C | `load-context-menu=yes` → 仍发 `context-menu` 命令 | 一样弹原生窗口（`#32768` 223×452）→ **两条路可以共存** |

源码依据：`cmd_context_menu()` 无条件 `vo_control(VOCTRL_SHOW_MENU)` → `mp_win32_menu_show()` → `TrackPopupMenuEx()`；而 Lua 菜单是 `select.lua` 的 `context-menu` 键绑定在 `use_context_menu_lua()` 为真时改走 `script-message-to context_menu open`。两条路互不影响。

**③ 于是有三种做法**（都能落，取舍在观感）：

| 方案 | 菜单尺寸 | 能否超出窗口 | 配色 |
|---|---|---|---|
| 现状：全程 OSD 菜单 | 随窗口缩放（可改固定） | 不能，矮窗口下翻页 | 跟设置程序一致（墨绿） |
| 全程原生菜单（`load-context-menu=no`） | 系统固定 | **能** | 跟 Windows 主题，改不了 |
| 混合：窗口够高用 OSD、太矮切原生 | 各自最优 | 矮窗口时能 | 矮窗口时是系统色 |

混合在技术上没有障碍（C 组已证明 `context-menu` 命令在 Lua 菜单加载时仍走原生），只需要一个几十行的脚本覆写 `context-menu` 键绑定，按 `menu.conf` 的条目数 × 行高与窗口高度比一下。**本轮没做**——它会在音频信息窗里换成系统配色，是观感取舍，等用户点头再动。

#### 后续

右栏「重置页面」现在是**只重置当前页草稿**（不写盘），这点在关于页写明了；阶段 ⑨ 的 `MpvRestoreDefaults:` 是另一件事——它会整文件覆盖 `mpv_default.conf` / `input_default.conf` / `menu_default.conf` / `hdr-autoswitch_default.conf`，**连带冲掉本轮加的 4 条 `osd-*`、`load-context-menu`、两条 `include=`、`input-ipc-server`、`input.conf` 改键**。所以阶段 ⑨ 必须把 `script-opts\context_menu.conf`、`scripts\mpv-settings.lua`、`audio-info.conf`、`MyMPC.conf`、`fit90.conf`、`MyMPC_ui.lua` 一并纳入模板与备份清单，且四个 `*_default.conf` 目前在生产与 `12.mpv` 模板里**都不存在**，要一并生成。**（2026-11-15 00:4x 已落地：不是生成四个模板，而是把默认值搬进 EXE、由 `--default` 直接写——见 6.15。）**

---

### 6.15 阶段 ⑨a：命令行 `--default` 与「默认值到底住在哪里」（`0.9.0-stage9a`）

用户原话：「给 MPV 简单设置程序预留一个命令行参数 -default，可以通过命令行方式直接恢复默认值，也就是说 EXE 内部要有默认值，最好能定下现在的所有能设置的选项需要哪些文件，能合并的就合并，setting.ahk 和安装程序直接引用命令行参数设置默认值」。

#### 为什么必须住在 EXE 里

阶段 ⑧ 之前，「恢复默认」靠 `setting.ahk` 的 `MpvRestoreDefaults:` 从四个模板整文件覆盖：

```
mpv_default.conf / input_default.conf / menu_default.conf / script-opts\hdr-autoswitch_default.conf
```

**这四个文件从来就没被生成过**（阶段 ⑨ 一直排在做完 EXE 之后），所以那条逻辑实际上一直是空转：`FileCopy` 找不到源文件，只弹一句「模板缺失」。更要命的是它只认这四个模板——本轮新增的 `context_menu.conf`、`mpv-settings.lua`、4 条 `osd-*`、`load-context-menu` 全在模板之外，就算模板存在也恢复不了。

把默认值放回 EXE 有三个直接好处：① 默认值只有一份，和 UI 控件、反推规则同源；② 安装包不必再带一份配置；③ 配置被改坏时有一条确定的归位路径。

#### 实现：不抄第二份键表

`--default` 没有另写一套「默认键表」，而是复用普通保存的那条 diff 链路：

```
目标态  new SettingsDraft() + ResetAll()      ← 字段初值就是出厂值，页 E 的 10 行发行态由 ResetToShipped 给出
基准态  DraftBaseline.Force(盘上草稿)          ← 新增：Changed() 恒 true、WasCustom() 恒 false
          ↓  WritePlanBuilder.Build(...)       ← 一行没改
        写盘计划 = 「每一项都写成出厂值」
```

`DraftBaseline` 只加了 12 行：

```csharp
public bool ForceAll { get; private set; }

public static DraftBaseline Force(SettingsDraft diskDraft)
{
    var b = Capture(diskDraft ?? new SettingsDraft());
    b.ForceAll = true;          // 属性一律「改过」
    return b;
}

public bool Changed(...)   => ForceAll || <原来的比较>;
public bool WasCustom(..)  => !ForceAll && <原来的比较>;
```

**为什么不用「一个空基准」了事**：页 E 的增删是按 `baseline.Shortcut(id)` 算 diff 的，不经过 `Changed()`。把基线清空会让「用户自定义过、但本包不发行该行」的绑定删不掉（目标态里它也是「不存在」，两边相等 → 跳过）。所以 `Force()` 保留从盘上抓来的快捷键状态，只把属性比较变成恒真。

`Guarded()` 那条「自定义态不覆盖」的拦截在 `ForceAll` 下自动失效——这正是「恢复默认」的定义：用户手工标定的值也要被恢复。

#### 默认值清单：11 个文件 + 1 组注册表

这是「现在所有能设置的选项」的完整落点，也是安装包与 `setting.ahk` 的对账表：

| # | 文件 / 键 | 归属 | 写法 | 内容 | `--default` 行为 |
|---|---|---|---|---|---|
| 1 | `mpv.conf` | **共享**（用户可能有自己的键） | 键级 | 最多 24 键：hwdec / deinterlace / loop-file / save-position-on-quit / subs-match-os-language / subs-fallback-forced / 色调 4 键 / video-output-levels / audio-channels / af / screenshot-format / screenshot-high-bit-depth / screenshot-dir / `include=~~/cover-art.conf` / `include=~~/audio-info.conf` / input-ipc-server / osd 4 键 / load-context-menu | 逐键写回出厂值；**别人的行一行不动** |
| 2 | `script-opts\hdr-autoswitch.conf` | 共享 | 键级 | 5 键：mode / sdr_passthrough / manage_system_hdr / osd_notify / only_fullscreen | 写 5 键（首行 BOM 保护注释保留） |
| 3 | `cover-art.conf` | EXE 独占 | 整文件 | 6 行条件 profile | 重写（`downscale-big` + `1200x1200`） |
| 4 | `audio-info.conf` | EXE 独占 | 整文件 | 7 行条件 profile | 重写（`640x180` + `osd-scale-by-window=no`） |
| 5 | `script-opts\MyMPC.conf` | EXE 独占 | 整文件 | 4 行 | 重写（信息窗开 / 单实例开 / pipe 名） |
| 6 | `script-opts\fit90.conf` | EXE 独占 | 整文件 | 3 行 | 重写（`ratio=90`） |
| 7 | `script-opts\context_menu.conf` | **EXE 独占（本轮新纳入）** | 整文件 | 23 行 | 从内嵌资源重写 |
| 8 | `scripts\MyMPC_ui.lua` | EXE 独占 | 整文件 | 内嵌 | 重写 |
| 9 | `scripts\mpv-settings.lua` | **EXE 独占（本轮新纳入）** | 整文件 | 64 行 | 从内嵌资源重写 |
| 10 | `input.conf` | 共享 | 行级 | 38 个受管动作 | 受管行回「发行态」（E4 的 10 行仍在），**不受管的行一律保留** |
| 11 | `menu.conf` | 共享 | 行级 | 补「选项(&O)」一行 | 只在一行缺失时补回，位置仍在「配置」之前 |
| 12 | `HKCU\Software\BassMax` | 注册表 | 6 键 | Bass 2 / Treble 2 / Drive 1 / Preset 4 / Enabled 1 / TrayIcon 保持原值 | 写 6 键；`--keep-registry` 可跳过 |

`--default` 只在**真的不一样**时才写：`ForceAll` 会把「本来就是默认值」的键也标成改动，所以在 `DefaultProfile.Build` 末尾把 `From == To` 的空改动滤掉（有 `Transform` 的 `af` 除外）。实测「已经是全默认」的现场再跑一次，文件哈希一个字节都不变。

#### 「能合并的就合并」——结论是不能

| 想合并的 | 技术上可行吗 | 结论 |
|---|---|---|
| 四个 `script-opts\*.conf` 合成一个 | **不行** | mpv 按脚本名读 `<script-name>.conf`：`fit90.lua` 只读 `fit90.conf`、`context_menu.lua` 只读 `context_menu.conf`、`MyMPC_ui.lua` 只读 `MyMPC.conf`、`hdr-autoswitch.lua` 只读 `hdr-autoswitch.conf`。合并等于改脚本，收益为零 |
| `cover-art.conf` / `audio-info.conf` 并进 `mpv.conf` | 可以（就是两个 profile 段 + 去掉 include 行） | **不建议**。现在「关掉信息窗」= 摘掉 `include` 一行、文件留着不碍事；并进去以后要按段落增删，还得处理用户自己写的 profile 段，风险高一个量级 |
| `context_menu.conf` 并进 `mpv.conf` | 不行 | 同上，脚本按名字读 |
| `MyMPC_ui.lua` + `mpv-settings.lua` 合成一个 | 可以（互不依赖，都是我们自己的脚本） | **没做**。两个脚本职责完全不同（一个是 OSD 信息窗 + 单实例，一个只负责「选项」拉起 EXE），合起来省一个文件却让 `--default`/备份的粒度变粗；而且 `mpv-settings.lua` 要能在「用户删掉 MyMPC_ui.lua」时独立工作 |

所以「文件数」这件事没有优化空间，能优化的是**谁拥有它**：9 个文件归 EXE 独占（可以整文件重写），2 个是共享文件（只碰自己的键），1 组注册表。

#### 命令行契约

```
MpvSettings.exe --default [--config-dir=<portable_config>] [--keep-registry] [--dry-run]
                [--dump-plan=<f.json>] [--dump-result=<f.json>]
```

- 别名：`-default`、`/default`（单横线、Windows 风格都认，大小写不敏感）。
- **不弹任何窗口**：在 `App.OnStartup` 建窗之前就分流，这条路一个 WPF 控件都不需要。
- 退出码：`0` 成功 / `1` 写入被阻止或失败 / `2` 配置目录不可用（找不到 mpv.exe 或 mpv.conf）。
- `--keep-registry`：不动 `HKCU\Software\BassMax`。给「只想重置播放器、不想动音效」的场合留的。
- `--dry-run`：只算计划并打印，一个字节都不写（实测文件哈希不变）。
- WinExe 默认没有控制台，代码里 `AttachConsole(-1)` 借父进程的控制台，从 cmd / AHK 跑时能看到明细；借不到就只剩退出码——**退出码才是给脚本用的接口**。

安装包里就是一行（Inno Setup，放在复制完文件之后）：

```
[Run]
Filename: "{app}\MediaPlayer\mpv\MpvSettings.exe"; \
  Parameters: "--default --config-dir=""{app}\MediaPlayer\mpv\portable_config"""; \
  Flags: runhidden waituntilterminated
```

装完即默认态，安装包不必再带一份配置。安装脚本（`*.iss`）不在本仓库里，这里只给调用方式。

#### `setting.ahk` 侧：从「覆盖四个模板」改成「叫一下 EXE」

```ahk
FileGetVersion, mDefVer, %mDefExe%
StringSplit, mDefV, mDefVer, .
if (mDefV1 = "" || mDefV1 + 0 < 0 || (mDefV1 + 0 = 0 && mDefV2 + 0 < 9)) { ...提示版本过旧... }
RunWait, "%mDefExe%" --default --config-dir="%mDefDir%", , Hide UseErrorLevel
mDefCode := ErrorLevel
```

**版本闸必须留**：0.9 之前的 EXE 不认识 `--default`（`App` 只认 `--dump-only` / `--apply-only` / `--plan-only`），会把它当未知参数继续走正常流程把设置窗口弹出来，而 `RunWait` 会一直等那个窗口关掉——界面就此卡死。实测旧版 `FileVersion=0.8.1.0`、新版 `0.9.0.0`，用前两位数字比较，比字符串比较可靠。

两个细节：① `screenshot-dir` 在恢复前先取出来、恢复后 `MpvSet` 写回——截图位置是安装时定的，恢复默认不该把它挪回桌面；② 失败分支按退出码分三档提示，不再统一弹「模板缺失」。

#### 验证

| 脚本 | 断言 | 覆盖 |
|---|---|---|
| `_ref\stage9_default_verify.py` | **约 40 条全过** | 在生产配置的副本上把 9 处改坏（改 6 个键 / 删 2 个文件 / 改 4 个整文件 / 删菜单条目 / 加 2 行 input）→ `--dry-run` 零写入 → `--default` 逐项回出厂值 → 独占文件与内嵌资源逐字节一致 → 再跑一次零写入 |
| `_ref\stage9_ahk_default_probe.py` | **10 条全过** | 真机端到端：备份生产配置 → 改坏 `loop-file` + 删 `context_menu.conf` → 点「恢复 MPV 默认配置」→ 确认框按「是」→ `loop-file` 回 `inf`、`context_menu.conf` 重建 792 B 且含主题色 → **生产目录哈希完整还原** |
| 编译产物冒烟 | 起窗通过 | `setting.exe` 1,427,968 B（+512） |

`stage9_ahk_default_probe.py` 顺带修掉一个**测试前提失效**：C 组原本假设「用户当前把 MPV 当主播放器」，实测这台机器存的是 MPC-HC，于是「点主按钮应拉起 MpvSettings.exe」必然失败——失败的其实是前提而不是代码。现在探针自己点 MPV 单选、测完再点回原来的选项。

#### 踩坑

1. **`#Warn` 名称冲突会让脚本一启动就卡在警告框**：`MpvOpenSettingsProgram()` 里的局部 `mExe` 与恢复默认那段新建的脚本级 `mExe` 同名 → AHK 弹「This local variable has the same name as a global variable」，`FindWindow` 找不到主窗口，探针直接判「语法错误」。改名为 `mProg`。**同理 `mDir` / `mCfg` 已经是全局变量**（截图函数里 `global ... mDir` 在用），新代码一律加前缀（`mDefXxx`），否则会悄悄冲掉别人的值。这个脚本第 2010 / 2028 行早就有同类注释，还是踩了第二次。
2. **XML 注释里不能出现 `--`**：`csproj` 注释写 `` `--default` `` 直接 `MSB4025`。改成「default 命令行开关」。
3. **`--input-ipc-server=\\.\pipe` + 名字** 少一个反斜杠就变成 `\\.\pipempv_xxx`，连不上还报「超时」而不是「路径错」。
4. **原生菜单只在光标位于客户区内才弹**：`mp_win32_menu_show()` 开头就 `PtInRect` 检查，探针不先 `SetCursorPos` 永远测不到弹窗——第一次跑出来的「原生菜单没弹」是假阴性。

#### 新增 / 修改文件

| 文件 | 说明 |
|---|---|
| `Services\DefaultProfile.cs`（新，约 180 行） | 目标态 + 外观键 + 两个内嵌文件 + menu.conf 条目，末尾滤掉空改动 |
| `Services\CliDefault.cs`（新，约 140 行） | 参数解析、`AttachConsole`、明细输出、退出码 |
| `Models\WritePlan.cs` | `DraftBaseline.ForceAll` / `Force(diskDraft)`（12 行） |
| `Services\ConfigLocator.cs` | `ContextMenuConf` / `MpvSettingsLua` 两个路径 |
| `MpvSettings.csproj` | 两个新 `EmbeddedResource`；`<Version>0.9.0-stage9a</Version>` |
| `Assets\context_menu.conf` / `Assets\mpv-settings.lua`（新） | 与生产文件逐字节相同的副本，`--default` 的恢复源 |
| `App.xaml.cs` | `--default` 在建窗之前分流 |
| `00.Core\setting.ahk` | `MpvRestoreDefaults:` 改成调 EXE + 版本闸；`MpvOpenSettingsProgram` 局部改名 |

部署：`MpvSettings.exe` 350,948 → **361,190 字节**（生产与 `12.mpv` 两处）；`setting.exe` 1,427,456 → **1,427,968 字节**（`00.Core` 与安装根两处，md5 `5c2c39d…`）。

#### 后续（阶段 ⑨ 剩下的部分）

备份 / 导出导入还没做：桌面「导出 / 导入播放器设置」目前只覆盖 MPC-HC / MPC-BE / PotPlayer，不含 MPV。有了 `--default` 之后这块反而简单了——**导出可以照 `DefaultProfile` 的表来收（9 个独占文件 + 2 个共享文件的受管键 + 注册表 6 键），导入时再走一遍 diff 写盘**，不需要另立一套清单。

---

### 6.16 阶段 ⑨b：拖动窗口不锁比例（`0.9.1-stage9b`）

#### 需求

> 「手动拖动 MPV 边框改变大小时，不要保持窗口比例，但要保持视频比例。能做到吗？能做到的话更新进简单设置 exe 并设为默认值。」

#### 结论：能做到，一个键就够

```ini
keepaspect-window=no
```

**为什么看起来像两件事、实际是两个独立的开关**：

| 选项 | 管什么 | 本包取值 |
|---|---|---|
| `keepaspect-window` | **窗口矩形**。`WM_SIZING` 时要不要把宽高强行凑成视频比例 | `no`（自由拉伸） |
| `keepaspect` | **画面渲染比例**。视频按原比例画、不足处补黑边，而不是拉伸填满 | `yes`（不动） |

源码依据（`video/out/w32_common.c:1258`，`handle_sizing()`）：

```c
bool keep_aspect = w32->opts->keepaspect && w32->opts->keepaspect_window;
if (!keep_aspect || !w32->o_dwidth || !w32->o_dheight)
    return;          // ← 为假时原样返回，窗口按用户拖的尺寸走
...
c_h = c_w * w32->o_dheight / w32->o_dwidth;   // ← 为真时强制成视频比例
rc->bottom = rc->top + w_h;
```

所以「不锁窗口、锁画面」不是折中方案，而是**关掉一个开关的自然结果**：窗口自由了，渲染仍然按 `keepaspect` 走，多出来的地方就是黑边。

#### 实测（2026-11-15 01:0x）

用 `SendMessageW(WM_SIZING)` 直接打 `handle_sizing`，模拟拖边框（比真拖鼠标更可控，数值可复现）：

| 组 | 配置 | 请求 900×360（拖右边缘） | 请求 1000×400（拖右下角） |
|---|---|---|---|
| A | `keepaspect-window=yes`（mpv 出厂） | 被改成 **900×506**（16:9） | 被改成 **1000×562** |
| B | `keepaspect-window=no` | **900×360** 原样 | **1000×400** 原样 |

画面侧另测（IPC 读属性，窗口 900×360 放 16:9 片源）：

```
video-params:     aspect=1.777778  dw=1280  dh=720
osd-dimensions:   w=900 h=360 aspect=2.5  ml=130  mr=130  mt=0  mb=0
```

`ml=130 / mr=130` 就是左右各 130px 黑边，`900-260=640`，`640/360=1.778`。**画面没被拉歪，是按比例居中补的黑边。**

#### 落到设置程序

- 位置：**画面页**新增「窗口」卡片，一项开关「拖动边框时锁住窗口比例」。
- 语义：勾上 = 锁（写 `yes`）；不勾 = 不锁（写 `no`）。**默认不勾**，与主流播放器一致。
- 键缺失时按本包默认 `false` 展示，**不跟 mpv 原生的 `yes` 走**——这就是「设为默认值」的含义。
- 反推：`SettingsDraft.KeepWindowAspect = ParseBool(m.Get("keepaspect-window", "no"), false)`。

顺带说明一个容易误解的点：选项名是 `keepaspect-window`，**不是** `keepaspect`。后者改的是画面比例覆盖（`--video-aspect-override` 的别名），动它会真的把画面拉变形，本包不碰。

#### 顺手修掉一个真 bug：mpv.conf 的颜色值必须带引号

查 `keepaspect-window` 时把 `mpv.conf` 交给 `mpv --list-options`，冒出三条：

```
Error parsing option osd-outline-color (option requires parameter)
mpv.conf:41: setting option osd-outline-color='' failed.
```

回查源码 `options/parse_configfile.c:117`：

```c
} else {
    // No quoting; take everything until the comment or end of line
    int end = bstrchr(line, '#');
    value = bstr_strip(end < 0 ? line : bstr_splice(line, 0, end));
    line.len = 0;
}
```

**未加引号的值从第一个 `#` 处截断**。于是 `osd-color=#FFF0F7F0` 的值是空字符串 → 解析失败 → 整行被丢弃 → 回落成默认黑。

结论：**上一轮 ⑥.14 加的四条 OSD 配色一条都没生效**。当时看到的「菜单配色跟设置程序一致」其实只靠 `script-opts\context_menu.conf` 里那几个键（脚本自己按 `#RRGGBB` 解析，不走 mpv 的 conf 解析器），菜单底色一直是默认的黑。

修法（三处一起改）：

| 位置 | 改法 |
|---|---|
| 生产 `mpv.conf` | 四条颜色加引号；另有三行编码坏掉的注释（GBK 误写，中文被 `?` 截断、还吞掉了 `osd-color` 行）一并重写 |
| `12.mpv\portable_config\mpv.conf` | 同上，四条颜色加引号 |
| `Services\DefaultProfile.cs` 的 `Appearance` 表 | 值改成 `"\"#FFF0F7F0\""` 这种带引号的形式，并在注释里写明原因 |

`ConfWriter.InlineComment()` 本来就要求 `#` 前面是空格才算注释，所以加引号不会被它误判成行内注释。而 `ConfFile.Get()` 不剥引号，键表与磁盘两边都带引号，比较一致。

**修完的实际值**（IPC 读 `options/*`，这是真正生效的那一份，`--list-options` 只显示 mpv 出厂默认）：

```
options/osd-color                    = '#FFF0F7F0'
options/osd-outline-color            = '#FF1B2918'
options/osd-selected-color           = '#FFA3F6BE'
options/osd-selected-outline-color   = '#FF131F11'
options/load-context-menu            = True
options/keepaspect-window            = False
options/keepaspect                   = True
```

#### 又揪出一个：`setting.ahk` 用 ANSI 编码读 UTF-8 文件（高危）

补完颜色引号后再跑一遍真机「恢复默认」探针，发现 `mpv.conf` 的中文注释又变成乱码了：

```
# 鍙抽敭鑿滃崟鍜?OSD 鐢ㄥ悓涓€濂楅厤鑹诧細澧ㄧ豢搴?+ 浜粸楂樹寒銆?
```

而且**行被合并**了——原本 4 行注释 + 1 行 `osd-color=` 挤成一整行。

根因在 `00.Core\setting.ahk`：

```ahk
FileRead, mTxt, %mFile%                     ; ← AHK v1 省略编码 = 系统 ANSI（中文系统 GBK）
...
FileAppend, %mOut%, %mFile%, UTF-8-RAW      ; ← 却按 UTF-8 写回
```

UTF-8 字节被按 GBK 解码（大部分组合在 GBK 里也合法，不报错）→ 得到「鍙抽敭」这类字符串 → 再以 UTF-8 写回 → 中文永久损坏。解码错位还会吃掉换行，于是相邻行被并成一行。

命中路径不止「恢复默认」：

| 触发 | 调用 |
|---|---|
| MPV 页任一切换开关 / 下拉 | `MpvSet` → `MpvWriteKey` |
| 恢复默认后写回 `screenshot-dir` | `MpvSet` |
| 读键反推界面状态 | `MpvReadKey` |

**也就是说，只要 `mpv.conf` 里有中文，用户在 `setting.exe` 里改任何一项 MPV 设置，整个文件的注释就会被烙成乱码。** 这是本轮最严重的问题，比颜色引号严重得多——颜色只是不生效，这个是毁数据。

修法是两处 `FileRead` 都显式指定 `UTF-8-RAW`：

```ahk
FileRead, mTxt, %mFile%, UTF-8-RAW
```

`UTF-8-RAW` 不剥 BOM、不做任何转换，与写侧的 `FileAppend ..., UTF-8-RAW` 对称，字节级保真。

> 为什么之前没暴露：`mpv.conf` 原本全是 ASCII，GBK 与 UTF-8 对 ASCII 完全一致，怎么读写都不出事。6.14 开始往里写中文注释，这个潜伏的编码假设才显形。

验证：`stage9_ahk_default_probe.py` 加了 5 条断言（D5–D9）——合法 UTF-8 / 中文注释完好 / 无 GBK 特征串 / `keepaspect-window` 仍在 / `load-context-menu` 仍在。实跑 15 条全过。

`setting.exe` 重新编译并部署：1,427,968 字节不变，md5 `5098f7b9…`（`00.Core` 与安装根两处一致），备份 `_bak_20261115_0106`。

#### 验证

| 脚本 | 条数 | 覆盖 |
|---|---|---|
| `stage9b_keepaspect.py` | 6 | `WM_SIZING` 两组对照：`yes` 被改写成 16:9、`no` 原样保留 |
| `stage9b_aspect_probe.py` | 3 | 窗口 2.5:1 时 `video-params/aspect` 仍是 1.778、`ml/mr=130` |
| `stage9b_cfg_effective.py` | 7 | 生产配置七项实际生效值 |
| `stage9b_default_verify.py` | 15 | 改坏 5 键 → dry-run 零写入 → 全量恢复 → **颜色带引号** → 二次零写入 → mpv 读到正确值 |
| `stage9_ahk_default_probe.py` | 15 | 真机点「恢复 MPV 默认配置」，**生产目录哈希完整还原**；D5–D9 守 `setting.ahk` 的编码（合法 UTF-8 / 中文注释完好 / 无 GBK 特征串 / 两个新键仍在） |

最后一条顺带印证了格式一致：探针的比对基准是手工修好的文件，`--default` 跑完哈希一致，说明它写出来的颜色格式与手改的逐字节相同。

#### 文件清单

| 文件 | 改动 |
|---|---|
| `Models\SettingsDraft.cs` | 新增 `KeepWindowAspect`；`LoadFrom` 读键、`SetVideoDefaults` 置 `false` |
| `Services\WritePlanBuilder.cs` | `Put(..., "keepaspect-window", YesNo(d.KeepWindowAspect), "KeepWindowAspect")` |
| `Services\DefaultProfile.cs` | `Appearance` 表四条颜色加引号 + 注释说明为什么 |
| `Views\VideoView.xaml` | 新增「窗口」卡片 |
| `MainWindow.xaml.cs` | 「画面」页副标题加「拖动窗口时的比例行为」 |
| `Views\AboutView.xaml` | 版本 `0.9.1`、选项控件 28 → 29、conf 键 31 → 32 |
| `MpvSettings.csproj` | `<Version>0.9.1-stage9b</Version>` |
| `00.Core\setting.ahk` | 两处 `FileRead` 加 `UTF-8-RAW`（修 GBK 乱码 bug） |
| 生产 / 模板 `mpv.conf` | 四条颜色加引号、`keepaspect-window=no`、重写三行坏注释 |

部署：publish **361,702 字节**（旧 361,190，+512），生产与 `12.mpv` 两处 md5 一致（`9a4bb029…`）；`setting.exe` **1,427,968 字节**（md5 `5098f7b9…`，`00.Core` 与安装根两处一致）。备份带 `_bak_20261115_0102` / `_0106`。

#### 后续

不需要动 —— `keepaspect-window` 是渲染层选项，改完即时生效于「下一次拖拽」，不必重启播放器。但 `mpv.conf` 是启动时读的，**已经开着的窗口要重开才不吃旧值**；6.17.10 加的左下角「打开 MPV 播放器」按钮就是干这个的（先关干净所有 mpv.exe 再重开）。这一条已经写在设置页的说明里。

---

### 6.17 阶段 ⑩：导入导出方案（**2026-11-15 22:1x 已拍板，⑩b 已落地，实施见 6.18**）+ 音效滤镜常驻 / 重启播放器按钮（**已落地**，`0.9.2-stage10a`）

用户 2026-11-15 21:4x 问三件事：① MPV 配置是多文件的，导入导出用压缩包还是自定义文件夹；② 「MPV 有专属音效，导入导出要强制写 `af=bassmax`，音效开关应该控制注册表而不是删除这句」，要求同时改 EXE 与 `setting.ahk`；③ 追加「给 EXE 左下角加一个『打开 MPV 播放器』按钮，方式是关掉已开播放器再重新打开」。②③ 本轮已落地；① 的三点拍板结论见 6.17.9，代码在 ⑩b 全部落地（见 6.18）。

#### 6.17.1 结论：**自定义文件夹，不做压缩包**

压缩包的唯一好处是「一个文件好传」，代价全是实打实的：

| 问题 | 具体 |
|---|---|
| AHK v1 没有原生 zip | 只能调 `Compress-Archive` cmdlet（起外壳进程、慢、杀软容易盯）或外挂 7z.exe（多带一个二进制进安装包，增加体积与误报面）。用户明确反感这两点 |
| 中文路径编码 | PS 5.1 的 `Compress-Archive` 与 .NET `ZipFile` 对非 ASCII 文件名的处理不一致（CP437 / UTF-8 flag），备份目录名含中文时容易解出乱码 |
| 不可查、不可改、不可 diff | 想改一个键要先解压、改完再压回去；固定文件夹能直接 `fc` / `git diff` / 手工改一行再导入 |
| 导入多一步 | 压缩包要先解到临时目录，路径不固定。固定文件夹的导出 / 导入各一行命令，可写进脚本或安装包 |

**真要存档 / 发给别人，在文件夹上右键压缩即可，工具不代劳。** 工具只保证文件夹是一个自洽、可校验、可重复导入的单元。

#### 6.17.2 目录结构：固定路径、覆盖式导出

用户指定一个文件夹（默认 `A_Desktop\MyMPC-MPV-备份`，路径存进 `playerini` 的 `MpvBackupDir`），导出**直接写这个目录本身、覆盖同名文件**，不建 `20261115_220000\` 之类的时间戳子目录。理由是导入路径固定、可脚本化、可重复调用；代价是会覆盖上一次备份——**用户拍板「就保留一份，不用太复杂」，不留最近 3 份历史**；由「导入前自动快照」兜住（见 6.17.8），那个快照同样固定一份、覆盖式。

```
<备份目录>\
  manifest.json          格式版本 / 导出时间 / EXE 版本 / 来源配置目录
                         + profile（受管键值、注册表 6 键、af 链）
                         + 每个文件的相对路径与 SHA256
  portable_config\       镜像目录结构，只放清单里的文件
    mpv.conf  input.conf  menu.conf
    cover-art.conf  audio-info.conf
    script-opts\MyMPC.conf  fit90.conf  hdr-autoswitch.conf  context_menu.conf
    scripts\MyMPC_ui.lua  mpv-settings.lua
  extra\                 可选：--include-extra 才收，用户自定义脚本整文件带走
  _before_import\        导入前自动做的现场快照（回滚 / 反悔用）
```

`manifest.json` 不是装饰：**格式版本不认或任一文件 SHA256 对不上，直接拒导（退出码 2）**，避免拿错目录、半截拷贝或旧版清单导进来。

#### 6.17.3 三层分类：**导入绝不整目录覆盖**

这是整个方案最容易做错的地方。生产 `portable_config` 有 15 个文件，`DefaultProfile` 只管 11 个；把备份目录整份拷回去会毁掉三样东西：

| 类 | 文件 | 导出 | 导入 |
|---|---|---|---|
| **A 共享文件**（只管自己的键） | `mpv.conf`、`input.conf`、`menu.conf` | 存原文（审计用）+ manifest 里记受管键值 | **按 diff 只写受管键**，绝不整文件覆盖 |
| **B 独占文件**（EXE 全权生成） | `cover-art.conf`、`audio-info.conf`、`script-opts\` 四个、`scripts\` 两个 lua | 存原文（审计 / 人工比对用） | 由 `WritePlanBuilder` 按目标草稿整文件重新生成（与 `--default` 同源，换机导入自动用当前 EXE 的脚本版本）；内容一致就跳过 |
| **C 本机 / 第三方** | `scripts\stats.lua`(74 KB)、`hdr-autoswitch.lua`、`fit90.lua`、`tone-ab.lua`、`cache\`、`*.bak_*` | 默认不带，`--include-extra` 才收 | 不动 |
| **D 注册表** | `HKCU\Software\BassMax` 六键 | 值写进 `manifest.json` | 走现成 `RegistryWriter`，带失败回滚 |

A 类为什么不能整覆盖，三个已验证的实例：

- `mpv.conf` 第 35 行 `screenshot-dir=<桌面>/` 是**本机路径**，换机导入会串到别人的桌面；`--default` 本来就不管这个键，恢复默认后由 `setting.ahk` 原样写回。
- `input.conf` 里可能有用户自绑键（`stage9_default_verify.py` 里就有一条 `Alt+9 script-message foo`，明确**不属于受管的 38 个动作，本就不该删**）。
- `menu.conf` 是 mpv 自带的 8.4 KB 菜单，我们只往里插了「选项(&O)」一行，整覆盖会抹掉用户自己加的条目。

#### 6.17.4 复用 `--default` 那条 diff 链路，**不另立第二份键表**

`--default` 的实现是「目标态 = 出厂草稿、基准态 = 盘上现状 + `DraftBaseline.Force()`（全部算改过）→ 复用 `WritePlanBuilder.Build()`」。导入只是把目标态换掉，其余一行不改：

```
导出 = SettingsDraft.LoadFrom(当前快照) → 序列化成 manifest.profile
导入 = 反序列化 profile
       → WritePlanBuilder.Build(profile, DraftBaseline.Force(盘上现状), 当前快照)
       → 追加 B 类整文件写回 + D 类注册表六键
       → ConfWriter.Apply（原子替换 + 失败回滚，现成的）
```

**键表只有 `WritePlanBuilder` / `DefaultProfile` 这一份**，与 UI、`--default`、导入导出三者同源。抄第二份清单迟早走岔——6.15 已经用这个理由干掉过四个 `*_default.conf` 模板，不要重蹈覆辙。导出时 A 类文件的「原文」只用于审计和人工比对，不参与导入写盘。

#### 6.17.5 `af=bassmax` 强制（本轮已落地）

用户定调：**MPV 有专属音效，`af=bassmax` 是不变量；音效开关只控注册表，绝不删这句。** 本轮改了三处：

| 位置 | 改动 |
|---|---|
| `Services\WritePlanBuilder.cs` | 删掉「按 `AfEnabled` 增删 af」的整段（原来关闭时会写空值行 `af=`），换成**无条件** `EnsureAfChain(plan, s)`：缺 bassmax 就补，用 `Transform` 写前重读、走 `AfChain.Merge(chain, true)` 保留用户自己的 `scaletempo2` / `lavfi=[...]`。`--default`、普通保存、将来的导入都自动获得这个保证 |
| `Views\AudioView.xaml` | 「启用音效链路」开关改成**只读说明行**（`af=bassmax` 常驻）；「音效开关」明确写成只写 `HKCU\Software\BassMax\Enabled`；`audio-spdif` 互斥提示改成「滤镜仍保留，用 Ctrl+b 或 Enabled=0 旁通」 |
| `00.Core\setting.ahk` | 新增 `EnsureMpvAf()`（读 af 链 → 缺 bassmax 就追加 → 写回），在 `MpvSet()` 里自动调用（覆盖 MPV 页所有开关 / 下拉），并在 `MpvRestoreDefaults:` 与 `DEF:` 末尾显式兜一次。**放在 `MpvSet` 而不是 `MpvWriteKey` 是为了避开递归**：`EnsureMpvAf → MpvWriteKey → EnsureMpvAf …`。变量一律 `mAfXxx` 前缀，躲开上次那个 `#Warn` 名称冲突坑 |

`setting.ahk` 的 Button1 本来**就只写注册表**（`targets1 := [MakeRegTarget(BassMaxRegPath, "Enabled", "1", "0", "SZ")]`），这一侧原本是对的，本轮只是把注释写明白并加了保底。`Ctrl+b` 的 `af toggle bassmax` 是运行时旁通，不改配置文件，保留。

导入时**不看备份里的 af 值**，无条件把 `bassmax` 合并回链——因为 `WritePlanBuilder` 已经无条件兜底，只要导入走同一条链路，这条自然成立。

#### 6.17.6 CLI 契约（⑩b 已实现）

```
MpvSettings.exe --export=<dir> [--config-dir=<pc>] [--include-extra]
MpvSettings.exe --import=<dir> [--config-dir=<pc>] [--dry-run] [--keep-registry] [--no-snapshot]
```

- 退出码沿用 `--default`：`0` 成功 / `1` 写入失败 / `2` 清单无效或配置不可用。
- `--dry-run` 打印将改什么，一个字节不写；`--keep-registry` 跳过 D 类（换机导入时不想带别人的调音值）。
- 导入前自动把现场导出到 `<dir>\_before_import\`（`--no-snapshot` 可关）。
- 沿用 `CliDefault` 的 `AttachConsole(-1)`，`--dump-plan` / `--dump-result` 复用，**放在建窗之前**，与 `--default` 并列。
- 版本闸：新参数要求 EXE ≥ `0.10`；`setting.ahk` 调用前按 `FileGetVersion` 前两位拦（现有 `--default` 的闸是 `< 0.9` 拦，导入导出要另设一档），否则旧 EXE 会把未知参数当普通启动、弹出窗口让 `RunWait` 永远等下去。

#### 6.17.7 `setting.ahk` 与安装包接入（CLI 已备好，本轮不接 AHK）

> **⑩b 结论**：用户要求「导入导出放在关于里面」「就保留一份，不用太复杂」，所以本轮**不改 `setting.ahk`**：GUI 入口在关于页，脚本入口就是 6.17.6 的两个开关；`MPC:` / `huifu:` 保持只管 MPC-HC / MPC-BE / PotPlayer 的 `.reg` + `.ini`。CLI 版本为 `0.10.0`，将来真要接 AHK 时再照下面四条加参数、并按 `FileGetVersion` 前两位设 0.10 版本闸（防旧 EXE 把未知参数当普通启动、弹窗让 `RunWait` 等死）。以下方案保留备用，代码未动。

- 现有 `MPC:` / `huifu:` 两个按钮只管 MPC-HC / MPC-BE / PotPlayer 的 `.reg` + `.ini`，**保持不动**，各管各的。
- 新增「MPV 备份位置」选择（`FileSelectFolder`），默认 `A_Desktop "\MyMPC-MPV-备份"`，路径写进 `playerini` 的 `MpvBackupDir`，选过就记住。
- `MPC:` 末尾追加 `RunWait, "%mBakExe%" --export="%mBakDir%", , Hide UseErrorLevel`，把结果并进状态栏文案；`huifu:` 追加 `--import=`。
- 安装包首次配置 / 修复安装直接调 `--default`（已在 6.15 给出 Inno Setup 片段）；要预置一套推荐配置则调 `--import=<随包目录>`，两者共用同一条 diff 链路。

#### 6.17.8 风险与边界

1. **不导本机路径**：`screenshot-dir` 不在受管键里，换机导入不会串桌面路径。
2. **不导缓存与备份**：`cache\`（着色器缓存，最大 128 MB）、`*.bak_*` 一律排除。
3. **第三方脚本默认不带**：`stats.lua` 等是 mpv / 安装包自带的，不是用户配置；`--include-extra` 才收 `scripts\` 下不在清单里的文件。
4. **注册表只收 6 个键的值**，不 `reg export` 整个子树，避免把无关的托盘状态带过来；导入走 `RegistryWriter` 的失败回滚。
5. **导入是覆盖操作**：自动先做现场快照 + `--dry-run` 预演，两道保险。
6. **`af` 强制**：不管备份里是什么，导入后 `af` 必含 `bassmax`，且保留链上其它滤镜。

#### 6.17.9 **用户拍板结论（2026-11-15 22:1x）**

| # | 问题 | 答复 | 落地 |
|---|---|---|---|
| 1 | 固定覆盖要不要同时保留最近 3 份历史？ | **就保留一份，不用太复杂** | 导出固定覆盖、无时间戳子目录；`_before_import\` 也只有一份 |
| 2 | 第三方脚本默认不带、`--include-extra` 够不够？ | **同意** | 默认只收 11 个受管文件；显式 `--include-extra` 才收 `scripts\` 下其它文本脚本，且导入也要同时给该开关才写回 |
| 3 | BassMax 注册表六键是否一起导出？ | **同意** | 六键值写进 `manifest.json`，导入走 `RegistryWriter`；换机用 `--keep-registry` 跳过 |

用户另加两条：**导出默认到桌面没问题**（`桌面\MyMPC-MPV-备份`），**导入导出放在关于页里**。

#### 6.17.10 本轮顺带落地：左下角「打开 MPV 播放器」

`MainWindow.xaml` 页脚左栏原来有一段三行长说明，页脚固定 `68px`，直接加按钮会顶出去。改成「按钮 + 一句短提示」横排（原说明的入口与写盘语义挪进按钮 ToolTip 和关于页），两行小字（配置源 / 快照）保留。`GhostButton` 模板的 `ContentPresenter` **没有绑 Padding**，所以 `Padding` 设了无效；改全局样式又会把顶部工具行撑宽（6.14 刚修好「重置页面」被裁），最终只给这个按钮单独加 `MinWidth="132"`，文字左右各留约 16px。

`RestartMpv()`：`Process.GetProcessesByName("mpv")` → 先 `CloseMainWindow()` 让它正常收尾（`save-position-on-quit=no`，不记进度）→ 1.2s 未退才 `Kill()` → `Process.Start` 带 `--idle=yes --force-window=yes --config-dir="<ConfigDir>"`。**必须先关干净**：`MyMPC_ui.lua` 的单实例管道会把第二个实例的文件 / 命令转发给第一个，旧进程还在时新实例只会转发后自己退出，配置也不会重读。只关 `mpv.exe`，不动 MPC-HC / PotPlayer（那是 `setting.exe` 的职责）。显式传 `--config-dir` 是为了 `--config-dir` 指向副本时，新起来的 mpv 不去读真实安装目录的配置。

#### 验证（本轮）

| 脚本 | 条数 | 结论 |
|---|---|---|
| `stage10_af_verify.py` | 19 | 删 af 行 / 空值行 / 换成别的滤镜链 → `--default` 与关音效开关都不摘 bassmax；中文注释、颜色引号、`keepaspect-window`、`load-context-menu` 全在；二次 `--default` 字节不变 |
| `stage10_restart_probe.py` | 8 | UIA `InvokePattern` 真机点按钮（WPF 控件没有 HWND，`BM_CLICK` 打不到）→ 旧 PID 消失、新 PID 唯一、命令行带齐三个参数 |
| `stage8b_ahk_probe.py` | 全过 | `setting.ahk` 改完无语法错误 / 无 `#Warn` 弹窗，两处入口仍正常 |
| `stage9_ahk_default_probe.py` | 15 | 真机点「恢复默认」，生产目录哈希完整还原，中文注释未被写坏 |

踩坑一条：第一轮 af 验证有 4 条假 FAIL，原因是**并行跑 `dotnet publish` 时验证脚本正在读同一个 `publish\MpvSettings.exe`**，EXE 被覆写导致读到半成品。串行重跑 19/19 全过。**别在编译的同时跑针对发布版 EXE 的验证。**

产物：publish **363,240 字节**（`0.9.2-stage10a`，md5 `f5e1b682…`，生产与 `12.mpv` 一致）；`setting.exe` **1,427,968 字节**（md5 `9992a50d…`，两处一致）。备份 `_bak_20261115_2159`。

### 6.18 阶段 ⑩b：导入导出落地 + 页脚对齐 / 主题弹窗（`0.10.0-stage10b`）

2026-11-15 22:1x 用户拍板 6.17.9 的三点，追加「导出默认到桌面没问题」「导入导出放在关于里面」，并指出页脚左按钮与右侧按钮不在同一高度。当晚全部落地，代码不再是设计稿。

#### 6.18.1 新增 / 改动的文件

| 文件 | 作用 |
|---|---|
| `Services\BackupService.cs`（新） | `Options` / `Result` / `Manifest`；导出、导入、SHA256 校验、目录守卫、注册表偏好（`HKCU\Software\MyMPC\MpvSettings\BackupDir`） |
| `Services\CliBackup.cs`（新） | `--export[=<dir>]` / `--import[=<dir>]` 无窗口入口；不带路径时取记住的目录；退出码 `0/1/2`；复用 `CliDefault` 的控制台与 dump |
| `App.xaml.cs` | 与 `CliDefault.Wants` 并列挂 `CliBackup.Wants`，都在建窗之前处理 |
| `Views\AboutView.xaml(.cs)` | 「配置备份 / 恢复」卡片：目录显示 + 导出 / 导入 / 更改文件夹 / 打开文件夹 + 三段说明；`RefreshDir()` 直接读静态偏好，不靠 `Loaded` |
| `MainWindow.xaml(.cs)` | `BackupDir` / `BackupPickDir` / `BackupExport` / `BackupImport` / `BackupOpenDir` / `ReloadDraftFromDisk`；页脚对齐；关于页版本号与「后续阶段」文案 |
| `NoticeWindow.xaml(.cs)`（新） | 墨绿主题的结果 / 确认弹窗，照抄 `ApplyResultWindow` 的画法（自绘标题栏、卡片明细、DWM 小圆角） |
| `Models\WritePlan.cs` | `EditableProps` 更名并公开为 `ManagedProps`，补登记 `KeepWindowAspect`（原来漏登记，导致每次保存都把它当成「改过」） |
| `Services\DefaultProfile.cs` | 抽出 `EnsureFixedAssets` / `TrimPlan` 并公开：`--default` 与导入共用定版资产（五条 `osd-*`、`context_menu.conf`、`mpv-settings.lua`、`menu.conf`「选项」行） |
| `Services\CliDefault.cs` | `AttachOutput` / `PrintPlan` / `Dump<T>` 改为 public 供 `CliBackup` 复用 |
| `MpvSettings.csproj` | 版本 `0.10.0-stage10b`（≥ 0.10，给将来 AHK 版本闸留档） |

#### 6.18.2 manifest 与导入链路（实施细节）

- **导出**：固定目录覆盖式，先删自己生成的 `portable_config\` / `extra\` / `manifest.json`，保留 `_before_import\` 作回滚点；目录里若有不属于本工具的文件且没有 manifest，直接拒导（防误删资料）。11 个受管文件按原字节镜像（保留 BOM / CRLF），`Files` 字典记「相对路径 → SHA256」。
- **profile 同源**：`DraftBaseline.Capture(盘上草稿).Get(name)` 逐个取 `ManagedProps`，不另抄键表；排除 `ShotDirIndex` / `ShotDirPath`（`screenshot-dir` 是本机路径，换机导入不串别人的桌面）。页 E 的 38 行单独存 `id → 键`，空串表示「不写这一行」。
- **导入**：先验 `Format=1` / `Tool=MpvSettings` / 每个文件 SHA256，任一不过退出码 2 且不写盘；`DryRun` 只算计划；否则先自动把现场导出到 `<dir>\_before_import\`（失败即取消导入），再 `WritePlanBuilder.Build(目标草稿, DraftBaseline.Force(盘上现状), 当前快照)` → `DefaultProfile.EnsureFixedAssets` → `TrimPlan` → `ConfWriter.Apply`（原子替换 + 注册表回滚）。
- **页 E 不走 `DraftPatch` 的 Shortcuts 分支**：那个分支调 `SetKey`，而 `SetKey` 把「键等于内置默认键」当成「不写这一行」，会把 E4 里显式写在 `input.conf` 的 10 行误判成删除。导入改为直接 `row.Load(present, key)` 保留 present 语义。
- **B 类实施调整**：6.17.3 原写「整文件写回备份原文」，落地时改成由 `WritePlanBuilder` 按目标草稿重新生成（导出时 `ForceAll` 保证 `coverChanged` / `uiChanged` 恒真，整文件一定会重建），备份里的原文只用于审计。理由：少一份「备份脚本版本 vs 当前 EXE」的冲突面，换机导入自动用当前 EXE 的脚本版本，且与 `--default` 完全同源。
- **不变量**：`af=bassmax` 由 `EnsureAfChain` 无条件兜底（不看备份里的 af 值）；音效开关仍只写 `HKCU\Software\BassMax\Enabled`；自定义态标志（`*Custom`）在导入时被强制清空，避免本机「非预设值」保护把备份拦下来。
- **C 类**：默认不收；`--include-extra` 导出时收 `scripts\` 下非受管的文本脚本（`.lua/.conf/.txt/.json/.md/.cfg`，排除 `*.bak_*` 与 >1 MB），导入也必须同时给该开关才整文件写回。

#### 6.18.3 关于页入口与主题弹窗

关于页第二张卡片即入口，默认目录是 `桌面\MyMPC-MPV-备份`（`Environment.SpecialFolder.DesktopDirectory`），选过的新目录记在 `HKCU\Software\MyMPC\MpvSettings\BackupDir`。导入前弹确认框（取消不写任何文件），成功后重新读盘刷新草稿 / 快照 / 基线，并提示用左下角「打开 MPV 播放器」重启生效。

**第一版结果框用的是系统 `MessageBox`（白底系统主题），用户 22:4x 指出「弹窗主题不一致，之前的弹窗不是挺好吗」** → 新增 `NoticeWindow`，完全沿用 `ApplyResultWindow` 的墨绿深色画法，标题按成功 / 失败 / 确认分别用 `AccentBrush` / `DangerBrush` / `WarnBrush`。确认框、结果框、选择文件夹失败提示三处系统弹窗全部换掉，项目里再无 `MessageBox.Show`。

#### 6.18.4 页脚对齐（修了两次）

用户原话：「打开 MPV 和右边按钮没有对齐，不在一个高度」。根因是左栏一个纵向 `StackPanel` 里塞了「按钮行 + 两行状态小字」，按钮落在第一行；右栏只有按钮行、整体居中，于是左按钮比右按钮高约 11px。

- **第一版（错的）**：改成两行 Grid，把左右按钮都放进第一行 → 两边对齐了，但**右侧「应用 / 确定 / 取消」整行被往上抬了约 24px**。用户立刻指出「怎么把最下面的按钮高度往上抬了？应该按原先应用按钮的高度设置打开 MPV 按钮的高度」。
- **定稿**：右侧按钮一个像素都不动（仍是 68px 页脚里垂直居中、距底 17px）；左栏改成**横向** `StackPanel`（按钮 + 右侧三行小字），按钮自身 `VerticalAlignment=Center`。左右两栏都 Center 于同一个 68px，按钮中心必然落在同一条水平线上，三行小字也不丢。UIA 实测四个按钮 Y 完全相同（150% DPI 下 `1484`、高 `51`），右侧按钮 Y 回到修改前的原值。

#### 6.18.5 验证（全部串行，避免 publish 覆写 EXE 造成假失败）

| 脚本 | 条数 | 结论 |
|---|---|---|
| `stage10b_backup_verify.py` | 58 | 生产配置副本 + 隔离注册表键：导出 manifest / 11 文件 SHA / profile 排除本机路径 / 38 行快捷键 / 注册表六键；破坏现场后导入只写受管键（自绑键、menu.conf 自定义条目、本机截图目录全保留）、af 强制、定版资产补齐、`_before_import` 快照；SHA / 格式 / 空目录拒导；dry-run、keep-registry、no-snapshot、include-extra、幂等、默认目录记忆 |
| `stage10b_ui_probe.py` | 9 | 整窗截图 + UIA：四个页脚按钮同 Y 同高、关于页四个备份按钮可见 |
| `stage10b_ui_export_click.py` | 5 | UIA 真机点「导出配置」→ 主题弹窗出现并关闭 → 桌面默认目录生成 manifest 与 `portable_config\mpv.conf`、无历史目录 |
| `stage10_af_verify.py` | 19 | af 常驻回归全过 |
| `stage9_default_verify.py` | 全过 | `--default` 回归；顺手把脚本里四条 `osd-*` 的旧断言从裸 `#...` 改成带引号（6.16 之后旧断言一直是假 FAIL） |
| `stage9b_default_verify.py` / `stage9b_keepaspect.py` | 全过 | 恢复默认与窗口比例回归 |

踩坑两则：① UI 探针里 UIA 按中文名找导航按钮找不到，WPF `RadioButton` 的 `Name` 为空，要按 `AutomationId=NavAbout` 找；② 第一版 `--shot-page=关于` 截图里备份目录显示占位「—」，`Loaded` 事件还没派发就渲染了，改成 `ShowPage` 主动调 `AboutView.RefreshDir()`，且 `RefreshDir` 不依赖 `Window.GetWindow`。

#### 6.18.6 产物与部署

`publish\MpvSettings.exe` **389,354 字节**，md5 `9b7bc5e3787f43ec63b295c204e5f8ae`；同步部署到生产 `<MPV安装目录>\MpvSettings.exe` 与安装包模板 `<项目根>/12.mpv/MpvSettings.exe`，三处逐字节一致。旧 EXE 备份在 `<备份目录>/`，源码备份在 `_bak_20261115_2220\`。`setting.exe` / `setting.ahk` 本轮**未改**。桌面已由端到端验证生成一份真实备份 `桌面\MyMPC-MPV-备份\`。


### 6.19 阶段 ⑩c：`1.0.0` 定版（图标 / 路径探测 / 界面文案 / 安装包模板）

2026-11-15 23:0x 用户一次给四件事，随后追加一条「图标不要这 2 个，帮我重新生成一个」：
① 安装程序跑 `--default` 会不会新建 App Paths 注册表；② 检测路径应该用 `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\mplayerl.exe` 的 `Path` 定义 + `MediaPlayer\mpv\mpv.exe`，因为 mplayerl.exe 是必装核心、MPV 是选装；③ 桌面那份备份是不是默认值，是的话复制到 `<项目根>/12.mpv` 并清理多余文件；④ 给 EXE 弄个图标、版本定为 `1.0.0`、删掉界面上跟开发有关的说明。

#### 6.19.1 注册表：只读，不新建

- `ConfigLocator.ReadInstallRoot()` 是工程里唯一读 **HKLM** 的地方（显式 64 位视图），`--default` / `--export` / `--import` 都只调它，**从不写 HKLM**。截图里那行「App Paths 注册表: …」只是只读的**配置来源标签**，不是写注册表。
- `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\mplayerl.exe` 由 `<安装包脚本>.iss` 第 171–172 行 `[Registry]` 创建，`Components: main`（必装核心），`(默认)={app}\mplayerl.exe`、`Path={app}`；本机实测 `Path = <安装根>`。
- 安装包 `[Run]` 目前只有 `reg.exe import default.reg`，**根本没有调用 `--default`**；真正调用的是 `setting.ahk` 的「恢复 MPV 默认配置」按钮，且显式带 `--config-dir="<A_WorkingDir>\MediaPlayer\mpv\portable_config"`，连 App Paths 都不用读。所以无论安装程序将来在哪一步加 `--default`，都不会由 EXE 新建这个键。
- 唯一会被 `--default` 写入 / 补齐的是 `HKCU\Software\BassMax` 六键（音效参数）；`BackupDir` 偏好写 `HKCU\Software\MyMPC\MpvSettings`。想完全不碰注册表加 `--keep-registry`。

#### 6.19.2 路径探测收紧（`Services\ConfigLocator.cs`）

按用户口径重写：安装根目录只认 App Paths 的 `Path`，MPV 是否安装只认 `<Path>\MediaPlayer\mpv\mpv.exe`。

- 新增 `InstallRoot` / `InstallMpvExe` 两个只读属性；`Resolve()` 先算 `mpvInstalled`。
- **有 `Path` 就把配置目录锚死在 `<Path>\MediaPlayer\mpv\portable_config`**，不再要求目录已存在（`--default` 首次安装时可能要整份建出来），更**不再往 EXE 附近回退**找 `12.mpv\portable_config` —— 旧逻辑在「装了 MyMPC 但没勾 MPV」的机器上会把开发包模板当生产配置来写。
- `Path` 存在但 `mpv.exe` 缺失 → 直接 `Reason = 未安装 MPV（找不到 …）` 并返回，`IsUsable=false`，`--default` / 导入退出码 2，一个字节都不写。
- 开发包回退只保留给「App Paths 完全不存在」的源码 / 验证环境；`--config-dir` 显式覆盖仍可用，但 `mpv.exe` 候选始终把 App Paths 路径放第一位。
- `BassMaxPanel` 候选改为优先 `InstallRoot`，`--config-dir` 指向纯配置副本时也能找到 `Codecs\bassmax_dshow.ax`。
- `setting.ahk` 的 `MpvInstalled()` 本来就是 `A_WorkingDir\MediaPlayer\mpv\mpv.exe`（`A_WorkingDir` 来自同一注册表 Path），两边语义现在完全一致，本轮**未改 AHK**。
- 关于页「当前配置」新增「MPV 程序」一行显示实际探测到的 exe 路径；`--dump-only` 实测 `ConfigSourceText = App Paths 注册表 · <MPV配置目录>`、`MpvExeText = <MPV安装目录>\mpv.exe`。

#### 6.19.3 桌面备份 = 默认值 → 同步 12.mpv 并清理

- **验证**：把桌面备份 11 个文件复制到临时目录，跑 `--default --keep-registry --dry-run --dump-plan` → 受管键 / 快捷键 / 注册表全零改动，只有 `cover-art.conf` 一条「整文件重写」（这是计划层常态：`WritePlanBuilder` 无条件列入整文件项，`ConfWriter` 比较同值会跳过）。再实跑 `--default --keep-registry`，输出「磁盘上的值已经与草稿一致，未重写文件」，**11 个文件 SHA256 与备份 11/11 全同** → 确认就是出厂默认态，不改桌面备份本身以保住 manifest 哈希。
- **同步**：把 11 个受管文件复制到 `<项目根>/12.mpv/portable_config/`。旧模板缺 `cover-art.conf` / `audio-info.conf` / `script-opts\MyMPC.conf` / `script-opts\fit90.conf` / `scripts\MyMPC_ui.lua`，`mpv.conf` 也少 `include=~~/cover-art.conf`、`include=~~/audio-info.conf`、`input-ipc-server=MyMPC_mpv` 三行，这次一次补齐；`mpv.conf` 的 `screenshot-dir` 仍是本机桌面占位，安装程序 `WriteMpvScreenshotDir()` 在 `ssPostInstall` 会按 `{userdesktop}` 重写。
- **清理**：`MyMPC_x64.iss` 第 151 行 `[Files] Source: "12.mpv\*"; recursesubdirs` 会把 12.mpv 里每个文件都装进用户机器，所以删掉 3 个 `MpvSettings.exe.bak_*`、`mpv.conf.bak_20261115_005805`、`cache\shader_*` 14 个运行时缓存并移除空的 `cache\` 目录。旧件备份在 `<备份目录>/`。保留 `mpv.exe` / `mpv.com` / `d3dcompiler_43.dll` / `bassmax_tray.exe` / `mpv\fonts.conf` 与 4 个第三方脚本（与生产逐字节一致）。
- `manifest.json` **不复制进模板**：它含本机路径、导出时间与 EXE 版本，属于备份元数据，不是 mpv 配置。端到端点「导出配置」时桌面备份的 `ExeVersion` 被刷新成 `1.0.0`，11 文件哈希仍与 12.mpv **11/11 一致**。

#### 6.19.4 图标：全新生成，不复用现成的两个

用户明确否掉 `01.ico\MPV2.ico`（蓝黄文件图标）与 `00.Core\ico.ico`（灰蓝齿轮），要求重新生成。

- 新做 `Assets\MpvSettings.ico`：墨绿圆角方片 `#131F11` + 同系描边 `#435A40` + 三条调音滑杆 `#37543E` + 薄荷圆钮 `#A3F6BE` / 高光 `#CCFFDF`，配色全部取自 `Themes\Theme.xaml`。
- 纯标准库生成（无第三方图像依赖）：7 个尺寸 `16/24/32/48/64/128/256`，4× 超采样抗锯齿，PNG 压缩 ICO 条目；脚本 `_ref\make_icon_1_0.py`。
- `MpvSettings.csproj` 加 `<ApplicationIcon>` 与 `<Resource>`；`MainWindow` / `ApplyResultWindow` / `NoticeWindow` 三个窗口都设 `Icon="pack://application:,,,/Assets/MpvSettings.ico"`，任务栏与标题栏同源。
- `_ref\stage10c_icon_pe_probe.py` 直接解析 PE 资源表：`RT_GROUP_ICON=32512`、`RT_ICON 1..7`，7 个条目字节数（322/429/562/770/918/1956/4468）与源 ICO 完全一致，从 EXE 里导出的 256 PNG 与生成图逐字节相同 → 图标确实嵌进了 EXE，不是只在源码目录里。

#### 6.19.5 版本与界面文案

- `Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion` 统一 `1.0.0`；实测 `FileVersion=1.0.0.0`、`ProductVersion=1.0.0`，`setting.ahk` 的 0.9 版本闸通过（提示文字仍写「0.9 或更高」，功能无误，本轮未重编译 setting.exe）。
- **关于页**：删掉技术栈（C# / WPF / .NET 10）、写盘实现（diff / 回滚 / REG_SZ / 整文件生成 / 逐行替换）、`V1 范围`、`后续阶段`、命令行参数、`manifest.json + SHA256` 说明；只留 1.0.0 标题、一段用途说明、备份卡片（4 按钮 + 用户语言的两段说明）、当前配置只读卡片。
- **其它页面**：`PlaybackView` / `VideoView` / `AudioView` / `ScreenshotView` / `ShortcutsView` 的 `Description` 与 ToolTip 去掉 `mpv.conf:`、`include 行`、`阶段 ②`、`V1`、`内存草稿`、键名等内部术语；音频页「af=bassmax」「5 项实时 · af 需重启」改为「始终启用」「音效实时 · 其余需重启」；写入结果窗口「本阶段未写入（按排期后续接入）」→「本次未写入」；禁用遮罩里的 `--config-dir` 提示改成「请确认安装时已勾选 MPV，或重新安装后重试」；页脚三个按钮的 ToolTip 也改成用户语言。
- `SnapshotText` 从「快照 … mpv.conf N 行 / hdr N 行 / input.conf N 行」简化成「读取时间 HH:mm:ss」；`SpdifText` / `CoverConfStatus` / `NoArtConfStatus` / `ScriptConfStatus` 去掉文件名与 include 字样。
- **保留两项实时状态**：`BassMaxRegStatus`（`6 / 6 齐全`）与 `ScriptConfStatus` 缺件分支里的文件名——`stage2_verify.py` / `stage7_verify.py` 有硬断言，且它们是状态反馈而非说明文字。

#### 6.19.6 验证与产物

| 脚本 | 条数 | 结论 |
|---|---|---|
| `stage10b_backup_verify.py` | 58 | 导入导出全链路回归 |
| `stage10b_ui_probe.py` | 9 | 整窗截图 + 四个页脚按钮同 Y 同高 + 关于页四个备份按钮可见（顺带验证 `pack://` 图标不会让窗口启动失败） |
| `stage10b_ui_export_click.py` | 5 | 真机点导出 → 主题弹窗 → 桌面备份 |
| `stage10_af_verify.py` | 19 | `af=bassmax` 常驻回归 |
| `stage9_default_verify.py` / `stage9b_default_verify.py` / `stage9b_keepaspect.py` | 全过 | 恢复默认、颜色引号、窗口比例回归 |
| `stage2_verify.py` | 52 | 修两条过时断言后通过 |
| `stage7_verify.py` | 全过 | 写盘事务 / 编码 / 生产不被触碰 |
| `stage10c_icon_pe_probe.py` | 7 尺寸 | EXE 原生图标资源与源 ICO 一致 |

`stage2_verify.py` 的两条**过时断言**（非产品 bug）：① 原来把 `fit_ratio=75` 写在 `MyMPC.conf` 就期望 `FitRatio=75`，但阶段 ⑦ 起比例以 `script-opts\fit90.conf` 的 `ratio` 为准，12.mpv 模板同步后自带 `ratio=90` 会把旧写法盖掉 → 改为在变异目录写 `fit90.conf ratio=75`；② `variant.AfEnabled` 原来期望 `False`，⑩ 起 `af=bassmax` 常驻、音效开关不再从 `af` 反推 → 改为 `True`。

产物 `publish\MpvSettings.exe` **416,982 字节**，`1.0.0`，md5 `10217cf223a10885a3c2a9f32a87a0de`；同步部署到生产 `<MPV安装目录>\MpvSettings.exe` 与模板 `<项目根>/12.mpv/MpvSettings.exe`，三处逐字节一致。部署时生产 EXE 被 22:56 打开的设置窗口占用，用 `CloseMainWindow()` 温和关闭后覆盖；旧版备份在 `<备份目录>/`。`setting.exe` / `setting.ahk` 本轮**未改**。

右键菜单改 mpv 源码仍是 `<mpv源码目录>/BUILD-NOTES.md` 第 10 节的待办，与 bassmax 重编译一起做，代码未动。

---


### 6.20 阶段 ⑩d：工程目录整理 + 安装包接入 MPV（EXE 仍为 1.0.0，未改代码）

用户 2026-11-15 23:4x 的三条收尾要求：① 把这次开发里**有用的文件汇总到 `<项目根>/MpvSettings`**；② **没用的 / 过期的 / 错误的删掉**；③ **更新 `<安装包脚本>.iss` 接入 MPV 的安装逻辑**。右键菜单改 mpv 源码**明确留到明天**，仍按 `<mpv源码目录>/BUILD-NOTES.md` 第 10 节执行，本轮代码未动。

#### 6.20.1 目录整理：先整份备份，再分类汇总

所有删除都可回退：先整份备份工作区 `_ref\` 与两处待移走的预览图到 **`<备份目录>/`**（19 MB，含 `整理报告.txt` 与 `MyMPC_x64.iss.⑩d前`）。整理后 `MpvSettings\` 的结构：

| 位置 | 内容 | 说明 |
|---|---|---|
| 根目录 `*.xaml / *.cs / .csproj / global.json` | 1.0.0 源码 10 个文件 | 一个不多一个不少，编译依赖全在 |
| `Assets\` `Controls\` `Models\` `Services\` `Themes\` `Views\` | 源码子目录 + 4 个内嵌资源 | `MpvSettings.ico` / 3 个 Lua·conf 内嵌资源 |
| `publish\MpvSettings.exe` | 416,982 B / `1.0.0` / md5 `10217cf2…` | 与生产 `MediaPlayer\mpv`、模板 `12.mpv` 三处逐字节一致 |
| `\_verify\` | **46 个**验证 / 工具脚本 + `subtest\` 真机测试素材（822 KB） | 从工作区 `_ref` 汇总；27 个脚本里过期的 `bin\Release\...\MpvSettings.exe` 路径统一改成 `publish\MpvSettings.exe`，截图输出改到 `预览\`，媒体路径改到 `_verify\subtest\` |
| `\_research\` | **17 份** mpv 源码 / 手册摘录、`bind_*.tsv`、默认绑定全表 | 明天改右键菜单的参考材料；`options.rst` / `input_default_v042.conf` 与另两份逐字节相同，去重只留一份 |
| `预览\` | **3 张** 1.0.0 成果图 | 关于页与页脚对齐、导出结果主题弹窗、EXE 内嵌图标 |

清掉的（都已备份）：工程根目录 **48 张**旧阶段预览 + `.png.txt` 小尾巴、打包根目录 **48 张**早期设计 / 阶段截图（含 4.3 MB `_dbg_screen.png`）、工作区 `_ref` 里的 133 MB 临时 `mpv.exe` 副本、`_draft_1_0.json`、`section_6_17.md`、`shot_*.png`、旧 `hdr_fix_backup_20261113_2203\`、一次性文档补丁 `update_spec_10b.py` / `update_spec_10c.py`、以及可再生的 `bin\` / `obj\`。规划文档 `MPV设置EXE_V1_参数与布局规划.md` **保留在 `<项目根>/` 根**，它是全项目共用规格，复制进工程只会多一份会漂移的副本。

整理后重新串行跑了 9 个回归脚本（备份 58/58、af 19/19、`--default`、9b、keepaspect、阶段 ② 52 项、阶段 ⑦、UI 9/9、图标 PE 7 条目），并 `dotnet build` 通过（0 警告 0 错误），随后再删掉重建的 `bin\` / `obj\`。三个还在用的探针顺手改成不再往工程根目录丢临时文件：`stage10b_ui_probe.py` 删掉 DPI 小尾巴、`stage10b_ui_export_click.py` 直接刷新成果预览、`stage10c_icon_pe_probe.py` 的重组 ICO 写系统临时目录。

#### 6.20.2 `<安装包脚本>.iss`：MPV 组件接入

ISS 是 **GBK + CRLF**，改动按字节读回、按原编码写回，8 处替换逐处断言只命中一次；改前备份在 `_bak_20261115_2345\MyMPC_x64.iss.⑩d前`。最终 81,875 B / 924 行。

| 段 | 改动 |
|---|---|
| `[Components]` | `mpv` 描述改成「MPV播放器 0.42 + MPV 简单设置 1.0.0」，仍是选装、只在 `full` 默认勾选 |
| `[InstallDelete]` | 兜底删老机器上的 `MpvSettings.exe.bak-*` 与 `portable_config\mpv.conf.bak_*`，避免 `12.mpv\*` 递归复制把它们装进新机 |
| `[Files]` | 注释写清模板 2026-11-15 已清干净、11 个默认受管文件 + 4 个第三方脚本随包、`screenshot-dir` 只是占位；并标明 `ignoreversion` 的既有语义（覆盖安装会刷新配置，升级前先用关于页导出） |
| `[Icons]` | 开始菜单新增「MPV 简单设置」快捷方式，`Components: mpv`，图标取 EXE 自身；与右键「选项」、快速设置 MPV 页构成第三个入口 |
| `[Run]` | **新增一行 `--default`**：`MpvSettings.exe --default --config-dir="{app}\MediaPlayer\mpv\portable_config"`，`runhidden waituntilterminated runasoriginaluser skipifdoesntexist` + `Components: mpv` + `Check: IsMpvSettingsRuntimePresent` |
| `[Code]` | 新增 `IsMpvSettingsRuntimePresent()`：读 32 位视图 `HKLM32\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App` 的值名，出现 `10.` 前缀才放行；修正 `WriteMpvScreenshotDir()` 与 `CurStepChanged` 里过期的「模板不含 screenshot-dir / 需要 UTF-8 BOM」注释 |

几个必须说清的点：

1. **`--default` 不会新建 `HKLM\…\App Paths\mplayerl.exe`**（⑩c 已确认，EXE 只读）；那个键由 `[Registry]` 的 `main` 组件创建，`mplayerl.exe` 是必装核心。
2. **实测 `--default` 对当前模板是零写入**：在 `12.mpv\portable_config` 副本上跑 `--default --keep-registry`，退出码 0、输出「磁盘上的值已经与草稿一致，未重写文件」、15 个文件哈希全同、`screenshot-dir` 原样保留。所以 `[Run]` 这一步是**保险**（补齐被旧版删掉的受管文件 / 六键注册表），不是唯一来源。
3. **`.NET 10` 运行时闸**：EXE 是 `SelfContained=false` 的框架相关单文件，缺 `Microsoft.WindowsDesktop.App 10.x` 会弹系统对话框；`runhidden + waituntilterminated` 看不见弹窗会把安装卡住，所以加 `Check`。本机 32 位视图实测值名含 `10.0.8` / `10.0.15`，探针返回 True。**若要在没装 .NET 10 的机器上分发，得改成自包含发布或随包带运行时**（明天可做，本轮未改 EXE）。
4. **时序**：Inno 官方安装顺序里 `[Run]` 在 `ssPostInstall` **之前**，所以 `--default` 先跑、`WriteMpvScreenshotDir()` 后跑，`screenshot-dir` 一定是最后一次写入，不会被整份重建盖掉；模板 mpv.conf 是 UTF-8 无 BOM + LF，Inno 6/7 的 `LoadStringsFromFile` 都支持无 BOM UTF-8，中文注释不会被按 ANSI 破坏。
5. **覆盖安装的既有行为**：`12.mpv\*` 用 `ignoreversion`，每次重装都会用模板刷新 `portable_config`；`default.reg` 也会重写 BassMax 六键。这不是本轮引入的，已在 `[Files]` 注释里写明「升级前先关于页导出、装完再导入」。

#### 6.20.3 编译验证

- `<Inno Setup目录>/ISCC.exe <安装包脚本>.iss` **完整编译成功**（267 s，产物 `<发布包目录>/MyMPC_<日期>.exe`，111 MB），说明 `[Run]` 的 `Check`、`RegGetValueNames` / `HKLM32`、`IconFilename` 等新写法全部通过。
- 机器上的 `Inno Setup 6` 是 6.2.2 增强版，**原版脚本就编不过**（第 51 行 `SetupArchitecture` 是 6.3+ 指令），与本轮改动无关；单独用小脚本验证 6.2.2 支持 `RegGetValueNames` / `HKLM32`，因此新 `[Code]` 两边都兼容。真要出包请用 Inno Setup 7。

> **2026-11-16 00:0x 用户澄清**：桌面 `MyMPC-MPV-备份\` 是他自己删掉的，明确「不用补桌面备份了」。本轮收尾核对时误以为丢失并重新导出过一份，已撤销（`_bak_20261115_2345\桌面备份_我误补_已撤销\`），桌面恢复无该目录的状态，注册表 `BackupDir` 记忆仍为空。**以后发现桌面没有这份备份属正常，不要自动补回**；出厂默认配置的权威副本是 `12.mpv\portable_config`。

> ✅ **2026-11-16 结案**：右键菜单已按 mpv 源码 owner-draw 方案做完（独立于本 EXE 的编译链），
> 见 `<mpv源码目录>/REBUILD-GUIDE.md` §13.2；配套要求 `load-context-menu=no` 三处一致（§12.1）。
> 下面 6.21 / 6.22 是同一天在**设置层**落定的两项，均不改 mpv 源码。

### 6.21 HDR 真直通（设置层定案，2026-11-16 21:4x）

#### 6.21.1 用户给的语义：Passthrough 与算法在**同一个互斥下拉**

MPCVR 的「直通显示器 / Passthrough」和 Reinhard / ACES / Hable / Mobius / BT2390 / Spline
是**同一个下拉里的互斥项**：选了直通就**完全不做色调映射**。色调修复档位默认关闭。
本包只保留两条例外（由 `hdr-autoswitch.lua` 负责，本轮不改 lua）：自动开关系统 HDR、
SDR 显示器回退 HDR 转 SDR。

#### 6.21.2 技术定性：只有 `clip` 是不处理

- gpu-next 里 `pl_tone_map_auto` 只是 **`pl_tone_map_spline` 的 deprecated alias** →
  `auto` / `spline` **都在做曲线压缩**，旧版把 `spline` 写死就是"关闭档仍被压暗"的根因。
- **`clip` 不做处理**（shader 里 `#define tone_map(x) clamp(x, ...)`），等价 MPCVR Passthrough。
- `tone-mapping=no` 本版不接受；**`tone-mapping` 启动期定死，运行时 `mp.set_property` 改不动** →
  任何改动都必须**重启 mpv**。
- `target-colorspace-hint=yes` 才能按显示器实际 CSP 输出；**绝不能写 `target-trc=bt.1886`**，
  那会强制所有内容走 SDR gamma 2.4。
- `hdr-autoswitch.lua` 运行时改 `d3d11-output-csp` 无效（启动后定死），别指望它救场。

#### 6.21.3 唯一规则表（两组控件共用一个键）

`tone-mapping` 由「HDR 模式」和「色调档位」两组控件**共同决定**，必须按盘上最终状态反推一次，
不能两组各写各的互相覆盖：

| 最终状态 | `tone-mapping` |
|---|---|
| 直通显示器 + 色调档位关闭 + 有 HDR 屏 | **`clip`**（零处理，出厂默认） |
| 转 SDR / 开启任一色调档位 / 无 HDR 屏 | `spline` |

#### 6.21.4 改动落点（4 处，缺一不可）

| # | 文件 | 改动 |
|---|---|---|
| 1 | 生产 `MediaPlayer\mpv\portable_config\mpv.conf` + 模板 `12.mpv\portable_config\mpv.conf` | 默认 `tone-mapping=spline → clip`；保持 `target-colorspace-hint=yes`、`hwdec=auto-copy`、**不写** `target-trc=bt.1886` |
| 2 | `00.Core\setting.ahk` | 新增 `MpvSyncToneMapping()`：读 `hdr-autoswitch.conf` 的 `mode` + `mpv.conf` 的 `inverse-tone-mapping` + 全局 `HdrSupported` 反推；在 `MpvAction` 末尾用 `InStr(mCtl,"MpvHdrMode")=1 \|\| InStr(mCtl,"MpvTone")=1` 对两组按钮统一调用（单条 `if`，别写多行续行） |
| 3 | `MpvSettings\Services\WritePlanBuilder.cs` | `Put(..., "tone-mapping", tm, "ToneMapping", force:true)`；**`force` 是必须的**，否则「恢复默认」不会把盘上残留的 `spline` 归位成 `clip` |
| 4 | `hdr-autoswitch.lua` | **不改**。两条例外（自动开系统 HDR / 无 HDR 屏禁用直通与档位）本来就在 lua + `MpvRefresh` 里 |

`MpvHdrMode2`（HDR 转 SDR）还必须同步写 `target-colorspace-hint=no` + `target-trc=bt.1886`；
`MpvHdrMode1`（直通）写回 `yes` + `auto`，**两个文件一起改**。

#### 6.21.5 验证与当前状态

- 日志：生产实跑 HDR10 出现 `Setting option 'tone-mapping' = 'clip'`，swapchain 仍
  `RGB_FULL_G2084_NONE_P2020`，`reconfig to ... bt.2020/pq`，shader `tone_map(x)=clamp(...)`。
- `MpvSettings.exe --default` 夹具：`mpv.conf · tone-mapping spline → clip`，只写这 1 个键。
- 用户 22:0x 实机：**P4 / P5 / P7 / P8 全部通过、真直通没有变灰、HDR 色调修复档位生效**。
- 当时的定版 `MpvSettings.exe` 为 425,686 B / md5 `5601f867…`；**22:5x 因 6.23 的
  「恢复默认」定版键又重编了一次**，当前定版见 6.23 末尾（427,222 B / `39679d86…`）。
- 用户随后已把生产目录恢复默认，现与模板同为 `tone-mapping=clip` /
  `inverse-tone-mapping=no` / `contrast=0`。**打包仍取 `12.mpv` 模板**：生产 `mpv.conf`
  与模板有排版差异（CRLF vs LF、`screenshot-dir` 行被 AHK 写盘挪到文件末尾），
  键值相同但字节不同，别拿生产目录当出厂默认副本。

### 6.22 `setting.ahk` 读写链路修复（2026-11-16 22:1x）

**症状**：HDR 色调修复在「MPV 简单设置」有效、在双击运行的 `setting.ahk` 里无效。
查下去发现**不是色调逻辑，是整个 MPV 设置页静默空转** —— 点任何按钮都不写盘。

**根因**：`MpvReadKey` / `MpvWriteKey` 两处写了 `FileRead, mTxt, %mFile%, UTF-8-RAW`。
AHK v1.1.37 的 `FileRead` **不支持第三参数指定编码**，该写法直接 `ErrorLevel=1` 读失败 →
读永远返回默认值（界面 `*` 恒在出厂档），写返回 `ERR_READ` 一个字节都不落盘。
此坑是 11-15 修 GBK 乱码（见当日修订记录）时引入的，之后一直没测过 MPV 页。

**实测四种写法**（真实生产 `mpv.conf`，中文判据「真直通」）：

| 写法 | 结果 |
|---|---|
| `FileRead, t, %f%, UTF-8-RAW` | `el=1` 读失败 |
| `FileRead, t, % f, UTF-8-RAW` 或省略第三参数 | `el=0` 但按系统 ANSI(GBK) 解 UTF-8 字节，中文乱码 |
| `FileEncoding, UTF-8-RAW` + 裸 `%f%` | 正确，**但不能用**（全局开关，脚本里有几十处 `IniRead`/`IniWrite`） |
| **`FileOpen(f, "r", "UTF-8-RAW").Read()`** | **正确且零副作用** |

**修法**：两处读统一改成 `mF := FileOpen(mFile, "r", "UTF-8-RAW")` + `mF.Read()` + `mF.Close()`；
写侧 `FileAppend, ..., UTF-8-RAW` 保持不变（实测写 UTF-8 正确）。
⚠️ **别用 `% mFile, UTF-8-RAW` 那种折中写法**：读进来是 GBK 乱码、写出去二次编码，
2.8 KB 配置一次写盘膨胀到 **190 KB**，中文注释全毁（夹具实测 `191,749 B`）。

**验证**：从修好的源码原样抽函数跑夹具，按钮序列
`clip → 0.8档 spline/inv=yes/con=39 → 关闭 clip → HDR转SDR spline → 直通 clip` 全对；
配置 `2833 B → 2833 B` 零膨胀、中文保真、无 BOM、CRLF 行数不变；源码 UTF-8 BOM 保留、花括号配对。

**部署约定**：用户是**双击 .ahk 运行，不编译 EXE**。改 `.ahk` 不等于改 `setting.exe`；
两处 `setting.exe` 于 22:27 被重编（526,336 B / md5 `4db5ad8d65ffdb524e51d7b05d75b1fd`），
若以后改回用 EXE，必须用当前 `.ahk` 重编并核对 md5（编译命令见 `REBUILD-GUIDE.md` §12.2.1）。


### 6.23 「恢复默认」补齐 HDR 直通定版键（2026-11-16 22:5x，方案 B）

**起因**：核对「`12.mpv\portable_config` 是否等于 MpvSettings 默认值」时，反向投毒测出程序
只管 `mpv.conf` 24 键 + `hdr-autoswitch.conf` 5 键，另有 15 个键不受管。逐个复核后发现其中
**两个键是真 BUG，不是刻意留白**：`setting.ahk` 的 `MpvHdrMode1/2`（1393~1405 行）会写
`target-colorspace-hint`（`yes`/`no`）与 `target-trc`（`auto`/`bt.1886`），而「恢复 MPV
默认配置」（1518~1581 行）只是 `RunWait MpvSettings.exe --default`，旧 EXE 的
`DefaultProfile` / `WritePlanBuilder` **完全不碰这两个键**。

**实测复现**（`_verify\default_check\result_hdr_roundtrip.json`）：先模拟点「HDR转SDR」
→ 再跑 `--default`，`tone-mapping` 回到 `clip`、`mode` 回到 `passthrough`，但
`target-colorspace-hint=no` 与 `target-trc=bt.1886` **原样残留** → 界面显示真直通、
输出侧却被 `bt.1886` 强制 SDR。又是一例「界面骗人」。

**方案选择**：用户在 A（只改 `setting.ahk`，零编译）与 B（改 EXE，彻底）中选 **B**。
B 的好处是两个入口共用同一个恢复默认实现，`.ahk` 一行都不用动。

**实现**（`MpvSettings\Services\DefaultProfile.cs` 新增 `AddHdrFixedKeys()`，由
`EnsureFixedAssets()` 调用，恢复默认与导入都会走）：

| 目标态 | `target-colorspace-hint` | `target-trc` |
|---|---|---|
| 直通（`mode=passthrough`） | `yes` | **删除该行** |
| 转 SDR（`mode=tonemap`） | `no` | `bt.1886` |

- 出厂态是 **hint=yes + 盘上没有 `target-trc` 这一行**（模板即如此）。所以直通时走
  `KeyChange.Remove=true` + 空 `MatchValue` 删任意值的行，**不是写 `auto`** —— 写 auto
  虽然功能等效，却会多出一行、破坏刚验证过的「模板 == 默认值逐字节一致」。
- 目标态按**计划里最终写入的 `mode`** 判定，不硬写成直通：导入 `mode=tonemap` 的备份时
  输出侧保持 no + bt.1886，不会被掰成直通。本方法在 `TrimPlan()` 之前执行（恢复默认与
  导入都是先 `EnsureFixedAssets` 再 `TrimPlan`），所以同值的 mode 改动此刻仍在计划里。
- `EnsureFixedAssets()` 也是导入路径的定版资产入口，配色 / 两个独占文件 / menu.conf
  早已是同一套语义，这里只是补上 HDR 那一组。

**其余 13 个键维持不受管**（刻意留的自由度，强行纳入会覆盖合理定制）：
`vo` / `gpu-api` 启动期定死改错立即黑屏且覆盖安装 `ignoreversion` 会刷新；
`screenshot-*` / `load-stats-overlay` / 色调配套参数是纯偏好；`displays` / `hdr_api` /
`input-ipc-server` 是多屏与排障的逃生口。

**验证**（新 EXE 双向夹具，`_verify\default_check\`）：
① 模板原样跑 `--default` → 15 文件逐字节零差异、`FilesWritten=0`；
② 改成「HDR转SDR」态再跑 → 日志出现 `target-colorspace-hint no → yes`、
`移除 target-trc=`，`tone-mapping` / `mode` / 四键同步归位，**15 文件逐字节回到模板**。
⚠️ 夹具污染只能替换行内值、不要重建文件，重建会多出尾随换行导致 diff 假失败。

**定版**：`MpvSettings.exe` **427,222 B / md5 `39679d86b2a68c0197a7358803765762`**
（版本仍 `1.0.0`，通过 `.ahk` 的 ≥0.9 版本闸），publish / 生产 `MediaPlayer\mpv` /
`12.mpv` 三处逐字节一致。旧版 `5601f867…` 备份在
`MpvSettings\_verify\exe_history\MpvSettings_5601f867_before_hdrfix.exe`。


## 7. 已定决策（截至 2026-11-16 22:5x：全部拍板，无待定项；1.0.0 定版见 6.19，安装包接入见 6.20，HDR 直通见 6.21，恢复默认定版键见 6.23）

| # | 决策 | 落地位置 |
|---|---|---|
| 1 | 新 EXE **不接管** `setting.exe` 的 MPV 专属页；只是快速设置的一个按钮入口 | 第 0 / 4 / 5 节 |
| 2 | VapourSynth 为 API v4，V1 不纳入、不预留对话框 | 2.1 节 |
| 3 | 音效参数用**原生控件直接写注册表** | `HKCU\Software\BassMax` 是统一注册表，所有滤镜读同一处；副标题加一句说明即可，原厂面板逃生口保留。第 0 节 + 页 C |
| 4 | 砍「启动时显示统计面板」「标记色彩空间」；**保留「高位深截图」开关** | 页 A / 页 D |
| 5 | 技术栈定为 **WPF / .NET 10 LTS**，SDK 10.0.404 已装；分发需 .NET 10 桌面运行时 | 第 6 节 + 6.1 节 |
| 6 | 「外挂字幕优先」是 mpv 原生默认值 → **不做开关、不写键，只加只读说明** | 1.1 节 |
| 7 | 新增「纯音频封面：默认显示 + 原始大小 + 上限」→ **纳入 V1** | 1.2 节 + 页 A 封面卡片 + 写盘契约第 8 条 |
| 8 | 新增第 5 页「快捷键」→ **38 行固定清单，有默认值显示、没值留空**；`input.conf` 从只读改为页 E 独占写 | 1.3 节 + 页 E + 写盘契约第 9 条 |
| 9 | 封面「窗口上限」上限 **1600** 确认可用，默认由 800 改为 **1200** | 1.2 节 + 页 A 封面卡片 |

**2026-11-13 01:56 追加两项，均已拍板**：

| # | 决策 | 落地位置 |
|---|---|---|
| 10 | 无封面音频黑框选 **B（脚本方案）**；与单实例**合并成一份 `MyMPC_ui.lua`**。真频谱不进 V1（运行时 `lavfi-complex` 段错误），右上柱形为 ASS 装饰动画 | 1.4 / 1.6 节、页 A 卡片、写盘契约第 10/11 条 |
| 11 | **接受「scripts 只读」边界收紧**：EXE 只管自己新建的 `MyMPC_ui.lua` / `MyMPC.conf` / `audio-info.conf`，现有 4 个脚本与 `input.conf` / `menu.conf` 一行不改。「允许同时运行多个播放器」默认关闭 | 1.5 / 1.6 节、页 A 卡片 |

**2026-11-15 01:2x 追加两项，均已拍板**：

| # | 决策 | 落地位置 |
|---|---|---|
| 12 | **右键菜单改 mpv 源码**，不自绘 EXE、不用 Lua 绕路。理由：反正每轮都要为 bassmax 重编译，边际成本低；技术路线定为 owner-draw 原生菜单（`MFT_OWNERDRAW` + `WM_MEASUREITEM` / `WM_DRAWITEM`），配色复用 `--osd-*` 四条。**改天实施，代码未动** | `<mpv源码目录>/BUILD-NOTES.md` 第 10 节 |
| 13 | **拖动窗口边框不锁窗口比例**，画面按视频比例居中补黑边（`keepaspect-window=no`），并作为本包出厂默认。`keepaspect` 保持 `yes` 不动 | 6.16 节 + 画面页「窗口」卡片 |

**2026-11-15 22:1x 追加三项（导入导出拍板）**：

| # | 决策 | 落地位置 |
|---|---|---|
| 14 | 固定路径覆盖式导出，**只保留一份**，不留最近 3 份历史；导入前 `_before_import\` 快照也只有一份 | 6.17.2 / 6.17.9 / 6.18 |
| 15 | 第三方 / 自定义脚本**默认不带**，显式 `--include-extra` 才收（够了）；BassMax 注册表**六键一起导出**，换机用 `--keep-registry` 跳过 | 6.17.3 / 6.17.9 / 6.18 |
| 16 | 导出默认目录为**桌面\MyMPC-MPV-备份**，导入导出入口放在**关于页**，弹窗沿用程序自带墨绿主题 | 6.18.3 |

**2026-11-16 追加六项（均已拍板并落地）**：

| # | 决策 | 落地位置 |
|---|---|---|
| 17 | **HDR 真直通**：MPCVR 语义下「直通显示器 + 色调档位关闭」= 零处理，mpv 侧只有 `tone-mapping=clip` 等价；`spline` / `auto` 都是曲线压缩 | 6.21 + 生产/模板 `mpv.conf` |
| 18 | `tone-mapping` 由「HDR 模式」「色调档位」两组控件**共用一个键**并按最终状态反推；`MpvSettings` 用 `force:true` 保证「恢复默认」归位 `clip` | 6.21.3 / 6.21.4 + `WritePlanBuilder.cs` |
| 19 | **杜比视界 P4 走源码修复**（两个补丁），`hwdec` **永久 `auto-copy`**、绝不用 `auto-safe`（P5 IPT 在本版 D3D11 零拷贝路径拿不到 RPU 系数会黑屏） | `REBUILD-GUIDE.md` §4.2 / §12.1；`12.mpv\mpv.conf` |
| 20 | **右键菜单走原生 owner-draw**，配套 `load-context-menu` 永久 `no`，生产 / 模板 / `DefaultProfile.cs` 三处一致 | `REBUILD-GUIDE.md` §12.1 / §13.2 |
| 21 | **.NET 10 桌面运行时不集成进安装包**（57 MB），改为 `RuntimeNoticePage` 纯告知页，只在「勾选 MPV 且缺运行时」时显示、不阻断安装 | `<安装包脚本>.iss` `[Code]` |
| 22 | **预览 / 测试构建必须新建隔离 iss**（换 AppId + `Uninstallable=no` + `CreateAppDir=no` + 掏空 `CurStepChanged`），绝不在正式 iss 上改路径 | `MpvSettings\_verify\iss_history\MyMPC_x64.PREVIEW_notice.iss` |
| 23 | **「恢复默认」必须把 HDR 直通定版键一起归位**：`target-colorspace-hint=yes` + **删掉 `target-trc` 行**（不写 `auto`，保住模板逐字节一致）；走 EXE 的 `DefaultProfile`（方案 B），`setting.ahk` 不动 | 6.23 |

**2026-11-13 20:07 追加排期（非新拍板项，只是顺序）**：

| # | 事项 | 落地位置 |
|---|---|---|
| 12 | 「MPV 默认设置 → 备份文件 / 默认模板」**放到最后做**；`setting.ahk` 已改为配置更改只提示、不关播放器（MPV 只读设置文件） | 第 0 / 4 / 5 节 + 第 6 节阶段 ⑨ |

**规格重新冻结，无待拍板项。** 上限 1600、默认 1200 已确认；1.6 末尾「Lua 连 named pipe」已验证通过并定稿。仍需实机处理两项：封面「窗口上限」的手感（`autofit-larger` 只在初始窗口尺寸计算时起作用），以及单实例转发后窗口恢复默认 960x540 的问题（1.6 末尾已记录复现与推断）。二者都不阻塞阶段 ①。新增阶段 ⑨ 属排期调整，不改变 V1 规格与键集。

> ✅ **2026-11-14 更新**：第二项（960x540）已在阶段 ⑦ 定位并修复，见 1.6 末尾「结案」与 6.13；第一项（封面上限手感）保持原样，属观感调优、不影响正确性。

---

## 修订记录

- 2026-11-12 初稿：V1 参数清单、界面布局、写盘契约。
- 2026-11-12 修订：按「setting.exe 是快速设置、新 EXE 只是一个按钮入口、同时打开以最后一次点击为准」删除接管 / 互斥 / 文件锁方案，旧 MPV 页保持现状；查证本机 mpv 的 VapourSynth 为 API v4 → V1 不纳入、不预留对话框。
- 2026-11-12 修订：查证「外挂字幕优先」——本机 ffmpeg 造片实测 + mpv 源码 `compare_track()` 确认外部字幕轨是自动选轨第一顺位，原生默认即等价 MPC-HC 打勾态；记录 fuzzy/all 选错轨风险、关闭态语义坑与 MPC-HC/BE 对照（1.1 节）。当日稍后按拍板改为不做开关、不写键。
- 2026-11-12 定稿拍板：① 音效原生控件直写统一注册表 `HKCU\Software\BassMax`，副标题加一句说明；② 砍启动统计面板与标记色彩空间，保留高位深截图；③ 技术栈定 WPF，缺 .NET 环境直接下载；④ 外挂字幕优先改为只读说明；⑤ 新增「纯音频封面」并纳入 V1（1.2 节）。
- 2026-11-12 封面实测结论：`audio-display=embedded-first` + `cover-art-auto=exact` 默认已显示封面；用独立 `cover-art.conf` + `include=~~/cover-art.conf` + 条件 profile 实现「原始大小 + 上限」。5 个 `profile-cond` 逐条实测，只有 `p["current-tracks/video/image"] == true` 不误伤正常视频；`profile-restore=copy` 必写（否则音频→视频播放列表残留 `downscale-big`）；`video-unscaled=downscale-big` 而非 `yes`（后者超上限会裁剪）；`~~/` 前缀必写（否则 mpv 按工作目录找文件）。参数规模约 25 控件 / 约 30 键，第 7 节改为已定决策表。
- 2026-11-13 01:56 规格重新冻结：用户拍板「黑框选 B + 单实例一起做」，并接受 scripts 边界收紧。补做实测：`ass-events` overlay 必须设 `ov.res_x/res_y`（取 `osd-dimensions`）才渲染；同一 ASS 事件行多个 `\pos` 只有第一个生效，须用 `\n` 分成独立事件行；ASS 颜色是 BGR（紫色写 `\c&HFF9AB5&`）；`file-loaded` 时用 `aid` 判定比 `audio-params` 稳；运行时 `set_property("lavfi-complex", ...)` 段错误（exit 139），真频谱 V1 不做；同名 IPC pipe 第二个实例不退出、仍正常播放。新增 1.6 节四层架构 + 完整实测表 + 明天开工前置验证（Lua 连 named pipe）；同步改开头、第 0 节、页 A 卡片、写盘契约 10/11、阶段拆分、第 7 节、V2 候选。隔离实测包 `<实测目录>/` 已生成并截屏验证（`shots/deliver2.png`），供用户自行实测。
- 2026-11-13 01:20 规格重新打开：追加「无封面音频黑框」与「允许同时运行多个播放器」两项。黑框实测为 `--force-window` 固定 960x540（截屏 982x596），`geometry=640x180` 可缩到 662x236 且 `profile-restore=copy` 能正确撤销；`osd-msg1` 不支持 ASS 标签（四种写法全失败），`--cover-art-files` 会污染正常视频，条件 profile 里设 `lavfi-complex` 会自撤销卡死——三条否决，两条可行方案写入 1.4。单实例查证：`Assoc.ahk` 只关联 `mplayerl.exe "%1"`，`mplayerl.exe` 只读 `MainPlayer`，判断归播放器自己；mpv 全表无单实例选项，只能 IPC + 脚本，语义与两个场景边界写入 1.5。第 0 节新增 4 条事实、写盘契约新增第 10/11 条、阶段拆分改为 8 步、第 7 节恢复 2 项待拍板。
- 2026-11-13 规格冻结：确认封面「窗口上限」上限 1600 可用，默认值由 `800x800` 改为 **`1200x1200`**（1.2 节示例、键取舍表、页 A 表格同步）；顺手把范围起点由 `320` 对齐为 `400`，使 100 步长能精确落在 1200 与 1600 上。V1 定为 5 页 / 约 25 个选项控件 + 38 行快捷键清单，第 7 节无待定项；下一步安装 .NET SDK 后按第 6 节阶段 ① 开工（**版本后于同日 20:04 定为 .NET 10 / 10.0.404，见第 6 节**）。
- 2026-11-13 新增第 5 页「快捷键」：用 Lua 读 `input-bindings` 实测出内置默认 **220 条**、叠加本包 `input.conf` **10 行**后实际生效 **230 条**；确认默认绑定烤在二进制里（`is_weak=true`），`input.conf` 是强绑定（priority 15），因此「有默认值显示、没值留空」正是两层机制的自然映射。全表另存 `MPV快捷键_默认绑定全表.md`。查明 `F8` 覆盖默认的 `show-text ${playlist}`、`Alt+1`/`Alt+2` 覆盖默认窗口缩放；`input.conf` 是 LF 无 BOM 且语法为 `KEY command`（无 `=`），与 `mpv.conf` 完全不同。据此推翻初稿「`input.conf` 只读」的契约，改为页 E 独占写（第 4 节第 9 条），并同步修订第 2 节「不做按键绑定编辑器」、第 3 节 5 页布局、第 6 节阶段拆分、第 7 节决策表。
- 2026-11-13 02:05–02:30 实测收尾：① 信息窗改 **V2 布局**（时间码 `H*0.27→0.155`、歌名 `H*0.10→0.14` 且按词折行最多 2 行、格式信息移到装饰柱形下方右对齐；柱形仍是假频谱，副标题如实标注），真实截屏 `MPV无封面音频_信息窗_布局V2.png`；② 隔离实测包补 `scripts\single_instance.lua` + `input-ipc-server=MyMPC_mpv`，**1.6 的「Lua 连 named pipe」已实测通过**（LuaJIT `ffi` + `GetNamedPipeServerProcessId` 与 `pid` 对比，双实例端到端转发成功），定稿方案与实测表写入 1.6 末尾，第 6/7 节同步；③ **新遗留**：转发后目标窗口恢复默认 960x540、内容放大（切歌时条件 profile 被 restore，`geometry` 只在窗口创建时生效），当晚未修，对照组脚本 `_ref\diag_window.py` 待跑。用户 02:27 下线，明天先对比他人做的 MPV 再继续。④ `mplayerl.exe` / `SetUserFTA.exe` 与 `native64` 源码工程已集中复制到 `<工作目录>/mplayerl/`（`native64\` 完整工程 + `00.Core\` 生产在用副本），**源位置保留不删**，因 `00.Core` 的两个 exe 是安装包正在使用的文件。
- 2026-11-13 20:04 技术选型落定 **.NET 10 LTS / SDK 10.0.404**（用户已装）。查证：`.NET 8` 于 2026-11-10 EOL（末代补丁 8.0.34），`.NET 11` 同日 GA 但为 STS，下载页「推荐」只代表最新 GA 版本；10 为 LTS、与 11 同在 2028-11-14 终止支持，但更新策略保守、WPF 工具链与分发运行时普及率更稳。本机 `dotnet --list-sdks` = `8.0.428` + `10.0.404`、`dotnet --version` = `10.0.404`，且已有 `Microsoft.WindowsDesktop.App 10.0.8`；**8 保留不卸**（也不卸 8 运行时，别的软件依赖）。第 6 节改为 .NET 10，补 `.csproj`（`net10.0-windows` / `UseWPF` / `RollForward=Minor` / `PublishSingleFile` / `SelfContained=false`）、`global.json` 锁 10.0.404、Release 发布命令；开头范围、第 7 节决策表第 5 项、阶段 ① 描述同步。**新增 6.1 分发说明**：别人打不开时只装「.NET 桌面运行时 10.0.x → Windows x64 → 安装程序」，别装 ASP.NET Core / 单独 .NET 运行时 / zip 二进制 / .NET 11（`Minor` 不跨大版本）；附可直接复制的对外文案与 `self-contained` 备选的体积、误报代价。项目尚未创建，配置片段等阶段 ① 建项目时照抄。
- 2026-11-13 20:07 用户告知 `setting.ahk` 当日 19:24 已更新：**MPV 只读设置文件、退出不回写，故配置更改只给状态栏提示「重启 MPV 后生效」、不关闭播放器**（实测 1270–1274、1789–1795；`mplayerl:` 仍用不含 `mpv.exe` 的 `allplayer`）；「MPV 默认设置 → 备份文件」**放到最后做**。据此新增阶段 ⑨ 并同步第 0 / 4 / 5 / 6 / 7 节。顺带校正过时事实：脚本里已无 `MpvWriteDefaultConf()`，恢复默认改为 `MpvRestoreDefaults:`（1305）从 `mpv_default.conf` / `input_default.conf` / `menu_default.conf` / `hdr-autoswitch_default.conf` 四个模板整文件覆盖——**会连 `input.conf` 一起冲掉**，旧文档「恢复默认不会冲掉快捷键 / 新 EXE 是 input.conf 唯一写者」两句已改写；桌面导出/导入备份目前不含 MPV。模板在源目录里不存在，归阶段 ⑨ 统一生成。
- 2026-11-13 20:14–20:22 **阶段 ① 完成并交付**：在 `<项目根>/MpvSettings//` 建 WPF 项目（`net10.0-windows` / SDK 10.0.404 / `global.json` 锁版本），实现 6.2 所列文件；5 页 + 关于页静态骨架，含完整暗色主题、卡片、三段式行、开关、下拉、数字步进器、38 行快捷键清单。控件全是静态出厂默认值，不接读写逻辑。`dotnet build -c Release` 0 警告 0 错误；单文件发布 `publish\MpvSettings.exe` 210 KB；真实窗口逐页点击实测导航与渲染通过（3 张截屏）。规格开头、第 6 节阶段 ① 描述、修订记录同步更新，新增 6.2 节。
- 2026-11-13 20:37–20:4x **阶段 ① 审阅修订**（用户看完截屏提两处，另给 BassMax 源码目录 `<工作目录>/Claw`）：① 音效 `Drive` 上限由 2.0 改为 **1.00**（源码 `BACKUP.md` 参数表确认 0.00~1.00），顺带把骨架里 `Bass/Treble` 误写的 0~12 校正为 **−12~+12**；② 全部数值控件由加减号步进器换成 **滑块 + 数值框**（鼠标拖动 / 点击轨道粗调，方向键按步长精调、PageUp/Down 大步长、Home/End 端点），新建 `ValueSlider` + `DarkSlider` 样式、删除 `NumericStepper.*`；③ 音效预设由裸数字改为 **6 个文字项** `标准乐 / 古典乐 / 俱乐部 / 流行乐 / 爵士乐 / 摇滚乐`（默认 4 爵士乐），名称取自 `bassmax_core.hpp` 的 `presets()`、短说明取自 `bassmax_chrome_ext\common.js`，下拉顺序即写盘值 0~5；④ 页 E「窗口适配 90%」正名为 **「窗口自动适配」**（对齐其它播放器的「自动」），90% 降为可调默认值 → 页 A 新增「窗口自动适配」卡片 + 比例滑块（默认 90%，50~100 步长 1）。实测滑块链路 `1.00 → 0.30（鼠标）→ 0.45（方向键 ×3）→ 0.55（PageUp）`，并修掉「点击轨道不转移键盘焦点」导致方向键失效的问题。重新编译 0 警告 0 错误、重新发布单文件 EXE，新增 6.3 节。**新遗留**：`fit90.lua` 硬编码 `0.9` 且与「一行不改」边界冲突，阶段 ⑦ 需定夺比例如何生效，UI 目前只是占位。
- 2026-11-13 20:51–20:53 **侧栏图标配色修复**：用户指出未选中导航图标是黑色、深色背景下看不见。根因是 `NavRadio` 样式缺 `Foreground` 默认 Setter，图标 `Path` 绑定到 `RadioButton.Foreground` 后回退黑色；选中态因触发器设紫色而正常。已在 `Theme.xaml` 补三态：默认 `SubTextBrush #C0C5E2`（浅白）、悬停 `TextBrush #F3F3FF`（纯白）、选中 `AccentBrush`（紫），并规定触发器顺序保证选中优先。只改 1 个样式文件，图标 Path 一行未动。build 0 警告 0 错误，真实窗口三态 + 改前改后对比截屏通过（含底部「关于」图标），重新发布单文件 EXE；规格新增 6.4 节。
- 2026-11-13 20:56–21:1x **阶段 ② 完成**：读盘 + 反推 + 搜索 / 重置 + 草稿模型，仍零写盘。新增 `Services\ConfigLocator.cs`（App Paths 定位、不硬编码路径、可用性判定）、`Models\ConfigSnapshot.cs`（ConfFile 逐行解析并保留原文 / BOM / CRLF 供阶段 ③ diff）、`Models\SettingsDraft.cs`（45 属性 + 全部反推规则 + ResetAll/ResetPage）、`Models\Converters.cs`；5 页 XAML 改 TwoWay 绑定，MainWindow 接搜索过滤 / 重置 / 配置源状态行 / 不可用遮罩，App 改手动建窗以支持 `--dump-only`。验证：`_ref\stage2_verify.py` 对基线与变异配置逐项断言 45 个属性全通过、两组配置目录哈希前后不变；`_ref\stage2_ui_verify.py` 真实窗口截屏验证自定义态置灰、组合反推、互斥置灰、搜索三态、重置、不可用遮罩。修掉两个实测缺陷：`--config-dir` 纯配置副本误判「未安装 MPV」（exe 候选改为始终含 App Paths 安装根目录）、切页滚动位置串页（`ScrollToTop`）。另补 `DarkTextBox` 禁用态视觉反馈。build 0 警告 0 错误、版本 `0.2.0-stage2`、重新 publish 单文件 EXE，规格新增 6.5 节。
- 2026-11-13 21:2x–21:3x **阶段 ③ 完成**：草稿 diff 写盘 + 联动校验，首次真正落盘，范围严格限定 `mpv.conf` 与 `script-opts\hdr-autoswitch.conf`。新增 `Models\WritePlan.cs`（计划 / 结果 / DraftBaseline / DraftPatch）、`Services\WritePlanBuilder.cs`（联动组表 + 自定义态保护 + 索引 / 路径校验）、`Services\ConfWriter.cs`（写前重读、逐行替换、原子替换、事务回滚）、`ApplyResultWindow.xaml(.cs)`（结果对话框）；确定按钮启用，成功才关窗，校验被拦留在窗口可重试。实现要点：diff 基准是「打开时的草稿」，同名键多行同步替换，行内备注保留，缺键追加，UTF-8 无 BOM + 原换行风格，文本未变连临时文件都不建。明确不写 ④ 注册表 / ⑤ 封面 conf + include / ⑥ input.conf / ⑦ MyMPC.conf 与 Lua，用户改了这些控件时结果窗口首屏提示条列出「本次未写入」，避免误以为生效。验证：`_ref\stage3_verify.py` 10 组 70+ 断言用 `bin\Release` 与 `publish` 两个 EXE 各跑一遍全通过（含写前重读外部改盘、失败注入回滚哈希一致、生产目录 58 文件哈希不变）；`_ref\stage3_ui_verify.py` 真实窗口三组截屏。build 0 警告 0 错误、版本 `0.3.0-stage3`、publish 单文件 EXE 278,756 字节，规格新增 6.6 节。
- 2026-11-13 22:0x **HDR 自动切换语义修正**（用户实测 A/B/C 三组均不复现「闪一下」，确认上轮「HDR 切换导致失焦」为误判；用户明确给出原意）：旧的 `sdr_passthrough=yes` 让 SDR 片源也去开系统 HDR，属错误语义，已废弃。新准则：显示器 HDR 常开 → 脚本完全静默、任何片源原样输出；显示器 SDR + SDR 片源 → 不介入；显示器 SDR + HDR 片源 → 自动开系统 HDR + 切 PQ 输出并调 HDR 映射，播完自动关回 SDR。改动：`hdr-autoswitch.conf` 改 `sdr_passthrough=no`；`hdr-autoswitch.lua` 默认值 `false` + `evaluate()` 开头新增 `system_hdr_is_on()` 短路（只回读、不调任何 DisplayConfig setter、不改输出属性）；生产 `portable_config` 与 `12.mpv` 模板两份 conf / lua 同步；`setting.ahk` 的「直通显示器」按钮与状态栏提示、`MpvSettings` 的 `SettingsDraft` 反推 / `WritePlanBuilder.HdrModes` / `VideoView` 说明、本文档联动表四处映射统一改为 `no`。`sdr_passthrough` 键保留，仅为后期 SDR→HDR 映射留口子。副作用：系统 HDR 常开时 F7 与 HDR 模式下拉不改变输出，人工对比用 F5。另已查证 mpv 官方手册 `--focus-on` 标注 **X11/macOS only**，Windows 无效，勿按网上说法加 `focus-on=all`。
- 2026-11-13 22:19 **「闪一下后失焦」结案**（用户实测：双击媒体文件启动，修正后**不闪、焦点正确**）。真正成因是 `hdr-autoswitch.lua` 对 SDR 片源仍走 `apply_pq_output()` 分支（`sdr_passthrough=yes` 让 `want_hdr` 恒为真），加载后 `switch_delay=0.4` 改 `d3d11-output-csp` / `target-trc` 等输出属性触发 VO / swapchain 重配 → 窗口闪一下并把焦点交还原前台窗口；与 `setting.ahk` / `mplayerl.exe` 启动链、Windows 前台锁定**无关**（双击文件时设置窗口本就不在前台，修正前照样复现）。修正后 `system_hdr_is_on()` 短路 + `sdr_passthrough=no`，SDR 片源不再进入 PQ 分支。**结论：以后遇到「mpv 窗口闪一下 / 抢不到焦点」，先查是否有脚本在 loadfile 后改输出属性，不要去改启动链或加 `ontop`。**
- 2026-11-13 22:24–22:3x **阶段 ④ 完成并交付**（音效链路双写）：用户先确认「音效调节是唯一实时的吧」—— 正确，`af_bassmax.c` 每 200 ms 从注册表重取参数，托盘同样 200 ms，所以 `Bass/Treble/Drive/Preset/Enabled` 实时；`af` 挂载与其余配置项都要重启 mpv。据此在页 C 加「生效时机」只读行、结果窗底部写明。落地内容：`af` 按滤镜链增删 `bassmax`（保留 `lavfi=[...]`、关时写空值行、基于写前重读合并）；`HKCU\Software\BassMax` 六键按 `SaveTo` 官方格式写 REG_SZ，缺项自动补齐，DWORD 只在整数时保留、带小数转 SZ 保精度，按数值比较避免 `"2"` 与 `"2.0"` 来回重写；注册表先写、文件后写，任一失败整体回滚。新增 `BassMaxStore.cs` / `AfChain.cs`，改 `WritePlan` / `WritePlanBuilder` / `ConfWriter` / `ConfigLocator` / `AudioView` / `AboutView`，版本 `0.4.0-stage4`，publish **288,484 字节**。验证：`stage4_verify.py` 13 组 60+ 断言（`bin\Release` 与 `publish` 各一遍）、`stage4_ui_verify.py` 三组真实窗口截屏、`stage3_verify.py` 回归全过；**全程用环境变量 `MPVSETTINGS_BASSMAX_KEY` 指向测试键，生产 `HKCU\Software\BassMax` 六键与生产配置目录哈希前后完全一致**；对生产配置 `--plan-only` 输出 `Files: []` / `Registry: 0`。规格新增 6.7 节。
- 2026-11-13 22:45 **补「应用」按钮**：用户要求「确定」不再独占，加一个不退出程序的应用。底部改为 `应用 · 确定 · 取消`：应用 = 只写盘、留在窗口并刷新草稿基线（写第二次不会重复写同样的键），确定 = 写盘后关闭，取消 = 不写关闭。结果窗底部按入口区分「继续编辑 / 返回修改」。规格新增 6.7.1。
- 2026-11-13 22:55 **外观收尾**：① 去掉窗口顶部约 7 px 的 `#4A5459` 系统细条——`WindowStyle=None` + `CanResize` 时 WPF 保留的非客户区，挂 `WindowChrome(GlassFrameThickness=0)` 解决，客户区恢复精确 `1000×680`；副作用是右侧 6 px 拉伸带吃掉滚动条命中，`PageScroll` 加 `Margin="0,0,8,0"` 让开。② 滚动条由系统 17 px 浅色带箭头改为 10 px 无箭头、轨道透明、圆角滑块三态。规格新增 6.7.2。
- 2026-11-13 23:0x **默认配色改为自有墨绿系**（`0.4.3-stage4`）：用户调了一版绿色，要求避免「一眼就是照抄 FrankyRenderer」。用 Pillow 对参考图逐像素采样定位九种主色（`#131F11` / `#1B2918` / `#243723` / `#2E462E` / `#435A40` / `#1B2C19` / `#A3F6BE` / `#37543E` / `#4B6444`），文字改纯白 + 浅绿灰；顺带把 `MainWindow` / `ApplyResultWindow` / 滚动条里散落的 9 处字面色抽成语义画刷（`Warn*` / `Danger*` / `ScrollThumb*` / `OverlayBrush`），全工程 XAML 只剩 `Theme.xaml` 顶部 20 行有十六进制。3.2 节整节重写为墨绿表。真实窗口逐页截屏与参考图同坐标比对五处主色完全一致；回归用临时配置副本 + `Software\BassMaxColorTest`，生产配置哈希与 `HKCU\Software\BassMax` 六键前后不变。规格新增 6.8。
- 2026-11-13 23:2x **配色收口补丁**（`0.4.4-stage4`）：用户截图指出快捷键页状态色未改——根因是那三个画刷在 `Models\ShortcutData.cs` 里用 `Color.FromRgb(...)` 写的，不含 `#`，上一轮 `grep '#hex'` 漏掉。改为 `ShortcutRow` 不再带画刷、由 `ShortcutsView.xaml` 的 `DataTrigger` 按 `State` 从主题取色（新增 `MutedTextBrush #7F9285` 用于未绑定）。**教训：查漏改颜色必须同时扫 `FromRgb` / `FromArgb` / `new SolidColorBrush` / `Brushes.`，不能只扫 XAML 十六进制。** 另把「未写入提示条」的触发位置写成表格（封面 → 阶段⑤、信息窗/适配比例/多实例 → 阶段⑦），并实测截屏存档。规格新增 6.9。
- 2026-11-13 23:3x **提示条改同系浅绿**（`0.4.5-stage4`）：用户给出新效果图，要求替掉琥珀 `#2B2A18`/`#6F6A3C`/`#E7C98A`。按图取色为 `#1A350E` 底 / `#468921` 边 / `#C2FF72` 标题 / `#C9F0E6` 正文（正文新增 `WarnTextBrush`，原来套 `SubTextBrush` 在绿底上发闷）。红色 `DangerBrush` 仍专用于「阻止写入」（校验失败整批不写），不跟着改绿。实测截屏四色与参考图完全一致；生产配置哈希与注册表六键未变。规格新增 6.10。
- 2026-11-13 23:35 **目录归并**：`<用户目录>//WorkBuddy//Claw` 迁到 `<项目根>/bassmax`（改名为 bassmax），`<工作目录>/mplayerl` 迁到 `<项目根>/mplayerl`。跨盘不能原子 rename，走「robocopy /E 复制 → 逐文件 MD5 全量校验 → 删源」；bassmax 834 文件 / 45,531,451 字节、mplayerl 29 文件 / 1,135,478 字节两边完全一致，`git fsck` 通过、`git status --porcelain` 哈希与源一致、remote 仍是 `github.com/<用户名>/BassMax.git`。**GitHub 发布路径无需改**：仓库名本来就是 BassMax，改名只影响本地目录名。唯一需改的是 `bassmax_filter\README.md` 里 `cp` 示例的源路径，已改为 `<项目根>/bassmax/...`。各 `obj\*.obj` / `*.exp` 里仍嵌旧路径，那是编译期写进调试符号的，重新编译即更新，不影响运行。
- 2026-11-14 20:0x **阶段 ⑤ 完成**（`0.5.0-stage5`）：纯音频封面真正写盘。新增 `FilePlan.NewContent`（整文件重写/新建）与 `KeyChange.AppendOnly`（include 只追加不替换，避免冲掉用户自己的 include）；`ConfWriter` 支持新建文件与「新建后回滚即删除」。触发条件改为 `coverChanged || (coverMissing && 有其它改动)`，避免「打开就写」破坏零改动零写盘；卡片新增「配置接入状态」行说明是否已接入。`_ref\stage5_verify.py` 10 组约 50 条断言全过；真机 mpv 验证音频→`downscale-big`、正常视频→`no`、播放列表正确 `Restoring profile`。规格新增 6.11。publish 296,164 字节。
- 2026-11-14 21:3x–21:5x **阶段 ⑥ 完成**（`0.6.1-stage6`）：快捷键页真正写 `input.conf`，首次处理「非 key=value」格式。三态语义落地为「默认不写行 / 自定义写一行 / 清空 = 删行恢复默认」；`ShortcutKeys` 做命令归一化（首词小写、数字最短写法、修饰键排序、`#`→`SHARP`），避免把内置默认 `1.0` 与盘上 `0.5` 判成同一键的两条强绑定。`InputConfWriter` 按命令串定位替换、无命中追加、删行合并重复、保持原换行风格（LF）；注释 / `[section]` / 键序列 / 分号多命令一律不解析、逐字保留。冲突分三级：强×强 `Blockers`、强覆盖弱默认 `Warnings`，并限定「只有本次真动过冲突行才阻断」——否则生产里本包重排过的 `Alt+1`/`Alt+2` 会拖住所有写入。补上写盘契约第 9 条只做了半句的部分：页面底部新增「其他绑定（只读 · N 行）」卡片，展示盘上不属于 38 行的绑定，无编辑入口、无写盘路径，没有这类行时整张卡片不显示。验证：`_ref\stage6_verify.py` 13 组 57 条断言（`bin\Release` 与 `publish` 各一遍）、`_ref\stage6_ui_verify.py` 真实窗口五组（录键写盘 / ✕ 删行 / 冲突拦截 / E5 未绑定 / 其他绑定只读），生产 81 文件哈希与 `HKCU\Software\BassMax` 六键前后不变。build 0 警告 0 错误，publish **320,740 字节**（+24,576）。规格新增 6.12；页 E 与第 4 节第 9 条同步补注。
- 2026-11-14 21:5x–22:x **阶段 ⑦ 完成**（`0.7.0-stage7`）：页 A 的三张卡片（无封面信息窗 / 窗口自动适配 / 播放行为）接到盘上，并修掉「单实例转发后窗口回 960x540」这个挂了一天的未结项。用户当日明确两项前提：「`fit90.lua` 硬编码 0.9 要改掉，我的原意是自动、90% 只是默认值」，以及「`MyMPC\MediaPlayer\mpv` 下所有文件都还不是最终版，**以本设置文件为准**」——据此破例修改既有脚本 `fit90.lua`，比例与自动开关外置到 `script-opts\fit90.conf`（`ratio` / `auto`，出厂 90 / yes），自动模式下每次 `file-loaded` 后 0.1s 静默应用，纯音频（`dwidth<=0`）/ 封面图（`current-tracks/video/image`）/ 全屏三种情况跳过。窗口 bug 的根因是两条事实叠加：① **纯音频在 mpv 原生行为下根本不创建窗口**（IPC 能连、`EnumWindows` 找不到窗口、`osd-dimensions` 为 0x0），profile 必须写 `force-window=yes`；② `profile-cond` 里带 `audio-params ~= nil` 会在 `loadfile replace` 期间抖动 → `Restoring profile` 把 `force-window` / `geometry` 一起撤销 → 窗口销毁重建；改为只判 `current-tracks/video == nil` 后稳定。信息窗 `audio-info.conf` 222 B / 7 行 CRLF，`MyMPC.conf` 137 B / 4 行，`fit90.conf` 100 B / 3 行，`MyMPC_ui.lua` 11,238 B / 282 行 LF（`EmbeddedResource` 嵌进 EXE），`mpv.conf` 只加 `include=~~/audio-info.conf` 与 `input-ipc-server=MyMPC_mpv` 两行。生产部署**由 EXE 自己写**（两趟 `--apply-only --config-dir=<生产>`，92 触发接线、再改回 90），改前整目录备份 `portable_config.bak_stage7_20261114_220403`。验证四个脚本全过：`stage7_verify.py` 12 组 56 条、`stage7_mpv_probe.py` 7 组 16 条、`stage7_prod_probe.py`（生产配置）9 组 25 条（含「把 `fit90.conf` 改成 70 → 窗口变 1008 高」证明比例真从文件读）、`stage7_ui_verify.py` 真实窗口 7 组 29 条。新踩四个坑：测试视频轨带 `image` 标记、纯音频 `osd-dimensions` 为 0x0、`mpv.conf` 是 CRLF 导致 `replace("...\n")` 摘行失败造成假通过、生产落盘后 `fresh()` 夹具复制生产文件使「首次接入」变成「已接入」的假通过。build 0 警告 0 错误，publish **339,172 字节**（+18,432）。规格新增 6.13，并在 1.4 / 1.6 / 6.3 / 第 7 节尾部补「已结案」更正；阶段 ①–⑦ 全部完成，下一步阶段 ⑧。
- 2026-11-14 22:2x–22:3x **阶段 ⑦ 语义修正**（`0.7.1-stage7`）：用户指出「只有 `Alt+4` 按屏幕百分比，按一下就把窗口设成屏幕的 N%；双击视频默认是视频分辨率的 100%；`Alt+` ` 反引号 / `Alt+1` / `Alt+2` / `Alt+3` 是视频分辨率的 25% / 50% / 100% / 200%」。据此撤掉上一版加的 `auto` 与 `file-loaded` 自动应用——它会把每次打开的文件都拉到屏高 90%，正好推翻「默认 100%」。`fit90.lua` 重写为 61 行、只响应 `script-message mpv-fit90`；`fit90.conf` 从 100 B / 3 行（`ratio` + `auto`）变成 163 B / 3 行（两行注释 + `ratio`），旧的 `auto=yes` 由 EXE 整文件重写清掉（`stage7_verify` 2.13 断言）。封面图当时不再跳过按键（**22:4x 已改回跳过**，见修订记录下一条），只保留「无画面」跳过。UI：卡片改名「Alt+4 · 按屏幕缩放」，第二行只读说明列出五个键的基准；页 E 四行标题补「（视频分辨率）」；关于页 / 页脚 / 页面副标题同步。真机：同一个 `out.mkv`（320x240）默认 **160 高**（视频 100%），按 `Alt+4` 变 **1296**（屏高 90%）；把 `fit90.conf` 改成 70 后按 `Alt+4` 变 **1008**（70%）。验证四套全过：`stage7_verify` 12 组 57 条、`stage7_mpv_probe` 6 组 19 条、`stage7_prod_probe` 9 组 27 条、`stage7_ui_verify` 7 组 29 条（`bin\Release` 与 `publish` 各一遍）。publish **339,172 字节**；安装包模板 `12.mpv\portable_config\scripts\fit90.lua` 已同步新版。
- 2026-11-14 22:4x **阶段 ⑦ 再修正：封面不参与 `Alt+4`**（仍 `0.7.1-stage7`）：用户贴实测截图指出「封面并没有变大」。原因是 `cover-art.conf` 的 `video-unscaled=downscale-big` **只缩不放**，`Alt+4` 只把窗口撑到 1296，封面仍是 800x600，四周一圈黑边——正是「无意义」的定义。`fit90.lua` 恢复 `current-tracks/video/image == true` 跳过（73 行 / 2,932 B），提示「Alt+4 不适用于封面：封面按自身尺寸显示」；UI 加第三行只读说明「Alt+4 不生效的情况」（纯音频信息窗、有封面音频）；`stage7_mpv_probe` 的 `D` 组与 `stage7_prod_probe` 的 `P5` 断言从「窗口高 ≈ 屏高 90%」改成「按键前后窗口尺寸完全相等」（800x600）。验证：逻辑 57 条 / 隔离 19 条 / 生产 28 条 / UI 29 条全过；publish **339,684 字节**；模板同步 2,932 B。**教训：只量窗口尺寸就宣布通过，没看画面内容——量窗口 ≠ 量效果。**
- 2026-11-14 22:5x **阶段 ⑦ 补丁：单实例转发后窗口必须回到前台**（`0.7.2-stage7`）：用户报「第二次双击媒体文件，窗口大小正确了，但焦点还在后台（首次是在前台）」。根因是 **Windows 前台锁定**：`SetForegroundWindow` 只允许当前前台进程、或由前台进程拉起的进程调用，其余被静默拒绝。链路里两次都撞上——第二实例在后台起窗被拒，第一实例只是被动换了个文件、mpv 本身不会抢焦点。修法三件事：① 第二实例转发前 `AllowSetForegroundWindow(宿主 pid)` 把前台权限转交过去；② 转发方式从直接 `loadfile` 改为 `script-message MyMPC-ui-forward`，让宿主知道「这次该抢前台」；③ 宿主收到后 `window-minimized=false` → `loadfile replace` → 三级降级抢焦点（`SetForegroundWindow` → `AttachThreadInput+重试` → `keybd_event(Alt)` 兜底），并在 `file-loaded` 里用新句柄补一次。窗口句柄直接取 `window-id`（Windows 上就是 HWND，实测 0x160628 与 `EnumWindows` 一致），最小化时 `ShowWindow(SW_RESTORE)`。UI：「播放行为」卡片说明补「并把它切到前台（最小化时自动恢复）」与「转发时把前台权限一并转交」。真机 `_ref\stage7_focus_probe.py` 5 组 **16 条**全过（后台转发回前台、连做两次、**最小化时转发自动恢复**），日志三次均为 `focus -> direct`、未降级到有副作用的 `Alt` 模拟。四套回归全过（57 / 19 / 28 / 29）；`stage7_ui_verify` 里写死「Lua 11238 字节」的断言改成与嵌入资源逐字节比对。publish **344,804 字节**（+5,632）。**教训：窗口尺寸正确 ≠ 交互正确。「转发」只换了播放内容，前台归属是独立的一件事，必须显式处理。** 另记一坑：mpv 的 `--log-file` 只认 `--log-file=xxx`，空格分隔会 `Fatal error` 直接退出。
- 2026-11-15 00:0x–00:4x **阶段 ⑧ 完成**（`0.8.1-stage8`）：用户离开前一次给四件事，全部落地。① **改名「MPV 简单设置」**——只有 5 页常用项、没有高级页，叫「MPV 设置」名不副实；改的是标题栏 / 关于页 / 不可用遮罩 / `setting.ahk` 按钮文案，**文件名仍叫 `MpvSettings.exe`**（`setting.ahk` 有 4 个候选路径硬指这个名字，改名要连带改安装包脚本，收益为零）。② **三处 UI 修正**：顶部工具行三列 `Auto/*/Auto` 被右侧按钮组撑宽、最右「重置页面」只丢一条右边框 → 改两列 `* / Auto` + 配置路径 `MaxWidth=240 TextTrimming`；页脚两个 `StackPanel` 同格叠加导致换行文字钻到「确定」底下 → 拆两列；窗口四角直角 → 新增 `Services\WindowCorners.cs`，`DWMWA_WINDOW_CORNER_PREFERENCE=33` + `DWMWCP_ROUND=2`（结果对话框 `3`），`Window_SourceInitialized` 时设，Win10 静默失败、最大化时 DWM 自动取消。**WindowChrome 的 `CornerRadius` 不裁窗口外形，别再拿它当圆角用。** ③ **右键「选项」**：`menu.conf` 第 150 行插「选项(&O)⇥script-message open-settings」→ `scripts\mpv-settings.lua`（2,178 B，`expand-path` 解析 `~~/` + 三候选路径 + 传 `--config-dir`）；配色只能走 OSD 菜单，原生 Win32 弹出菜单的颜色由系统主题决定、mpv 改不了——`load-context-menu=yes` + `script-opts\context_menu.conf`（792 B）+ `mpv.conf` 四条 `osd-*`（41–52 行）。④ **主按钮对 MPV 的分支**：`setting.ahk` 抽出 `MpvOpenSettingsProgram()`，`mplayerl:` 里判 `InStr(selectedPath, "mpv")` 就开设置程序且不再 `Run, mplayerl.exe`，MPV 页原按钮保留、两处共用同一函数。验证：`stage8b_open_settings.py` 12 条（含 mpv 日志里 `args="--config-dir=..."`）、`stage8b_ahk_probe.py` 7 条（`BM_CLICK` 点击，断言拉起 `MpvSettings.exe` 且**没有** `mplayerl.exe`）、`stage8b_corner_probe.py` 新旧 `corner_pref` 对照 **0 → 2**、渲染图 3 张（重置按钮右边距 37px / 页脚 2 行 / 圆角）、写盘回归（`loop-file` `inf → no`，`KeysWritten=0`，新键未丢）。publish 348,388 → **350,948 字节**。**新踩的坑：远程 / 锁屏会话里 `ImageGrab` / `PrintWindow` / `BitBlt` 全都拿不到 WPF 窗口内容（整窗位置只有桌面壁纸，记事本同样），改 app 内 `RenderTargetBitmap` + `--shot=` 钩子，且必须等首帧（400ms `DispatcherTimer`；`Loaded` 当场与 `ContextIdle` 都只拿到纯白/半成品）。** 另两个小坑：旧版窗口标题是「MPV 设置」没有「简单」，新旧对照要各按各的标题探一次；`wmic` 被安全策略拦，进程断言改 `tasklist //FI`。部署：生产 `portable_config`（备份 `portable_config.bak_stage8b_20261115_000233`）、`12.mpv` 模板、两处 `setting.exe`（1,427,456 B）；顺手改正 `setting.ahk` 里「不用传参数」的过期注释——**注释和实现对不上比没注释更坏**；改完重编一次确认 md5 与部署版**逐字节一致**（改注释不改二进制），故两处 `setting.exe` 无需重新部署。顺带记录编译参数：`/base` 不是 `/bin`、`/icon` 不能漏（漏了少 30,208 字节）。规格新增 6.14；阶段 ①–⑧ 全部完成，只剩阶段 ⑨（默认模板与备份，且必须把 `context_menu.conf` / `mpv-settings.lua` 一并纳入，否则「恢复默认」会把本轮 4 条 `osd-*` 与 `load-context-menu` 一起冲掉）。
- 2026-11-15 00:2x–00:5x **阶段 ⑨a：`--default` 恢复默认**（`0.9.0-stage9a`）。起因是用户两问：① 「MPV 右键菜单和窗口大小强绑定吗，能不能像别的播放器那样固定大小」；② 「给设置程序预留 `-default` 命令行，EXE 内部要有默认值，定下所有选项需要哪些文件、能合并的合并，setting.ahk 与安装包直接引用」。**①的答案**（查 `<mpv源码目录>/packages/mpv` 源码 + 实测）：OSD 菜单的坐标系就是窗口，`height = min(内容, osd_h)`，**永远画在视频表面内、超出只翻页**；字号由 `osd-scale-by-window` 决定（`[no-art]` 里是 `no`，所以 180px 音频窗里菜单偏大被裁）。想要「固定大小 + 能伸到窗口外」只有原生菜单这条路——实测 644×180 窗口里弹出的 `#32768` 是 **223×452 的独立顶层窗口**（`mp_win32_menu_show` → `TrackPopupMenuEx`），而 Lua 菜单**一个 Win32 弹窗都不建**；且 `load-context-menu=yes` 时 `context-menu` 命令**仍然走原生**，所以三条路（OSD / 原生 / 按窗口高度混合）都可行，混合本轮未做（观感取舍，等用户点头）。**②的落地**：新增 `Services\DefaultProfile.cs` + `CliDefault.cs`，`DraftBaseline` 加 `ForceAll`/`Force(diskDraft)` —— 恢复默认不抄第二份键表，而是「目标态 = 出厂草稿、基准态 = 盘上现状且全部算改过」复用同一条 diff 链路；键表只此一份，与 UI 同源。默认值覆盖 **11 个文件 + 注册表 6 键**（mpv.conf 最多 24 键 / hdr 5 键 / cover-art / audio-info / MyMPC.conf / fit90.conf / **context_menu.conf 与 mpv-settings.lua 本轮新纳入内嵌资源** / MyMPC_ui.lua / input.conf 38 个受管动作 / menu.conf「选项」行），并把 `From == To` 的空改动滤掉，保证「已经全默认」时零写入。**「能合并的就合并」结论：不能**——四个 `script-opts\*.conf` 是 mpv 按脚本名硬绑定（`fit90.lua` 只读 `fit90.conf`），`cover-art.conf`/`audio-info.conf` 理论上能并进 `mpv.conf` 但会让「关掉信息窗」从摘一行变成按段落增删，不值。CLI 契约：`--default` / `-default` / `/default`，退出码 `0/1/2`，`--keep-registry`、`--dry-run`、`--dump-plan`、`--dump-result`，`AttachConsole` 借父控制台打印明细。`setting.ahk` 的 `MpvRestoreDefaults:` 从「覆盖四个 `*_default.conf` 模板」改成 `RunWait ... --default`——**那四个模板从来就没生成过，旧逻辑一直是空转**；加了**版本闸**（`FileGetVersion` 前两位 < 0.9 直接拦），否则旧 EXE 不认 `--default`、会弹窗口而 `RunWait` 永远等下去；`screenshot-dir` 恢复后原样写回。验证：`stage9_default_verify.py` 约 40 条（生产副本改坏 9 处 → dry-run 零写入 → 全量回默认 → 独占文件与内嵌资源逐字节一致 → 幂等零写入）、`stage9_ahk_default_probe.py` 10 条（真机点「恢复 MPV 默认配置」→ 确认 → `loop-file` 回 inf、`context_menu.conf` 重建 792 B → **生产目录哈希完整还原**）、编译产物冒烟通过。publish 350,948 → **361,190 字节**；`setting.exe` 1,427,456 → **1,427,968 字节**（md5 `5c2c39d…`，两处部署、md5 一致）。**踩坑**：`#Warn` 名称冲突——函数里局部 `mExe` 与新建的脚本级 `mExe` 同名，脚本一启动就卡在警告框（探针误判「语法错误」）；`mDir`/`mCfg` 本来就是全局变量，新代码统一 `mDefXxx` 前缀（这是同类坑第二次，第 2010/2028 行早有注释）；`csproj` 注释里不能出现 `--`（`MSB4025`）；原生菜单 `PtInRect` 不过就静默不弹，探针不先 `SetCursorPos` 是假阴性。另修掉 `stage8b_ahk_probe.py` 的**测试前提失效**：它假设「主播放器=MPV」，实测这台机器存的是 MPC-HC，于是「点主按钮应拉起设置程序」必然失败——现在探针自己点 MPV 单选、测完点回原选项。规格新增 6.15 与 6.14.1；阶段 ⑨ 只剩备份 / 导出导入。
- 2026-11-15 01:0x–01:2x **阶段 ⑨b：拖动窗口不锁比例 + 修颜色引号 bug**（`0.9.1-stage9b`）。用户问「手动拖动 MPV 边框改变大小时，不要保持窗口比例、但要保持视频比例，能做到吗」，并要求进设置程序且设为默认。**能做到，一个键**：`keepaspect-window=no`。关键是把两件事分清楚——`keepaspect-window` 管**窗口矩形**（`video/out/w32_common.c:1258` 的 `handle_sizing()`，两个开关的与为假时直接 `return`，让窗口按用户拖的尺寸走），`keepaspect` 管**画面渲染比例**（永远按原比例画、不足补黑边）；关掉前者不会把画面拉歪，只会出现黑边，这正是用户要的。实测（直接 `SendMessageW(WM_SIZING)` 打 `handle_sizing`）：`yes` 时请求 900×360 被改写成 900×506、1000×400 被改成 1000×562；`no` 时两者原样保留。画面侧 IPC 读数：窗口 900×360 时 `video-params/aspect` 仍是 1.777778，`osd-dimensions` 给出 `ml=130 mr=130`，即左右各 130px 黑边、`(900-260)/360=1.778`。落到设置程序：画面页新增「窗口」卡片一项开关，勾上=锁、不勾=不锁，**默认不勾**（键缺失时读 `no`，不跟 mpv 原生的 `yes` 走，这才是「设为默认值」）。**顺手揪出一个真 bug**：查选项时把 `mpv.conf` 交给 `mpv --list-options`，冒出三条 `option requires parameter`。回查 `options/parse_configfile.c:117`——未加引号的值从第一个 `#` 处截断（`bstrchr(line, '#')`），所以 `osd-color=#FFF0F7F0` 的值是空串、整行被丢弃、回落默认黑。**结论：6.14 加的四条 OSD 配色一条都没生效**，当时看到的「配色统一」其实只靠 `context_menu.conf` 里脚本自己解析的那几个键。修法三处：生产 `mpv.conf` 四条颜色加引号（另有三行 GBK 误写导致中文被 `?` 截断、还把 `osd-color` 行吞进注释的坏行，一并重写）、`12.mpv` 模板同步、`DefaultProfile.cs` 的 `Appearance` 表值改成带引号形式并注释缘由。`ConfWriter.InlineComment()` 本就要求 `#` 前是空格才算注释，加引号不会被误判；`ConfFile.Get()` 不剥引号，两边都带引号故比较一致。验证：`stage9b_keepaspect.py` 6 条（两组对照）、`stage9b_aspect_probe.py` 3 条（比例未被拉歪）、`stage9b_cfg_effective.py` 7 条（生产七项实际生效值，用 IPC 读 `options/*` 而非 `--list-options`，后者只显示 mpv 出厂默认）、`stage9b_default_verify.py` 15 条（改坏 5 键 → dry-run 零写入 → 全量恢复且颜色带引号 → 二次零写入 → mpv 读到正确值）、`stage9_ahk_default_probe.py` 10 条（真机点恢复默认，**生产目录哈希完整还原**——哈希一致反证 `--default` 写出的格式与手改的逐字节相同）。publish 361,190 → **361,702 字节**（+512，md5 `9a4bb029…`，生产与 `12.mpv` 一致）。**踩坑**：`--no-config` 会把配置文件整个禁掉，用它测 conf 解析必然「零报错」，是个假阴性陷阱；`--list-options` 的 `(default: …)` 是 mpv 出厂默认，不反映配置加载后的值，要验生效值只能读 IPC 的 `options/<name>` 属性。另：右键菜单已按用户决定「改源码、与 bassmax 一起重编译」，技术路线与源码落点写进 `<mpv源码目录>/BUILD-NOTES.md` 第 10 节，**代码未动，改天实施**。
- 2026-11-15 01:2x–01:4x **同轮追加：修 `setting.ahk` 的 GBK 乱码（本轮最严重的问题）**。补完颜色引号后重跑真机「恢复默认」探针，`mpv.conf` 的中文注释又成乱码（`鍙抽敭鑿滃崟鍜?OSD…`），且 4 行注释 + `osd-color=` 被并成一行。根因在 `00.Core\setting.ahk`：`FileRead, mTxt, %mFile%` 省略编码 = 系统 ANSI（中文系统 GBK），读 UTF-8 文件时字节被按 GBK 解码（多数组合在 GBK 里也合法、不报错），得到乱码字符串后又以 `FileAppend ..., UTF-8-RAW` 写回，中文永久损坏；解码错位还吃掉换行导致行合并。**命中路径不止恢复默认**：MPV 页任一切换开关/下拉都走 `MpvSet → MpvWriteKey`，读键反推走 `MpvReadKey`——也就是只要 `mpv.conf` 有中文，**用户改任何一项 MPV 设置都会毁掉整个文件的注释**。修法：两处 `FileRead` 显式 `UTF-8-RAW`（不剥 BOM、不转换，与写侧对称，字节级保真）。为什么之前没暴露：`mpv.conf` 原本全 ASCII，GBK 与 UTF-8 对 ASCII 完全一致，6.14 开始写中文注释才把这个潜伏假设逼出来。验证：`stage9_ahk_default_probe.py` 新增 D5–D9 五条（合法 UTF-8 / 中文注释完好 / 无 GBK 特征串 / `keepaspect-window` 仍在 / `load-context-menu` 仍在），实跑 15 条全过、生产目录哈希完整还原。`setting.exe` 重编部署，1,427,968 字节不变，md5 `5098f7b9…`（`00.Core` 与安装根一致），备份 `_bak_20261115_0106`。**教训**：AHK v1 的 `FileRead`/`FileAppend` 默认编码是 ANSI，凡涉及非 ASCII 内容必须显式指定 UTF-8-RAW，读和写要成对。
- 2026-11-15 21:4x–22:0x **阶段 ⑩a：导入导出方案定稿（设计待拍板）+ `af=bassmax` 常驻 + 左下角「打开 MPV 播放器」**（`0.9.2-stage10a`）。用户三问：① 「MPV 配置是多文件的，导入导出用压缩包？或者支持自定义文件夹直接导出到这个自定义、也方便导入？」② 「MPV 有专属音效，导入导出需要强制写 `af=bassmax`，音效开关应该控制注册表而不是删除这句，包括简单设置 EXE 里面的逻辑和 `setting.ahk`」③ 追加「给 EXE 左下角加一个『打开 MPV 播放器』按钮（关闭已开播放器再重新打开的方式）」，随后又要求「按钮边框顶着文字，稍微加一点宽度」。**① 的结论：自定义文件夹，不做压缩包**——AHK v1 没有原生 zip，只能调 `Compress-Archive` cmdlet（起外壳进程、杀软误报面）或外挂 7z.exe；中文路径在 PS 5.1 与 .NET `ZipFile` 的编码处理不一致；压缩包不可查 / 不可单文件改 / 不可 diff，导入还多一步解压。固定文件夹路径可脚本化，导出导入各一行命令。方案写进 **6.17**：固定路径覆盖式导出 + `manifest.json`（格式版本 / 时间 / EXE 版本 / 受管键值 / 注册表六键 / 每文件 SHA256，校验不过拒导）；**三层分类，导入绝不整目录覆盖**——A 共享文件（`mpv.conf` / `input.conf` / `menu.conf`）按 diff 只写受管键，B 独占文件（8 个）整文件写回，C 本机 / 第三方（`stats.lua` 等、`cache\`、`*.bak_*`）默认不带，D 注册表六键走 `RegistryWriter`。A 类不能整覆盖的三个实证：`screenshot-dir` 是本机路径、自绑键 `Alt+9 script-message foo` 不属于受管 38 个动作、`menu.conf` 只插了「选项」一行。**架构上不另立第二份键表**：导出 = `SettingsDraft.LoadFrom` 序列化，导入 = 反序列化后走 `WritePlanBuilder.Build(profile, DraftBaseline.Force(盘上现状), 当前快照)`，与 `--default` 同一条 diff 链路。CLI 设计为 `--export=<dir>` / `--import=<dir>` + `--dry-run` / `--keep-registry` / `--include-extra` / `--no-snapshot`，退出码沿用 `0/1/2`，版本闸另设 0.10 档。**代码未动，等用户拍板 6.17.9 的三点**（固定覆盖要不要留历史 / 第三方脚本带不带 / 注册表六键带不带）。**② 已落地**：`WritePlanBuilder` 删掉按 `AfEnabled` 增删 af 的整段（原来关闭会写空值行 `af=`），换成无条件 `EnsureAfChain(plan, s)`（`Transform` 写前重读 + `AfChain.Merge(chain, true)` 保留用户其它滤镜），`--default` / 普通保存 / 将来的导入自动获得保证；`AudioView` 的「启用音效链路」开关改只读说明行，`BassMaxEnabled` 明确只写 `HKCU\Software\BassMax\Enabled`；`setting.ahk` 新增 `EnsureMpvAf()` 挂在 `MpvSet()`（覆盖 MPV 页所有写盘入口）+ `MpvRestoreDefaults:` / `DEF:` 末尾兜底，**挂在 `MpvSet` 而非 `MpvWriteKey` 是为了避开递归**，变量用 `mAfXxx` 前缀躲 `#Warn` 冲突。查证：`setting.ahk` 的 Button1 本来就只写注册表，这一侧原本就是对的。**③ 已落地**：页脚左栏三行长说明收成「按钮 + 一句提示」横排（页脚固定 68px，原说明挪进 ToolTip / 关于页），`RestartMpv()` 先 `CloseMainWindow()` 让 mpv 正常收尾、1.2s 未退才 `Kill()`，再带 `--idle=yes --force-window=yes --config-dir=<ConfigDir>` 起新窗口——**必须先关干净**，否则 `MyMPC_ui.lua` 的单实例管道会把新实例转发给旧的、自己退出且不重读配置。只关 `mpv.exe`，不动别的播放器。按钮边框贴文字的修法：`GhostButton` 模板的 `ContentPresenter` 没绑 `Padding`，设 `Padding` 无效，改全局样式又会把顶部工具行撑宽（6.14 刚修好「重置页面」被裁），最终只给这个按钮加 `MinWidth="132"`。验证：`stage10_af_verify.py` **19 条**（删 af 行 / 空值行 / 换成 `scaletempo2,bassmax` → `--default` 与关音效开关都不摘 bassmax，中文注释 / 颜色引号 / 两个新键全在，二次 `--default` 字节不变）、`stage10_restart_probe.py` **8 条**（WPF 控件无 HWND，`BM_CLICK` 打不到，改用 UIA `InvokePattern` 真机点按钮 → 旧 PID 消失 / 新 PID 唯一 / 三个参数齐）、`stage8b_ahk_probe.py` 全过、`stage9_ahk_default_probe.py` 15 条全过（生产目录哈希完整还原）。**踩坑**：第一轮 af 验证有 4 条假 FAIL，根因是并行跑 `dotnet publish` 时验证脚本正在读同一个 `publish\MpvSettings.exe`，EXE 被覆写读到半成品；串行重跑 19/19 全过——**别在编译的同时跑针对发布版 EXE 的验证**。另一条：规格文档里写 `Compress-Archive` 时带上了外壳名，触发了命令安全检查的误判，改用临时文件写入再插入。产物 publish **363,240 字节**（md5 `f5e1b682…`，生产与 `12.mpv` 一致）、`setting.exe` **1,427,968 字节**（md5 `9992a50d…`，两处一致），备份 `_bak_20261115_2159`。规格新增 6.17，阶段状态改 ①–⑩a，6.16「后续」补上新按钮的用途。
- 2026-11-15 22:1x–22:4x **阶段 ⑩b：导入导出落地 + 页脚对齐 + 主题弹窗**（`0.10.0-stage10b`）。用户拍板 6.17.9 三点（① 只保留一份、不用太复杂；② 第三方脚本默认不带 `--include-extra` 够；③ 注册表六键一起带），追加「导出默认到桌面没问题」「导入导出放在关于里面」，并要求修「打开 MPV 和右边按钮没有对齐」。新增 `Services\BackupService.cs` / `CliBackup.cs` / `NoticeWindow.xaml(.cs)`，关于页加备份卡片，CLI `--export` / `--import` 与 `--default` 并列在建窗前处理；`manifest.json` 记 11 个受管文件 SHA256 + 受管键值 + 38 行快捷键 + 注册表六键，导入先验清单再走 `WritePlanBuilder.Build(profile, Force(盘上现状), 快照)` 这条与 `--default` 同源的 diff 链路，A 类只写受管键、B 类由 EXE 按目标草稿重建、定版资产由 `DefaultProfile.EnsureFixedAssets` 补齐、af 链无条件含 bassmax。**两处用户当场纠正**：第一版把页脚改成两行 Grid 后右侧按钮整行上抬 24px，用户要求「按原先应用按钮的高度设置打开 MPV 按钮的高度」——最终改成左栏横向 StackPanel + 按钮 Center，右侧按钮位置一个像素不动；第一版结果弹窗用系统 `MessageBox`（白底），用户指出「弹窗主题不一致，之前的弹窗不是挺好吗」——新增 `NoticeWindow` 沿用 `ApplyResultWindow` 的墨绿画法，三处系统弹窗全部替换。踩坑：页 E 不能走 `DraftPatch` 的 Shortcuts 分支（`SetKey` 会把显式写出的默认键行误判成删除），改直接 `row.Load`；`RefreshDir` 不能只靠 `Loaded`（`--shot` 渲染在前）。验证：`stage10b_backup_verify.py` 58/58、`stage10b_ui_probe.py` 9/9、`stage10b_ui_export_click.py` 5/5、`stage10_af_verify.py` 19/19、`stage9_default_verify.py`（顺手修掉 6.16 之后过时的颜色断言）与 `stage9b_*` 全过。产物 publish **389,354 字节**（md5 `9b7bc5e3…`），生产与 `12.mpv` 三处一致；`setting.exe` / `setting.ahk` 本轮未改（用户要 GUI 入口、不要复杂化）。规格新增 6.18，6.17 标为已拍板并落地，阶段状态改 ①–⑩b 全部完成。
- 2026-11-15 23:0x–23:2x **阶段 ⑩c：`1.0.0` 定版**（`1.0.0`）。用户四问 + 一条追加：① 确认 `--default` 不会新建 `HKLM\…\App Paths\mplayerl.exe`（EXE 只读，键由 `MyMPC_x64.iss` 171–172 行 `[Registry] main` 创建，安装包当前也没调 `--default`，调用方是 setting.ahk 且带显式 `--config-dir`）；② 按「mplayerl.exe 必装 / MPV 选装」收紧 `ConfigLocator`：有 `Path` 就锚定 `<Path>\MediaPlayer\mpv\portable_config`，只认 `<Path>\MediaPlayer\mpv\mpv.exe`，缺 exe 直接禁用且不回退开发包；③ 临时副本上实跑 `--default --keep-registry` 证明桌面备份就是默认态（11 文件哈希 11/11 全同、输出「未重写文件」），把 11 个受管文件同步进 `12.mpv\portable_config` 并清掉 3 个 EXE .bak、mpv.conf.bak、14 个 shader 缓存（旧件备份 `_bak_20261115_2310`）；④ 定版 `1.0.0`、界面删掉技术栈 / 阶段 / V1 范围 / 后续阶段 / 命令行 / diff 等开发说明，各设置页描述改为用户语言；追加「图标不要这 2 个」→ 新生成墨绿主题 `Assets\MpvSettings.ico`（7 尺寸、纯标准库、PE 资源探针验证已嵌入 EXE），三个窗口共用。九个回归脚本全过（含修 `stage2_verify.py` 的 `FitRatio` / `AfEnabled` 两条过时断言），产物 **416,982 字节** md5 `10217cf2…` 三处一致，旧版备份 `_bak_20261115_2325`。
- 2026-11-15 23:4x–2026-11-16 00:0x **阶段 ⑩d：目录整理 + 安装包接入**（EXE 仍 `1.0.0`，未改代码）。用户要求把这次开发有用的文件汇总进 `<项目根>/MpvSettings`、删掉没用/过期/错误的，并更新 `<安装包脚本>.iss` 接入 MPV 安装逻辑，右键菜单明确留到明天。**整理**：先整份备份到 `_bak_20261115_2345`（19 MB）；工程根目录只留 10 个源码文件 + `Assets/Controls/Models/Services/Themes/Views/publish` + 新建 `_verify`（46 个脚本 + `subtest` 素材）、`_research`（17 份 mpv 源码/手册摘录）、`预览`（3 张 1.0.0 成果图）；清掉工程根 48 张旧预览、打包根 48 张早期截图、133 MB 临时 mpv.exe 副本、一次性文档补丁与可再生 `bin/obj`；27 个脚本里过期的 `bin\Release` EXE 路径统一改到 `publish`，截图输出改到 `预览\`。规划文档保留在打包根目录（全项目共用，不复制漂移副本）。九个回归脚本从新位置串行全过，`dotnet build` 0 警告 0 错误。**ISS**：GBK/CRLF 原样写回，8 处替换逐处断言；`[Components]` 描述改 1.0.0、`[InstallDelete]` 兜底删 `MpvSettings.exe.bak-*` / `mpv.conf.bak_*`、`[Icons]` 加开始菜单「MPV 简单设置」、`[Run]` 加一行 `--default --config-dir=...`（`runasoriginaluser` + `skipifdoesntexist` + `Components: mpv` + `Check: IsMpvSettingsRuntimePresent`）、`[Code]` 新增 .NET 10 桌面运行时探针并修正两处过期注释（模板其实含 screenshot-dir 占位；Inno 6/7 的 `LoadStringsFromFile` 都支持无 BOM UTF-8）。实测 `--default` 对当前模板零写入（退出码 0 / 15 文件哈希全同）；官方安装顺序里 `[Run]` 在 `ssPostInstall` 之前，所以 `WriteMpvScreenshotDir()` 最后写、不会被盖。`Inno Setup 7` 完整编译 267 s 成功，产物 `<发布包目录>/MyMPC_<日期>.exe`（111 MB）；本机 `Inno Setup 6` 是 6.2.2，**原版脚本就因 `SetupArchitecture` 编不过**，出包请用 7。两点遗留：EXE 是框架相关单文件，没装 .NET 10 桌面运行时的机器上 `Check` 会跳过 `--default`（模板本身已是默认态，不影响使用），要全覆盖得改自包含或随包带运行时；`ignoreversion` 让覆盖安装刷新配置是既有行为，已在 `[Files]` 注释写明升级前先导出。右键菜单改源码仍按 `<mpv源码目录>/BUILD-NOTES.md` 第 10 节，本轮未动。
- 2026-11-16 白天–21:4x **mpv 自编译全套完成，P4 与右键菜单一起落地**（本 EXE 之外的另一条链）。自编 `mpv.exe` 定版 **99,624,989 B / md5 `3a7c53d1d144c0c278b8ee9cd303b68b`**（比原版减 33.5 MB / 25%），构建目录 / 生产 / `12.mpv` 四处一致，bassmax 在册。减体积三件套：`-Db_lto=false` + `-ffunction-sections -fdata-sections` + `-Wl,--gc-sections` + 砍 7 个 ffmpeg 编码器；**连锁**：`--screenshot-format` 只剩 jpg/png，`MpvSettings` 与 `setting.ahk` 已同步裁掉 WebP/AVIF。右键菜单改走原生 owner-draw（`load-context-menu=no` 三处一致）。**杜比视界 P4**（`LG Dolby Comparison 4K Demo.ts`, `dvhe.04.06 BL+EL+RPU`）暗 4~5 档的根因是 mpv v0.42 两层新代码：`detect_dovi_split_streams` 漏查 `dv_profile` 把 P4 当 P7 拆虚拟 EL，加上 libplacebo 把带 DOVI side data 的帧无条件标成 4008nit PQ；运行时手段全部无效，最终用两个源码补丁（`demux_lavf.c` 加 profile 判断 + `vd_lavc.c` 按 `dv_profile==4` 剥 side data）解决，**P5 / P7 / FEL / P8 回归全过**。全部命令、补丁、参数、故障速查归档在 `<mpv源码目录>/REBUILD-GUIDE.md`（唯一入口），`BUILD-NOTES.md` 降级为原理档案。
- 2026-11-16 21:4x–22:0x **HDR 真直通（设置层，新增 6.21）**。用户给出 MPCVR 截图与语义：Passthrough 与各色调映射算法在同一个互斥下拉里，默认关闭、关闭+直通时零处理。定性：`pl_tone_map_auto` 只是 `pl_tone_map_spline` 的别名，只有 `clip` 不处理；`tone-mapping` 启动期定死、运行时改不动。规则与 4 处改动落点见 6.21.3 / 6.21.4；`MpvSettings.exe` 重编定版 **425,686 B / md5 `5601f86788e10622d21b1e70a706ee81`** 三处一致，`--default` 夹具验证 `spline → clip`。用户 22:0x 实机确认 P4/P5/P7/P8 全过、真直通不灰、色调档位生效。
- 2026-11-16 22:0x–22:2x **`setting.ahk` 读写链路修复（新增 6.22）**。用户报「HDR 色调修复：简单设置有效、双击 .ahk 无效」，实测根因是 `FileRead, ..., UTF-8-RAW` 在 AHK v1 下第三参数不生效、读直接失败，导致 MPV 页所有按钮空转（11-15 修 GBK 乱码时引入）。两处改用 `FileOpen(..., "r", "UTF-8-RAW")`；夹具验证按钮序列 clip↔spline 全对、配置零膨胀、中文保真。用户明确**双击 .ahk 运行、不需要编译 EXE**；两处 `setting.exe` 于 22:27 被重编（526,336 B / md5 `4db5ad8d…`），以后改回用 EXE 必须重编并核 md5。
- 2026-11-16 22:2x **文档归档**：`REBUILD-GUIDE.md` 新增 §3.0「拉新源码先查 P4 上游是否已修」（修好就不用打那两个补丁，必打补丁只有 bassmax + menu）、§4.3「停用补丁的 4 处改动」、§13.3 条件补丁档案、§16「必须参数速查」，并更新 §0/§1 重编顺序、§11 体积判据、§12 部署与安装包状态。**未办**：`<发布包目录>/MyMPC_<日期>.exe` 仍是 01:25 的旧包（内含旧 mpv.exe / 旧 MpvSettings / spline 默认配置），iss 无需改动，纯重打即可；验收三连里 4K 视频 3 连跑、wav 2 连跑尚未跑（bassmax 已过）。
- 2026-11-16 22:4x–23:0x **模板默认值核对 → 发现并修掉「恢复默认」的 HDR 幽灵键（新增 6.23）**。先用 EXE 自身 `--default` 双向夹具证明 `12.mpv\portable_config` 15 文件与出厂默认值逐字节一致（零写入），再用投毒法列出程序管辖 24+5 键与 15 个不受管键。用户追问「不受管的要不要修」，实测坐实 `target-trc=bt.1886` 残留会让界面显示真直通、实际强制 SDR；用户在 A（只改 .ahk）/ B（改 EXE）中**选 B**：`DefaultProfile.AddHdrFixedKeys()` 把 `target-colorspace-hint` / `target-trc` 纳入恢复默认与导入的定版资产，直通态删行（不写 auto，保住模板逐字节一致），重编 `MpvSettings.exe` **427,222 B / md5 `39679d86b2a68c0197a7358803765762`** 三处部署，`setting.ahk` 一行未动。双向夹具复验 PASS（切到 HDR转SDR 再恢复默认，15 文件逐字节回到模板）。**23:10 顺带重打了安装包**：`<发布包目录>/MyMPC_<日期>.exe` 107,982,458 B（原 115,497,212 B），ISCC 7 编译 235.5 秒 / 退出码 0，`iss` 一字未改；内含新 mpv.exe + 新 MpvSettings + `clip` 默认配置。上面 22:2x 那条「未办：安装包仍是旧包」至此结案。

# MpvSettings 1.0.1 后续修改记录

> **版本背景**：`1.0.0` 已发布。本文记录 2026-11-17 在 1.0.0 基础上追加的改动，按 **1.0.1** 归档。
> 1.0.1 相对 1.0.0 **只新增功能与修正，未改变既有设置项语义**；已发布用户升级后原有配置照常读取。
>
> 记录日期：2026-11-17　｜　归档人：本次收尾
> **更新（当日晚间）**：追加第 4 项「外挂字幕修改后及时重载」，并入 1.0.1（发布前），见 1.4 / 2.4 节。
> **更新（当日 23:0x）**：追加第 5、6 项「全屏切换默认键改 Alt+ENTER」「快捷键键名翻译成中文习惯写法」，并入 1.0.1（发布前），见 1.5 / 1.6 节。

---

## 一、功能改动（1.0.1）

### 1.1 自动加载同名外部音频（默认开，视频自带音轨优先）

- **界面**：音频页新增「外部音频」卡片 →「自动加载同名外部音频」开关，出厂默认 **开**。
- **写盘**：`mpv.conf` 的 `audio-file-auto`：开 = `exact`，关 = `no`；缺键时按出厂默认补 `exact`（`PutOrFill`）。
- **关键坑（实测）**：mpv v0.42 原生 `audio-file-auto=exact` 会把 `aid` 切到**外部**同名音轨，
  与需求的「视频自带优先」相反。实测：内嵌 440Hz（`clip.mkv`）+ 外部 262Hz（`clip.m4a`），
  exact 模式下选中的是外部轨。纯配置做不到内嵌优先。
- **解法**：在 `Assets/MyMPC_ui.lua` 的 `file-loaded` 加 `prefer_internal_audio()`：
  同时存在内嵌与外部音轨时把 `aid` 拨回第一条内嵌轨；只有外部轨时保持 mpv 选择（正常兜底）；
  不动播放途中用户手动切轨，只在换文件瞬间执行一次；0.2s 兜底重判。
- **验证**：生产 mpv 端到端——内嵌轨 `selected=true`、外部轨 `false`、`aid=1`；无内嵌时外部轨正常兜底。

### 1.2 标题栏显示版本号

- MainWindow.xaml 的「· 常用项」→ `VersionLabel`；MainWindow.xaml.cs 构造里从程序集
  `InformationalVersion` 读取，显示「· v1.0.1」。以后改版本只需动 csproj，不用碰 XAML。
- 关于页标题同步为「MPV 简单设置 1.0.1」（这一处是硬编码，改版本时**必须手动改**，见第五节）。
- 注意：MainWindow.xaml.cs 的行尾是**双 CR（`\r\r\n`）**，Edit 工具匹配失败，用字节级替换改的。

### 1.3 HDR转SDR 开启时禁用 HDR 色调修复

用户拍板语义：**置灰 + 界面显示「关闭」+ 配置真的清零**（不是只置灰、也不是只改显示）。

- **EXE**：
  - `SettingsDraft` 加 `_hdrRawMode` / `ToneLockedBySdr` / `ToneInteractive`；tonemap 时
    `ToneIndex` 反推归 0、下拉置灰、显示「关闭」。
  - `WritePlanBuilder` 加 `toneDirty` 判定：盘上四键只要非关闭值就 **force** 写成
    `inverse=no / contrast=0 / brightness=0 / saturation=0`。
    反推时已把档位归零，diff 看不到盘上残留，**必须 force，否则残留键不会清**；
    同时限定只在盘上真有残留时才写，配置本就干净时不制造无效写盘。
  - `Themes/Theme.xaml` 的 `DarkComboBox` 原本**没有 `IsEnabled=False` 样式**（全局隐患），
    补了 `Style.Triggers` `Opacity=0.45`，置灰才可见。
- **setting.ahk**：`MpvRefresh` 在 `mode=tonemap || !HdrSupported` 时禁用 `MpvTone*` 五按钮并标记
  `MpvTone0`（关闭）；`MpvAction` 的转 SDR 分支加 `MpvApplyTone(0)` 真清零四键。
  无 HDR 显示器时 Lua 启动会降级为 tonemap，行为与界面一致，故一并处理。

### 1.4 外挂字幕修改后及时重载（默认开，晚间追加）

- **背景**：用户实践发现——用字幕编辑器改盘上外挂字幕后，播放中的 mpv 仍显示旧内容，
  必须手动切轨或 Ctrl+r 重载文件。
- **关键真相（查证 + 实测）**：**mpv 没有任何"字幕改动自动重载"的配置参数**。
  `--list-options` 里只有 `clipboard-monitor`（剪贴板，无关）；命令列表里有 `sub-reload <id>` /
  `rescan-external-files`，但要主动触发，mpv 原生不监视字幕文件。所以纯配置做不到，靠 Lua。
- **实测证据**（本机 mpv v0.42，造 `clip.mp4` + `clip.srt`）：
  - 改盘上字幕后 `sub-text` 仍是旧值 `VERSION_ONE_AAA` —— 印证问题；
  - `sub-reload <id>` 后立即变 `VERSION_TWO_BBB` —— 命令有效；
  - 外轨 `track.external-filename` 给出绝对路径，`mp.utils.file_info()` 返回 `mtime`/`size` —— 可做变化签名。
- **方案**：`script-opts/MyMPC.conf` 加 `sub_autoreload=yes|no`（默认 yes，不是 mpv.conf 键）；
  `MyMPC_ui.lua` 加 ①c 段：0.75s 周期扫描外挂字幕轨的 `external-filename`，
  `mtime:size` 签名变化就 `sub-reload <id>`。只管 `external=true` 的字幕轨；
  换文件/end-file 清缓存；首次记录签名不触发 reload；关开关彻底 kill 定时器。
- **UI**：播放页「字幕」卡片新增「字幕修改后及时重载」开关（默认开）。
- **验证**：`_verify/sub_autoreload_verify.py` 11 项全过（默认/反推/写盘/`--default` 恢复/内嵌 Lua）；
  端到端实测开关 yes 自动变、no 保持旧值；生产 conf + 生产 Lua 真实路径加载也通过。

### 1.5 全屏切换默认键改为 Alt+ENTER（晚间追加）

- **需求**：其他播放器全屏/退出都用 Alt+Enter；用户实测 mpv 的 `f` 也能退出全屏，故退出全屏不动，
  只把「全屏切换」的默认键从 `f` 改成 `Alt+Enter`。同一功能允许两个键共存。
- **技术真相**（改前必读，否则改了不生效）：
  - mpv 默认键 `f` 是**编译进二进制的弱绑定**，不在任何配置文件里，改不了也删不掉。
  - 要让 Alt+Enter 成为实际默认，只能在 `input.conf` 写一行**强绑定** `Alt+ENTER cycle fullscreen`
    去覆盖（键名写法是 `ENTER` 不是 `Enter`）。
  - 只改 `ShortcutCatalog` 的默认键不改 `input.conf` = 界面骗人、实际无效；必须两处一起改。
- **改动落点**：
  - `Models/ShortcutCatalog.cs`：`e2.fs` 的 `DefaultKey` 由 `f` 改为 `Alt+ENTER`，
    并标 `ShipLine=true`（本包发行自带行）——这样「恢复默认 / 重置页面」也会把这行写回，不会丢。
  - `12.mpv\portable_config\input.conf` 与生产 `input.conf`：各加一行 `Alt+ENTER cycle fullscreen`（LF 行尾）。
  - Note 写明「原默认 f 仍可用；ESC 退出全屏不变」——f 是弱绑定仍在、ESC 退出没动，正好是同一功能多键。
- **实测**：`--input-conf` 注入该行后 `keypress Alt+ENTER` 使 `fullscreen` 由 no→yes；
  生产界面截图确认显示 `Alt+Enter`（自定义态）。

### 1.6 快捷键键名翻译成中文习惯写法（晚间追加）

- **需求**：用户问「大写 S 是不是 Shift+s」——是。mpv 把大小写当两个键（`s` 与 `S`），
  `S` 即 Shift+s。界面上一堆 `SPACE` / `LEFT` / `SHARP` 不直观，要求全部翻译成一看就会按的写法。
- **改动**：只改 `ShortcutKeys.Display`（**显示层**），逐段翻译 + 拆 `+` 组合键：
  `S`→`Shift+s`、`SPACE`→`空格`、`ESC`→`Esc`、`ENTER`→`Enter`、`LEFT/RIGHT/UP/DOWN`→`←/→/↑/↓`、
  `SHARP`→`Shift+3`、`BS`→`退格`、`KP0`→`小键盘0` 等；组合键里的修饰键原样（`Alt+ENTER`→`Alt+Enter`）。
- **当日再修正（23:12）**：用户指出两处显示不合直觉——`SHARP` 显示成 `#`、`S` 显示成 `Shift+S`。
  改为 `SHARP`→`Shift+3`（# 的实际按法）、大写单字母→`Shift+小写`（`S`→`Shift+s`，键帽印小写 s，
  组合键 `Ctrl+S`→`Ctrl+Shift+s`、`Ctrl+SHARP`→`Ctrl+Shift+3`）。仅改 `DisplayToken` 两行 + 注释，
  写盘层 `ToFile`（`#`→`SHARP`）与录入层完全不动。DLL 直调验证 15 项全过。
- **再修正（23:20，最终）**：用户改主意——符号键要「保留符号 + 括号标按法」，不要只留按法。
  `SHARP` 从 `Shift+3` → **`# (Shift+3)`**；新增 `<`→**`< (Shift+,)`**、`>`→**`> (Shift+.)`**
  （上/下一个文件，美式键盘上 Shift+逗号/句号上档）。大写单字母 `S`→`Shift+s` 维持不变。
  仍只改 `DisplayToken` 三行 + 注释。mpv 侧 keypress 确认 `<`/`>`/`SHARP` 均为合法键名（rc=true）；
  DLL 直调 15 项全过（含组合键 `Ctrl+<`→`Ctrl+< (Shift+,)`）。写盘/比较层仍收原生键名，未动。
- **关键安全约束**：`Display` 只用于展示（键位框 / 冲突提示 / 写盘明细）；
  **写盘走 `ToFile`、比较走 `Canonical`**，两者始终收 mpv 原生键名。已逐个核对 8 个调用点全是显示用途，
  所以翻译不影响录入、冲突检测、写盘、`--default` 恢复的任何逻辑。
- **验证**：`stage6_verify.py` 全套通过（含 1.9b 断言 e2.fs 默认键 Alt+ENTER）；
  生产界面截图确认「空格」「←」「→」「Shift+s」「Ctrl+s」「Alt+Enter」全部正确显示。

---

## 二、默认值同步清单（新增设置项时的检查表）⚠️

**这是本次收尾的重点，也是以后再加设置项必须逐条核对的两处同步。**
新增一个带默认值的设置项，默认值同时"住"在两个地方，缺一处就会出现
「本机正常、全新安装/恢复默认后失效」的不一致。

| # | 同步位置 | 作用 | 1.0.1 `audio-file-auto` 状态 |
|---|---|---|---|
| 1 | `<项目根>/12.mpv/portable_config` 里的文件 | **打包源模板**：安装包把 `12.mpv\*` 整目录复制到 `{app}\MediaPlayer\mpv`（`<安装包脚本>.iss` 第164行），决定全新安装拿到的默认值 | ❌ 上午漏做 → **本次已补**：`mpv.conf` 加 `audio-file-auto=exact`；`scripts\MyMPC_ui.lua` 换成含 `prefer_internal_audio()` 的新版（旧版备份 `MyMPC_ui.lua.bak_20261117_before_audio`） |
| 2 | 「恢复默认」命令行参数对应的内容 = `--default` 恢复的出厂值（`SettingsDraft.SetAudioDefaults` / `WritePlanBuilder` / `DefaultProfile`） | 用户点「恢复默认」或 AHK 调 `MpvSettings.exe --default` 时写回的默认值 | ✅ 上午已做：`SetAudioDefaults()` 有 `ExternalAudioAuto = true`；`WritePlanBuilder` 用 `PutOrFill` 写 exact；`DefaultProfile` 复用这两者，`--default` 走同一条链路 |

### 2.1 第 1 处的两个文件都要改，别只改 mpv.conf

- `12.mpv\portable_config\mpv.conf`：加默认键（本次加在 `volume-max` 之后、`loop-file` 之前）。
- `12.mpv\portable_config\scripts\MyMPC_ui.lua`：**EXE 独占脚本**，行为逻辑在这里。
  本次上午只同步到了生产目录 `<MPV配置目录>\scripts\`，
  **漏了打包源 12.mpv**，等于全新安装的功能是残缺的——本次已补齐，现三处 md5 一致。

### 2.2 一个容易忽略的连带点

`DefaultProfile` 的注释明确「默认值住在 EXE 里」，AHK 的恢复默认只是调 `--default`
（`setting.ahk` 第1578行 `RunWait ... --default --config-dir=...`），不再依赖 `*_default.conf` 模板。
所以**只要改 `SettingsDraft` + `WritePlanBuilder`，第 2 处就自动同步**；不需要、也不应该再去找模板文件改。

### 2.3 setting.ahk（涉及 AHK 联动的设置项）

- 本次上午**已同步**（1.3 节所列改动均在 `<项目根>/00.Core/setting.ahk`）。
- 以后凡是「EXE 设置 + AHK 按钮/菜单联动」的项，改完 EXE 必须回头核对 setting.ahk 的
  `MpvRefresh`（反推与置灰）、`MpvAction`（点击动作）、`MpvApplyTone`（成组写键）三处。

### 2.4 字幕热重载（1.4 节）同样落在两处 —— 本次同步记录

这一项验证了上面这套检查表的通用性：默认值 `sub_autoreload=yes` 一样"住"在两处。

| 位置 | 本次处理 |
|---|---|
| 打包源 `12.mpv\portable_config` | ✅ 已补：`script-opts\MyMPC.conf` 加 `sub_autoreload=yes`；`scripts\MyMPC_ui.lua` 换成含 ①c 轮询段的新版（旧版备份 `.bak_before_subreload`） |
| 出厂值（`SettingsDraft.SetPlaybackDefaults` / `WritePlanBuilder`） | ✅ 已做：`SubAutoReload = true`；`BuildMyMpcConf` 写 `sub_autoreload=`；ManagedProps 已登记；`--default` 自动跟随（验证 4.2 通过） |

**注意默认值载体不同**：音频项是 mpv.conf 键（`audio-file-auto`），字幕项是 script-opts 键
（`sub_autoreload`）——因为 mpv 根本没有字幕重载参数。判断新设置该写哪个文件，
取决于**能力在哪一层**：播放器原生参数走 mpv.conf，EXE 自研脚本行为走 script-opts/MyMPC.conf。

---

## 三、产物与部署状态

| 位置 | 文件 | 版本 | 说明 |
|---|---|---|---|
| 源码 | `MpvSettings.csproj` | 1.0.1 | Version / AssemblyVersion / FileVersion / InformationalVersion 四处已改 |
| 发布产物 | `MpvSettings\publish\MpvSettings.exe` | **1.0.1** | 435,926 B，md5 `91a7096098bccd2d7a41455a0549a341`（含 1.4 字幕热重载 + 1.5/1.6 快捷键 + 23:20 符号键带括号显示） |
| 打包源 | `12.mpv\MpvSettings.exe` | **1.0.1** | 已同步，与 publish md5 一致（否则安装包还会打出旧版） |
| 生产目录 | `<MPV安装目录>\MpvSettings.exe` | **1.0.1** | 已同步，与 publish md5 一致 |
| 旧版备份 | `MpvSettings\_verify\exe_history\` | 1.0.0 / 1.0.1 前 | `MpvSettings_1.0.0_before_101_*.exe`（升 1.0.1 前）、`MpvSettings_1.0.1_before_subreload_*.exe`（加字幕功能前） |

> 三处 EXE md5 完全一致（含内嵌 Lua）；每次覆盖前都备份了旧版，可回退。

---

## 四、改动文件清单

**源码 / 界面（1.0.1 功能）**
- `Models/SettingsDraft.cs`、`Models/WritePlan.cs`、`Services/WritePlanBuilder.cs`
- `Views/AudioView.xaml`、`Views/VideoView.xaml`、`Themes/Theme.xaml`、`Views/PlaybackView.xaml`
- `MainWindow.xaml`、`MainWindow.xaml.cs`、`Views/AboutView.xaml`
- `Models/ShortcutCatalog.cs`（e2.fs 默认键 Alt+ENTER + Display 键名翻译；晚间）
- `Assets/MyMPC_ui.lua`（含 ①b 音频内嵌优先 + ①c 字幕热重载）
- `MpvSettings.csproj`（版本号）

**AHK**
- `<项目根>/00.Core/setting.ahk`（上午已同步）

**打包模板（本次收尾补做）**
- `12.mpv\portable_config\mpv.conf`：`+ audio-file-auto=exact`
- `12.mpv\portable_config\script-opts\MyMPC.conf`：`+ sub_autoreload=yes`
- `12.mpv\portable_config\scripts\MyMPC_ui.lua`：同步含 `prefer_internal_audio()` + 字幕轮询段的新版
- `12.mpv\portable_config\input.conf` 与生产 `input.conf`：各加 `Alt+ENTER cycle fullscreen`（晚间）

**EXE 部署（本次收尾）**
- `publish/`、`12.mpv/`、`生产目录` 三处同步为 1.0.1（含内嵌 Lua）

---

## 五、验证记录

- `dotnet build -c Release`：**0 警告 0 错误**；`dotnet publish` 成功。
- 界面截屏：`_verify/feature_20261117/shot_v101_main.png`（标题栏 `· v1.0.1` + 音频页新卡片）、
  `shot_v101_about.png`（关于页 `MPV 简单设置 1.0.1`）。
- 功能验证脚本：`_verify/feature_20261117_verify.py`（15 项全过：草稿反推、audio-file-auto 写盘往返、
  SDR 置灰 / 真关闭 / 干净时不误写 / 直通解锁）。
- mpv 端到端：内嵌音轨优先、无内嵌时外部兜底，两种场景均实测通过。

**字幕热重载（1.4，晚间）**
- `_verify/sub_autoreload_verify.py`：11 项全过（默认开 / 反推三态 / 写盘往返 / `--default` 恢复 / 内嵌 Lua 四处断言）。
- mpv 端到端：开关 yes 时改字幕 `AAA→BBB` 自动生效；开关 no 时保持旧值；生产 conf + 生产 Lua 真实 config-dir 加载路径同样通过。
- 界面：用户实测截图确认播放页「字幕」卡片新开关（绿色开启），标题栏 `· v1.0.1`。

**快捷键（1.5 / 1.6，晚间）**
- mpv 端到端：`Alt+ENTER cycle fullscreen` 注入后 `keypress Alt+ENTER` 使 `fullscreen` no→yes。
- `_verify/stage6_verify.py` 全套通过：1.9b 断言 e2.fs 默认键=Alt+ENTER 且为自定义态；
  本轮顺手修了脚本两处遗留问题——`fresh()` 漏复制 5 个 wiring 文件导致"零改动"误报，
  以及摘要精确串断言过脆（改包含判断），均与功能实现无关。
- `_verify/feature_20261117_verify.py`（15 项）、`_verify/sub_autoreload_verify.py`（11 项）回归全过。
- 界面：用户实测截图确认 E2 组「全屏切换」显示 `Alt+Enter`（自定义，附「原默认 f 仍可用」说明）、
  「截图（不含字幕）」`Shift+s`、「截图当前窗口」`Ctrl+s`、E1 组「空格」/「←」/「→」全部正确翻译。
- 23:12 键名再修正：DLL 直调验证 15 项全过——`SHARP`→`Shift+3`、`S`→`Shift+s`、
  `Ctrl+S`→`Ctrl+Shift+s`、`Ctrl+SHARP`→`Ctrl+Shift+3`，其余翻译无回归；写盘层 `ToFile`（`#`→`SHARP`）未动。
- 23:20 符号键带括号显示（最终）：mpv keypress 确认 `<`/`>`/`SHARP` 均合法；DLL 直调 15 项全过——
  `SHARP`→`# (Shift+3)`、`<`→`< (Shift+,)`、`>`→`> (Shift+.)`、`Ctrl+<`→`Ctrl+< (Shift+,)`，
  `S`→`Shift+s` 及其余翻译无回归。写盘/比较层未动。

> ⚠️ **下次改版本号要改 5 处**：csproj 的 Version / AssemblyVersion / FileVersion /
> InformationalVersion，以及 `Views/AboutView.xaml` 里硬编码的「MPV 简单设置 x.y.z」。
> 标题栏从程序集自动读取，关于页这处硬编码最容易被漏掉。改完还要同步 `publish` → `12.mpv` → 生产目录三处 EXE。

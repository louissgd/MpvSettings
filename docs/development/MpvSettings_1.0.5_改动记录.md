# MpvSettings 1.0.5 改动记录

> 1.0.4 → 1.0.5。一项功能：SRT 默认字幕样式设置面板（字体 / 颜色 / 字号 / 行间距 / 描边 / 边距 / ASS 保护），对齐 MPC-HC「默认样式」的常用字段。
> 前次基线见 `MpvSettings_1.0.4_改动记录.md`（1.0.4，末次产物 md5 `2fc3e2d8…`）。
>
> 需求来源：`github.com/Yaozhil/mpv-Yaozhi/issues/37`——给 SRT 加字体 / 颜色 / 字号 / 行间距调整，
> 且「只调 SRT，SSA/SUP 跟作者参数」。原仓库无人回复，本包自行实现。
>
> 记录日期：2026-11-22　｜　归档人：本次收尾
> **更新（22:4x，版本号仍 1.0.5）**：字体名从可编辑文本框改为「只读展示 + 按钮打开系统字体对话框」。
> 已重编覆盖三处 EXE（md5 见第五节）。详见 1.5 节。
> **更新（22:4x，版本号仍 1.0.5）**：用户追问「对话框带大小，确定会忽略吗」——修掉这个 UI 欺骗：
> 字形（粗/斜）与大小都真正生效，字符集用 `CF_NOSCRIPTSEL` 隐藏。新增 `sub-italic` 键（10→11 键）。
> **更新（23:03，版本号仍 1.0.5）**：三项收尾——① 弹窗「完成」直接写盘（不用再回主界面点应用/确定）；
> ② 修正字号换算：从错误的屏幕 DPI 96/72 改为 ASS PlayResY=1080→mpv 720p 的 720/1080；
> ③ 默认字号 38→48（微软雅黑）/ 华文楷体 52。重编覆盖三处，见 1.6 与第五节。
> **更新（23:16，版本号仍 1.0.5）**：交互修正——颜色色块改点开系统调色板（不再只能手输色码）；
> 数值滑杆右侧改为可直接键入精确值。重编覆盖三处，见 1.7 与第五节。
> **更新（23:34，版本号仍 1.0.5）**：默认值定版——纯白 `#FFFFFFFF`；配置文件静态微软雅黑48，
> 安装时 `--default` 探测到 `STKaiti` 自动升级楷体52。两处 mpv.conf 写入整套 11 键。
> 重编覆盖三处，见 1.8 与第五节。

## 一、功能改动（1.0.5）

### 1. 播放页字幕卡片新增「SRT 默认样式 · 打开设置」按钮 → 独立样式窗口

没有新增侧边栏分类。落点在播放页现有「字幕」SettingCard 末尾，加一条说明 + `SmallButton`「打开设置」，
点击弹出独立窗口 `Views/SubtitleStyleWindow`，分四区：

| 分区 | 控件 | 写盘键 | 出厂默认 |
|---|---|---|---|
| 作用范围说明 | 只读文字 | — | — |
| 字幕类型保护 | 「保护 ASS 特效字幕作者样式」开关 | `sub-ass-override` = `no` / `scale` | `no`（保护开） |
| 字体 | 字体名 / 颜色 / 加粗 | `sub-font` / `sub-color` / `sub-bold` | `Microsoft YaHei`、`'#FF9A9A9A'`（暗灰）、`no` |
| 尺寸间距 | 字号 / 行间距 / 描边 / 阴影 | `sub-font-size` / `sub-line-spacing` / `sub-outline-size` / `sub-shadow-offset` | `38` / `0` / `1` / `0` |
| 屏幕边距 | 左右 / 底部 | `sub-margin-x` / `sub-margin-y` | `19` / `34` |

- **弹窗只改草稿，不写盘**：窗口接收同一个 `SettingsDraft` 实例作 DataContext；「确定」写回草稿关闭，
  「取消」用打开时的字段快照完整回滚 11 个字段。真正落盘仍走主界面「应用 / 确定」。
- **出厂默认写入语义用 `PutOrFill`**：盘上缺键就补默认值，界面显示与实际一致。
  首次保存会一次写入 11 个 `sub-*` 键（这是「安装后不操作也有合理默认」的取舍），之后只写 diff。
- 默认值定稿依据：测试片 `<测试片名>`（MKV 内封 ASS 字体 STKaiTi、MarginV=60、Alignment=2，
  同内容外部 ASS 在目录里）+ 用户当前 SRT 用 mpv 默认微软雅黑。暗灰用 `#FF9A9A9A`。

### 2. 核心结论：mpv 原生就支持「只调 SRT、不碰 ASS/SUP」，不用改核心、不用 Lua

源码证据（本机 `<mpv源码目录>/packages/mpv/sub/sd_ass.c`，libass 侧 `ass_render.c`）：

- **556 行** `if (converted || ... == FORCE)` → `converted=true`（SRT 等纯文本被转成 ASS 内部格式）时 `total_override=true`。
- **585–589 行** 只有 `total_override` 才打开 `ASS_OVERRIDE_BIT_FONT_NAME / FONT_SIZE_FIELDS / COLORS / BORDER`
  → 字体、字号、颜色、描边只对 converted 字幕强制；ASS 原生字幕默认拿不到这些位。
- **565–567 行** `if (converted || ass_style_override)` → 行间距的覆盖条件比字体那组**宽**，
  默认 `sub-ass-override=scale`（枚举值 2，非 0）时连 ASS 也会被套上行间距——这是唯一的坑。
  libass 的 `ass_set_line_spacing` 是渲染器全局量，不区分字幕来源。
- **解法**：`sub-ass-override=no`（枚举 0）→ 第 565 行对 ASS 原生字幕不成立，行间距也不碰 ASS；
  SRT 因 `converted=true` 两个分支都照常生效。**一行开关同时满足 issue 的四条诉求。**
  SUP/PGS 位图字幕天然免疫（`mpv_options.rst:2510-2513` 明说图像字幕无法改样式）。
- **互斥路线排除**：`sub-ass-styles` 只在 `ass_style_override != 0` 时加载样式文件，
  与 `sub-ass-override=no` 互斥；走样式文件就无法保护 ASS，故放弃，回到 `sub-*` 参数化路线。

### 3. 对齐 MPC-HC「默认样式」的实际情况

- **完全对齐**：字体名、颜色+透明度（`#AARRGGBB`）、字号、行间距、描边宽度、阴影、左右/底部边距、加粗、对齐
  （对齐本版未放进 UI——mpv 默认居中底部，与截图一致）。
- **mpv 无独立选项，不做**：字符集 GB2312、OpenType 语言、角度 z°、ScaleX/ScaleY 比例。
  这四项属于用户说的「更多特效字幕以后再找」范畴。

### 4. 关键坑（实测）：颜色 `#` 在 mpv.conf 里被当注释，必须加引号

- 写 `sub-color=#FF9A9A9A` → mpv 把 `#` 当注释起始，颜色静默回落白色（`sub-color=#FFFFFFFF`）。
- 实测三种写法：`#RRGGBB`（失败，白色）、`0.6/0.6/0.6`（成功）、`'#FF9A9A9A'`（成功）。
- **修法**：`WritePlanBuilder` 写盘时把颜色包成 `sub-color='#FF9A9A9A'`（单引号）；
  `SettingsDraft.NormalizeColor` 反推时 `Trim('\'', '"')` 去引号，UI 内部统一 `#AARRGGBB` 便于色块预览。

### 5. 字体名改按钮选择（22:4x，用户反馈后调整，版本号不变）

用户反馈：字体名不该能手输（容易填 mpv 不认的名字），要像 MPC-HC 那样点按钮弹出系统字体对话框。

- **实现**：`Views/SubtitleStyleWindow.xaml` 字体名 `TextBox` 改 `IsReadOnly=True` + 旁边「选择…」按钮。
  按钮 `PickFont_Click` 用 **P/Invoke `comdlg32.dll` 的 `ChooseFont`** 弹出原生字体对话框，不引 WinForms
  （避免 `<UseWindowsForms>true</UseWindowsForms>` 把整个 WinForms 拖进单文件 EXE、增大体积与杀软误报面）。
- **结构体**：`LOGFONT`（92 B）/ `CHOOSEFONT`（104 B，x64）按 Unicode 布局声明，自测 `Marshal.SizeOf`
  与字体名往返均正确（`Microsoft YaHei` 读回无误）。
  Flags = `CF_SCREENFONTS | CF_INITTOLOGFONTSTRUCT | CF_NOSCRIPTSEL`，
  用当前字体名、粗体（FW_BOLD=700 / FW_NORMAL=400）、斜体初始化对话框回显；
  `lfHeight` 按字号换算（见下文，23:03 已从 `-字号` 改为 `-字号×2`）。
- **三项都生效（22:4x 修正）**：确定后写回
  - `SubFont = lfFaceName`
  - `SubBold = lfWeight >= 600`、`SubItalic = lfItalic != 0`（字形栏全四项有效；mpv 有 `--sub-italic`）
  - `SubFontSize`：读 `CHOOSEFONT.iPointSize`（1/10 pt）。**换算基准 23:03 修正，见 1.6 节**——
    早期写的 `px = pt × 96/72` 是屏幕 DPI 换算，用错了坐标系，已废弃；
    现按 ASS 脚本 1080p 基准 → `sub-font-size = pt × 720/1080`（72pt→48、78pt→52）。
    回显 `lfHeight = -字号×2`，与换算互为逆运算（mpv 48 → 对话框 72pt）。
  - **字符集**：`CF_NOSCRIPTSEL` 直接隐藏下拉——mpv 无对应选项，不给用户留无效控件。
- **取消**：`ChooseFont` 返回 false 不动草稿；弹窗「取消」还原 11 字段（含字体名/粗体/斜体/字号）。
- 字号滑杆保留，可在字体对话框选完后再微调。

### 3. 1.6 收尾（23:03）：直接应用 + 字号换算修正 + 默认字号

**① 弹窗「完成」直接写盘**
之前弹窗改的只是草稿，还要回主界面点「应用/确定」才落盘，用户视为多余一步。
现 `SubtitleStyleWindow.Ok_Click` 直接调 `MainWindow.ApplySubtitleGroup(this)`：
- 从 `BuildPlan()` 里只过滤 `Group == "字幕默认样式（仅 SRT）"` 的键写盘，
  **不碰主界面其他页的未保存改动**（否则弹窗顺手替用户提交了没预览过的修改）。
- 写盘成功后 `_baseline.AdvanceSubtitleStyle(_draft)` 只推进字幕那 11 个属性的基线，
  其他页 diff 完整保留；不推进会让主界面结果窗口把字幕键再列一遍（写本身幂等，但明细是假的）。
- 结果照旧弹 `ApplyResultWindow`（`closeOnSuccess:false`），用户能看到写了哪些键。
- 弹窗「取消 / ✕」仍是回滚 11 字段、不写盘。

**② 字号换算修正（关键，之前的 96/72 是错的）**
用户指出 mpv 字幕大小跟 ASS 一样按视频/窗口分辨率算，不该用屏幕 DPI。查源码确认：
- `mpv_options.rst:3097`：`sub-font-size` 单位是「窗口高 720 时的 scaled pixels」，随窗口高度缩放。
- `ass_mp.c:52`：`double scale = res_y / 720.0; style->FontSize = opts->font_size * scale;`
  —— mpv 内部把 sub-font-size 当作 PlayResY=720 的字号；libass 再按 `frame_h / res_y` 缩放到实际像素。
  两者都是相对高度的比例，**与屏幕 DPI 无关**。
- 用户实测 ASS：`PlayResX=1920 / PlayResY=1080 / STKaiTi / Fontsize=72`。
  等效 720p 字号 = `72 × 720/1080 = 48`——正好是用户说的微软雅黑习惯值。
- 修正：`mpv值 = pt × 720/1080`（= pt×2/3），回显 `lfHeight = -字号×2`（48 → 对话框 72pt，对上 ASS 的 72）。
  例：72pt→48、78pt→52（华文楷体习惯值）。

**③ 默认字号 38 → 48**
`SettingsDraft` 三处默认值（字段初始化 / `LoadFrom` / `ResetDefaults`）改为 48。
默认值属"面板出厂默认"，与用户实测对齐（微软雅黑 48 / 华文楷体 52）；字体切换仍靠字体对话框点选。

### 4. 1.7 交互修正（23:16）：颜色调色板 + 数值可键入

**① 颜色色块 → 系统调色板**
用户反馈「颜色只能手输 `#AARRGGBB` 要求太高」。现点色块弹 Win32 `ChooseColor`（P/Invoke `comdlg32.dll`，
不引 WinForms，与字体对话框同一策略），`Flags = CC_RGBINIT | CC_FULLOPEN`（回显当前色 + 展开自定义区）。
- 两套颜色格式必须分清（用户当场提出「ASS 是 AABBGGRR」）：
  - **ASS 字幕文件内部**：`&HAABBGGRR`（BGR 序、AA 反向，00=不透明）——字幕作者写在 .ass 里的格式。
  - **mpv.conf `--sub-color` 选项**：`#AARRGGBB`（RGB 序、AA 正常，FF=不透明）——本程序写盘的格式。
    依据：mpv_options.rst:3188「Alpha is given with #AARRGGBB」、3193 例 `--sub-color='#C0808080'`。
  - mpv 内部 `MP_ASS_COLOR` 宏（`ass_mp.h:34-38`）负责把 `#AARRGGBB` 转成 ASS 的 AABBGGRR
    → `(r<<24)|(g<<16)|(b<<8)|(0xFF-a)`。所以写 `#FF9A9A9A` 正确，不是写 ASS 的 `&H`。
- Win32 `COLORREF` 是 `0x00BBGGRR`：用 `R | G<<8 | B<<16` 初始化、取色后 `&0xFF / >>8 / >>16` 解回 RGB；
  `CHOOSECOLOR` 结构体 72 B（x64）自测通过，红/蓝非对称色解码不串位。
- **AA 保留**：系统调色板无 alpha 概念，选色只换 RGB，保留当前 AA（默认 FF=不透明）；要改透明度在文本框手输。
- 文本框保留 `TwoWay` + `PropertyChanged`，作为精确输入/透明度的高级兜底；色块点击只改草稿，仍走「完成」写盘。

**② 数值框可直接键入**
用户反馈「下面那些数字反而不能编辑，反人类」。根因：`ValueSlider` 右侧是 `TextBlock`。
- 给 `Controls/ValueSlider` 加 `IsEditable` 依赖属性（**默认 false——其他页面零影响**）：
  false 显示原 TextBlock；true 显示 `TextBox`（OneWay 绑 `ValueText`），回车/LostFocus 提交、Esc 还原。
  提交逻辑解析 Invariant/Current culture → 夹到 `[Minimum, Maximum]` → 写 `Value`（不吸附 tick，保留精确值）。
  可编辑且焦点在数值框时，`OnPreviewKeyDown` 不接管方向键（否则左右键没法移光标），`PreviewMouseDown` 不抢焦点。
- XAML 设 `IsEditable="True"` 时 DP 回调早于子控件创建，构造函数里补应用一次。
- 字幕弹窗的 6 个数值滑杆（字号/行间距/描边/阴影/左右边距/底部边距）全部打开 `IsEditable="True"`。

### 1.8 字幕默认值定版：静态微软雅黑48/纯白 + `--default` 按本机字体探测升级楷体52

用户实测：SRT 用**纯白 `#FFFFFFFF`** 在 HDR 下不会比视频更亮（白色安全），
偏好**华文楷体 52**（无该字体时回落微软雅黑 48）。字体族名经 WPF `Fonts.SystemFontFamilies`
枚举确认是 **`STKaiti`**（测试片内封 ASS 用的正是 `STKaiTi`）。

**关键约束**：mpv 的 `--sub-font` 只接受单一字体名，libass 的字体 fallback **不会**跟着改
`sub-font-size`。所以"没楷体就把字号从 52 降到 48"这条联动，mpv.conf 静态文本做不到，
必须在设置程序侧探测。用户定下的分层：

| 层 | 值 | 时机 |
|---|---|---|
| mpv.conf 模板 + GUI 普通保存 / `--plan-only` | **Microsoft YaHei / 48 / `#FFFFFFFF`** | 跨机器通用静态默认，任何机器可用 |
| `--default`（安装时执行 / 恢复默认） | 探测到 `STKaiti` → **STKaiti / 52 / `#FFFFFFFF`** | 装完适配本机；探测不到保持静态默认 |

改动：
- `Models/SettingsDraft.cs`：字段初值、`LoadFrom` 缺键回落、`SetPlaybackDefaults`、`NormalizeColor`
  空值回落全改纯白（`#FF9A9A9A` → `#FFFFFFFF`）；字体/字号字段初值仍保持微软雅黑/48（静态）。
- `Services/WritePlanBuilder.cs`：颜色空值兜底改 `#FFFFFFFF`。
- `Services/DefaultProfile.cs`：**新增 `ApplyDetectedSubtitleFont(d)`**，在 `DefaultDraft()`
  末尾、`ResetAll()` 之后调用。用 `Fonts.SystemFontFamilies.Any(f => f == "STKaiti")` 探测——
  `--default` 跑在 `Application.OnStartup` 的 STA 线程上，WPF 字体枚举安全；
  探测到就把 `d.SubFont="STKaiti"`、`d.SubFontSize=52`；整套 try-catch，字体枚举失败
  就保持静态默认，不阻断安装（安装包只认退出码）。
  **不放字段初值的原因**：模板与导出/导入的出厂值必须逐字节一致，探测结果只属于"这台机器恢复默认"。
- `Views/SubtitleStyleWindow.xaml`：色块预览初值 `#FF9A9A9A` → `#FFFFFFFF`。
- **两处 mpv.conf**（`12.mpv\portable_config` 与生产目录）写入整套 11 个 `sub-*` 键的
  静态默认（微软雅黑/48/纯白）+ 注释说明 `--default` 会按字体探测升级楷体52。

**验证**（publish EXE，临时配置目录，不碰真实配置）：
- `--default` 探测路径：`mpv.conf` 写出 `sub-font=STKaiti` / `sub-color='#FFFFFFFF'` / `sub-font-size=52` ✅
- 静态路径（`--plan-only --dump-plan`）：`sub-font=Microsoft YaHei` / `#FFFFFFFF` / `48` ✅
- 打包源真实配置保持静态微软雅黑48，未被验证污染 ✅

## 二、版本号（1.0.4 → 1.0.5）

按 1.0.1 / 1.0.4 约定改 **5 处**：

| 位置 | 改动 |
|---|---|
| `MpvSettings.csproj` | Version / AssemblyVersion / FileVersion / InformationalVersion → 1.0.5 / 1.0.5.0 |
| `Views/AboutView.xaml` | 「MPV 简单设置 1.0.4」→「MPV 简单设置 1.0.5」 |

标题栏 `· v1.0.5` 由 `MainWindow.xaml.cs` 读程序集 `InformationalVersion` 自动显示，无需改 XAML。
EXE 元数据实测：`FileVersion=1.0.5.0` / `ProductVersion=1.0.5`；生产目录副本同。

## 三、文件改动清单

**源码 / 界面**
- `Models/SettingsDraft.cs`：新增 11 个字幕样式字段（含 `SubBold` / `SubItalic` bool）、`LoadFrom` 反推（带默认值，
  避开 `Num` 缺键返回 NaN 误夹到下限）、`NormalizeColor` 颜色归一化、`ResetDefaults` 同步。
- `Models/WritePlan.cs`：`ManagedProps` 登记 11 个新属性（漏登记会导致每次保存都判成「改过」）；
  `DraftBaseline.AdvanceSubtitleStyle()`（1.6）——弹窗直接写盘后只推进字幕组基线。
- `Services/WritePlanBuilder.cs`：`BuildMpvConf` 新增字幕样式成组写入（逐键「改过或缺键就写」，
  invariant culture 数值，颜色包单引号），组名「字幕默认样式（仅 SRT）」。
- `Views/PlaybackView.xaml` / `.xaml.cs`：字幕卡片末尾说明行 + 「打开设置」按钮及点击转发。
- `Views/SubtitleStyleWindow.xaml` / `.xaml.cs`（**新增**）：独立样式窗口，沿用自绘标题栏与主题；
  取消回滚 11 字段。字体名只读 + 「选择…」按钮，P/Invoke `ChooseFont` 取字体名/字形/大小（见 1.5）；
  颜色色块 P/Invoke `ChooseColor` 弹系统调色板（见 1.7）；6 个数值滑杆 `IsEditable=True` 可直接键入（见 1.7）。
- `Controls/ValueSlider.xaml` / `.xaml.cs`：新增 `IsEditable` 依赖属性（默认 false）与可键入 TextBox，
  回车/LostFocus 提交、Esc 还原、焦点处理（1.7）；其他页面默认行为不变。
- `MainWindow.xaml.cs`：`ApplySubtitleGroup(Window owner)`（1.6）——按 Group 过滤只写字幕样式组、显示结果窗、
  成功后推进字幕基线；不动其他页 diff。
- `Services/DefaultProfile.cs`：`DefaultDraft()` 末尾新增 `ApplyDetectedSubtitleFont(d)`（1.8）——
  `Fonts.SystemFontFamilies` 探测到 `STKaiti` 则把草稿改 STKaiti/52，否则保持静态微软雅黑/48。
- `MpvSettings.csproj` / `Views/AboutView.xaml`：版本号。

**配置文件（1.8 新增）**
- `12.mpv\portable_config\mpv.conf`：新增整套 11 个 `sub-*` 静态默认（微软雅黑/48/纯白 `#FFFFFFFF`）
  + 注释说明 `--default` 会按字体探测升级楷体52。
- 生产 `...\MediaPlayer\mpv\portable_config\mpv.conf`：原「也字工厂陨石黑」三行改为静态默认
  （微软雅黑/48/纯白），与模板一致。

**配置 / 脚本（模板 + 生产）**
- 本次**未改**任何 `mpv.conf` / `MyMPC.conf` / `MyMPC_ui.lua` / `input.conf`。
  默认值住在本机 EXE（草稿默认 + WritePlanBuilder），不预置到模板文件；
  打包源 `12.mpv\portable_config\mpv.conf` 仍无 `sub-*` 键，首次安装行为是 mpv 原生默认，
  用户点弹窗「完成」（或主界面「确定」）后才写入样式键。这与 1.0.1 里「默认值住两处」的音频/字幕热重载项不同——
  那两项是脚本行为必须预置，本项是纯样式参数，可安全由设置程序首次写入。

**验证脚本**
- `_verify/` 下字幕计划 / 端到端探针为临时夹具，验证后已清理，不是回归基线。

## 四、验证

- `dotnet build -c Release`：**0 警告 0 错误**；`dotnet publish -c Release -r win-x64 --self-contained false`：成功。
- 写盘计划（`publish\MpvSettings.exe --plan-only --dump-plan`，隔离只读）：11 个 `sub-*` 键全部生成，
  值为出厂默认；颜色写成 `'#FF9A9A9A'`（带引号）；diff 测试改行间距/颜色时只有对应键变化。
- 真实 mpv 加载（临时 config-dir，不碰生产配置）：`sub-font / sub-color / sub-font-size /
  sub-line-spacing / sub-bold / sub-italic / sub-outline-size / sub-margin-y / sub-ass-override` 全部返回预期值；
  修复前 `sub-color` 曾回落白色，加引号后正确。
- 源码层：`sd_ass.c` 556 / 565 / 585 行 + `ass_render.c` 全局 line_spacing 已逐行核对（见 1.2 节）。
- 本次（1.0.5 收尾）重新验证：新 EXE `FileVersion=1.0.5.0`，写盘计划与上述一致；
  三处 EXE md5 一致。
- 字体按钮（1.5）：P/Invoke 结构体尺寸自测 `LOGFONT=92 / CHOOSEFONT=104`（x64 标准）、
  字体名 `Microsoft YaHei` 往返读回无误；重编后 `dotnet build -c Release` 仍 0 警告 0 错误，
  publish 后写盘计划为 11 个 `sub-*` 键（新增 `sub-italic=no`），三处 md5 一致。
- 1.6 收尾验证：`dotnet build -c Release` 0 警告 0 错误；写盘计划 11 键、`sub-font-size=48`、
  全部带 `Group="字幕默认样式（仅 SRT）"`（直接应用过滤可命中）；真实 mpv 加载临时配置
  返回 `size=48 spacing=-2 override=no`，字号配置生效。EXE `FileVersion=1.0.5.0`，三处 md5 一致。
- 字号换算的源码证据：`mpv_options.rst:3097` + `ass_mp.c:52`（scale=res_y/720），推导 72pt@1080p=48@720p，
  与用户实测 ASS（PlayResY=1080 / Fontsize=72）及其习惯值（微软雅黑 48 / 华文楷体 52）三方吻合。
- 1.7 交互修正验证：P/Invoke `CHOOSECOLOR=72 B`（x64 标准）、`COLORREF 0x00BBGGRR` 红/蓝非对称色解码不串位
  （自测）；`dotnet build -c Release` 0 警告 0 错误；写盘计划仍 11 键，颜色后于 1.8 定版为 `#FFFFFFFF`（见下）。
- 字体对话框 / 调色板的实际弹出与点选回写、数值框键入提交需 GUI 实测（用户侧，无头环境测不了）。

## 五、产物与部署状态

| 位置 | 文件 | 版本 | 说明 |
|---|---|---|---|
| 源码 | `MpvSettings.csproj` | **1.0.5** | 四处版本号 + AboutView 硬编码均已改 |
| 发布产物 | `MpvSettings\publish\MpvSettings.exe` | **1.0.5** | 487,638 B，md5 `6025b35430b88d8ee4482852b1669a9c`（含 1.8 默认值定版 + 字体探测） |
| 打包源 | `12.mpv\MpvSettings.exe` | **1.0.5** | 已同步，与 publish md5 一致 |
| 生产目录 | `<MPV安装目录>\MpvSettings.exe` | **1.0.5** | 已同步，与 publish md5 一致（元数据实测 1.0.5.0） |
| 旧版备份 | `<项目根>/_bak_exe_105_20261122_223548\` | 1.0.4 | 三份：`MpvSettings_{publish,12mpv,production}_104.exe`，均为 469,718 B / `2fc3e2d8…` |
| 中间版备份 | `<备份目录>/` | 1.0.5 前 | 三份：加字体按钮前的 1.0.5（`c5787fe0…`），可回退 |
| 中间版备份 | `<备份目录>/` | 1.0.5 前 | `12mpv_beforePtSize.exe` / `production_beforePtSize.exe` 为大小/字形生效前的 1.0.5（`485474bd…`） |
| 上版备份 | `<备份目录>/` | 1.0.5 前 | `12mpv_beforeDirectApply.exe` / `production_beforeDirectApply.exe` 为 1.6 前的 1.0.5（`2b2b0d65…`） |
| 上版备份 | `<备份目录>/` | 1.0.5 前 | `12mpv_beforeColorPicker.exe` / `production_beforeColorPicker.exe` 为 1.7 前的 1.0.5（`70291969…`） |
| 上版备份 | `<备份目录>/` | 1.0.5 前 | `12mpv_beforeFontDefault.exe` / `production_beforeFontDefault.exe` 为 1.8 前的 1.0.5（`05a4d35d…`） |

> 三处 EXE md5 完全一致；旧版备份放在数字目录之外（`_bak_*`），不污染打包源。
> 1.0.5 内六次产物：`c5787fe0…`（字体名可编辑）→ `485474bd…`（字体按钮）→ `2b2b0d65…`（字形/大小生效）
> → `70291969…`（直接应用 + 字号换算修正 + 默认 48）→ `05a4d35d…`（调色板 + 数值可键入）
> → **`6025b354…`（最终，默认值定版 + `--default` 字体探测，11 键）**，以本节最终值为准。

## 六、使用说明 / 边界

- **弹窗「完成」立即写盘**（1.6）：不用再回主界面点「应用/确定」。只写字幕样式这一组键，
  主界面其他页的未保存改动仍要回主界面自己点「应用/确定」。改完点「打开 MPV 播放器」重启播放器生效。
  弹窗「取消 / ✕」会撤销本次在弹窗里的全部改动、不写盘。
- **字号换算基准**（1.6）：字体对话框里的「大小」按 ASS 脚本 1080p 点数填（72→48、78→52），
  程序自动换算到 mpv 的 720p 基准；下方字号滑杆显示的也是这个 720p 值。默认 48（微软雅黑）/ 华文楷体习惯 52。
- **`sub-ass-override=no` 是全局开关**：ASS 原生字幕从此不吃任何 `--sub-*` 覆盖，正好是「跟作者参数」要的效果。
  若以后某个 ASS 排版烂、原本靠 mpv 统一样式去救（如 `sub-pos` / `sub-scale`），该开关会让它失效——
  到弹窗里关掉「保护 ASS 特效字幕作者样式」即可恢复覆盖。
- 对测试片内封 STKaiTi ASS（默认 sub-pos / scale / 行距均为中性值）观感无差别，已核对。
- **颜色输入**（1.7）：点色块弹系统调色板（只改 RGB、AA 保留）；文本框仍可手输 `#AARRGGBB`。
  两套格式别混：ASS 字幕文件内部是 `&HAABBGGRR`（BGR 序、AA 反向），mpv.conf 的 `--sub-color` 是 `#AARRGGBB`
  （RGB 序、AA 正常），mpv 用 `MP_ASS_COLOR` 宏在内部转换。UI 用 `#AARRGGBB`；mpv 也接受 `#RRGGBB` 和 `r/g/b`，
  反推时尽量归一，无法识别的手写值原样保留。
- **默认值分层**（1.8）：mpv.conf 模板 / GUI 普通保存是静态 **微软雅黑 / 48 / 纯白**；
  只有安装时跑的 `MpvSettings.exe --default`（和 GUI「全部重置」按钮旁的恢复默认命令行）会探测本机字体，
  有 `STKaiti` 就升级成 **华文楷体 / 52 / 纯白**。手动在弹窗里选的字体永远优先，不会被探测覆盖。
  纯白 `#FFFFFFFF` 是用户 HDR 实测的安全值（不会比视频更亮）。
- 行间距负值收紧双行字幕间距；幅度观感需实测，源码只能证明「生效」，多少合适由用户定。
- 未覆盖项（字符集 / 角度 / ScaleX,Y）仍由字幕作者参数或 mpv 默认决定，本版不提供 UI。

### 1.9 描边宽度出厂默认 1.65 → 1（收细到 ASS 常用值）

mpv 自带 `--sub-outline-size` 默认 **1.65**，实测比 ASS 作者常用的 **1** 略粗。
把本设置程序的出厂默认统一改成 **1**（`sub-outline-size=1`），仍只作用于 SRT 默认样式，ASS/SUP 照旧走作者参数。

改动点（默认值三处同源，缺一不可）：

| 位置 | 文件 | 改动 |
|---|---|---|
| 字段初值 | `Models/SettingsDraft.cs` | `_subOutlineSize = 1.65` → `1`（附注释说明收细理由） |
| 反推缺键回退 | `Models/SettingsDraft.cs` `LoadFrom` | `ParseDouble(m.Get("sub-outline-size", "1.65"), 1.65)` → `"1", 1` |
| 全部重置 | `Models/SettingsDraft.cs` `ResetDefaults` | `SubOutlineSize = 1.65` → `1` |
| UI 说明 | `Views/SubtitleStyleWindow.xaml` | 描述改为「默认 1（ASS 常用值），0 为无描边」 |
| 配置模板 | `12.mpv/portable_config/mpv.conf` | `sub-outline-size=1.65` → `1` |
| 生产配置 | `MediaPlayer/mpv/portable_config/mpv.conf` | `sub-outline-size=1.65` → `1` |

说明：
- `WritePlanBuilder` 用 `F(d.SubOutlineSize)` 直接取草稿值，无独立默认值，无需改。
- `_research/mpv_options.rst` 里的 `Default: 1.65` 是 **mpv 上游文档原文**，属研究资料，保持不动。
- 描边默认值没有按字体探测分层（字体/字号才有 STKaiti→52 的升级），静态 1 即最终值。
- `--default` 与 GUI「全部重置」均由 `ResetDefaults` 取默认，改动后自动写出 `sub-outline-size=1`。

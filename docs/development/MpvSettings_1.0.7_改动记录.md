# MpvSettings 1.0.7 改动记录

> 1.0.6 → 1.0.7。四项改动：**① 截图崩溃修复（关 zimg 后端）**；**② RTX 最低分辨率下限归入本版**（1.0.6 之后的提交，此前 tag 停在 1.0.6）；**③ 右键「截图 → N×M 缩略图」新功能**（纯 mpv lavfi 实现，参数在设置程序里改）；**④ 纯音频封面倍率键屏蔽 + 封面随窗口等比缩放（对齐 MPC-HC）**。
> 记录日期：2026-11-24。产物 md5 `33c10ddf…`（publish / 12.mpv / 生产三处一致）。

## 一、截图崩溃修复：sws-allow-zimg=no

### 定位过程（三次翻案，最终坐实 zimg）

1. 最初按 WER 崩溃偏移 `mpv.exe+0x248ce63` 用 addr2line 解析 mpv.debug，返回 `??` 后凭栈帧猜成 `xmlParseQNameHashed`（libxml2）。
2. 回退 fontconfig 到 expat backend 增量重编（13m36s），新 exe 里该符号数归零，**截图仍崩 5/12** → libxml2 假设证伪。
3. 改用 `nm` + PE 基址 `0x140000000 + RVA` 重算：旧 exe `0x248ce63`、新 exe `0x24ae663` **都落在 zimg 的 `GraphBuilder::impl::build_subgraph`**。
4. 决定性 A/B（同片源、同 exe，各 12 次）：默认 **7/12 崩**，`--sws-allow-zimg=no` **0/12 崩**。生产完整配置三组：默认 6/12、关 zimg 0/12、显式关 RTX HDR 但留 zimg 2/12 → RTX HDR 不是根因，zimg 是。

### 结论与落地

- 根因：本地 zimg 是 `release-3.0.6-253-g67e0603`（`libzimg.cmake` 未钉版本、拉浮动主干），HDR10 截图做色彩转换时崩在 filters 图构建，退出码 `0xC0000005`。
- 修法：`mpv.conf` 加 `sws-allow-zimg=no`，截图转换走 libswscale。原始分辨率截图画质差异可忽略。
- 落地为**定版键**（`Services/DefaultProfile.cs::AddScreenshotFixedKeys`）：该键无对应控件，但恢复默认/导入必须归位，否则用户点一次"恢复默认"崩溃就回来。同 HDR 直通定版键机制。
- 出厂模板 `12.mpv/portable_config/mpv.conf` 与生产副本同步加行。

## 二、RTX 最低分辨率下限归入 1.0.7

`rtx_min_res`（默认 720p，档位 off/sd/720p/1080p/1440p）是 1.0.6 tag 之后的提交 `9789476`，此前 tag `v1.0.6` 仍指向其父提交。本版一并纳入：
- `script-opts/MyMPC.conf` 写 `rtx_min_res`；
- `MyMPC_ui.lua` 用短边下限同时约束 VSR 与 RTX HDR，修复 720×480 DVD 下 d3d11vpp 小尺寸帧崩溃（实测 DVD 0/16、1080P 0/8）。

## 三、右键「截图 → N×M 缩略图」

### 实现

- 新增 `Assets/thumbnails.lua`（同时作为内嵌资源，恢复默认/导入时整文件写回），注册 `script-message thumbnails`。
- `menu.conf` 在"截图"下加子菜单"N×M 缩略图 → 生成缩略图"。
- 纯 mpv lavfi 实现，零外部依赖（生产包无 ffmpeg.exe / ImageMagick / Python）：
  ```
  fps=(n*m)/duration → scale → drawtext(时间戳) → tile(cols×rows) → pad(banner) → drawtext(标题/信息/水印)
  ```
  启动隐藏 mpv 子进程 `--vo=image` 离屏输出 PNG，不动当前播放窗口、不改当前 vf。
- 尺寸算法对齐 MPC-HC：给**总宽度上限**（默认 1200px），单格宽 = `(max_width - gap*(cols+1))/cols`，单格高 = 单格宽 × 视频显示宽高比（`dwidth/dheight`，含 SAR）。间距默认 4px。
- Banner：第一行文件名（28px，绿色，`max_glyphs=52` 防溢出），第二行"时长 · 分辨率 · 网格"（24px），右上角 MyMPC 水印（20px，绿色），无生成日期。
- 每格右下角 `%{pts:hms}` 时间戳标注。
- mpv.exe 路径：`script-opts/thumbnails.conf` 的 `mpv_exe=` 由设置程序探测写入（口径同 Assoc 的 `<App Paths 根>\MediaPlayer\mpv\mpv.exe`）；为空时 Lua 回退 `~~/../mpv.exe`。

### 关键坑（都已解决）

- lavfi filtergraph 里 Windows 路径盘符冒号要写成 `'C\:/Windows/Fonts/msyh.ttc'`，否则被当选项分隔符。
- drawtext 文本里的 `:`, `,`, `|`, `'` 都要转义或替换（时间 `03:17` 的冒号曾导致解析失败）；统一走 `lavfi_text()`。
- 字体必须显式 `fontfile=`：生产包 fontconfig 无默认配置，`font=Sans` 会报 "Cannot find a valid font"。

### 配置项

设置程序"截图"页新增 N×M 缩略图卡片：网格行列（各 1–8，默认 3×3）、总宽度上限（默认 1200）、间距（默认 4）。写 `script-opts/thumbnails.conf`（`BuildThumbnailsConf`）。

## 四、纯音频封面：屏蔽倍率键 + 封面随窗口等比缩放

- `input.conf`：Alt+`/1/2/3` 从裸 `set window-scale` 改为 `script-message mpv-scale-key <值>`；`ShortcutCatalog.cs` 的默认绑定同步，避免设置程序"恢复默认"把裸命令写回去。
- `fit90.lua` 新增 `mpv-scale-key` 处理器：检测到 `current-tracks/video/image == true`（纯音频有封面）时，Alt+`/1/2/3/4` **全部拦截**并提示"请拖动窗口边框调整大小（封面会等比缩放）"；无画面纯音频同样拦截。
- `cover-art.conf`：`video-unscaled=downscale-big`（只缩不放）→ `no`（随窗口等比缩放），对齐 MPC-HC。`SettingsDraft.CoverOriginal` 出厂默认同步改 false；设置程序文案说明"默认关闭=跟随窗口等比缩放，开启=固定原始大小"。
- 封面比例由 mpv `keepaspect`（默认 yes）保证；`keepaspect-window=no` 只管窗口矩形，不冲突。

## 五、落地清单

| 文件 | 改动 |
|---|---|
| `MpvSettings.csproj` | 版本 1.0.6→1.0.7；内嵌 `Assets/thumbnails.lua` |
| `Views/AboutView.xaml` | 关于页版本 1.0.7 |
| `docs/发布说明.md` | tag 规范 1.0.7 |
| `Services/DefaultProfile.cs` | `AddScreenshotFixedKeys`（sws-allow-zimg=no 定版键）；缩略图脚本进定版资产 |
| `Services/ConfigLocator.cs` | `ThumbnailsConf` / `ThumbnailsLua` / `ThumbnailsLuaFileExists` |
| `Models/ConfigSnapshot.cs` | 装载 `Thumbnails` ConfFile |
| `Models/SettingsDraft.cs` | 缩略图字段/属性/反推/默认值；`CoverOriginal` 默认 false |
| `Services/WritePlanBuilder.cs` | `BuildThumbnailsConf` / `ThumbnailsLuaSource`；uiChanged 纳入 Thumb* |
| `Models/ShortcutCatalog.cs` | 四个倍率键默认命令改 script-message |
| `Views/ScreenshotView.xaml` | N×M 缩略图设置卡片 |
| `Views/PlaybackView.xaml` | 封面/倍率键文案更新 |
| `Assets/thumbnails.lua` | 新增（缩略图脚本） |
| 12.mpv / 生产 portable_config | mpv.conf 加 sws-allow-zimg=no；menu.conf 加缩略图子菜单；fit90.lua / input.conf 更新；cover-art.conf 改 no；新增 thumbnails.lua + thumbnails.conf |

## 六、验证

- `dotnet publish -c Release -r win-x64`：0 错误 0 警告；EXE ProductVersion `1.0.7+9789476`，三处 md5 一致 `33c10ddf…`。
- 生产 `--default --dry-run`：只报 cover-art.conf 整文件重写（换行符归一化，非实质差异），无异常写入。
- 缩略图脚本：实机生成 `00000001.png` 成功（3×3 均匀取帧 + 时间戳 + banner + 水印），用户确认"成功了"，仅要求第二行字号加大（已 18→24，banner 高 56→72，标题 30→28 且加 max_glyphs=52）。

## 七、已知限制

- 缩略图"均匀取全片"需解码近乎整部视频，长片/4K 耗时较长（MPC-HC 同样要扫全片）。当前字号调整后的脚本已同步三处，但未在自动化环境重跑出图（上次成功是用户手动右键触发）；字号为纯参数改动，逻辑不变。
- zimg 主干真 bug 未修（仍会崩），本版通过关 zimg 后端规避。若未来钉 zimg 到正式版 `release-3.0.6` 验证通过，可考虑恢复 zimg 画质。
- DV P4 两个补丁（dovi-p4-fix / dovi-p4-metadata-fix）仍长期自行维护，上游不会修。

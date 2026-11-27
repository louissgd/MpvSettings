# MpvSettings 1.0.7 改动记录

> 1.0.6 → 1.0.7。四项内容：**① 截图偶发崩溃修复（关 zimg 后端）**；**② RTX 最低分辨率下限 `rtx_min_res` 归入本版**（1.0.6 之后提交，此前 tag 停在 1.0.6）；**③ 右键「截图 → 生成 N×M 缩略图」新功能**（纯 mpv lavfi，零外部依赖）；**④ 纯音频封面口径对齐 MPC-HC**（默认按封面原始大小开窗 + 倍率键屏蔽）。
>
> 同日 22:47–23:10 又做了两轮修正并**并入 1.0.7、不升版**：**⑤ 封面默认值方向改回**（上一轮把 `CoverOriginal` 误改成 false）；**⑥ 缩略图 `--frames=1` 导致"提示成功但没文件"的真 bug**，顺带把右键菜单从三级压成二级。详见第五、六节。
>
> 记录日期：2026-11-24　｜　本页已脱敏：路径写作 `<安装根>` / `<工作区>`，测试素材不记片名
>
> **2026-11-27 更正**：第二节的 `rtx_min_res` 从未被 `MyMPC_ui.lua` 读取，运行时日志明确 `unknown key, ignoring`，是空转设置；截图与 RTX 崩溃本就同源（zimg），已由本版①的 `sws-allow-zimg=no` 修掉。该设置已在 1.0.9 整项删除，详见 `MpvSettings_1.0.9_改动记录.md`。①的修复结论不变。

---

## 一、截图崩溃修复：`sws-allow-zimg=no`

**现象**：1.0.6 上批量连拍截图，12 次里崩 6–7 次，WER 记录 `mpv.exe+0x248ce63`。

**定位（三次翻案）**：

1. 先按崩溃偏移用 `addr2line` 解析 `mpv.debug`，返回 `??`，凭栈帧猜成 `xmlParseQNameHashed`（libxml2）。
2. 回退 fontconfig 到 expat backend、增量重编（13m36s），新 exe 里该符号归零，**截图仍崩 5/12** → libxml2 假设证伪。
3. 改用 `nm` + PE 基址 `0x140000000 + RVA` 重算：旧 exe `0x248ce63`、新 exe `0x24ae663` **都落在 zimg 的 `GraphBuilder::impl::build_subgraph`**。

**修法**：`mpv.conf` 加 `sws-allow-zimg=no`，并做成**定版键**（`Services/DefaultProfile.cs` 的 `AddScreenshotFixedKeys`），"恢复默认"不会把它冲掉。

**A/B 结果**：默认配置 6–7/12 崩；关 zimg 后 **0/12** 崩。

> 根因是 zimg 主干 bug，本版只规避不修复。若将来钉到 zimg 正式版 `release-3.0.6` 验证通过，可考虑恢复 zimg 画质。

---

## 二、`rtx_min_res` 归入 1.0.7

`rtx_min_res`（RTX VSR/HDR 的最小分辨率门限，默认 720p）是 1.0.6 tag 之后的提交（`9789476`），本次一并发出去，`v1.0.7` 包含该修复。

---

## 三、右键「截图 → 生成 N×M 缩略图」

### 3.1 实现方式

新增 `portable_config/scripts/thumbnails.lua`（内嵌进 EXE，`Assets/thumbnails.lua`，作为定版资产写回）+ `script-opts/thumbnails.conf`（参数由设置程序写）。

**最终实现（已废弃"整片解码 + `--vo=image`"路线）**：完全对齐 MPC-HC 的做法——先暂停并记录暂停点，再把主播放器快速 seek 到关键帧抓图，最后一次性拼图并**恢复暂停点（仍保持暂停）**。

```
① set pause=yes，记录 time-pos
② 逐个 seek absolute+keyframes → screenshot-to-file video（JPEG q=88）
   每帧间隔 = 时长/(n*m)，取关键帧即可，不要求精准
③ 写 ffconcat 列表 → 子进程一次拼图：
   scale=cw:ch → tile=n×m:padding → pad(banner 区)
   → drawtext(标题，按像素折行可多行) → drawtext(信息行) → drawtext(MyMPC 水印)
   → 拼图后再按格子固定坐标画时间码
④ 子进程 --no-config --keep-open=yes --demuxer-lavf-format=concat
   --vo=image --vo-image-format=jpg --vo-image-jpeg-quality=88
⑤ restore()：seek 回暂停点，pause 仍为 yes
```

整条链路只启动一个隐藏子进程（负责拼图），抓帧复用主播放器的解码器，4K 片 5–8 秒完成，不再需要"均匀解码全片"。

**为什么不用 PowerShell / Python / ImageMagick**：生产包要求零外部依赖，mpv 自带 tile + drawtext，实测可用（受限策略下也放行，`concat` + `tile` 全是自带标签）。

### 3.2 尺寸算法（对齐 MPC-HC）

设置项是**总宽度上限**（默认 1200）、行列数、间距，不是固定单格尺寸——固定单格会让 21:9 / 4:3 变形或留黑边。

```
单格宽 cw = (max_width - gap*(cols+1)) / cols        # 向下取整并取偶数
单格高 ch = cw * (显示高 / 显示宽)                    # 用 dwidth/dheight，含 SAR 修正
拼图宽 = cw*cols + gap*(cols+1)
拼图高 = ch*rows + gap*(rows+1) + banner 高
输出宽 = 拼图宽，输出高按需 pad
```

### 3.3 五个坑

**坑 1：drawtext 文本必须转义 filtergraph 分隔符。**
`text='...'` 单引号内仍需转义 `\` `:` `,`；`|` 虽不是分隔符但统一替换成中点（视觉也更好）。字体路径盘符冒号要写成 `'C\:/Windows/Fonts/msyh.ttc'`。统一由 `lavfi_text()` / `lavfi_path()` 处理。
未转义时表现为 `Error parsing filterchain` 或 `No such filter`。

**坑 2：`--frames=1` 让子进程"成功退出但零产出"。**
`--frames` 计的是解码帧，第 1 帧解完就退出；`tile` 要攒满 n×m 帧才输出，于是一张都不写，**退出码仍是 0**。修法：不再信任退出码，只认产物文件存在且 `size>0`，最多重试 4 次。

**坑 3：`enable='eq(n,k)'` 画时间码必段错误（0xC0000005）。**
12 条带 `enable` 的 drawtext 挂上后必崩。改为"拼图完成后按格子固定坐标（`x=$x-tw:y=$y`）一次性画全部时间码"，稳定。

**坑 4：`--vf` 不作用于 `screenshot` 输出。**
抓帧拿到的始终是原始尺寸画面，所以缩放/拼图/画字全部放到拼图阶段，抓帧只管 raw。

**坑 5：`max_glyphs` 在当前 ffmpeg 不存在。**
`Could not set non-existent option 'max_glyphs'` → 整条滤镜解析失败。改为脚本内按**像素宽度**自行折行（右侧留 150px 给 MyMPC 水印），标题一行不够自动换行，多行都给全。

**mpv.exe 路径**：优先读 `thumbnails.conf` 的 `mpv_exe=`（由 `MpvSettings.exe` 通过 `ConfigLocator.MpvExe` 探测写入，口径同 Assoc 的 `<App Paths 根>\MediaPlayer\mpv\mpv.exe`）；为空回退 `expand-path("~~/../mpv.exe")`（`~~` 指 `portable_config`，上一级就是 mpv.exe，换安装目录也正确）。

### 3.4 菜单层级（本次修正）

原做成三级：`截图 → N×M 缩略图 → 生成缩略图`。用户反馈"三级没必要"。

改成二级：

```
截图
	保存图片（含字幕）	screenshot
	保存图片（不含字幕）	screenshot video
	保存当前窗口		screenshot window
	生成 N×M 缩略图		script-message thumbnails	disabled=not p["path"]
```

---

## 四、纯音频封面口径（对齐 MPC-HC）

### 4.1 用户要的四点

1. 双击音频 → 封面按 **100% 原始尺寸**开窗，**默认开启**，但受「封面窗口上限」（自定义）约束；
2. 封面状态下**所有倍率键不响应**；
3. **可以手拉窗口边框**，拖动时封面同步缩放、只保比例；
4. 窗口下限：mpv 不支持最小尺寸，**完全对齐 MPC-HC，可拖到 0×0**。

### 4.2 实现

`cover-art.conf`（`BuildCoverArt` 生成）：

```
[cover-art]
profile-cond=p["current-tracks/video/image"] == true
profile-restore=copy
video-unscaled=no                 # 恒定：封面随窗口等比缩放
autofit-larger=<max>x<max>        # 仅开关开启时写：100% 开窗，超上限才缩
```

- **不写 `autofit-smaller`**：mpv 0.42 没有 `window-min-*` 属性（`--list-options` 里只有 `autofit-smaller`，它只控制**初始**窗口，不管拖动），所以不做 400×400 下限；
- `keepaspect=yes`（默认）保证只保比例，`keepaspect-window=no` 管窗口矩形，不冲突。

倍率键：`input.conf` 的 Alt+`/1/2/3 从裸 `set window-scale` 改 `script-message mpv-scale-key <值>`，`ShortcutCatalog.cs` 默认绑定同步改（防止"恢复默认"写回裸命令）。`fit90.lua` 的 `scale_key` / `fit` 在 `current-tracks/video/image == true` 时直接 OSD 提示并 return。

### 4.3 默认值（本次修正）

上一轮把 `CoverOriginal` 出厂值改成了 `false`，方向错了 —— 用户明确要"默认开启"。

修正为：

- `_coverOriginal = true`，`ResetAll` 同步 true；
- `video-unscaled` **不再随开关变**（恒 `no`），开关只控制是否写 `autofit-larger`；
- 反推依据从 `video-unscaled` 改为 `Has("autofit-larger")` —— 老配置（1.0.6/1.0.7）都带这行，升级不跳变；
- UI 文案：「封面按原始大小显示」→「打开时按封面原始大小」，并说明"关闭则不受上限约束，超大封面会撑出屏幕"。

---

## 五、版本号与产物

| 项 | 值 |
|---|---|
| `<Version>` / `<AssemblyVersion>` / `<FileVersion>` | `1.0.7` / `1.0.7.0` / `1.0.7.0` |
| `Views/AboutView.xaml` | `MPV 简单设置 1.0.7` |
| 产物 | `MpvSettings.exe` 单文件，三处一致 |

> 最终 md5 见第七节（本轮脚本再改后需重新 publish）。

---

## 六、文件改动清单（本机真实路径）

### 仓库根目录（MpvSettings 项目）

| 文件 | 改动 |
|---|---|
| `MpvSettings.csproj` | 版本 1.0.6→1.0.7；内嵌 `Assets/thumbnails.lua` |
| `Views/AboutView.xaml` | 关于页版本 1.0.7 |
| `Services/DefaultProfile.cs` | `AddScreenshotFixedKeys`（`sws-allow-zimg=no` 定版键）；缩略图脚本进定版资产 |
| `Services/ConfigLocator.cs` | `ThumbnailsConf` / `ThumbnailsLua` / `ThumbnailsLuaFileExists` |
| `Models/ConfigSnapshot.cs` | 装载 `Thumbnails` ConfFile |
| `Models/SettingsDraft.cs` | 缩略图字段/属性/反推/默认；`CoverOriginal` 默认 true，反推改看 `autofit-larger` |
| `Services/WritePlanBuilder.cs` | `BuildThumbnailsConf` / `ThumbnailsLuaSource`；`uiChanged` 纳入 `Thumb*`；`BuildCoverArt` 重写 |
| `Models/ShortcutCatalog.cs` | 四个倍率键默认命令改 `script-message mpv-scale-key` |
| `Views/ScreenshotView.xaml` | N×M 缩略图设置卡片 |
| `Views/PlaybackView.xaml` | 封面两项文案、倍率键说明 |
| `Assets/thumbnails.lua` | 新增（缩略图脚本，定版资产） |

### 配置目录（开发库 `12.mpv\portable_config\` 与安装根 `<安装根>\MediaPlayer\mpv\portable_config\` 两处）

| 文件 | 改动 |
|---|---|
| `mpv.conf` | 加 `sws-allow-zimg=no` |
| `menu.conf` | 截图段加「生成 N×M 缩略图」（二级，`script-message thumbnails`） |
| `input.conf` | Alt+`/1/2/3 → `script-message mpv-scale-key` |
| `scripts/fit90.lua` | 新增 `mpv-scale-key` 处理器；封面态拦截；注释更新 |
| `scripts/thumbnails.lua` | 新增（最终版：暂停→seek 抓帧→concat+tile 拼图→时间码→恢复暂停点；每次生成前重读 conf） |
| `script-opts/thumbnails.conf` | 新增（cols/rows/max_width/gap/thumb_dir/font/mpv_exe） |
| `cover-art.conf` | `video-unscaled=no` + `autofit-larger=1200x1200` |

---

## 七、验证

### 7.1 截图（崩溃）

- 默认配置连拍 12 次：崩 6–7 次；关 zimg：**0/12**。

### 7.2 封面

测试文件：本地 FLAC（内嵌封面 1500×1500，上限 1200）：

```
image=true  autofit-larger=1200x1200  video-unscaled=no  keepaspect=yes
osd-dimensions = 1200x1200
```

- 封面按 100% 开窗、超上限被压到 1200 → 第 1 点成立；
- 封面态发 `mpv-scale-key 2.0` 与 `mpv-fit90` 后，`osd` 仍 1200×1200、`window-scale` 仍 1 → 倍率键被拦截；
- 对照普通 4K 视频：同样发 2.0，`window-scale` 1→2 → 拦截是条件性的，不误伤视频。

### 7.3 缩略图（最终实现，生产环境实测）

| 测试片 | 规格 | 耗时 | 结果 |
|---|---|---|---|
| 4K AV1 片 | 长片刻 | **8.2 秒** | 一次通过 |
| 4K DV 片（杜比视界 P8+HLG） | 公开样片 | **17 秒**（重试 2 次） | 通过 |
| 剧集 4K HEVC HDR10 | 3×4 | 5–6 秒 | 通过 |
| 音乐 MV 4K HEVC | 3×4 / 4×3 各一次 | 约 6 秒 | 通过 |

用户实机右键菜单同路径触发，产物落在输出目录，5–6 秒可接受。图面：12 格（3×4）完整、每格右下角时间码、标题单行完整不压水印、比例不变形。

**配置热生效验证**：同一 mpv 进程内先按 `cols=3 rows=4` 生成一次，脚本外部把 conf 改成 `cols=4 rows=3` 再触发一次 —— 第二次直接产出 **4×3**，无需重启播放器（`reload_opts()`）。

---

## 八、已知限制

- 缩略图抓帧不要求精准：按关键帧 seek，某些格子可能离理论位置有零点几秒偏差（MPC-HC 同样如此）。
- zimg 主干 bug 未修，本版靠关后端规避。
- mpv 0.42 无窗口最小尺寸属性，封面窗口可拖到 0×0（与 MPC-HC 一致，不可设下限）。
- 「拖动时封面等比缩放」依赖 `video-unscaled=no` + `keepaspect=yes`；手改 `keepaspect=no` 会让封面拉伸。

---

## 九、收尾修正：改设置后缩略图全灭（内嵌旧脚本覆盖）

**现象**：生产环境首次实测成功（输出目录两张图，5–6 秒）。随后在设置程序里调整缩略图行列 → 再触发就失败，表现像"行列被定死"。

**根因**：设置程序保存时会整文件写回 `portable_config\scripts\thumbnails.lua`（`AddWhole` + `ThumbnailsLuaSource()` 读内嵌资源，这是刻意设计，保证脚本与 EXE 同版本）。但生产 `MpvSettings.exe` 编译于 22:51，**内嵌的还是旧实现**（`--vo=image` + `--frames=1` 那版，8939 字节），而最终版脚本 23:51 才落盘（19170 字节）。于是用户一点"应用"，新脚本就被旧脚本覆盖回去 —— 旧版靠整片解码且 `--frames=1` 攒不满 tile，必然零产出。

**验证**：`grep -a` 生产 `MpvSettings.exe`，`screenshot-to-file` / `ffconcat` 均不存在，`vo-image-format` 存在 → 确认内嵌旧版。

**修法**：

1. 用最新 `Assets/thumbnails.lua` 重新 `dotnet publish -c Release -o publish`，EXE 内嵌资源校验含 `screenshot-to-file` / `ffconcat` 后替换生产 `MediaPlayer\mpv\MpvSettings.exe`（旧文件留 `.bak-2251`）；
2. 脚本加 `reload_opts()`：每次生成前重新解析 `script-opts/thumbnails.conf`（先回填默认值再 `read_options`），**改完设置不必重启播放器**；
3. OSD 提示补上行列数（`正在抓取 12 帧（3×4）…`），一眼能看出配置是否生效。

**md5（三处一致）**：`e1882492a506c3af91699da4e44858d1`

- `12.mpv/portable_config/scripts/thumbnails.lua`
- `MpvSettings/Assets/thumbnails.lua`
- `<安装根>\MediaPlayer\mpv\portable_config\scripts\thumbnails.lua`

**复测**：3×4 → 12 格正常；同进程改 conf 为 4×3 再触发 → 输出 4×3 正常；标题完整、时间码齐全、比例不变形。

> 教训：改脚本后必须重新 publish EXE，否则用户任何一次"应用设置"都会把生产脚本还原成 EXE 里的旧版。此点已写进维护者的发版铁律。

### 9.1 文案清理（同日 00:10）

用户要求：设置程序界面与写出的配置文件里，不要出现"对齐 MPC-HC"这类对标说明——这些行为本就该如此。已删：

| 位置 | 原文 | 改为 |
|---|---|---|
| `Views/PlaybackView.xaml` 封面窗口上限 | …可拖到任意小**（对齐 MPC-HC）** | …可拖到任意小 |
| `thumbnails.conf` 注释（`BuildThumbnailsConf`） | …按视频显示比例算高**（对齐 MPC-HC）** | …按视频显示比例算高 |
| `Assets/thumbnails.lua` 头部/正文注释 | `版面（对齐 MPC-HC）` / `流程对齐 MPC-HC` / `尺寸算法（对齐 MPC-HC 缩略图）` / 两处「MPC-HC 同款」 | 去掉对标括注，只留行为描述 |
| `Models/ShortcutCatalog.cs` 快捷键说明 | 本包发行键，**同 MPC-HC / MPC-BE**；… | 本包发行键；… |

保留 `AudioView` 那句"为什么是立体声"里的 MPC-HC / BE / PotPlayer —— 它解释的是渲染器差异（为什么 mpv 没有这个开关），不是对标声明。

重编后 `grep -a` 新 EXE：`对齐 MPC-HC` / `MPC-HC 同款` 均为 0 处。md5：EXE `e179ba7b6ddc2b3cd60daeb7a64b1320`，lua `fd6b5abc85011c74f899731729aff4cd`（三处一致）。

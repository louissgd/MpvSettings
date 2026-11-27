# MpvSettings 1.0.9 改动记录

> 1.0.8 → 1.0.9。单一改动：**删除「RTX 最低分辨率」`rtx_min_res`**（1.0.7 引入，实际从未生效）。
>
> 结论先说在前面：**截图崩溃和 RTX 崩溃是同一个 zimg bug**，1.0.7 的 `sws-allow-zimg=no` 已经把两条路径一起修掉。`rtx_min_res` 只写进配置、Lua 从不读取，是彻头彻尾的空转设置，没有保留价值。
>
> 记录日期：2026-11-27

---

## 一、为什么要删

### 1.1 这个设置到底改了什么

1.0.7 的 commit `9789476`（"修复 d3d11vpp 小尺寸帧崩溃"）只动了 5 个文件：

| 层 | 改动 |
|---|---|
| `Models/SettingsDraft.cs` | `RtxMinResValues/Labels`（`off|sd|720p|1080p|1440p`，默认 720p）、`_rtxMinResIndex`、`RtxMinResIndex/Options/Interactive`、`NotifyRtxState` 通知、`LoadFrom()` 反推、`ResetDefaults()` |
| `Services/WritePlanBuilder.cs` | `BuildMyMpcConf()` 无条件写 `rtx_min_res=<档位>` |
| `Views/VideoView.xaml` | 「RTX 最低分辨率」下拉 + 已知问题文案改写 |
| 排查文档 / 功能说明 | 文案 |

**Lua 侧一行都没改。** commit 里没有 `Assets/MyMPC_ui.lua`，也没有任何运行时脚本。文档写的「`MyMPC_ui.lua` 新增 `rtx_min_res`，按短边判定，`rtx_big_enough = min(w,h) >= 阈值`」从未实现。

### 1.2 三重空转的硬证据

1. **运行时明确报未知键**：生产 mpv 日志里 16 份出现同一行——
   `[   0.014][w][MyMPC_ui] script-opts/MyMPC.conf:9 unknown key 'rtx_min_res', ignoring`
   （例：`_verify/audiotest/<验证日志>.log`）。mpv 的 read_options 直接忽略它。
2. **三处 Lua 全文都没有这个键**：仓库 `Assets/MyMPC_ui.lua`、开发包与生产配置目录下的 `scripts/MyMPC_ui.lua`，md5 全为 `fe088abe8fee5d7f5e427ff7504c3205`，搜 `rtx_min_res` / `big_enough` / `min_res` 均为 0 处。
3. **连"改了会写盘"都不成立**：`RtxMinResIndex` 既不在 `Models/WritePlan.cs` 的 `DraftBaseline.ManagedProps` 白名单，也不在 `Services/WritePlanBuilder.cs` 的 `uiChanged` 字段表。单改这个下拉不会被识别为改动，不触发写盘；只有别的设置变更顺带整文件重写 `MyMPC.conf` 时，才会把这个键写进去——然后被 Lua 忽略。

用户实测印证：DVD 上 RTX HDR / VSR 依旧生效，NVIDIA App 侧照常亮灯。不是"限制设得太松"，是**根本没限制**。

### 1.3 崩溃到底是谁修好的

| 时间 | 事实 |
|---|---|
| 11-21 | 9 个 dump，8 个故障偏移 `mpv.exe+0x248CE63`，判为 RTX/d3d11vpp 竞态 |
| 11-24 20:45 | 截图崩溃 dump 偏移 `mpv.exe+0x248ce63`，与 11-21 那批**完全相同**；当时误符号化成 `xmlParseQNameHashed` |
| 11-24 晚 | A/B 实测：DVD 720×480 纯 VSR 10/20、纯 HDR 11/20、共挂 10/20、全关 0/20；1080P 挂 446 次零崩。据此翻案为「d3d11vpp 小尺寸帧初始化是根因」，做出 `rtx_min_res` |
| 1.0.7（11-24 深夜） | 换 `nm` + PE 基址 `0x140000000 + RVA` 重算：旧 exe `0x248ce63`、换 expat 后端重编的新 exe `0x24ae663`，**都落在 zimg `GraphBuilder::impl::build_subgraph`**。截图 A/B：默认 6–7/12 崩，`sws-allow-zimg=no` 后 **0/12** |
| 1.0.8 实机（11-27） | 用户确认崩溃缓解，但 DVD 上 RTX 照常生效 → d3d11vpp 仍然挂载，`rtx_min_res` 没起作用 |

**定性**：不是"截图修复间接修好了 RTX 崩溃"，是**两条触发路径撞到同一个 bug**——截图触发 VO 重建、小帧 + d3d11vpp 挂载都会去构建 zimg 缩放图，zimg 主干在建图时空指针。11-24 的翻案找对了触发条件，认错了根因。真正的修复是 1.0.7 的定版键 `sws-allow-zimg=no`，与分辨率下限无关。

**边界**：zimg 主干 bug 没有修，本版只是关掉后端规避。将来若钉 zimg 正式版并验证通过，可再考虑恢复画质；在那之前 `sws-allow-zimg=no` 是定版键，"恢复默认"不会冲掉，别动它。

## 二、改动清单

| 文件 | 改动 |
|---|---|
| `Models/SettingsDraft.cs` | 删 `RtxMinResValues/Labels`、`_rtxMinResIndex`、`RtxMinResIndex/Options/Interactive`、`NotifyRtxState` 与两个 RTX setter 里的对应通知、`LoadFrom()` 反推段、`ResetDefaults()` 赋值 |
| `Services/WritePlanBuilder.cs` | `BuildMyMpcConf()` 删 `hmin` 计算与 `rtx_min_res=` 写入行 |
| `Views/VideoView.xaml` | 删「RTX 最低分辨率」OptionRow；「已知问题 · 等上游修」改为「崩溃问题 · 已规避（1.0.7）」，文案改成 zimg 同源结论 |
| `MpvSettings.csproj` / `Views/AboutView.xaml` | 版本 1.0.8 → 1.0.9 |
| `docs/功能说明.md` | 删「RTX 最低分辨率」配置表行 |
| 开发包与生产配置目录下的 `script-opts/MyMPC.conf` | 删 `rtx_min_res=720p`（Lua 本来就忽略，纯清理） |
| 本记录 + `mpv偶发崩溃排查_20261121.md` | 补第四次翻案；`更新计划总览.md` 加空转设置铁律 |

**未改动**：`Assets/MyMPC_ui.lua`（本来就没有相关代码）、`sws-allow-zimg=no` 定版键、VSR 上限 `rtx_vsr_limit`、RTX HDR 峰值、任何快捷键绑定。

## 三、版本号与产物

| 项 | 值 |
|---|---|
| `<Version>` / `<InformationalVersion>` | `1.0.9` |
| `<AssemblyVersion>` / `<FileVersion>` | `1.0.9.0` |
| `Views/AboutView.xaml` | `MPV 简单设置 1.0.9` |
| `MpvSettings.exe` | 519,382 B，md5 `98bc45d9f30bb1d4f56d966524d3f278`；publish / 开发包 / 生产三处一致（22:04 按用户反馈重编，见第六节） |
| 元数据实测 | `FileVersion=1.0.9.0` / `ProductVersion=1.0.9+bf3691a…` |

旧 EXE 备份：`<备份目录>/`（三份 1.0.8，均 `2654377a8d3795dffd7205193af3fb88`）。

## 四、验证

- `dotnet build -c Release --nologo`：**0 警告 0 错误**；`dotnet publish -c Release -r win-x64 -o publish`：成功。
- 源码全局搜索 `rtx_min_res|RtxMinRes|_rtxMinRes`（`*.cs/*.xaml/*.conf/*.lua/*.csproj`）：**0 处**。
- 新 EXE 二进制搜索：`rtx_min_res` / `RtxMinRes` 的 ASCII 与 UTF-16LE 字节各 **0 处**。
- 三处 EXE md5 一致（见上），版本元数据 1.0.9.0。
- 临时配置目录跑 `--default`：退出码 **0**，写出的 `script-opts/MyMPC.conf` 共 9 行（含 `rtx_vsr/rtx_hdr/rtx_vsr_limit/rtx_hdr_peak`），**无 `rtx_min_res`**；目录内 grep 该键 0 处。生产配置未被 `--default` 污染。
- 用户侧需确认（本轮未重跑）：DVD/VCD 上 RTX HDR / VSR 仍生效且不崩。

## 五、发版状态

- 用户 2026-11-27 22:00 确认「崩溃缓解 + DVD 上 RTX HDR/VSR 仍生效 + 开启几乎无延迟」，本机实测硬门槛满足。
- 本轮已按检查单走完发版：版本号三处一致 → 本目录脱敏记录 → commit/push → tag `v1.0.9` → Release 仅附单个 `MpvSettings.exe`（正文套发布说明模板）。
- 国内网盘链接属 MyMPC 整包，本次不动。

## 六、收尾措辞修正（2026-11-27 22:04，用户确认发版前）

用户实机反馈：RTX HDR 现在几乎看不到开启延迟，原「播放约 2 秒后才变亮是在等输出色彩协商，MPCVR 为渲染器内建、无此延迟」已过时。`Views/VideoView.xaml` 的「崩溃问题 · 已规避（1.0.7）」说明末句改为「RTX HDR 现在几乎看不到开启延迟」，并顺手把「建缩放图时空指针」改成「构建缩放图时触发空指针」，去掉 MPCVR 对标句。

按检查单的资产铁律重新 `dotnet publish` 并替换三处 EXE。最终 md5 见第三节：`98bc45d9f30bb1d4f56d966524d3f278` / 519,382 B。EXE 内嵌文案校验：新句 UTF-8 命中 1 次、旧「播放约 2 秒后才变亮」0 次、`rtx_min_res` 0 次。

**本节并入 1.0.9、不升版**；发版动作（commit / tag `v1.0.9` / Release）在用户确认后执行。

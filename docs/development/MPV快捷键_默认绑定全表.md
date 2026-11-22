# MPV 默认快捷键全表（本机实测）

来源：`12.mpv\mpv.com v0.42.0-38-gbcde6b93c-dirty`（2026-11-11 构建）。
方法：`mpv.com --no-config --script=dumpbind.lua` 读取 mpv 运行时属性 `input-bindings`，导出全部内置默认绑定 **220 条**；再用 `--input-conf=portable_config\input.conf` 导出实际生效 **230 条**，两者差集即本包自定义的 10 行。
交叉验证：`mpv-player/mpv@v0.42.0/etc/input.conf`（14739 B / 265 行，模板全注释，编译时去掉 `#` 烤进二进制）——键名与命令一致。

> **读表注意**：默认绑定是 `is_weak=true` 的弱绑定，`input.conf` 里的行是强绑定（priority 15），同键时强绑定直接覆盖弱绑定。`--input-default-bindings` 默认为 `yes`（本包 `mpv.conf` 未关闭），所以这 220 条全部默认生效。

## 一、本包已自定义的 10 行（`portable_config\input.conf`）

| 键 | 命令 | 原默认绑定 | 说明 |
|---|---|---|---|
| `F5` | `script-message tone-ab` | —（原本未绑定） | 色调增强 A/B 对比（tone-ab.lua） |
| `F6` | `script-message hdr-status` | —（原本未绑定） | 查看 HDR 状态 |
| `F7` | `script-message hdr-toggle` | —（原本未绑定） | HDR 直通 / 色调映射切换 |
| `F8` | `script-message hdr-restore` | `show-text ${playlist}` | 恢复系统 HDR 原始状态 |
| `Ctrl+b` | `af toggle bassmax` | —（原本未绑定） | BassMax 增强 / 旁通 |
| Alt+&#96; | `set window-scale 0.25` | —（原本未绑定） | 窗口缩放 25% |
| `Alt+1` | `set window-scale 0.5` | `set window-scale 1` | 窗口缩放 50% |
| `Alt+2` | `set window-scale 1.0` | `set window-scale 2` | 窗口缩放 100% |
| `Alt+3` | `set window-scale 2.0` | —（原本未绑定） | 窗口缩放 200% |
| `Alt+4` | `script-message mpv-fit90` | —（原本未绑定） | 窗口适配 90%（fit90.lua） |

**被覆盖的三处**：`F8` 原为 `show-text ${playlist}`（显示播放列表，右键菜单「播放列表」仍可打开）；`Alt+1` / `Alt+2` 原为 `set window-scale 1` / `2`，与现写法 `1.0` / `2.0` 语义等价，无功能损失。

## 二、内置默认绑定 220 条（按用途分组，顺序保持 mpv 原始加载顺序）

### 播放 / 定位（54 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `RIGHT` | `seek  5` | seek 5 seconds forward |
| `LEFT` | `seek -5` | seek 5 seconds backward |
| `UP` | `seek  60` | seek 1 minute forward |
| `DOWN` | `seek -60` | seek 1 minute backward |
| `Shift+RIGHT` | `no-osd seek  1 exact` | seek exactly 1 second forward |
| `Shift+LEFT` | `no-osd seek -1 exact` | seek exactly 1 second backward |
| `Shift+UP` | `no-osd seek  5 exact` | seek exactly 5 seconds forward |
| `Shift+DOWN` | `no-osd seek -5 exact` | seek exactly 5 seconds backward |
| `HOME` | `seek 0 absolute` | seek to the start |
| `PGUP` | `add chapter 1` | seek to the next chapter |
| `PGDWN` | `add chapter -1` | seek to the previous chapter |
| `Shift+PGUP` | `seek  600` | seek 10 minutes forward |
| `Shift+PGDWN` | `seek -600` | seek 10 minutes backward |
| `[` | `multiply speed 1/1.1` | decrease the playback speed |
| `]` | `multiply speed 1.1` | increase the playback speed |
| `{` | `multiply speed 0.5` | halve the playback speed |
| `}` | `multiply speed 2.0` | double the playback speed |
| `BS` | `set speed 1` | reset the speed to normal |
| `Shift+BS` | `revert-seek` | undo the previous (or marked) seek |
| `Shift+Ctrl+BS` | `revert-seek mark` | mark the position for revert-seek |
| `q` | `quit` |  |
| `Q` | `quit-watch-later` | exit and remember the playback position |
| `p` | `cycle pause` | toggle pause/playback mode |
| `.` | `frame-step` | advance one frame and pause |
| `,` | `frame-back-step` | go back by one frame and pause |
| `SPACE` | `cycle pause` | toggle pause/playback mode |
| `>` | `playlist-next` | skip to the next file |
| `ENTER` | `playlist-next` | skip to the next file |
| `<` | `playlist-prev` | skip to the previous file |
| `Shift+HOME` | `no-osd set playlist-pos 0` | skip to the first file |
| `Shift+END` | `no-osd set playlist-pos-1 ${playlist-count}` | skip to the last file |
| `POWER` | `quit` |  |
| `PLAY` | `cycle pause` | toggle pause/playback mode |
| `PAUSE` | `cycle pause` | toggle pause/playback mode |
| `PLAYPAUSE` | `cycle pause` | toggle pause/playback mode |
| `PLAYONLY` | `set pause no` | unpause |
| `PAUSEONLY` | `set pause yes` | pause |
| `STOP` | `quit` |  |
| `FORWARD` | `seek 60` | seek 1 minute forward |
| `REWIND` | `seek -60` | seek 1 minute backward |
| `NEXT` | `playlist-next` | skip to the next file |
| `PREV` | `playlist-prev` | skip to the previous file |
| `CLOSE_WIN` | `quit` |  |
| `Ctrl+w` | `quit` |  |
| `E` | `cycle edition` | switch edition |
| `l` | `ab-loop` | set/clear A-B loop points |
| `Ctrl+c` | `quit 4` |  |
| `Ctrl+r` | `set file-local-options/start ${=time-pos}; playlist-play-index current yes; show-text "Reloading current file..."` | reload the current file |
| `F8` | `show-text ${playlist}` | show the playlist **← 本包已覆盖** |
| `g-p` | `script-binding select/select-playlist` |  |
| `g-c` | `script-binding select/select-chapter` |  |
| `g-e` | `script-binding select/select-edition` |  |
| `!` | `add chapter -1` | seek to the previous chapter |
| `@` | `add chapter 1` | seek to the next chapter |

### 画面 / 窗口（47 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `Alt+LEFT` | `add video-pan-x  0.1` | move the video right |
| `Alt+RIGHT` | `add video-pan-x -0.1` | move the video left |
| `Alt+UP` | `add video-pan-y  0.1` | move the video down |
| `Alt+DOWN` | `add video-pan-y -0.1` | move the video up |
| `Alt++` | `add video-zoom   0.1` | zoom in |
| `Alt+-` | `add video-zoom  -0.1` | zoom out |
| `Alt+KP_ADD` | `add video-zoom  0.1` | zoom in |
| `Alt+KP_SUBTRACT` | `add video-zoom -0.1` | zoom out |
| `Alt+BS` | `set video-zoom 0; no-osd set panscan 0; no-osd set video-pan-x 0; no-osd set video-pan-y 0; no-osd set video-align-x 0; no-osd set video-align-y 0` | reset zoom and pan settings |
| `ESC` | `set fullscreen no` | leave fullscreen |
| `Alt+0` | `set window-scale 0.5` | halve the window size |
| `Alt+1` | `set window-scale 1` | reset the window size **← 本包已覆盖** |
| `Alt+2` | `set window-scale 2` | double the window size **← 本包已覆盖** |
| `T` | `cycle ontop` | toggle placing the video on top of other windows |
| `f` | `cycle fullscreen` | toggle fullscreen |
| `w` | `add panscan -0.1` | decrease panscan |
| `W` | `add panscan +0.1` | shrink black bars by cropping the video |
| `e` | `add panscan +0.1` | shrink black bars by cropping the video |
| `A` | `cycle-values video-aspect-override "16:9" "4:3" "2.35:1" "no"` | cycle the video aspect ratio |
| `Alt+KP1` | `repeatable add video-rotate -1` | rotate video counterclockwise by 1 degree |
| `Alt+KP5` | `set video-rotate  0` | reset rotation |
| `Alt+KP3` | `repeatable add video-rotate  1` | rotate video clockwise by 1 degree |
| `KP1` | `add video-zoom    -0.01` | zoom out video |
| `KP2` | `add video-scale-y -0.01` | scale down video vertically |
| `KP4` | `add video-scale-x -0.01` | scale down video horizontally |
| `KP5` | `set video-scale-x  1.00; set video-scale-y 1; set video-zoom 0` | reset video scale |
| `KP6` | `add video-scale-x  0.01` | scale up video horizontally |
| `KP8` | `add video-scale-y  0.01` | scale up video vertically |
| `KP9` | `add video-zoom     0.01` | zoom in video |
| `Ctrl+KP1` | `add video-pan-x -0.01; add video-pan-y  0.01` | move video left and down |
| `Ctrl+KP2` | `add video-pan-y  0.01` | move video down |
| `Ctrl+KP3` | `add video-pan-x  0.01; add video-pan-y  0.01` | move video right and down |
| `Ctrl+KP4` | `add video-pan-x -0.01` | move video left |
| `Ctrl+KP5` | `set video-pan-x  0.00; set video-pan-y  0.00` | reset video position |
| `Ctrl+KP6` | `add video-pan-x  0.01` | move video right |
| `Ctrl+KP7` | `add video-pan-x -0.01; add video-pan-y -0.01` | move video left and up |
| `Ctrl+KP8` | `add video-pan-y -0.01` | move video up |
| `Ctrl+KP9` | `add video-pan-x  0.01; add video-pan-y -0.01` | move video right and up |
| `Ctrl+KP_END` | `add video-align-x -0.01; add video-align-y  0.01` | align video left and down |
| `Ctrl+KP_DOWN` | `add video-align-y  0.01` | align video down |
| `Ctrl+KP_PGDWN` | `add video-align-x  0.01; add video-align-y  0.01` | align video right and down |
| `Ctrl+KP_LEFT` | `add video-align-x -0.01` | align video left |
| `Ctrl+KP_BEGIN` | `set video-align-x  0.00; set video-align-y  0.00` | reset video alignment |
| `Ctrl+KP_RIGHT` | `add video-align-x  0.01` | align video right |
| `Ctrl+KP_HOME` | `add video-align-x -0.01; add video-align-y -0.01` | align video left and up |
| `Ctrl+KP_UP` | `add video-align-y -0.01` | align video up |
| `Ctrl+KP_PGUP` | `add video-align-x  0.01; add video-align-y -0.01` | align video right and up |

### 截图（4 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `s` | `screenshot` | take a screenshot of the video in its original resolution with subtitles |
| `S` | `screenshot video` | take a screenshot of the video in its original resolution without subtitles |
| `Ctrl+s` | `screenshot window` | take a screenshot of the window with OSD and subtitles |
| `Alt+s` | `screenshot each-frame` | automatically screenshot every frame; issue this command again to stop taking screenshots |

### 字幕（20 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `Ctrl+LEFT` | `no-osd sub-seek -1` | seek to the previous subtitle |
| `Ctrl+RIGHT` | `no-osd sub-seek  1` | seek to the next subtitle |
| `Shift+Ctrl+LEFT` | `sub-step -1` | change subtitle timing such that the previous subtitle is displayed |
| `Shift+Ctrl+RIGHT` | `sub-step 1` | change subtitle timing such that the next subtitle is displayed |
| `z` | `add sub-delay -0.1` | shift subtitles 100 ms earlier |
| `Z` | `add sub-delay  0.1` | delay subtitles by 100 ms |
| `x` | `add sub-delay  0.1` | delay subtitles by 100 ms |
| `G` | `add sub-scale  0.1` | increase the subtitle font size |
| `F` | `add sub-scale -0.1` | decrease the subtitle font size |
| `r` | `add sub-pos -1` | move subtitles up |
| `R` | `add sub-pos +1` | move subtitles down |
| `t` | `add sub-pos +1` | move subtitles down |
| `v` | `cycle sub-visibility` | hide or show the subtitles |
| `Alt+v` | `cycle secondary-sub-visibility` | hide or show the secondary subtitles |
| `V` | `cycle sub-ass-use-video-data` | cycle which video data gets used in ASS rendering to fix broken files |
| `u` | `cycle-values sub-ass-override "force" "scale"` | toggle overriding SSA/ASS subtitle styles with the normal styles |
| `J` | `cycle sub down` | switch subtitle track backwards |
| `g-s` | `script-binding select/select-sid` |  |
| `g-l` | `script-binding select/select-subtitle-line` |  |
| `g-L` | `script-binding select/select-secondary-subtitle-line` |  |

### 音量 / 音轨（16 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `Ctrl++` | `add audio-delay  0.1` | change audio/video sync by delaying the audio |
| `Ctrl+-` | `add audio-delay -0.1` | change audio/video sync by shifting the audio earlier |
| `Ctrl+KP_ADD` | `add audio-delay  0.1` | change audio/video sync by delaying the audio |
| `Ctrl+KP_SUBTRACT` | `add audio-delay -0.1` | change audio/video sync by shifting the audio earlier |
| `9` | `add volume -2` |  |
| `/` | `add volume -2` |  |
| `KP_DIVIDE` | `add volume -2` |  |
| `0` | `add volume  2` |  |
| `*` | `add volume  2` |  |
| `KP_MULTIPLY` | `add volume 2` |  |
| `m` | `cycle mute` | toggle mute |
| `SHARP` | `cycle audio` | switch audio track |
| `VOLUME_UP` | `add volume 2` |  |
| `VOLUME_DOWN` | `add volume -2` |  |
| `MUTE` | `cycle mute` | toggle mute |
| `g-d` | `script-binding select/select-audio-device` |  |

### 画质 / 滤镜开关（11 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `1` | `add contrast -1` |  |
| `2` | `add contrast 1` |  |
| `3` | `add brightness -1` |  |
| `4` | `add brightness 1` |  |
| `5` | `add gamma -1` |  |
| `6` | `add gamma 1` |  |
| `7` | `add saturation -1` |  |
| `8` | `add saturation 1` |  |
| `b` | `cycle deband` | toggle the debanding filter |
| `d` | `cycle deinterlace` | cycle the deinterlacing filter |
| `Ctrl+h` | `cycle-values hwdec no auto` | toggle hardware decoding |

### OSD / 统计 / 选择菜单（21 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `O` | `no-osd cycle-values osd-level 3 1` | toggle displaying the OSD on user interaction or always |
| `o` | `show-progress` | show playback progress |
| `P` | `show-progress` | show playback progress |
| `i` | `script-binding stats/display-stats` | display information and statistics |
| `I` | `script-binding stats/display-stats-toggle` | toggle displaying information and statistics |
| `?` | `script-binding stats/display-page-4-toggle` | toggle displaying key bindings |
| ``` | `script-binding commands/open` | open the console |
| `Ctrl+v` | `update-clipboard text; loadfile ${clipboard/text} append-play; show-text '+ ${clipboard/text}'` | append the copied path |
| `F9` | `show-text ${track-list}` | show the list of video, audio and sub tracks |
| `g-S` | `script-binding select/select-secondary-sid` |  |
| `g-a` | `script-binding select/select-aid` |  |
| `g-v` | `script-binding select/select-vid` |  |
| `g-t` | `script-binding select/select-track` |  |
| `g-h` | `script-binding select/select-watch-history` |  |
| `g-w` | `script-binding select/select-watch-later` |  |
| `g-b` | `script-binding select/select-binding` |  |
| `g-r` | `script-binding select/show-properties` |  |
| `g-m` | `script-binding select/menu` |  |
| `Ctrl+p` | `script-binding select/menu` |  |
| `MENU` | `script-binding select/context-menu` |  |
| `Shift+F10` | `script-binding select/context-menu` |  |

### 鼠标 / 滚轮（14 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `MBTN_LEFT` | `ignore` | don't do anything |
| `MBTN_LEFT_DBL` | `cycle fullscreen` | toggle fullscreen |
| `MBTN_RIGHT` | `script-binding select/context-menu` | open context menu |
| `MBTN_BACK` | `playlist-prev` | skip to the previous file |
| `MBTN_FORWARD` | `playlist-next` | skip to the next file |
| `Ctrl+MBTN_LEFT` | `script-binding positioning/drag-to-pan` | pan around the clicked point |
| `WHEEL_UP` | `add volume  2` |  |
| `WHEEL_DOWN` | `add volume -2` |  |
| `WHEEL_LEFT` | `seek -10` | seek 10 seconds backward |
| `WHEEL_RIGHT` | `seek  10` | seek 10 seconds forward |
| `ZOOMIN` | `add video-zoom   0.1` | zoom in |
| `ZOOMOUT` | `add video-zoom  -0.1` | zoom out |
| `Ctrl+WHEEL_UP` | `script-binding positioning/cursor-centric-zoom  0.1` | zoom in towards the cursor |
| `Ctrl+WHEEL_DOWN` | `script-binding positioning/cursor-centric-zoom -0.1` | zoom out towards the cursor |

### 多媒体 / 硬件键（1 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `L` | `cycle-values loop-file inf no` | toggle infinite looping |

### 其他 / 键位序列（6 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `j` | `cycle sub` | switch subtitle track |
| `_` | `cycle video` | switch video track |
| `DEL` | `script-binding osc/visibility` | cycle OSC visibility between never, auto (mouse-move) and always |
| `g` | `ignore` |  |
| `Ctrl+m` | `discnav menu` | jump to the disc's root menu |
| `Ctrl+M` | `discnav popup` | show/hide Blu-ray popup menu |

### OSC / 特殊 section（26 条）

| 键 | 命令 | 备注 |
|---|---|---|
| `q` | `quit 4` |  |
| `ESC` | `quit 4` |  |
| `CLOSE_WIN` | `quit 4` |  |
| `UP` | `discnav up` |  |
| `DOWN` | `discnav down` |  |
| `LEFT` | `discnav left` |  |
| `RIGHT` | `discnav right` |  |
| `ENTER` | `discnav select` |  |
| `ESC` | `discnav prev` |  |
| `BS` | `discnav prev` |  |
| `MOUSE_MOVE` | `discnav mouse-move` |  |
| `MBTN_LEFT` | `discnav mouse-click` |  |
| `MOUSE_MOVE` | `script-binding osc/__keybinding1` |  |
| `MOUSE_LEAVE` | `script-binding osc/__keybinding2` |  |
| `MOUSE_MOVE` | `script-binding osc/__keybinding3` |  |
| `MOUSE_LEAVE` | `script-binding osc/__keybinding4` |  |
| `MBTN_LEFT` | `script-binding osc/__keybinding5` |  |
| `MBTN_MID` | `script-binding osc/__keybinding6` |  |
| `MBTN_RIGHT` | `script-binding osc/__keybinding7` |  |
| `Shift+MBTN_LEFT` | `script-binding osc/__keybinding8` |  |
| `WHEEL_UP` | `script-binding osc/__keybinding9` |  |
| `WHEEL_DOWN` | `script-binding osc/__keybinding10` |  |
| `MBTN_LEFT_DBL` | `ignore` |  |
| `Shift+MBTN_LEFT_DBL` | `ignore` |  |
| `MBTN_RIGHT_DBL` | `ignore` |  |
| `MBTN_LEFT` | `script-binding osc/__keybinding11` |  |

## 三、`input.conf` 语法要点（v0.42.0 `DOCS/man/input.rst`）

- 行格式 ``[Shift+][Ctrl+][Alt+][Meta+]<key> [{<section>}] <command> ( ; <command> )*``；**没有 `=`**，与 `mpv.conf` 完全不同。
- `#` 起始为注释（引号外的）；要把命令绑到 `#` 键，键名写 `SHARP`。
- 键名大小写敏感：`s` 与 `S`、`z` 与 `Z` 是不同键；符号名见 `mpv.com --input-keylist`（本机 400+ 个）。
- `ignore` 命令用于解绑某个默认键（不写 `--no-input-default-bindings` 的情况下）。
- 单键绑定覆盖内置默认；**多键序列不会**——`b-c` 会被内置 `b` 抢走，得先把 `b` 绑 `ignore`。
- 参数含空格必须加引号；分号可串多条命令；属性展开写 `${...}`。
- 本包 `input.conf` 实测为 **UTF-8 无 BOM + LF（10 行 / 279 字节）**，与 `mpv.conf` 的 CRLF 不同，写盘须保持原换行。
- `setting.ahk` 的 `MpvWriteDefaultConf()` 只重建 `mpv.conf`，**不触碰 `input.conf`**；`setting.exe` 全程不写它。

-- thumbnails.lua —— n*m 缩略图（contact sheet）
--
-- 右键菜单「截图 → N×M 缩略图」触发 script-message thumbnails。
-- 用 mpv 自身离屏跑一条 lavfi 链：按全片时长均匀取 n*m 帧 → 缩放 →
-- 每帧角落打时间戳 → tile 拼成网格 → 顶部 banner（文件名/时长/分辨率/MyMPC）。
-- 全程只启动一个隐藏的 mpv 子进程，不动当前播放窗口、不改当前 vf。
--
-- 参数来自 script-opts/thumbnails.conf（由 MpvSettings.exe 管理）：
--   cols=3  rows=3               网格行列
--   max_width=1200               拼图总宽度上限（像素）。单格宽度据此自动反推，
--                                每格按视频显示比例算高度，格间留小间距。
--   gap=4                        缩略图之间的间距（像素）
--   thumb_dir=<空>               输出目录，空=跟随 screenshot-directory
--   font=<空>                    字体文件，空=自动探测
--   mpv_exe=<空>                 mpv.exe 绝对路径，由 MpvSettings.exe 探测写入；
--                                空=回退 ~~/../mpv.exe（换目录也自动正确）
--
-- 尺寸算法（对齐 MPC-HC 缩略图）：
--   单格宽 = (max_width - gap*(cols+1)) / cols
--   单格高 = 单格宽 * (视频显示高 / 视频显示宽)   ← 保持视频比例，不变形
--   拼图总宽 = 单格宽*cols + gap*(cols+1) ≈ max_width
--   拼图总高 = 单格高*rows + gap*(rows+1) + banner 高
--
-- 为什么用 lavfi 而不是 PowerShell/Python：生产包零外部依赖，mpv 自带滤镜链，
-- 且 tile/drawtext 在 11-24 实测可用。字体路径的盘符冒号在 filtergraph 里
-- 必须写成 'C\:/...'（单引号 + 反斜杠转义冒号），否则被当成选项分隔符。

local mp = require("mp")
local utils = require("mp.utils")

local o = {
    cols = 3,
    rows = 3,
    max_width = 1200,
    gap = 4,
    thumb_dir = "",
    font = "",
    mpv_exe = "",
}
require("mp.options").read_options(o, "thumbnails")

local script_dir = mp.get_script_directory()
local busy = false

-- lavfi filtergraph 里 Windows 路径的盘符冒号要转义：C:/ -> C\:/
-- 外面包单引号，避免路径里的空格/特殊字符被 filter 解析器拆开。
local function lavfi_path(path)
    local p = path:gsub("\\", "/"):gsub(":", "\\:")
    return "'" .. p .. "'"
end

local function pick_font()
    if o.font ~= "" then return o.font end
    local candidates = {
        "C:/Windows/Fonts/msyh.ttc",   -- 微软雅黑（简中，Win10/11 自带）
        "C:/Windows/Fonts/msyhbd.ttc",
        "C:/Windows/Fonts/simsun.ttc", -- 宋体
        "C:/Windows/Fonts/arial.ttf",
    }
    for _, c in ipairs(candidates) do
        local f = io.open(c, "rb")
        if f then f:close() return c end
    end
    return nil
end

local function fmt_time(sec)
    if not sec or sec < 0 then return "" end
    local h = math.floor(sec / 3600)
    local m = math.floor((sec % 3600) / 60)
    local s = math.floor(sec % 60)
    if h > 0 then return string.format("%d:%02d:%02d", h, m, s) end
    return string.format("%02d:%02d", m, s)
end

local function display_dir()
    if o.thumb_dir ~= "" then return o.thumb_dir end
    local d = mp.get_property("screenshot-directory")
    if not d or d == "" then
        d = mp.command_native({ "expand-path", "~~desktop" })
    end
    return d
end

-- drawtext 的 text 值在单引号内，仍需转义 filtergraph 的分隔符：
--   \  -> \\    ,  -> \,    :  -> \:    ' -> (去掉，和外层单引号冲突)
-- 竖线 | 不是 filtergraph 分隔符，但为稳妥一并替换成中点（视觉也更好看）。
local function lavfi_text(s)
    s = tostring(s or ""):gsub("|", "·")
    s = s:gsub("\\", "\\\\"):gsub(":", "\\:"):gsub(",", "\\,"):gsub("'", "")
    return s
end

local function make()
    if busy then
        mp.osd_message("缩略图：正在生成中，请稍候", 2)
        return
    end
    local src = mp.get_property("path")
    if not src then
        mp.osd_message("缩略图：当前没有打开媒体文件", 3)
        return
    end
    -- 网络流 / 非本地文件：lavfi 子进程要重新读取，可能失败，给出提示但继续尝试。
    if src:match("^%a+://") then
        mp.osd_message("缩略图：网络流可能无法离屏重读", 3)
    end
    busy = true
    mp.osd_message("缩略图：生成中…", 30)

    local duration = mp.get_property_number("duration", 0)
    local width = mp.get_property_number("width", 0)
    local height = mp.get_property_number("height", 0)
    -- 显示宽高比：优先 dwidth/dheight（已含 SAR/anamorphic 修正），回退编码宽高。
    local dw = mp.get_property_number("dwidth", 0)
    local dh = mp.get_property_number("dheight", 0)
    if dw <= 0 or dh <= 0 then dw, dh = width, height end
    local aspect = (dw > 0 and dh > 0) and (dh / dw) or 0.5625

    local cols = math.max(1, math.floor(tonumber(o.cols) or 3))
    local rows = math.max(1, math.floor(tonumber(o.rows) or 3))
    local total = cols * rows
    local max_w = math.max(300, math.floor(tonumber(o.max_width) or 1200))
    local gap = math.max(0, math.min(20, math.floor(tonumber(o.gap) or 4)))
    if duration <= 0 then
        mp.osd_message("缩略图：无法获取视频时长", 3)
        busy = false
        return
    end

    -- MPC-HC 式尺寸：总宽上限反推单格宽，单格宽按视频比例算单格高。
    local cw = math.floor((max_w - gap * (cols + 1)) / cols)
    cw = math.max(80, cw)
    -- scale 目标需偶数，避免 yuv420 编码告警。
    cw = cw - (cw % 2)
    local ch = math.floor(cw * aspect)
    ch = math.max(46, ch)
    ch = ch - (ch % 2)

    local fps = math.min(60, total / duration)
    local font = pick_font()
    if not font then
        mp.osd_message("缩略图：找不到可用中文字体", 3)
        busy = false
        return
    end
    local fp = lavfi_path(font)

    local name = mp.get_property("filename") or "video"
    name = name:match("([^/\\]+)$") or name
    name = name:gsub("%.[^%.]+$", "")
    local safe_name = lavfi_text(name)

    local banner_h = 72
    local pad_top = banner_h + 8
    local total_w = cw * cols + gap * (cols + 1)
    local total_h = ch * rows + gap * (rows + 1) + pad_top
    local info = string.format("%s  ·  %dx%d  ·  %dx%d 缩略图",
        fmt_time(duration), dw or 0, dh or 0, cols, rows)
    local safe_info = lavfi_text(info)

    local ts = string.format(
        "drawtext=fontfile=%s:text='%%{pts\\:hms}':fontsize=20:fontcolor=white:box=1:boxcolor=black@0.65:x=8:y=h-28",
        fp)
    local banner = string.format(
        "pad=%d:%d:0:%d:color=0x101910," ..
        "drawtext=fontfile=%s:text='%s':fontsize=28:fontcolor=0xA3F6BE:x=16:y=8:max_glyphs=52," ..
        "drawtext=fontfile=%s:text='%s':fontsize=24:fontcolor=0xF0F7F0:x=16:y=42," ..
        "drawtext=fontfile=%s:text='MyMPC':fontsize=20:fontcolor=0x6FA88A:x=w-tw-16:y=14",
        total_w, total_h, pad_top, fp, safe_name, fp, safe_info, fp)

    local filter = string.format(
        "lavfi=[fps=%.6f,scale=%d:%d:flags=lanczos,%s,tile=%dx%d:padding=%d:margin=%d:color=0x101910,%s]",
        fps, cw, ch, ts, cols, rows, gap, gap, banner)

    local out_dir = display_dir()
    -- mpv.exe 自带 --vo=image 离屏输出；用 --no-config 避免主配置干扰。
    -- 字体路径用显式 fontfile（已在前面转义），不依赖 fontconfig。
    -- 优先用 MpvSettings.exe 探测写入的真实路径（口径与 Assoc 的 <App Paths 根>\MediaPlayer\mpv\mpv.exe 一致）；
    -- 没写则用 ~~/../mpv.exe 回退：~~ 指向 portable_config，其上一级就是 mpv.exe，换安装目录也自动正确。
    local mpv_exe = o.mpv_exe
    if not mpv_exe or mpv_exe == "" then
        mpv_exe = mp.command_native({ "expand-path", "~~/../mpv.exe" })
    end
    local cmd = {
        mpv_exe,
        "--no-config",
        "--vo=image",
        "--vo-image-format=png",
        "--vo-image-outdir=" .. out_dir,
        "--vo-image-png-compression=3",
        "--vf=" .. filter,
        "--frames=1",
        "--ao=null",
        "--really-quiet",
        "--msg-level=all=error",
        "--",
        src,
    }
    mp.command_native_async({ name = "subprocess", args = cmd, playback_only = false, capture_stdout = false, capture_stderr = false },
        function(success, result, err)
            busy = false
            if success and result and result.status == 0 then
                mp.osd_message("缩略图已生成：" .. out_dir, 5)
            else
                local reason = err or (result and tostring(result.status)) or "未知错误"
                mp.osd_message("缩略图生成失败：" .. tostring(reason), 6)
                mp.msg.error("thumbnail subprocess failed: " .. tostring(reason))
            end
        end)
end

mp.register_script_message("thumbnails", make)

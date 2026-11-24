-- thumbnails.lua —— n*m 缩略图（contact sheet）
--
-- 右键菜单「截图 → 生成 N×M 缩略图」触发 script-message thumbnails。
--
-- 版面：
--   · 每格右下角白字时间码（半透明底框）；格间留 gap 间距
--   · 顶部 banner：文件名（完整显示，一行不够自动折行，字号固定）+
--     总时长 · 分辨率 · 网格，右上角 MyMPC 水印
--   · 产物直接出 JPEG（缩略图不需要无损，也不用 lanczos 那种画质滤镜）
--
-- 流程（这是关键，别再改成"全片解码"）：
--   1. 立刻暂停当前播放，并记下暂停点 time-pos；
--   2. 只 seek 到 n*m 个目标位置各抓一帧（不要求帧精确，取到就行），
--      走主播放器自己的解码器，所以有硬解、有缓存，比另起进程快得多；
--   3. 把 n*m 张图交给一个隐藏的 mpv 子进程：concat 解复用 → 缩放 → tile 拼网格
--      → 顶部 banner，输出一张 JPEG；
--   4. 回到原来的暂停点，保持暂停状态。
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
-- 尺寸算法：
--   单格宽 = (max_width - gap*(cols+1)) / cols
--   单格高 = 单格宽 * (视频显示高 / 视频显示宽)   ← 保持视频比例，不变形
--   拼图总宽 = 单格宽*cols + gap*(cols+1) ≈ max_width
--   拼图总高 = 单格高*rows + gap*(rows+1) + banner 高
--
-- 为什么不用 --vo=image 单独跑全片：实测那套子进程在 4K AV1 下随机段错误，
-- 而且 --vo=image 默认按实时时钟走，197 秒的片子要跑三分多钟。走主播放器
-- seek 抓帧，197 秒的 4K 片子 9 帧只要 3 秒左右。
--
-- 为什么不用 PowerShell/Python 拼图：生产包要零外部依赖，Add-Type 这类调用
-- 在受限策略下会被拦。concat + tile 全部由 mpv 自带标签完成，实测稳定。
--
-- 字体路径的盘符冒号在 filtergraph 里必须写成 'C\:/...'（单引号 + 反斜杠转义冒号），
-- 否则被当成选项分隔符。

local mp = require("mp")
local utils = require("mp.utils")

-- 参数默认值。read_options 只在脚本加载时读一次，改设置必须重启播放器才生效；
-- 因此每次生成前用 reload_opts() 重新解析 conf，改完设置立即按新行列出图。
local DEFAULTS = {
    cols = 3,
    rows = 3,
    max_width = 1200,
    gap = 4,
    thumb_dir = "",
    font = "",
    mpv_exe = "",
}
local o = {}

local function reload_opts()
    for k, v in pairs(DEFAULTS) do o[k] = v end
    require("mp.options").read_options(o, "thumbnails")
end
reload_opts()

local busy = false

-- lavfi filtergraph 里 Windows 路径的盘符冒号要转义：C:/ -> C\:/
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
local function lavfi_text(s)
    s = tostring(s or ""):gsub("|", "·")
    s = s:gsub("\\", "\\\\"):gsub(":", "\\:"):gsub(",", "\\,"):gsub("'", "")
    return s
end

-- 按 UTF-8 字符拆开，便于按像素宽度估算折行
local function utf8_chars(s)
    local out, i, len = {}, 1, #s
    while i <= len do
        local b = s:byte(i)
        local step = 1
        if b >= 0xF0 then step = 4
        elseif b >= 0xE0 then step = 3
        elseif b >= 0xC0 then step = 2 end
        out[#out + 1] = s:sub(i, i + step - 1)
        i = i + step
    end
    return out
end

-- 按可用宽度把标题折成多行（ASCII 约 0.55 字宽，中日韩约 1 字宽）。
-- 标题必须完整，不截断——banner 高度随行数增加。
local function wrap_title(s, fs, max_px)
    local lines, cur, curw = {}, "", 0
    for _, ch in ipairs(utf8_chars(s)) do
        local w = (#ch >= 3) and fs or math.floor(fs * 0.55)
        if curw + w > max_px and #cur > 0 then
            lines[#lines + 1] = cur
            cur, curw = ch, w
        else
            cur = cur .. ch
            curw = curw + w
        end
    end
    if #cur > 0 then lines[#lines + 1] = cur end
    if #lines == 0 then lines[1] = "" end
    return lines
end

local function winpath(p) return (p:gsub("/", "\\")) end

local function run(cmd, capture)
    return mp.command_native({
        name = "subprocess",
        args = cmd,
        playback_only = false,
        capture_stdout = capture or false,
        capture_stderr = capture or false,
    })
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
    local duration = mp.get_property_number("duration", 0)
    local width = mp.get_property_number("width", 0)
    local height = mp.get_property_number("height", 0)
    if duration <= 0 or width <= 0 or height <= 0 then
        mp.osd_message("缩略图：当前文件没有可用的视频画面", 3)
        return
    end

    busy = true
    reload_opts()   -- 设置刚改过也立刻生效，不必重启播放器

    -- ---- ① 暂停并记录暂停点（先暂停再动）----
    local pos0 = mp.get_property_number("time-pos", 0) or 0
    local fmt0 = mp.get_property("screenshot-format")
    mp.set_property("pause", "yes")

    -- ---- ② 计算网格几何 ----
    local dw = mp.get_property_number("dwidth", 0)
    local dh = mp.get_property_number("dheight", 0)
    if dw <= 0 or dh <= 0 then dw, dh = width, height end
    local aspect = (dw > 0 and dh > 0) and (dh / dw) or 0.5625

    local cols = math.max(1, math.floor(tonumber(o.cols) or 3))
    local rows = math.max(1, math.floor(tonumber(o.rows) or 3))
    local total = cols * rows
    local max_w = math.max(300, math.floor(tonumber(o.max_width) or 1200))
    local gap = math.max(0, math.min(20, math.floor(tonumber(o.gap) or 4)))

    local cw = math.floor((max_w - gap * (cols + 1)) / cols)
    cw = math.max(80, cw)
    cw = cw - (cw % 2)                      -- scale 目标取偶数，避免 yuv420 告警
    local ch = math.floor(cw * aspect)
    ch = math.max(46, ch)
    ch = ch - (ch % 2)

    local font = pick_font()
    if not font then
        busy = false
        mp.osd_message("缩略图：找不到可用中文字体", 3)
        return
    end
    local fp = lavfi_path(font)

    local name = mp.get_property("filename") or "video"
    name = name:match("([^/\\]+)$") or name
    name = name:gsub("%.[^%.]+$", "")

    -- ---- banner 排版：标题完整显示，一行不够就折行，高度随行数增长 ----
    local total_w = cw * cols + gap * (cols + 1)
    local title_fs, info_fs, line_h = 26, 22, 34
    -- 右边留出水印宽度，标题折行不要压到右上角 MyMPC
    local title_lines = wrap_title(name, title_fs, total_w - 150)
    local title_y = 8
    local info_y = title_y + #title_lines * line_h + 6
    local pad_top = info_y + info_fs + 12
    local total_h = ch * rows + gap * (rows + 1) + pad_top
    local info = string.format("%s  ·  %dx%d  ·  %dx%d 缩略图",
        fmt_time(duration), dw, dh, cols, rows)

    -- ---- ③ 准备目录 ----
    local out_dir = display_dir()
    local tmp_base = (os.getenv("TEMP") or os.getenv("TMP") or out_dir):gsub("\\", "/")
    tmp_base = tmp_base:gsub("/+$", "")
    local stamp = os.date("%Y%m%d-%H%M%S")
    local pid = (utils.getpid and tostring(utils.getpid())) or tostring(math.random(1000, 9999))
    local tmp_dir = string.format("%s/mympcdhumb_%s_%s", tmp_base, stamp, pid)
    local grab_dir = tmp_dir .. "/g"
    local out_tmp = tmp_dir .. "/o"
    run({ "cmd", "/c", "mkdir", winpath(grab_dir) })
    run({ "cmd", "/c", "mkdir", winpath(out_tmp) })

    mp.osd_message(string.format("缩略图：正在抓取 %d 帧（%d×%d）…", total, cols, rows), 60)

    local function cleanup()
        run({ "cmd", "/c", "rmdir", "/s", "/q", winpath(tmp_dir) })
    end

    -- ---- ④ 收尾：回到暂停点，保持暂停 ----
    local function restore()
        if fmt0 and fmt0 ~= "" then pcall(function() mp.set_property("screenshot-format", fmt0) end) end
        mp.commandv("seek", string.format("%.3f", pos0), "absolute+exact")
        mp.set_property("pause", "yes")
    end

    local function fail(msg)
        busy = false
        restore()
        mp.osd_message("缩略图生成失败：" .. msg, 7)
        mp.msg.error("thumbnail failed: " .. msg)
        cleanup()
    end

    -- ---- ⑤ 逐帧抓取（seek 完成后再截图，不靠猜延时）----
    pcall(function() mp.set_property("screenshot-format", "jpg") end)
    local results = 0
    local i = 0
    local compose

    local function next_shot()
        i = i + 1
        if i > total then
            return compose()
        end
        local t = duration * (i - 0.5) / total
        if t >= duration then t = math.max(0, duration - 1) end
        -- relative+keyframes：只跳到关键帧，快；缩略图不要求帧精确。
        mp.command_native_async({ "seek", string.format("%.3f", t), "absolute+keyframes" },
            function(seek_ok)
                if not seek_ok then
                    mp.msg.warn(string.format("thumbnail: seek %.2f 失败，跳过", t))
                    return next_shot()
                end
                -- 给 vo 一点时间把新帧推上来（暂停态下 seek 后需要一次重绘）
                mp.add_timeout(0.15, function()
                    local f = string.format("%s/%02d.jpg", grab_dir, i)
                    mp.command_native_async({ "screenshot-to-file", f, "video" }, function(shot_ok)
                        if shot_ok then
                            results = results + 1
                        else
                            mp.msg.warn(string.format("thumbnail: 第 %d 帧截图失败", i))
                        end
                        next_shot()
                    end)
                end)
            end)
    end

    -- ---- ⑥ 拼图：concat 解复用 n*m 张图 → 缩放 → tile → banner ----
    compose = function()
        if results == 0 then
            return fail("没有抓到任何画面")
        end
        mp.osd_message("缩略图：正在拼图…", 30)

        local pl = tmp_dir .. "/list.txt"
        local fh = io.open(pl, "w")
        if not fh then
            return fail("无法写入临时文件")
        end
        fh:write("ffconcat version 1.0\n")
        for k = 1, total do
            local f = string.format("%s/%02d.jpg", grab_dir, k)
            local ok = io.open(f, "rb")
            if ok then
                ok:close()
                fh:write(string.format("file '%s'\nduration 0.2\n", f))
            end
        end
        fh:close()

        -- 子进程自己盯着产物：mpv 的 image vo 在 EOS 时会在 PNG/JPEG 还没写完
        -- 就退出（日志有 Saving、文件是空的）。所以 --keep-open=yes 让它别走，
        -- 由这个脚本确认文件写完后再 quit。
        local hold = tmp_dir .. "/hold.lua"
        local hf = io.open(hold, "w")
        if not hf then
            return fail("无法写入临时脚本")
        end
        hf:write(string.format([[
local mp = require("mp")
local dir = %q
local done = false
local function size_of(p)
    local f = io.open(p, "rb")
    if not f then return 0 end
    local s = f:seek("end") or 0
    f:close()
    return s
end
mp.add_periodic_timer(0.1, function()
    if done then return end
    for _, n in ipairs({ "00000001.jpg", "00000001.png" }) do
        if size_of(dir .. "/" .. n) > 0 then
            done = true
            mp.add_timeout(0.4, function() mp.commandv("quit") end)
            return
        end
    end
end)
]], out_tmp))
        hf:close()

        local mpv_exe = o.mpv_exe
        if not mpv_exe or mpv_exe == "" then
            mpv_exe = mp.command_native({ "expand-path", "~~/../mpv.exe" })
        end

        -- 缩略图本身很小，不需要 lanczos，用默认双线性就够，快很多。
        -- 最终产物也直接出 JPEG（--vo-image-jpeg-quality=88）。
        --
        -- 标题/信息走 textfile：drawtext 的 text 里塞中文、冒号、逗号要一路转义，
        -- 用文件省掉所有转义问题，也天然支持多行（标题完整显示，一行不够就折行）。
        local title_txt = tmp_dir .. "/title.txt"
        local tfh = io.open(title_txt, "w")
        if not tfh then return fail("无法写入标题文件") end
        tfh:write(table.concat(title_lines, "\n"))
        tfh:close()

        local info_txt = tmp_dir .. "/info.txt"
        local ifh = io.open(info_txt, "w")
        if not ifh then return fail("无法写入信息文件") end
        ifh:write(info)
        ifh:close()

        -- 每格时间码（右下角小白字）：
        -- 拼完图后每个格子的像素位置是确定的，所以直接在固定坐标上画，
        -- 不用 enable 表达式。实测 enable 版本 12 条必崩（0xC0000005），
        -- 固定坐标版本稳定得多。
        local stamp_fs = math.max(12, math.floor(cw / 23))
        local stamps = {}
        for k = 1, total do
            local r = math.floor((k - 1) / cols)
            local c = (k - 1) % cols
            local xr = gap + c * (cw + gap) + cw - 6
            local y = pad_top + gap + r * (ch + gap) + ch - math.floor(stamp_fs * 1.4) - 4
            local t = duration * (k - 0.5) / total
            stamps[#stamps + 1] = string.format(
                "drawtext=fontfile=%s:text='%s':fontsize=%d:fontcolor=white:box=1:boxcolor=black@0.45:boxborderw=3:x=%d-tw:y=%d",
                fp, lavfi_text(fmt_time(t)), stamp_fs, xr, y)
        end

        local filter = string.format(
            "lavfi=[scale=%d:%d,tile=%dx%d:padding=%d:margin=%d:color=0x101910," ..
            "pad=%d:%d:0:%d:color=0x101910,%s," ..
            "drawtext=fontfile=%s:textfile=%s:fontsize=%d:fontcolor=0xA3F6BE:x=16:y=%d," ..
            "drawtext=fontfile=%s:textfile=%s:fontsize=%d:fontcolor=0xF0F7F0:x=16:y=%d," ..
            "drawtext=fontfile=%s:text='MyMPC':fontsize=%d:fontcolor=0x6FA88A:x=w-tw-16:y=%d]",
            cw, ch, cols, rows, gap, gap,
            total_w, total_h, pad_top, table.concat(stamps, ","),
            fp, lavfi_path(title_txt), title_fs, title_y,
            fp, lavfi_path(info_txt), info_fs, info_y,
            fp, math.max(18, math.floor(total_w / 60)), title_y + 6)

        local cmd = {
            mpv_exe,
            "--no-config",
            "--keep-open=yes",
            "--script=" .. hold,
            "--demuxer-lavf-format=concat",
            "--demuxer-lavf-o=safe=0",
            "--vo=image",
            "--vo-image-format=jpg",
            "--vo-image-jpeg-quality=88",
            "--vo-image-outdir=" .. out_tmp,
            "--vf=" .. filter,
            "--ao=null",
            "--really-quiet",
            "--msg-level=all=error",
            "--",
            pl,
        }

        local function find_produced()
            for _, ext in ipairs({ "jpg", "png" }) do
                local p = out_tmp .. "/00000001." .. ext
                local f = io.open(p, "rb")
                if f then
                    local sz = f:seek("end") or 0
                    f:close()
                    if sz > 0 then return p end
                end
            end
            return nil
        end

        local function finish(produced)
            busy = false
            local base = name:gsub('[\\/:*?"<>|]', "_")
            local ext = produced:match("%.(%w+)$") or "jpg"
            local dest = out_dir:gsub("[/\\]+$", "") .. "/"
                .. string.format("%s_缩略图_%s.%s", base, stamp, ext)
            local mv = run({ "cmd", "/c", "move", "/y", winpath(produced), winpath(dest) }, true)
            local ok = mv and mv.status == 0
            restore()
            if ok then
                mp.osd_message("缩略图已保存：" .. dest, 6)
                mp.msg.info("thumbnail saved: " .. dest)
            else
                mp.osd_message("缩略图生成失败：无法移动到目标目录", 7)
                mp.msg.error("thumbnail move failed: " .. tostring(mv and mv.stderr))
            end
            cleanup()
        end

        -- 这套 mpv 构建的 image vo 偶发访问冲突（同一命令重跑就正常），
        -- 所以失败先重试，最多 3 次。
        local attempt = 0
        local try_compose
        try_compose = function()
            attempt = attempt + 1
            -- 清掉上一次可能残留的半成品（不存在的文件不调 del，免得日志刷中文错误）
            local stale = out_tmp .. "/00000001.jpg"
            local sf = io.open(stale, "rb")
            if sf then
                sf:close()
                run({ "cmd", "/c", "del", "/q", winpath(stale) })
            end
            mp.command_native_async({ name = "subprocess", args = cmd, playback_only = false },
                function(_, result, err)
                    local produced = find_produced()
                    if produced then return finish(produced) end
                    if attempt < 4 then
                        mp.msg.warn(string.format("thumbnail: 拼图第 %d 次失败，重试", attempt))
                        mp.add_timeout(0.2, try_compose)
                        return
                    end
                    local reason = err or (result and ("exit " .. tostring(result.status))) or "未产出图像"
                    fail(tostring(reason))
                end)
        end
        try_compose()
    end

    next_shot()
end

mp.register_script_message("thumbnails", make)

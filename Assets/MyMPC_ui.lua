-- MyMPC_ui.lua —— 由「MPV 设置 EXE」生成并独占管理，请勿手工编辑（改设置请用设置程序）
--
-- 一份脚本干两件事，避免多个脚本抢同一批属性：
--   ① 无封面音频的紧凑信息窗：ass-events overlay 画时间码 / 曲目 / 进度条 / 右上柱形装饰
--   ② 单实例转发：双击媒体文件时把文件送进已在播放的窗口，自己退出
--   （窗口自动适配在 fit90.lua，比例读 script-opts/fit90.conf）
--
-- 开关都在 script-opts/MyMPC.conf（EXE 写）：
--   no_art_ui       = yes|no      是否画信息窗。no 时 EXE 同时移除 audio-info.conf 的 include，
--                                 回到 mpv 原生行为（纯音频无窗口）
--   single_instance = yes|no      yes = 双击文件复用当前窗口；no = 每个文件都开新窗口
--   pipe_name       = MyMPC_mpv   必须与 mpv.conf 的 input-ipc-server 一致
--   sub_autoreload  = yes|no      yes = 外挂字幕文件被改动后自动重读（2026-11-17 新增，默认 yes）
--   rtx_vsr         = yes|no      NVIDIA RTX 超分辨率（2026-11-18 新增，默认 yes）
--   rtx_hdr         = yes|no      NVIDIA RTX Video HDR，SDR→HDR10（默认 yes，需 Windows HDR 开；HDR 源一律跳过）
--   rtx_vsr_limit   = sd|720p|1080p|1440p   VSR 启用上限，阈值照抄 MPCVR 的 SetSuperRes()
--   rtx_hdr_peak    = 400..2000   NVIDIA App 里设的峰值 nit，用来给 mpv 标注 HDR 输出
--
-- 实测踩坑（改代码前先读，2026-11-13 / 11-14 实测）：
--   · mp.create_osd_overlay("ass-events") 必须显式设 res_x/res_y，否则空 VO 窗口完全不渲染（纯黑）
--   · 同一条 ASS 事件行里写多个 \pos 只有第一个生效，每段必须是独立事件行（用 \n 连接）
--   · ASS 颜色是 BGR：紫色 #B59AFF 必须写 \c&HFF9AB5&
--   · file-loaded 瞬间 audio-params 常为 nil，判定用「track-list 无 video 轨 + aid ~= no」更稳
--   · 单实例不能指望 mpv 自己拒绝第二个 pipe：它只打一行 Couldn't create first pipe instance
--     (0x80070005) 然后照常播放，必须自己检测 server pid 并转发 + 自退
--   · 运行时 set_property("lavfi-complex") 直接段错误（exit 139），真频谱不进 V1
--   · 转发只换文件、不会自己抢前台：窗口停在后台是 Windows 前台锁定的正常结果。
--     必须 ①第二实例先 AllowSetForegroundWindow(第一实例 pid) 转交权限，
--          ②第一实例收到消息后自己 SetForegroundWindow。两步缺一，窗口尺寸对但焦点留在后台
--   · window-id 属性在 Windows 上就是 HWND（实测 0x160628），不必再 EnumWindows 找窗口

local mp = require("mp")
local msg = require("mp.msg")
local options = require("mp.options")
local utils = require("mp.utils")

local o = {
    no_art_ui = true,
    single_instance = true,
    pipe_name = "MyMPC_mpv",
    sub_autoreload = true,
    rtx_vsr = true,
    rtx_hdr = true,
    rtx_vsr_limit = "1080p",
    rtx_hdr_peak = 1000,
}
-- 读 script-opts/MyMPC.conf；文件缺失时用上面的默认值（与 EXE 的出厂值一致）
options.read_options(o, "MyMPC")

-- ============================================================
-- ① 无封面音频信息窗
-- ============================================================
local PURPLE = [[\c&HFF9AB5&]]   -- #B59AFF 的 BGR 写法
local WHITE  = [[\c&HFFFFFF&]]
local GREY   = [[\c&H909090&]]
local BARBG  = [[\c&H303040&]]

local ov, timer, no_art

local function fmt(t)
    local s = math.floor(tonumber(t) or 0)
    return string.format("%02d:%02d", math.floor(s / 60), s % 60)
end

-- ASS 文本里的 \ { } 会被当标签起始，替换成全角，否则整段被吞
local function esc(s)
    return (s:gsub("[\\{}]", { ["\\"] = "＼", ["{"] = "｛", ["}"] = "｝" }))
end

-- 粗估字符显示宽度：ASCII ≈ 0.56 倍字号，CJK/全角 ≈ 1 倍，其余 ≈ 0.75 倍
local function char_w(ch, fs)
    local b = ch:byte(1)
    if b < 0x80 then return fs * 0.56 end
    if b >= 0xE0 then return fs end
    return fs * 0.75
end

local function utf_chars(s)
    return s:gmatch("[%z\1-\127\194-\244][\128-\191]*")
end

local function str_w(s, fs)
    local w = 0
    for ch in utf_chars(s) do w = w + char_w(ch, fs) end
    return w
end

-- 按词折行（优先在空格处断，避免把 .ape 之类扩展名拆成孤零零一行）；
-- 单个词本身超宽时按字符切。最多 2 行，超出时第二行末尾加省略号。
local function wrap2(s, fs, maxw)
    local lines, cur, w = {}, {}, 0
    local SPACE = char_w(" ", fs)
    local function pushline()
        if #cur > 0 then
            lines[#lines + 1] = (table.concat(cur):gsub("%s+$", ""))
            cur, w = {}, 0
        end
    end
    for tok in (s .. " "):gmatch("([^ ]+) *") do
        local tw = str_w(tok, fs)
        if w + tw > maxw and #cur > 0 then pushline() end
        if tw > maxw then
            for ch in utf_chars(tok) do
                local cw = char_w(ch, fs)
                if w + cw > maxw and #cur > 0 then pushline() end
                cur[#cur + 1] = ch
                w = w + cw
            end
        else
            cur[#cur + 1] = tok
            w = w + tw
        end
        cur[#cur + 1] = " "
        w = w + SPACE
    end
    pushline()
    if #lines > 2 then
        lines[2] = (lines[2] or "") .. "…"
        lines[3] = nil
    end
    return lines
end

local function redraw()
    if not no_art or not ov then return end
    local dim = mp.get_property_native("osd-dimensions") or {}
    local W, H = dim.w or 640, dim.h or 180
    if W <= 0 or H <= 0 then W, H = 640, 180 end
    ov.res_x, ov.res_y = W, H

    local pos   = mp.get_property_number("time-pos") or 0
    local dur   = mp.get_property_number("duration") or 0
    local title = mp.get_property("media-title") or "(no title)"
    local codec = mp.get_property("audio-codec-name") or "-"
    local sr    = mp.get_property_number("audio-params/samplerate") or 0
    local ch    = mp.get_property_number("audio-params/channel-count") or 0
    local idx   = mp.get_property("playlist-pos-1") or "1"
    local cnt   = mp.get_property("playlist-count") or "1"
    local pct   = dur > 0 and pos / dur or 0
    if pct > 1 then pct = 1 end

    local bw = W * 0.92
    local filled = math.floor(bw * pct)
    local x0 = W * 0.04

    local F_TIME, F_META, F_TITLE, F_FMT = H * 0.155, H * 0.072, H * 0.14, H * 0.072
    local line_h = F_TITLE * 1.20

    local a = {}
    -- ① 当前时间
    a[#a + 1] = string.format([[{\an7\pos(%.0f,%.0f)\fs%.0f\b1%s}%s]], x0, H * 0.045, F_TIME, WHITE, fmt(pos))
    -- ② 总时长 + 曲序
    a[#a + 1] = string.format([[{\an7\pos(%.0f,%.0f)\fs%.0f%s}%s   %s/%s]], x0, H * 0.27, F_META, GREY, fmt(dur), idx, cnt)
    -- ③ 歌名（最多 2 行，每行独立 ASS 事件）
    for i, ln in ipairs(wrap2(esc(title), F_TITLE, W * 0.60 - x0 - F_TITLE * 0.8)) do
        a[#a + 1] = string.format([[{\an7\pos(%.0f,%.0f)\fs%.0f%s}%s]],
            x0, H * 0.40 + (i - 1) * line_h, F_TITLE, WHITE, ln)
    end
    -- ④ 进度条（底 + 已播）
    a[#a + 1] = string.format([[{\an7\p1\pos(%.0f,%.0f)%s}m 0 0 l %.0f 0 l %.0f 4 l 0 4{\p0}]], x0, H * 0.85, BARBG, bw, bw)
    a[#a + 1] = string.format([[{\an7\p1\pos(%.0f,%.0f)%s}m 0 0 l %.0f 0 l %.0f 4 l 0 4{\p0}]], x0, H * 0.85, PURPLE, filled, filled)
    -- ⑤ 右上柱形：基于时间的装饰动画，不读 PCM（真频谱 V1 不做）
    local bar_bottom = H * 0.56
    for i = 0, 19 do
        local h = 4 + math.floor(24 * math.abs(math.sin(pos * 2.4 + i * 0.55)))
        local bx, by = W * 0.62 + i * (W * 0.015), bar_bottom - h
        a[#a + 1] = string.format([[{\an7\p1\pos(%.0f,%.0f)%s}m 0 0 l 6 0 l 6 %d l 0 %d{\p0}]], bx, by, PURPLE, h, h)
    end
    -- ⑥ 格式信息：柱形正下方右对齐（\an9）
    a[#a + 1] = string.format([[{\an9\pos(%.0f,%.0f)\fs%.0f%s}%s]], W * 0.96, H * 0.62, F_FMT, GREY,
        string.format("%s  %d Hz  %d ch", codec, sr, ch))

    ov.data = table.concat(a, "\n")
    ov:update()
end

local function decide()
    if not o.no_art_ui then
        if ov then ov:remove() end
        if timer then timer:kill(); timer = nil end
        no_art = false
        return
    end
    if not ov then
        local ok, created = pcall(mp.create_osd_overlay, "ass-events")
        if not ok or not created then
            msg.warn("[MyMPC/ui] 当前 mpv 不支持 ass-events overlay，信息窗已跳过")
            o.no_art_ui = false
            return
        end
        ov = created
    end

    local tl = mp.get_property_native("track-list") or {}
    local has_video = false
    for _, t in ipairs(tl) do
        if t.type == "video" then
            has_video = true
            break
        end
    end
    local aid = mp.get_property("aid")
    no_art = (not has_video) and (aid ~= nil and aid ~= "no")

    if no_art then
        redraw()
        if not timer then timer = mp.add_periodic_timer(0.15, redraw) end
    else
        if ov then ov:remove() end
        if timer then timer:kill(); timer = nil end
    end
end

-- ============================================================
-- ①b 同名外部音频：内嵌音轨优先（2026-11-17 用户要求）
--   mpv 的 audio-file-auto=exact 会在加载外部同名音轨后把 aid 切到外部轨
--   （实测：clip.mkv 内嵌 440Hz + clip.m4a 262Hz，exact 模式选中的是外部轨）。
--   这里在 file-loaded 后检查：若同时存在内嵌音轨和外部音轨，把 aid 拨回第一条内嵌轨。
--   只有外部轨、没有内嵌轨时保持 mpv 的选择（外部同名音频正常兜底）。
--   不动用户播放中手动切轨：只在换文件瞬间执行一次。
-- ============================================================
local function prefer_internal_audio()
    local tl = mp.get_property_native("track-list") or {}
    local first_internal, has_external = nil, false
    for _, t in ipairs(tl) do
        if t.type == "audio" then
            if t.external then has_external = true
            elseif first_internal == nil then first_internal = t.id end
        end
    end
    if has_external and first_internal ~= nil then
        mp.set_property("aid", first_internal)
    end
end

mp.register_event("file-loaded", function()
    prefer_internal_audio()
    decide()
    mp.add_timeout(0.2, function() prefer_internal_audio(); decide() end)  -- 兜底：属性初始化完成后再判一次
end)
mp.register_event("end-file", function()
    no_art = false
    if ov then ov:remove() end
    if timer then timer:kill(); timer = nil end
end)

-- ============================================================
-- ①c 外挂字幕改动后自动重读（2026-11-17 新增，用户要求，默认开）
--   mpv 原生只在你主动 sub-reload / 重载文件时才重读外挂字幕；
--   用字幕编辑器（如 Subtitle Edit / Aegisub）改盘上 .srt 后，播放中的
--   mpv 仍显示旧内容，必须手动切一次轨或 Ctrl+r。这里做轮询补救。
--
--   实测（本机 v0.42）：改文件后 sub-text 仍是旧值；`sub-reload <id>` 立即变新值。
--   外挂字幕轨的 track.external-filename 给出绝对路径；mp.utils.file_info()
--   返回 mtime/size，用二者做签名即可可靠判定"（内容）变了"。
--
--   设计取舍：
--     · 只管 external=true 的字幕轨——内封字幕在文件里，不存在"外部改动"；
--     · 只认 mtime+size 变化，不读文件内容（省 IO，0.75s 一次无感）；
--     · 换文件/end-file 时清空缓存，避免把上一部的签名带到下一部误判；
--     · 编辑器常见的"先清空再写入"会让 mtime 连变两次，reload 两次无害；
--     · 关闭开关时彻底 kill 定时器，不留运行开销。
-- ============================================================
local sub_signatures = {}   -- [track id] = "mtime:size"
local sub_timer = nil
local SUB_POLL = 0.75       -- 秒；0.75 是"够快又无感"的折中

local function scan_sub_reload()
    if not o.sub_autoreload then return end
    local tl = mp.get_property_native("track-list") or {}
    local live = {}
    for _, t in ipairs(tl) do
        if t.type == "sub" and t.external then
            local path = t["external-filename"]
            local id = t.id
            live[id] = true
            if path and path ~= "" then
                local info = utils.file_info(path)
                if info and info.is_file then
                    local sig = tostring(info.mtime) .. ":" .. tostring(info.size)
                    if sub_signatures[id] ~= nil and sub_signatures[id] ~= sig then
                        -- 首次记录签名不算变化；之后变化才 reload（避免刚加载就 reload 一次）
                        mp.commandv("sub-reload", tostring(id))
                    end
                    sub_signatures[id] = sig
                end
            end
        end
    end
    -- 清掉已不存在的轨（切轨/换字幕）的旧签名
    for id in pairs(sub_signatures) do
        if not live[id] then sub_signatures[id] = nil end
    end
end

local function reset_sub_reload()
    sub_signatures = {}
    if o.sub_autoreload and not sub_timer then
        sub_timer = mp.add_periodic_timer(SUB_POLL, scan_sub_reload)
    elseif not o.sub_autoreload and sub_timer then
        sub_timer:kill()
        sub_timer = nil
    end
end

-- 文件加载后稍等再启动轮询（等外挂字幕轨挂进 track-list）
mp.register_event("file-loaded", function()
    reset_sub_reload()
    mp.add_timeout(0.5, function()
        reset_sub_reload()
        scan_sub_reload()   -- 建立初始签名，这次不触发 reload
    end)
end)
mp.register_event("end-file", function()
    sub_signatures = {}
    if sub_timer then sub_timer:kill(); sub_timer = nil end
end)

-- ============================================================
-- ①d NVIDIA RTX 视频增强（2026-11-18 新增，1.0.2）
--   FrankyRenderer 1.2 的 RTX Video HDR 与 MPC Video Renderer、Chromium 走的是
--   同一个 D3D11 视频处理器扩展（VideoProcessorSetStreamExtension），不用 NVAPI。
--   mpv 0.40+ 把它包成 vf=d3d11vpp：nvidia-true-hdr 开 HDR，scaling-mode=nvidia 开 VSR。
--
--   为什么不直接写进 mpv.conf 的 vf=：
--     · VSR 有「分辨率上限」——MPCVR 只在源不超过档位时才让驱动处理，超了白烧 GPU；
--       mpv 的 d3d11vpp 没有这个开关，必须运行时按 width/height 动态挂卸。
--     · RTX HDR 只对 SDR 源有意义；HDR 片源开着会把 PQ 再转换一次，画质反而坏。
--     · d3d11vpp 要 d3d11va 零拷贝帧，auto-copy 下驱动拿不到原生纹理，挂了也不生效。
--   这里全部在 file-loaded / 属性变化时判断，不满足就 remove，零常驻开销。
--
--   ★【已知问题 · 等上游修】RTX HDR 偶发空指针崩溃（2026-11-21 定性）
--     播 SDR 片时偶发 0xC0000005（9 个 dump 中 8 个固定 mpv.exe+0x248CE63），截图时更易触发。
--     根因不是本脚本的 timer 竞态，也不是 target-colorspace-hint：是 d3d11vpp 的
--     nvidia-true-hdr 这一路动态挂载本身——只要 HDR 真挂上，D3D11 渲染线程在色彩协商 /
--     VO 重建 / 截图的敏感期挂卸滤镜就有空指针概率。hint=yes/auto/no 在 HDR 都挂上时
--     崩溃率无差别（实测 2/5、3/5、2/5），hint 只间接决定 HDR 判据（依赖
--     video-target-params/gamma==pq）是否成立——这也是"片播 2 秒后才变亮"的原因：
--     mpv 的输出色彩协商比 file-loaded 晚，判据成立才挂 HDR；MPCVR 是渲染器内建、
--     初始化一次配好，所以没有这个延迟。
--     结论：RTX HDR 必须保留，接受偶发；hint=yes 不动，auto 已实测无效。等上游修
--     （mpv issue #17800；PR #18199 只修了 HDR 源误触发，未覆盖本例）。
--     下次拉新源码时按 D:\mpvbuild\REBUILD-GUIDE.md §3.0b 复查是否修复。
--     完整证据链：MpvSettings\mpv偶发崩溃排查_20261121.md。
--
--   阈值照抄 MPCVR 的 CD3D11VP::SetSuperRes()（长边/短边归一化，横竖屏通吃）：
--     sd 1024×576 / 720p 1280×720 / 1080p 2048×1088 / 1440p 2560×1440
-- ============================================================
local RTX_LIMITS = {
    sd    = { 1024, 576 },
    ["720p"]  = { 1280, 720 },
    ["1080p"] = { 2048, 1088 },   -- MPCVR 注释 "a little more than Full HD"
    ["1440p"] = { 2560, 1440 },
}
local RTX_LABEL = "MyMPC-rtx"
local rtx_filter = nil            -- 当前已挂的滤镜串，nil = 没挂
local rtx_luma = false            -- 是否挂了峰值标注滤镜
local rtx_applying = false
local rtx_timer = nil            -- 统一防抖 timer：所有触发源共用，后一次取消前一次

local function rtx_remove()
    -- 截图 / VO 重建期间 mpv 可能已自行清掉滤镜，再 remove 会让 Lua 抛错中断；
    -- 用 pcall 包住。C 层空指针则由统一防抖降低命中概率。
    if rtx_luma then
        pcall(mp.commandv, "vf", "remove", "@" .. RTX_LABEL .. "-luma")
        rtx_luma = false
    end
    if rtx_filter == nil then return end
    pcall(mp.commandv, "vf", "remove", "@" .. RTX_LABEL)
    rtx_filter = nil
end

-- 长边≤long、短边≤short 才启用，与 MPCVR checkDimension 完全一致
local function rtx_fit(w, h, long, short)
    if w >= h then return w <= long and h <= short end
    return h <= long and w <= short
end

local function apply_rtx()
    if rtx_applying then return end
    if not (o.rtx_vsr or o.rtx_hdr) then rtx_remove() return end
    if not mp.get_property("path") then rtx_remove() return end

    -- 关于硬件解码：实测本机 mpv 0.42 + RTX 5070，d3d11va / auto-copy / 纯软解
    -- 三种路径下 d3d11vpp 都能拿到帧并启用驱动扩展（日志 "NVIDIA RTX ... enabled"）。
    -- mpv 会自动把软件帧上传到硬件，所以这里不按 hwdec 拦截——社区 autovsr.lua
    -- 的「必须 d3d11va」是旧版/特定场景结论，照抄会误杀可用配置。

    -- 前置 ②：必须有视频轨，且属性已就绪
    local w = mp.get_property_number("width") or 0
    local h = mp.get_property_number("height") or 0
    if w < 1 or h < 1 then rtx_remove() return end

    local prim  = mp.get_property("video-params/primaries") or ""
    local gamma = mp.get_property("video-params/gamma") or ""
    local pixel = mp.get_property("video-params/pixelformat") or ""
    local is_sdr = not (prim == "bt.2020" or gamma == "pq" or gamma == "hlg")

    -- VSR（MPCVR 策略）：源分辨率不超过档位上限 且 **画面实际被放大** 才挂。
    --
    -- 【2026-11-18 二次修复：scale 分子绝不能用 osd-width】
    --   用 osd 当分子会形成正反馈滚雪球：挂 d3d11vpp scale=N → filter 输出帧被撑大 N 倍
    --   → mpv 按 window-scale 重算窗口 → osd 跟着变大 → osd-width observe 再触发本函数
    --   → 算出更大的 scale → 再挂……实测按 Alt+3 后 scale 2.0→4.0→8.0→8.5、osd 320→2737，
    --   画面被连放两次（就是"放大两次"现象）。IPC 时间线见 _verify/rtx_ws_probe.py。
    --   手动探针（rtx_feedback_probe.py）证明：window-scale 是用户意图、挂滤镜后稳定不变，
    --   且 window-scale=2 + filter scale=2 时 osd 稳定 640（画面正好 2×，无二次放大）。
    --
    --   【2026-11-18 三次修复：滤镜倍率与窗口倍率解耦，消除"放大两次"】
    --   mpv 的硬机制：最终窗口 = 源 × filter scale × window-scale，两个倍率会相乘。
    --   之前 filter scale 跟着 window-scale 走（Alt+3=2 → filter=2），叠成 4×；
    --   ms 级时间线实测 osd 720→1440→2880，就是用户说的"放大两次"。
    --   正解（用户选定的方案2）：窗口放大全交给 window-scale（Alt+3 就是 2×），
    --   滤镜 scale 固定一个极小值 1.001——只为向驱动发 VSR 启用扩展，不参与画面放大。
    --   实测（min_scale_real.py，真实 DVD 片）：fs=1.001 时 osd=1440(正好2×)、out=720、VSR=ON、
    --   无 failed；1.01 起 osd 就会略超 2×。所以固定 1.001 是"触发 VSR 且零二次放大"的最优点。
    --   代价：VSR 的 AI 超分成分很小（帧几乎 1:1），主要是锐化/去噪/边缘重建；
    --   这是"不动快捷键体系"换取无二次放大的取舍。
    --
    --   zoom = 用户放大意图（只用于判断该不该挂 VSR，100% 不挂，符合 MPCVR 策略）：
    --     · 窗口模式：window-scale（Alt+2=1.0 / Alt+3=2.0 / fit90 设置的值）
    --     · 全屏模式：window-scale 不反映真实放大，用 osd/源；全屏窗口固定，无反馈风险
    local limit = RTX_LIMITS[o.rtx_vsr_limit] or RTX_LIMITS["1080p"]
    local fullscreen = mp.get_property_native("fullscreen") or false
    local zoom = 0
    if fullscreen then
        local ow = mp.get_property_number("osd-width") or 0
        local oh = mp.get_property_number("osd-height") or 0
        if ow > 0 and oh > 0 and w > 0 and h > 0 then
            zoom = math.floor(math.min(ow / w, oh / h) * 10 + 1e-9) / 10
        end
    else
        local ws = mp.get_property_number("window-scale") or 0
        if ws > 0 then
            zoom = math.floor(ws * 10 + 1e-9) / 10
        end
    end
    local VSR_FILTER_SCALE = 1.001   -- 仅触发驱动 VSR 扩展，不放大画面（见上方实测）
    local vsr = o.rtx_vsr and zoom > 1 and rtx_fit(w, h, limit[1], limit[2])

    -- HDR：只对 SDR 源生效，且必须 Windows HDR 真的开着。
    -- 判据用 mpv 协商出来的输出色彩空间：HDR 桌面下 target gamma=pq / primaries=bt.2020
    -- （实测 DISPLAY1 = RGB_FULL_G2084_NONE_P2020，10bit）；SDR 桌面是 bt.1886/bt.709。
    -- 不判这一条就是 mpv#17800 的洗白问题——驱动会把 SDR 按 PQ 输出，SDR 屏上直接过曝。
    local target_gamma = mp.get_property("video-target-params/gamma") or ""
    local target_prim  = mp.get_property("video-target-params/primaries") or ""
    local desktop_hdr  = target_gamma == "pq" or target_prim == "bt.2020"
    local hdr = o.rtx_hdr and is_sdr and desktop_hdr
    local is_10bit = pixel:match("10le$") ~= nil or pixel == "p010"

    if not vsr and not hdr then rtx_remove() return end

    local parts = { "@" .. RTX_LABEL .. ":d3d11vpp" }
    local function add(s) parts[#parts + 1] = s end
    if vsr then
        add("scaling-mode=nvidia")
        add("scale=" .. string.format("%.3f", VSR_FILTER_SCALE))
        if is_10bit and not hdr then add("format=nv12") end
    end
    if hdr then
        if is_10bit then add("format=x2bgr10") end
        add("nvidia-true-hdr")
    end

    local wanted = table.concat(parts, ":")
    if wanted == rtx_filter then return end

    rtx_applying = true
    rtx_remove()
    local ok, err = pcall(mp.commandv, "vf", "add", wanted)
    if ok then
        rtx_filter = wanted
        -- HDR 峰值标注：mpv 默认猜 1000 nit，和 NVIDIA App 里设的不一致时高光会偏暗/偏亮
        if hdr then
            pcall(mp.commandv, "vf", "add", "@" .. RTX_LABEL .. "-luma:format=max-luma="
                .. tostring(o.rtx_hdr_peak))
            rtx_luma = true
        end
        msg.info("[MyMPC/rtx] " .. (vsr and "VSR " or "") .. (hdr and "RTX-HDR" or "")
            .. " (" .. tostring(w) .. "x" .. tostring(h) .. ")")
    else
        msg.warn("[MyMPC/rtx] vf add failed: " .. tostring(err))
    end
    rtx_applying = false
end

-- 统一的防抖调度（2026-11-21 偶发崩溃修复）：
-- 原来 6 个 observe_property + file-loaded 各自 add_timeout 排队，一次播放能排 8 个
-- 独立 timer，彼此无互斥，在 VO 重建敏感期反复 remove/add d3d11vpp，偶发触发
-- mpv d3d11vpp 空指针（0xC0000005 @ mpv.exe+0x248CE63，9 个 dump 中 8 个命中）。
-- 现在所有触发源共用单个 timer：后一次触发 kill 前一次 pending，只保留最后一次意图。
--
-- 【2026-11-21 14:4x 回归修复】单一 timer 会被「流式属性」饿死：
--   mpv.conf 里 hdr-compute-peak=yes，video-target-params 的 max-luma 每帧都在变，
--   实测 6 s 内 180 次通知（≈30 Hz）。每次通知都 kill 掉重排 0.15 s → 永远走不到，
--   apply_rtx 只在开场跑过一两次，之后按 Alt+4 / Alt+3 放大窗口完全没反应（VSR 挂不上）。
--   两手一起改：① 触发源侧只认 gamma / primaries 变化（见下方 observe）；
--                ② 这里加最大等待，任何来源连续刷屏时满 0.6 s 不再重置，让已排的照常触发。
local RTX_DEBOUNCE_MAX = 0.6
local rtx_wait_from = nil        -- 本次防抖窗口第一次触发的时间

local function schedule_apply_rtx(delay)
    local now = mp.get_time()
    if rtx_timer then
        if rtx_wait_from and now - rtx_wait_from >= RTX_DEBOUNCE_MAX then
            return   -- 已等到上限：别再往后推，让已排的 timer 自己触发
        end
        rtx_timer:kill()
        rtx_timer = nil
    else
        rtx_wait_from = now
    end
    rtx_timer = mp.add_timeout(delay or 0.15, function()
        rtx_timer = nil
        rtx_wait_from = nil
        apply_rtx()
    end)
end

mp.register_event("file-loaded", function()
    rtx_remove()   -- 换文件先摘掉上一部的滤镜，再按新片源判定
    -- VO 色彩协商比 file-loaded 晚；0.25s 判一次
    schedule_apply_rtx(0.25)
    -- 1s 兜底：HDR 判据靠 video-target-params，协商可能更晚；只在还没挂上时补判，
    -- 挂对了直接跳过（幂等），不额外制造 vf 挂卸。
    mp.add_timeout(1.0, function()
        if rtx_filter == nil then apply_rtx() end
    end)
end)
mp.register_event("end-file", function()
    if rtx_timer then rtx_timer:kill(); rtx_timer = nil end
    rtx_wait_from = nil
    rtx_remove()
end)
mp.observe_property("video-params", "native", function() schedule_apply_rtx(0.15) end)
mp.observe_property("hwdec-current", "native", function() schedule_apply_rtx(0.15) end)
-- MPCVR 策略：窗口倍率变化（Alt+2/3 / fit90 Alt+4 / 拖拽窗口）要实时重算 VSR。
-- 【关键】observe window-scale 而不是 osd-width——osd 会被滤镜自己的输出撑大，
-- 拿它当触发源会和 scale 计算形成正反馈（实测滚到 scale=8.5 / osd=2737）。
-- window-scale 是用户意图且挂滤镜后稳定，不参与反馈。
mp.observe_property("window-scale", "native", function() schedule_apply_rtx(0.2) end)
mp.observe_property("fullscreen", "native", function() schedule_apply_rtx(0.2) end)
-- video-target-params 在 hdr-compute-peak=yes 下每帧都变（max-luma 在刷），原样转发会把
-- 统一防抖 timer 无限重置（实测 30 Hz → apply_rtx 0 次）。HDR 判据只用到 gamma / primaries，
-- 这两个不变就丢弃通知；首次 nil 通知照常排程，用来拿初始判据。
local rtx_target_key = nil
mp.observe_property("video-target-params", "native", function(_, v)
    local key = v and (tostring(v.gamma) .. "|" .. tostring(v.primaries)) or ""
    if key == rtx_target_key then return end
    rtx_target_key = key
    schedule_apply_rtx(0.15)
end)

-- ============================================================
-- ② 单实例转发
--   语义：启动时带文件参数（双击媒体文件）→ 若已有别的实例，转发 loadfile 并自退
--         不带文件参数（直接运行 mplayerl.exe）或只有自己 → 正常开新窗口
-- ============================================================
local forwarded_done = false
local pending_focus = false   -- 本次 file-loaded 来自别的实例转发，需要把窗口抢回前台

local function log(s) msg.info("[MyMPC/single] " .. s) end

local function esc_json(s)
    return (s:gsub('[\\"]', { ["\\"] = "\\\\", ['"'] = '\\"' }))
end

-- ------------------------------------------------------------
-- Win32 绑定（LuaJIT ffi）。绑定失败时单实例与提窗口一起跳过，
-- 绝不因为 cdef 出问题而打断播放。
-- ------------------------------------------------------------
local ffi
if jit ~= nil and jit.os == "Windows" then
    local ok_ffi, m = pcall(require, "ffi")
    if ok_ffi then
        ffi = m
        pcall(ffi.cdef, [[
        typedef void* HANDLE; typedef void* HWND; typedef unsigned long DWORD;
        typedef int BOOL; typedef unsigned int UINT;
        HANDLE CreateFileA(const char*, DWORD, DWORD, void*, DWORD, DWORD, HANDLE);
        int GetNamedPipeServerProcessId(HANDLE, DWORD*);
        int WriteFile(HANDLE, const void*, DWORD, DWORD*, void*);
        int CloseHandle(HANDLE);
        BOOL AllowSetForegroundWindow(DWORD);
        HWND GetForegroundWindow(void);
        BOOL SetForegroundWindow(HWND);
        BOOL ShowWindow(HWND, int);
        BOOL IsIconic(HWND);
        BOOL BringWindowToTop(HWND);
        BOOL SetWindowPos(HWND, HWND, int, int, int, int, UINT);
        DWORD GetWindowThreadProcessId(HWND, DWORD*);
        DWORD GetCurrentThreadId(void);
        BOOL AttachThreadInput(DWORD, DWORD, BOOL);
        void keybd_event(unsigned char, unsigned char, DWORD, void*);
        ]])
    end
end

-- 把播放窗口提到前台并拿焦点。三级降级，每级都是为了绕开
-- 「非前台进程不许抢焦点」这条 Windows 规则：
--   ① SetForegroundWindow   —— 第二实例已把权限转交过来，正常情况这一下就成
--   ② AttachThreadInput     —— 借当前前台窗口的输入队列，跳过检查
--   ③ 模拟一次 Alt 键       —— 让系统认为有用户输入，解除前台锁定（最后手段）
local function focus_window()
    if not ffi then return false end
    local id = mp.get_property_native("window-id")
    if type(id) ~= "number" or id <= 0 then return false end
    local h = ffi.cast("HWND", id)
    if h == nil then return false end

    if ffi.C.IsIconic(h) ~= 0 then ffi.C.ShowWindow(h, 9) end   -- SW_RESTORE

    local SWP = 0x0003                                          -- SWP_NOSIZE | SWP_NOMOVE
    ffi.C.SetWindowPos(h, ffi.cast("HWND", -1), 0, 0, 0, 0, SWP) -- HWND_TOPMOST
    ffi.C.SetWindowPos(h, ffi.cast("HWND", -2), 0, 0, 0, 0, SWP) -- HWND_NOTOPMOST
    ffi.C.BringWindowToTop(h)

    if ffi.C.SetForegroundWindow(h) ~= 0 then return "direct" end

    local fg = ffi.C.GetForegroundWindow()
    local pidbuf = ffi.new("DWORD[1]")
    local fgt = ffi.C.GetWindowThreadProcessId(fg, pidbuf)
    local myt = ffi.C.GetCurrentThreadId()
    if fgt ~= 0 and fgt ~= myt then
        ffi.C.AttachThreadInput(myt, fgt, 1)
        local ok2 = ffi.C.SetForegroundWindow(h) ~= 0
        ffi.C.AttachThreadInput(myt, fgt, 0)
        if ok2 then return "attach" end
    end

    ffi.C.keybd_event(0x12, 0, 0, nil)   -- Alt down
    ffi.C.keybd_event(0x12, 0, 2, nil)   -- Alt up
    if ffi.C.SetForegroundWindow(h) ~= 0 then return "alt" end
    return false
end

local function try_focus(why)
    if not pending_focus then return end
    local noerr, ok = pcall(focus_window)   -- ffi 调用出错也不能让脚本挂掉
    if not noerr then
        log("focus(" .. why .. ") ERROR: " .. tostring(ok))
        pending_focus = false
        return
    end
    log(string.format("focus(%s) -> %s", why, tostring(ok)))
    if ok then pending_focus = false end
end

local function try_forward()
    if forwarded_done or not o.single_instance then return end
    local n = mp.get_property_number("playlist-count") or 0
    local path = (n > 0) and mp.get_property("playlist/0/filename") or nil
    if not path or path == "" then
        log("no file on command line -> keep this window")
        return
    end
    forwarded_done = true

    if not ffi then
        log("ffi unavailable -> single-instance skipped (keep this window)")
        return
    end

    local my_pid = mp.get_property_number("pid") or -1
    local ok, result, server_pid = pcall(function()
        local h = ffi.C.CreateFileA([[\\.\pipe\]] .. o.pipe_name, 0x40000000, 0, nil, 3, 0, nil)
        local INVALID = ffi.cast("HANDLE", -1)
        if h == nil or h == INVALID then return "no-pipe", 0 end

        local pidbuf = ffi.new("DWORD[1]")
        if ffi.C.GetNamedPipeServerProcessId(h, pidbuf) == 0 or pidbuf[0] == my_pid then
            ffi.C.CloseHandle(h)
            return "self", pidbuf[0]
        end

        -- 关键一步：把「可以设置前台窗口」这一权限转交给已在运行的那一实例。
        -- 本进程是用户双击文件时由 explorer 拉起来的，带着前台权限；
        -- 不转交的话，对方的 SetForegroundWindow 会被系统前台锁定挡掉
        -- —— 表现就是「窗口尺寸对，但焦点留在后台」。
        ffi.C.AllowSetForegroundWindow(pidbuf[0])

        -- 用 script-message 而不是直接 loadfile：让对方知道这次是「被转发」，
        -- 换完文件顺手把窗口抢到前台（直接发 loadfile 时对方无从判断要不要抢）
        local data = '{"command":["script-message","MyMPC-ui-forward","' .. esc_json(path) .. '"]}\n'
        local written = ffi.new("DWORD[1]")
        local okw = ffi.C.WriteFile(h, data, #data, written, nil)
        ffi.C.CloseHandle(h)
        if okw == 0 or written[0] < #data then return "write-failed", pidbuf[0] end
        return "forwarded", pidbuf[0]
    end)

    if not ok then
        log("ffi ERROR: " .. tostring(result) .. " -> keep this window")
        return
    end

    if result == "forwarded" then
        log(string.format("FORWARDED to pid=%s -> %s", tostring(server_pid), path))
        -- 留一点时间让服务端读走管道缓冲区，再退出自己（约 0.15s 闪窗）
        mp.add_timeout(0.15, function() mp.commandv("quit") end)
    else
        log(string.format("no other instance (%s) -> keep this window", tostring(result)))
    end
end

try_forward()

-- 已在运行的那一实例收到转发：换文件 + 把窗口抢回前台
mp.register_script_message("MyMPC-ui-forward", function(path)
    if not path or path == "" then
        log("MyMPC-ui-forward: empty path (ignored)")
        return
    end
    log("received forward -> " .. path)
    pending_focus = true
    mp.set_property_native("window-minimized", false)
    mp.commandv("loadfile", path, "replace")
    -- 立即试一次；若换文件途中窗口被重建，file-loaded 里会用新句柄再试一次
    mp.add_timeout(0.10, function() try_focus("after-loadfile") end)
    mp.add_timeout(0.60, function() try_focus("late") end)
end)

mp.register_event("file-loaded", function()
    if not forwarded_done then
        try_forward()
    elseif pending_focus then
        mp.add_timeout(0.15, function() try_focus("file-loaded") end)
    end
end)

-- 右键菜单「选项(&O)」→ 打开「MPV 简单设置」（外部 WPF 程序）
--
-- 为什么写在 lua 里而不是 input.conf 的 run：run 不做 ~~/ 展开，路径一旦写死，
-- 换个安装目录就失效。这里用 expand-path 拿到 portable_config 的真实路径，
-- 再按三种可能的摆放位置找 MpvSettings.exe，并把 --config-dir 显式传过去，
-- 保证设置程序改的是「当前这个 mpv」的配置，而不是注册表里那个。
--
-- 触发方式（menu.conf 顶层条目）：
--   选项(&O)	script-message open-settings

local function exists(path)
    local f = io.open(path, "rb")
    if f then
        f:close()
        return true
    end
    return false
end

local function config_dir()
    local dir = mp.command_native({"expand-path", "~~/"})
    if dir == nil or dir == "" then
        return nil
    end
    if dir:sub(-1) ~= "/" and dir:sub(-1) ~= "\\" then
        dir = dir .. "/"
    end
    return dir
end

local function find_exe(cfg)
    local candidates = {
        cfg .. "../MpvSettings.exe",                  -- MediaPlayer\mpv\MpvSettings.exe（生产布局）
        cfg .. "MpvSettings.exe",                     -- 塞进 portable_config 里
        cfg .. "../MpvSettings/MpvSettings.exe",      -- 单独一层子目录
    }
    for _, path in ipairs(candidates) do
        if exists(path) then
            return path
        end
    end
    return nil
end

mp.register_script_message("open-settings", function()
    local cfg = config_dir()
    if not cfg then
        mp.osd_message("读不到 portable_config 路径，打不开 MPV 简单设置", 4)
        return
    end

    local exe = find_exe(cfg)
    if not exe then
        mp.osd_message("没找到 MpvSettings.exe：应放在 mpv.exe 同级目录", 4)
        return
    end

    -- 传给设置程序的路径去掉结尾分隔符：Path.GetFullPath 会原样保留它，
    -- 后面的 Path.GetFileName / GetParent 判断都会跟着别扭
    local arg = "--config-dir=" .. cfg:gsub("[/\\]+$", "")

    -- run 是非阻塞的：设置程序起来后 mpv 继续播，不被挂住
    mp.command_native({"run", exe, arg})
end)

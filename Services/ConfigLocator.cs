using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace MpvSettings.Services;

/// <summary>
/// 配置目录定位。与 setting.ahk 第 19 行保持一致：
/// 安装根目录读 HKLM\SOFTWARE\...\App Paths\mplayerl.exe 的 Path 值，
/// 配置在 &lt;root&gt;\MediaPlayer\mpv\portable_config。
/// 绝不硬编码开发机绝对路径。
///
/// 2026-11-15 收紧：mplayerl.exe 是必装核心，App Paths 的 Path 定义一定存在；
/// MPV 是选装，所以只有 &lt;Path&gt;\MediaPlayer\mpv\mpv.exe 存在才算装了 MPV。
/// 一旦读到 Path，就把配置目录锚死在它下面，绝不再往 EXE 附近回退找
/// 12.mpv\portable_config —— 否则"没装 MPV 的机器"会误把开发包模板当生产配置来写。
///
/// 额外提供两条仅用于开发 / 验证的路径：
///   --config-dir=&lt;dir&gt;  显式指定 portable_config（验证 / 排错用）
///   开发包回退            仅在 App Paths 完全不存在时，从 EXE 所在目录向上找
/// 窗口会把实际来源与完整路径显示出来，避免"以为在改 A，实际在读 B"。
/// </summary>
public sealed class ConfigLocator
{
    private const string AppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\mplayerl.exe";

    public string Root { get; private set; } = string.Empty;

    /// <summary>App Paths 读到的安装根目录（必装核心，读不到就是空串）。</summary>
    public string InstallRoot { get; private set; } = string.Empty;

    /// <summary>&lt;InstallRoot&gt;\MediaPlayer\mpv\mpv.exe，判定 MPV 是否已安装的唯一依据。</summary>
    public string InstallMpvExe { get; private set; } = string.Empty;

    public string ConfigDir { get; private set; } = string.Empty;
    public string MpvExe { get; private set; } = string.Empty;
    public string Source { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public bool IsUsable { get; private set; }

    public string MpvConf => Join("mpv.conf");
    public string InputConf => Join("input.conf");
    public string MenuConf => Join("menu.conf");
    public string HdrConf => Join("script-opts", "hdr-autoswitch.conf");
    public string MyMpcOpts => Join("script-opts", "MyMPC.conf");
    public string Fit90Conf => Join("script-opts", "fit90.conf");

    /// <summary>1.0.7：n*m 缩略图参数（行列/总宽上限/间距/mpv.exe 路径），由 EXE 独占生成。</summary>
    public string ThumbnailsConf => Join("script-opts", "thumbnails.conf");

    /// <summary>1.0.7：缩略图 Lua 脚本，作为定版资产从 EXE 内嵌资源写回。</summary>
    public string ThumbnailsLua => Join("scripts", "thumbnails.lua");
    public bool ThumbnailsLuaFileExists => File.Exists(ThumbnailsLua);

    /// <summary>阶段 ⑧b 起由 EXE 独占生成的右键菜单外观（OSD 菜单配色）。</summary>
    public string ContextMenuConf => Join("script-opts", "context_menu.conf");

    public string CoverArtConf => Join("cover-art.conf");
    public string AudioInfoConf => Join("audio-info.conf");

    /// <summary>杜比视界硬解兜底：零拷贝默认下给 DV 片源回退 auto-copy（2026-11-24 新增）。</summary>
    public string DolbyVisionConf => Join("dolby-vision.conf");
    public string CacheDir => Join("cache");
    public string ScriptsDir => Join("scripts");

    /// <summary>阶段 ⑦ 起由 EXE 独占生成 / 管理的 Lua 脚本（整文件写，不做 diff）。</summary>
    public string MyMpcLua => Join("scripts", "MyMPC_ui.lua");

    /// <summary>阶段 ⑧b 起由 EXE 独占生成：右键「选项」→ 拉起本 EXE，路径解析在 Lua 里做。</summary>
    public string MpvSettingsLua => Join("scripts", "mpv-settings.lua");

    /// <summary>BassMax 原厂面板（DShow 滤镜自带的属性页）。找不到就禁用逃生口按钮，不猜路径。</summary>
    public string BassMaxPanel
    {
        get
        {
            var candidates = new List<string>();
            if (InstallRoot.Length > 0)
            {
                candidates.Add(Path.Combine(InstallRoot, "Codecs", "bassmax_dshow.ax"));
                candidates.Add(Path.Combine(InstallRoot, "00.Core", "bassmax_dshow.ax"));
            }
            if (Root.Length > 0 && !string.Equals(Root, InstallRoot, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(Root, "Codecs", "bassmax_dshow.ax"));
                candidates.Add(Path.Combine(Root, "00.Core", "bassmax_dshow.ax"));
            }
            if (ConfigDir.Length > 0)
            {
                candidates.Add(Path.Combine(ConfigDir, "bassmax_dshow.ax"));
                string parent = Directory.GetParent(ConfigDir)?.FullName ?? string.Empty;
                if (parent.Length > 0) candidates.Add(Path.Combine(parent, "bassmax_dshow.ax"));
            }
            return FirstExisting(candidates.ToArray());
        }
    }

    public bool HasBassMaxPanel => BassMaxPanel.Length > 0;

    /// <summary>
    /// BassMax 托盘程序。与 mplayerl.exe 的 StartBassMaxTray 同一目标：
    /// 生产包在 &lt;InstallRoot&gt;\Codecs\bassmax_tray.exe，开发包 12.mpv 放在 mpv.exe 同目录。
    /// 找不到返回空串——调用方静默跳过，不提示。
    /// </summary>
    public string BassMaxTray
    {
        get
        {
            var candidates = new List<string>();
            if (InstallRoot.Length > 0)
            {
                candidates.Add(Path.Combine(InstallRoot, "Codecs", "bassmax_tray.exe"));
                candidates.Add(Path.Combine(InstallRoot, "00.Core", "bassmax_tray.exe"));
            }
            if (Root.Length > 0 && !string.Equals(Root, InstallRoot, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(Path.Combine(Root, "Codecs", "bassmax_tray.exe"));
                candidates.Add(Path.Combine(Root, "12.mpv", "bassmax_tray.exe"));
                candidates.Add(Path.Combine(Root, "bassmax_tray.exe"));
            }
            if (ConfigDir.Length > 0)
            {
                string mpvDir = Directory.GetParent(ConfigDir)?.FullName ?? string.Empty;
                if (mpvDir.Length > 0) candidates.Add(Path.Combine(mpvDir, "bassmax_tray.exe"));
                candidates.Add(Path.Combine(ConfigDir, "bassmax_tray.exe"));
            }
            return FirstExisting(candidates.ToArray());
        }
    }

    private string Join(params string[] parts)
        => ConfigDir.Length == 0 ? string.Empty : Path.Combine(new[] { ConfigDir }.Concat(parts).ToArray());

    public static ConfigLocator Resolve(string[] args)
    {
        var loc = new ConfigLocator();

        // 必装核心 mplayerl.exe 的 App Paths 是安装根目录的唯一权威来源。
        // MPV 是选装：还得再看 <Path>\MediaPlayer\mpv\mpv.exe 在不在。
        string installRoot = ReadInstallRoot() ?? string.Empty;
        loc.InstallRoot = installRoot;
        loc.InstallMpvExe = installRoot.Length > 0
            ? Path.Combine(installRoot, "MediaPlayer", "mpv", "mpv.exe")
            : string.Empty;
        bool mpvInstalled = loc.InstallMpvExe.Length > 0 && File.Exists(loc.InstallMpvExe);

        // 1) 命令行覆盖（支持 --config-dir=X 与 --config-dir X 两种写法）
        for (int i = 0; i < args.Length; i++)
        {
            string dir = null;
            if (args[i].StartsWith("--config-dir=", StringComparison.OrdinalIgnoreCase))
                dir = args[i].Substring("--config-dir=".Length);
            else if (string.Equals(args[i], "--config-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                dir = args[++i];
            if (dir is null) continue;
            dir = dir.Trim().Trim('"');
            loc.Root = installRoot.Length > 0
                ? installRoot
                : Directory.GetParent(Path.GetFullPath(dir))?.FullName ?? Path.GetFullPath(dir);
            loc.ConfigDir = Path.GetFullPath(dir);
            loc.Source = "命令行 --config-dir";
            break;
        }

        // 2) App Paths 注册表（生产路径）。只要 Path 有值就锚定，不再看目录是否存在：
        //    --default 在首次安装时也可能要把配置整份建出来。
        if (loc.ConfigDir.Length == 0 && installRoot.Length > 0)
        {
            loc.Root = installRoot;
            loc.ConfigDir = Path.Combine(installRoot, "MediaPlayer", "mpv", "portable_config");
            loc.Source = "App Paths 注册表";
        }

        // 3) 开发包回退：仅当这台机器压根没有 App Paths（源码包 / 验证环境）才允许。
        //    装了 MyMPC 却没勾 MPV 时绝不走这里，免得把 12.mpv 模板当生产配置。
        if (loc.ConfigDir.Length == 0 && installRoot.Length == 0)
        {
            var probe = new DirectoryInfo(AppContext.BaseDirectory);
            for (int up = 0; up <= 5 && probe is not null; up++, probe = probe.Parent)
            {
                foreach (var rel in new[] { Path.Combine("12.mpv", "portable_config"),
                                           Path.Combine("MediaPlayer", "mpv", "portable_config") })
                {
                    var cfg = Path.Combine(probe.FullName, rel);
                    if (File.Exists(Path.Combine(cfg, "mpv.conf")))
                    {
                        loc.Root = probe.FullName;
                        loc.ConfigDir = cfg;
                        loc.Source = "开发包目录回退";
                        break;
                    }
                }
                if (loc.ConfigDir.Length > 0) break;
            }
        }

        if (loc.ConfigDir.Length == 0)
        {
            loc.Reason = "未找到 mpv 配置目录（App Paths 注册表无记录，且未在 EXE 附近找到 portable_config）";
            return loc;
        }

        // 硬闸：装了 MyMPC 但没装 MPV —— 直接禁用，不许回退、不许写盘。
        if (installRoot.Length > 0 && !mpvInstalled)
        {
            loc.Reason = "未安装 MPV（找不到 " + loc.InstallMpvExe + "），设置已禁用";
            return loc;
        }

        // 可用性：mpv.exe 优先认安装根目录下那颗；--config-dir 常指向只有配置的副本目录，
        // 本身没有 exe，所以无论如何都把 App Paths 的路径放在候选第一位。
        loc.MpvExe = FirstExisting(
            loc.InstallMpvExe,
            Path.Combine(loc.Root, "MediaPlayer", "mpv", "mpv.exe"),
            Path.Combine(Directory.GetParent(loc.ConfigDir)?.FullName ?? string.Empty, "mpv.exe"),
            Path.Combine(loc.ConfigDir, "mpv.exe"));

        if (loc.MpvExe.Length == 0)
            loc.Reason = "未安装 MPV（找不到 mpv.exe），设置已禁用";
        else if (!File.Exists(loc.MpvConf))
            loc.Reason = "缺少 mpv.conf，设置已禁用";
        else
            loc.IsUsable = true;

        return loc;
    }

    private static string FirstExisting(params string[] candidates)
    {
        foreach (var p in candidates)
            if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return p;
        return string.Empty;
    }

    private static string ReadInstallRoot()
    {
        try
        {
            // setting.ahk 用了 SetRegView 64，这里必须显式 64 位视图
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(AppPathsKey);
            return key?.GetValue("Path") as string;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

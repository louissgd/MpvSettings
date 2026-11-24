using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Media;
using MpvSettings.Models;

namespace MpvSettings.Services;

/// <summary>
/// 「恢复默认」的唯一实现。命令行 <c>--default</c> 与设置程序里的按钮都走这里。
///
/// 目标态 = 出厂草稿（<see cref="SettingsDraft"/> 的字段初值 + ResetAll），
/// 基准态 = 盘上现状 + <see cref="DraftBaseline.Force"/>（所有属性都算「改过」）。
/// 于是 diff 结果就是「把每一项都写成出厂值」，而不需要另抄一份键表——
/// 抄一份迟早和 <see cref="WritePlanBuilder"/> 走岔，那才是最贵的 bug。
///
/// 与普通保存的差别只有三处：
///   1. 不跳过「自定义态」（用户手工标定的值也要被恢复，这是「恢复默认」的定义）；
///   2. ⑧b 的外观键（osd-* / load-context-menu）与 HDR 直通定版键
///      （target-colorspace-hint / target-trc）也一起恢复；
///   3. ⑧b 的两个独占文件（context_menu.conf / mpv-settings.lua）从 EXE 内嵌资源重写。
/// </summary>
public static class DefaultProfile
{
    private const string MpvLabel = "mpv.conf";
    private const string HdrLabel = "script-opts\\hdr-autoswitch.conf";
    private const string ContextMenuLabel = "script-opts\\context_menu.conf";
    private const string MenuLuaLabel = "scripts\\mpv-settings.lua";
    private const string MenuLabel = "menu.conf";
    private const string DolbyLabel = "dolby-vision.conf";

    /// <summary>出厂草稿。ResetAll 会把页 E 也刷成「本包发行时的 10 行」。</summary>
    public static SettingsDraft DefaultDraft()
    {
        var d = new SettingsDraft();
        // --default 不经过 GUI 的 LoadFrom/RefreshReadOnly，三项环境状态必须先读。
        // 顺序要命：ResetAll()→SetVideoDefaults() 会按环境状态归零不合格的 RTX 开关，
        // 若在读值之前 ResetAll，看到的是 -1（「未检测到」），会把本该出厂打开的开关误关，
        // 恢复默认反而写出 rtx_vsr=no / rtx_hdr=no。所以先读、后 ResetAll。
        d.NvVsrAppRaw = NvidiaAppState.ReadVsr();
        d.NvHdrAppRaw = NvidiaAppState.ReadHdr();
        d.WindowsHdrEnabled = WindowsHdrState.IsHdrEnabled();
        d.HdrSupported = WindowsHdrState.IsHdrSupported();
        d.ResetAll();
        ApplyDetectedSubtitleFont(d);
        return d;
    }

    /// <summary>
    /// 按本机字体把字幕默认从「静态兜底（微软雅黑 / 48）」升级为「华文楷体 / 52」。
    ///
    /// <para>为什么放这里而不是 SettingsDraft 的字段初值：mpv.conf 模板与导出/导入的
    /// 出厂值必须逐字节一致（微软雅黑 48），探测结果只属于「这台机器恢复默认」这一步。
    /// 放字段初值会让 GUI 一打开就写出台机值，破坏模板一致性；放这里则只有安装时
    /// 跑的 <c>--default</c> 与导入路径会升级，正是需要「装完适配本机字体」的时机。</para>
    ///
    /// <para>字体族名用 WPF 的 Fonts.SystemFontFamilies 探测（程序本身是 WPF，
    /// <c>--default</c> 在 Application.OnStartup 的 STA 线程上跑，安全）。
    /// 测试片内封 ASS 用的就是 STKaiti，用户实测 52 是楷体的舒适字号；
    /// 探测不到（DLL 加载失败 / 精简系统）时保持静态默认，不报错、不阻断安装。</para>
    /// </summary>
    private static void ApplyDetectedSubtitleFont(SettingsDraft d)
    {
        try
        {
            bool hasKaiti = Fonts.SystemFontFamilies.Any(
                f => string.Equals(f.ToString(), "STKaiti", StringComparison.OrdinalIgnoreCase));
            if (!hasKaiti) return;

            d.SubFont = "STKaiti";
            d.SubFontSize = 52;
        }
        catch (Exception)
        {
            // 字体枚举失败就用静态默认，恢复默认不能因此失败（安装包只认退出码）。
        }
    }

    public static WritePlan Build(ConfigSnapshot s)
    {
        var disk = new SettingsDraft();
        disk.LoadFrom(s);

        var plan = WritePlanBuilder.Build(DefaultDraft(), DraftBaseline.Force(disk), s);

        EnsureFixedAssets(plan, s);
        TrimPlan(plan);
        return plan;
    }

    /// <summary>
    /// 「定版资产」：没有对应控件、但每台机器都必须是这一份的东西。
    /// 恢复默认与导入都调用它——导入不能把别的机器上的旧脚本 / 被改坏的配色带过来。
    /// 五条 osd 配色 + load-context-menu + HDR 直通定版键写进 mpv.conf；
    /// 三个独占文件从 EXE 内嵌资源重写；menu.conf 只补「选项(&O)」一行（用户自己加的条目不动）。
    ///
    /// 2026-11-24：dolby-vision.conf 进这套定版资产。默认硬解改 d3d11va 零拷贝后，
    /// 这个文件是 DV 片源不黑屏的唯一兜底——恢复默认 / 导入漏掉它，等于把 P5 搞坏。
    /// include 行按 cover-art 同样的「只补缺」规则处理。
    /// </summary>
    public static void EnsureFixedAssets(WritePlan plan, ConfigSnapshot s)
    {
        AddAppearanceKeys(plan, s);
        AddHdrFixedKeys(plan, s);
        AddScreenshotFixedKeys(plan, s);
        AddWholeFile(plan, ContextMenuLabel, s.Locator.ContextMenuConf, Resource("MpvSettings.context_menu.conf"));
        AddWholeFile(plan, MenuLuaLabel, s.Locator.MpvSettingsLua, Resource("MpvSettings.mpv-settings.lua"));
        AddWholeFile(plan, DolbyLabel, s.Locator.DolbyVisionConf, Resource("MpvSettings.dolby-vision.conf"));
        // 1.0.7：缩略图脚本走定版资产——恢复默认/导入都用 EXE 内嵌的最新版，
        // 不让备份里的旧脚本把新功能带回去。conf 参数由 WritePlanBuilder 按草稿正常生成。
        AddWholeFile(plan, "scripts\\thumbnails.lua", s.Locator.ThumbnailsLua, Resource("MpvSettings.thumbnails.lua"));
        EnsureDolbyInclude(plan, s);
        EnsureMenuEntry(plan, s);
    }

    /// <summary>include=~~/dolby-vision.conf 缺失时补回；已有则一行不动。</summary>
    private static void EnsureDolbyInclude(WritePlan plan, ConfigSnapshot s)
    {
        if (s.Mpv.HasInclude("dolby-vision.conf")) return;
        var file = plan.For(MpvLabel, s.Locator.MpvConf);
        if (file.Changes.Any(c => string.Equals(c.Key, "include", StringComparison.OrdinalIgnoreCase) &&
                                  c.To == "~~/dolby-vision.conf")) return;
        file.Changes.Add(new KeyChange
        {
            Key = "include",
            To = "~~/dolby-vision.conf",
            Group = "杜比视界硬解兜底",
            Append = true,
            AppendOnly = true,
        });
    }

    /// <summary>
    /// 全量写（--default / 导入）后的计划瘦身：ForceAll 会把「本来就是这个值」的键
    /// 也标成改动，写盘时 ConfWriter 自己会跳过，但明细会变成一堆「no → no」。
    /// </summary>
    public static void TrimPlan(WritePlan plan)
    {
        foreach (var f in plan.Files)
            f.Changes.RemoveAll(c => c.Transform is null && !c.Append && !c.Remove
                                     && string.Equals(c.From, c.To, StringComparison.Ordinal));
        plan.Files.RemoveAll(f => f.Changes.Count == 0 && !f.IsWholeFile && !f.IsInputFile);
        plan.Warnings.RemoveAll(w => w.Contains("非预设值（") || w.Contains("本次未写入"));
    }

    /// <summary>
    /// ⑧b 的五个外观键。它们没有对应控件（配色是定版设计），所以不在 SettingsDraft 里，
    /// 但「恢复默认」必须把它们一起恢复，否则用户改坏配色后没有任何归位手段。
    ///
    /// <para>颜色值必须带引号。mpv 读 conf 时，未加引号的值会从第一个 '#' 处截断
    /// （options/parse_configfile.c 的 bstrchr(line, '#')），于是 <c>osd-color=#FFF0F7F0</c>
    /// 的值为空 → 整行被丢弃 → 回落到默认黑。必须写成 <c>osd-color="#FFF0F7F0"</c>。
    /// 2026-11-15 踩过一次：四条颜色全没生效，日志里三行 "option requires parameter"。</para>
    /// </summary>
    private static readonly (string Key, string Value)[] Appearance =
    {
        ("osd-color", "\"#FFF0F7F0\""),
        ("osd-outline-color", "\"#FF1B2918\""),
        ("osd-selected-color", "\"#FFA3F6BE\""),
        ("osd-selected-outline-color", "\"#FF131F11\""),
        // 2026-11-16：右键菜单走 Windows 原生菜单（mpv 的 Windows 默认值）。
        // 原生菜单已由 menu-ownerdraw 补丁按同一套配色自绘，不需要 Lua OSD 菜单；
        // 写 yes 会让补丁完全失效（右键样式回到 OSD 那套、且出不了窗口）。
        ("load-context-menu", "no"),
    };

    private static void AddAppearanceKeys(WritePlan plan, ConfigSnapshot s)
    {
        var m = s.Mpv;
        var file = plan.For(MpvLabel, s.Locator.MpvConf);

        foreach (var (key, value) in Appearance)
        {
            // 已经是这个值就不写：恢复默认在「已经全默认」的现场应当零写入
            if (m.Has(key) && string.Equals(m.Get(key), value, StringComparison.Ordinal)) continue;
            if (file.Changes.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))) continue;

            file.Changes.Add(new KeyChange
            {
                Key = key,
                To = value,
                Group = "右键菜单配色",
                From = m.Has(key) ? m.Get(key) : string.Empty,
                Append = !m.Has(key),
            });
        }
    }

    /// <summary>
    /// HDR 直通定版键（2026-11-16）。这两个键没有对应控件，但「恢复默认」必须把它们归位，
    /// 否则会出现「界面显示真直通、盘上却仍被强制 SDR」的骗人状态：
    /// <c>setting.ahk</c> 的「HDR转SDR」会写 <c>target-colorspace-hint=no</c> +
    /// <c>target-trc=bt.1886</c>，而恢复默认走的是本 EXE 的 <c>--default</c>，
    /// 旧版完全不碰这两个键，结果 <c>tone-mapping</c> 已回到 <c>clip</c>、
    /// <c>mode</c> 已回到 <c>passthrough</c>，<c>bt.1886</c> 却把输出钉死在 SDR。
    ///
    /// <para>出厂态 = <c>target-colorspace-hint=yes</c> + <b>没有 target-trc 这一行</b>
    /// （模板 12.mpv\portable_config\mpv.conf 就是如此）。所以直通时是「删行」而不是
    /// 写 <c>auto</c>——写 auto 虽然功能等效，却会多出一行、破坏模板与默认值逐字节一致。</para>
    ///
    /// <para>目标态按「本次计划最终写入的 mode」判定，而不是硬写成直通：导入的若是
    /// <c>mode=tonemap</c> 的备份，输出侧就该保持 no + bt.1886，不能把转 SDR 掰成直通。
    /// 本方法在 TrimPlan 之前执行（恢复默认与导入都是先 EnsureFixedAssets 再 TrimPlan），
    /// 所以同值的 mode 改动此刻仍在计划里；取不到才回退盘上现值。</para>
    /// </summary>
    private static void AddHdrFixedKeys(WritePlan plan, ConfigSnapshot s)
    {
        string mode = s.Hdr.Get("mode", "passthrough");
        foreach (var f in plan.Files)
        {
            if (!string.Equals(f.Label, HdrLabel, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var c in f.Changes)
                if (!c.Remove && string.Equals(c.Key, "mode", StringComparison.OrdinalIgnoreCase))
                    mode = c.To;
        }

        bool toSdr = string.Equals(mode.Trim(), "tonemap", StringComparison.OrdinalIgnoreCase);
        string wantHint = toSdr ? "no" : "yes";

        var m = s.Mpv;
        var file = plan.For(MpvLabel, s.Locator.MpvConf);
        bool Pending(string key)
            => file.Changes.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

        // 已有别的逻辑（HDR 模式联动）准备写同一个键时不重复加，避免计划里出现两条同名改动。
        if (!Pending("target-colorspace-hint") &&
            (!m.Has("target-colorspace-hint") ||
             !string.Equals(m.Get("target-colorspace-hint"), wantHint, StringComparison.Ordinal)))
        {
            file.Changes.Add(new KeyChange
            {
                Key = "target-colorspace-hint",
                To = wantHint,
                Group = "HDR 直通定版",
                From = m.Has("target-colorspace-hint") ? m.Get("target-colorspace-hint") : string.Empty,
                Append = !m.Has("target-colorspace-hint"),
            });
        }

        if (Pending("target-trc")) return;

        if (!toSdr)
        {
            // Remove + 空 MatchValue：删掉任意值的 target-trc 行，回到「出厂无此键」。
            if (m.Has("target-trc"))
                file.Changes.Add(new KeyChange
                {
                    Key = "target-trc",
                    Remove = true,
                    Group = "HDR 直通定版",
                });
        }
        else if (!m.Has("target-trc") ||
                 !string.Equals(m.Get("target-trc"), "bt.1886", StringComparison.Ordinal))
        {
            file.Changes.Add(new KeyChange
            {
                Key = "target-trc",
                To = "bt.1886",
                Group = "HDR 直通定版",
                From = m.Has("target-trc") ? m.Get("target-trc") : string.Empty,
                Append = !m.Has("target-trc"),
            });
        }
    }

    /// <summary>
    /// 截图崩溃定版键（2026-11-24，1.0.7）：
    /// <c>sws-allow-zimg=no</c> 让 swscale 跳过 zimg 后端。zimg 主干
    /// （release-3.0.6-253-g67e0603）在 HDR10 截图做色彩转换时，崩在
    /// zimg::graph::GraphBuilder::impl::build_subgraph，退出码 0xC0000005。
    /// 生产完整配置 A/B 实测：默认 6/12 崩，关 zimg 后 0/12（出图 1248）。
    /// 该键没有对应控件，但恢复默认/导入必须归位——否则用户点一次恢复默认，
    /// 崩溃就回来了。走定版键与 HDR 直通同一套机制。
    /// </summary>
    private static void AddScreenshotFixedKeys(WritePlan plan, ConfigSnapshot s)
    {
        const string Key = "sws-allow-zimg";
        const string Value = "no";
        var m = s.Mpv;
        var file = plan.For(MpvLabel, s.Locator.MpvConf);

        // 已有别的逻辑准备写这个键时不重复加（虽然目前没有控件会写它）。
        if (file.Changes.Any(c => string.Equals(c.Key, Key, StringComparison.OrdinalIgnoreCase))) return;
        if (m.Has(Key) && string.Equals(m.Get(Key), Value, StringComparison.Ordinal)) return;

        file.Changes.Add(new KeyChange
        {
            Key = Key,
            To = Value,
            Group = "截图崩溃缓解",
            From = m.Has(Key) ? m.Get(Key) : string.Empty,
            Append = !m.Has(Key),
        });
    }

    /// <summary>内容与盘上一致就什么都不做——恢复默认不制造无意义的写盘时间戳。</summary>
    private static void AddWholeFile(WritePlan plan, string label, string path, string desired)
    {
        // 导入时可能已经把备份里的原文挂进计划了，别用内嵌资源再盖一次
        if (plan.Files.Any(x => x.Label == label && x.IsWholeFile)) return;

        string have = File.Exists(path)
            ? Encoding.UTF8.GetString(File.ReadAllBytes(path))
            : null;

        if (have is not null && Same(have, desired)) return;

        var f = plan.For(label, path);
        f.NewContent = desired;
        f.IsCreate = have is null;
        f.WholeFileKind = "脚本 / 配置";
    }

    /// <summary>
    /// menu.conf 的「选项(&O)」一行。菜单文件本身是 mpv 自带的（用户可能自己加过条目），
    /// 所以不做整文件覆盖：只在这一行缺失时补回来，位置与 ⑧b 手工插入时一致（「配置」之前）。
    /// </summary>
    private static void EnsureMenuEntry(WritePlan plan, ConfigSnapshot s)
    {
        if (plan.Files.Any(x => x.Label == MenuLabel && x.IsWholeFile)) return;
        string path = s.Locator.MenuConf;
        if (!File.Exists(path)) return;

        string raw = Encoding.UTF8.GetString(File.ReadAllBytes(path));
        if (raw.Contains("script-message open-settings", StringComparison.OrdinalIgnoreCase)) return;

        string nl = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = raw.Replace("\r\n", "\n").Split('\n').ToList();

        string entry = "选项(&O)\tscript-message open-settings";
        int at = lines.FindIndex(l => l.StartsWith("配置", StringComparison.Ordinal));
        if (at < 0) at = Math.Max(0, lines.Count - 1);   // 没有「配置」就放到末尾前
        lines.Insert(at, entry);

        var f = plan.For(MenuLabel, path);
        f.NewContent = string.Join(nl, lines);
        f.WholeFileKind = "右键菜单（补「选项」）";
    }

    private static string Resource(string logicalName)
    {
        var asm = typeof(DefaultProfile).Assembly;
        using var st = asm.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException("内嵌资源丢失（打包问题）：" + logicalName);
        using var sr = new StreamReader(st, Encoding.UTF8);
        // 保留文件原样：这些文件的换行风格（LF）本身就是约定，别在这里"修正"
        return sr.ReadToEnd();
    }

    private static bool Same(string a, string b)
        => string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);

    private static string Norm(string t) => (t ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n');
}

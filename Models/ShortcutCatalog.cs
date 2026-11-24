using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MpvSettings.Models;

/// <summary>页 E 的一行固定清单（阶段 ⑥）。默认键来自 mpv 内置弱绑定，ShipLine 表示本包发行时 input.conf 里本来就有这一行。</summary>
public sealed class ShortcutAction
{
    public string Id { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;

    /// <summary>mpv 0.42.0 的内置默认键；空串表示没有默认绑定（E5 那四行）。</summary>
    public string DefaultKey { get; init; } = string.Empty;

    /// <summary>本包 input.conf 发行时已有这一行（E4 的 10 行）。</summary>
    public bool ShipLine { get; init; }

    /// <summary>副标题补充说明，照抄规格里的既有踩坑提示，别删。</summary>
    public string Note { get; init; } = string.Empty;

    public bool HasDefault => DefaultKey.Length > 0;

    /// <summary>匹配 input.conf 行用的规范命令串。</summary>
    public string NormCommand => ShortcutKeys.NormCommand(Command);
}

public sealed class ShortcutGroupDef
{
    public string Name { get; init; } = string.Empty;
    public List<ShortcutAction> Rows { get; init; } = new();
}

/// <summary>
/// 页 E 的 39 行固定清单（规格第 3 节页 E / 1.3 节）。
/// 默认表版本固定为 mpv 0.42.0；换 mpv 大版本时改这张表即可，不在启动时跑 mpv 导出（32 条不值得拉长打开时间）。
/// 1.0.4 起播放统计两条为本包发行时自带的强绑定：Tab = 临时显示（同 PotPlayer），
/// Ctrl+j = 常显切换（同 MPC-HC/BE）；mpv 原默认 i / I 是弱绑定，仍然可用。
/// </summary>
public static class ShortcutCatalog
{
    public const string DefaultBindingsVersion = "mpv 0.42.0";

    public static List<ShortcutGroupDef> Groups { get; } = Build();

    public static IEnumerable<ShortcutAction> All => Groups.SelectMany(g => g.Rows);

    private static ShortcutAction D(string id, string action, string command, string defKey,
                                    string note = "", bool ship = false)
        => new() { Id = id, Action = action, Command = command, DefaultKey = defKey, Note = note, ShipLine = ship };

    private static ShortcutAction N(string id, string action, string command, string note = "")
        => new() { Id = id, Action = action, Command = command, DefaultKey = string.Empty, Note = note };

    private static List<ShortcutGroupDef> Build() => new()
    {
        new ShortcutGroupDef
        {
            Name = "E1 · 播放控制",
            Rows =
            {
                D("e1.pause", "播放 / 暂停", "cycle pause", "SPACE", "默认还另有 p 键"),
                D("e1.quit", "停止 / 退出", "quit", "q"),
                D("e1.prev", "上一个文件", "playlist-prev", "<"),
                D("e1.next", "下一个文件", "playlist-next", ">"),
                D("e1.seek-5", "快退 5 秒", "seek -5", "LEFT"),
                D("e1.seek+5", "快进 5 秒", "seek 5", "RIGHT"),
                D("e1.seek-60", "快退 60 秒", "seek -60", "DOWN"),
                D("e1.seek+60", "快进 60 秒", "seek 60", "UP"),
                D("e1.frame", "逐帧前进", "frame-step", "."),
            }
        },
        new ShortcutGroupDef
        {
            Name = "E2 · 画面 / 截图",
            Rows =
            {
                // 本包把全屏切换默认键从 mpv 内置弱绑定 f 改为 Alt+ENTER。
                // 弱绑定删不掉，只能写一行强绑定覆盖；ShipLine=true 让它随「发行/恢复默认」一起写回 input.conf。
                // f 仍是 mpv 弱绑定，照常可用；ESC 退出全屏也不变 —— 即同一功能保留两个键。
                D("e2.fs", "全屏切换", "cycle fullscreen", "Alt+ENTER",
                  "原默认 f 仍可用；ESC 退出全屏不变", ship: true),
                D("e2.exitfs", "退出全屏", "set fullscreen no", "ESC"),
                D("e2.shot", "截图（含字幕）", "screenshot", "s"),
                D("e2.shotv", "截图（不含字幕）", "screenshot video", "S"),
                D("e2.shotw", "截图当前窗口", "screenshot window", "Ctrl+s",
                  "会连同字幕与 OSD 一起截入，并按窗口尺寸缩放；一般请用 s"),
                D("e2.progress", "显示进度", "show-progress", "o"),
                D("e2.stats", "播放统计 · 临时（Tab）", "script-binding stats/display-stats", "TAB",
                  "本包发行键，同 PotPlayer；mpv 原默认 i 仍可用", ship: true),
                D("e2.stats-toggle", "播放统计 · 常显切换（Ctrl+j）", "script-binding stats/display-stats-toggle", "Ctrl+j",
                  "本包发行键，同 MPC-HC / MPC-BE；mpv 原默认 I 仍可用", ship: true),
            }
        },
        new ShortcutGroupDef
        {
            Name = "E3 · 音轨 / 字幕 / 音量",
            Rows =
            {
                D("e3.audio", "切换音轨", "cycle audio", "SHARP"),
                D("e3.sub", "切换字幕轨", "cycle sub", "j"),
                D("e3.subvis", "字幕显示开关", "cycle sub-visibility", "v"),
                D("e3.subdelay-", "字幕延迟 −0.1s", "add sub-delay -0.1", "z"),
                D("e3.subdelay+", "字幕延迟 +0.1s", "add sub-delay 0.1", "x"),
                D("e3.vol-", "音量 −2", "add volume -2", "9"),
                D("e3.vol+", "音量 +2", "add volume 2", "0"),
                D("e3.mute", "静音", "cycle mute", "m"),
            }
        },
        new ShortcutGroupDef
        {
            Name = "E4 · 本包专属（10 行已有值）",
            Rows =
            {
                D("e4.tone-ab", "色调增强 A/B 对比", "script-message tone-ab", "F5", ship: true),
                D("e4.hdr-status", "查看 HDR 状态", "script-message hdr-status", "F6", ship: true),
                D("e4.hdr-toggle", "HDR 直通 / 色调映射", "script-message hdr-toggle", "F7", ship: true),
                D("e4.hdr-restore", "恢复系统 HDR", "script-message hdr-restore", "F8",
                  "原默认 F8 是「显示播放列表」，已被本包占用", ship: true),
                D("e4.bassmax", "BassMax 增强 / 旁通", "af toggle bassmax", "Ctrl+b", ship: true),
                // 1.0.7：四个倍率键统一走 mpv-scale-key 脚本消息，纯音频封面时由 fit90.lua 拦截。
                // 旧的裸 set window-scale 会绕过屏蔽，切勿改回。
                D("e4.win25", "窗口 25%（视频分辨率）", "script-message mpv-scale-key 0.25", "Alt+`", ship: true),
                D("e4.win50", "窗口 50%（视频分辨率）", "script-message mpv-scale-key 0.5", "Alt+1",
                  "原默认 Alt+1 是 100%，本包重排为 50%", ship: true),
                D("e4.win100", "窗口 100%（视频分辨率）", "script-message mpv-scale-key 1.0", "Alt+2",
                  "原默认 Alt+2 是 200%，本包重排为 100%；双击打开视频也是这个", ship: true),
                D("e4.win200", "窗口 200%（视频分辨率）", "script-message mpv-scale-key 2.0", "Alt+3", ship: true),
                D("e4.fit90", "窗口缩放到屏幕比例", "script-message mpv-fit90", "Alt+4",
                  "默认 90%（屏幕可用区域），比例在「播放」页更改", ship: true),
            }
        },
        new ShortcutGroupDef
        {
            Name = "E5 · 留空可自设",
            Rows =
            {
                N("e5.playlist", "显示播放列表", "show-text ${playlist}",
                  "原默认 F8 已被本包占用，可在此自行指定按键"),
                N("e5.stop", "停止播放（保留列表）", "stop keep-playlist", "mpv 没有默认绑定"),
                N("e5.seek+10", "快进 10 秒", "seek 10", "默认只有快进 5 / 60 秒"),
                N("e5.seek-10", "快退 10 秒", "seek -10", "默认只有快退 5 / 60 秒"),
            }
        },
    };
}

/// <summary>
/// input.conf 的键名与命令归一化。三条硬约束都落在这里：
/// 键名大小写敏感（s ≠ S）、`#` 键必须写成 SHARP、命令里的数值写法差异（1.0 vs 1）先归一化再比对。
/// </summary>
public static class ShortcutKeys
{
    /// <summary>命令归一化：首词小写，纯数字 token 统一成最短写法，空白折叠成一个空格。</summary>
    public static string NormCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return string.Empty;
        var tokens = command.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return string.Empty;

        var outTokens = new string[tokens.Length];
        outTokens[0] = tokens[0].ToLowerInvariant();
        for (int i = 1; i < tokens.Length; i++)
            outTokens[i] = NormalizeNumber(tokens[i]);
        return string.Join(" ", outTokens);
    }

    private static string NormalizeNumber(string token)
        => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d.ToString("0.######", CultureInfo.InvariantCulture)
            : token;

    /// <summary>键名规范形：修饰键顺序无关、别名统一，主键大小写保留（mpv 里 s 与 S 是两个键）。</summary>
    public static string Canonical(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return string.Empty;
        var parts = key.Trim().Split('+').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return string.Empty;

        string main = parts[^1];
        if (main == "#") main = "SHARP";            // mpv 里 # 键只能写成 SHARP
        var mods = parts.Take(parts.Count - 1).Select(NormModifier).OrderBy(m => m, StringComparer.Ordinal);
        return string.Join("+", mods.Append(main));
    }

    private static string NormModifier(string m) => m.ToLowerInvariant() switch
    {
        "ctrl" or "control" or "strg" => "Ctrl",
        "shift" => "Shift",
        "alt" => "Alt",
        "meta" or "super" or "win" => "Meta",
        _ => m,
    };

    /// <summary>
    /// 界面显示：把 mpv 键名翻译成一眼就知道怎么按的写法。
    /// 只用于展示（键位框 / 冲突提示 / 写盘明细），绝不参与写盘或比较——
    /// 写盘用 ToFile、比较用 Canonical，两者始终收 mpv 原生键名。
    /// 规则：大写单字母 A–Z = Shift+小写字母（mpv 里 s 与 S 是两个键，键帽印小写 s）；
    /// 需要 Shift 上档的符号键显示成「符号 (Shift+按键)」——SHARP→# (Shift+3)、
    /// &lt;→&lt; (Shift+,)、&gt;→&gt; (Shift+.)；组合键按 + 拆开逐段翻译，修饰键原样保留。
    /// </summary>
    public static string Display(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        var parts = key.Split('+');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = DisplayToken(parts[i].Trim());
        return string.Join("+", parts);
    }

    private static string DisplayToken(string t) => t switch
    {
        "SPACE" => "空格",
        "ESC" => "Esc",
        "ENTER" => "Enter",
        "TAB" => "Tab",
        "BS" => "退格",
        "DEL" => "Delete",
        "INS" => "Insert",
        "HOME" => "Home",
        "END" => "End",
        "PGUP" => "PgUp",
        "PGDWN" => "PgDn",
        "LEFT" => "←",
        "RIGHT" => "→",
        "UP" => "↑",
        "DOWN" => "↓",
        "SHARP" => "# (Shift+3)",  // 保留符号 + 括号标按法：# 的实际按法是 Shift+3
        "<" => "< (Shift+,)",      // < 是 Shift+逗号（美式键盘同键位上档）
        ">" => "> (Shift+.)",      // > 是 Shift+句号
        "KP_MULT" => "小键盘*",
        "KP_ADD" => "小键盘+",
        "KP_SUB" => "小键盘-",
        "KP_DIV" => "小键盘/",
        "KP_DEC" => "小键盘.",
        _ when t.Length == 1 && t[0] >= 'A' && t[0] <= 'Z' => "Shift+" + t.ToLowerInvariant(),
        _ when t.Length == 2 && t[0] == 'K' && t[1] >= '0' && t[1] <= '9' => "小键盘" + t[1],
        _ => t,
    };

    /// <summary>写盘前把显示值转回文件里合法的键名。</summary>
    public static string ToFile(string key) => key switch
    {
        null => string.Empty,
        "#" => "SHARP",
        " " or "" => string.Empty,
        _ => key,
    };

    /// <summary>
    /// 解析 input.conf 的一行：`KEY command`（没有等号）。
    /// 注释 / [section] / 空行 / 键序列（a-b）/ 分号多命令一律返回 false —— 不认识的不猜，原样保留。
    /// </summary>
    public static bool TryParseLine(string line, out string key, out string command)
    {
        key = string.Empty;
        command = string.Empty;

        string t = (line ?? string.Empty).Trim();
        if (t.Length == 0 || t[0] is '#' or ';' or '[') return false;

        int sp = t.IndexOfAny(new[] { ' ', '\t' });
        if (sp <= 0) return false;

        string k = t.Substring(0, sp);
        string c = t.Substring(sp + 1).Trim();
        if (c.Length == 0 || c.Contains(';') || k.Contains('-')) return false;

        key = k;
        command = c;
        return true;
    }
}

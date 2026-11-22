using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MpvSettings.Services;

/// <summary>
/// menu.conf 里「打开本地文件」那一行的路径自检。
///
/// <para>那一行长这样（制表符分隔）：</para>
/// <code>打开本地文件	run "C:/…/Assoc.exe" --openfile</code>
/// <para>mpv 的 <c>run</c> 既不展开 <c>~~/</c>，也不认 <c>%VAR%</c>（实测
/// <c>run "~~/x.cmd"</c> 返回 success 但什么都没启动），所以它必须是绝对路径。
/// 而 menu.conf 是随包发的静态文件，里面的路径是打包机的——换安装目录、换机器、
/// 或者用户把 MyMPC 装到别的盘，这一行就指向一个不存在的 exe：右键点「打开本地文件」
/// 毫无反应，也没有任何提示。</para>
///
/// <para>所以每次启动（以及安装包跑 <c>--default</c> 时）按当前安装根目录重算一遍，
/// 不一致就改回来。路径已经正确时连文件都不碰，不制造无意义的写盘时间戳。</para>
///
/// <para>写回一律 UTF-8 <b>无 BOM</b>、沿用文件原有的换行风格：安装包那边
/// （Inno 的 <c>SaveStringsToUTF8File</c>）强制写 BOM，BOM 会让 mpv 的第一个菜单项
/// 标题变成 "\uFEFF打开本地文件"，所以这里顺手把 BOM 也清掉。</para>
/// </summary>
public static class MenuConfPath
{
    /// <summary>菜单项文字。选择文件框的标题是另一回事，别混。</summary>
    public const string Label = "打开本地文件";

    private const string Marker = "--openfile";
    private const string ExeName = "Assoc.exe";

    /// <summary>
    /// 检查并修正 <c>&lt;config&gt;\menu.conf</c> 的「打开本地文件」行。
    /// </summary>
    /// <returns>改过了就返回一句可读的说明；不需要改 / 改不了返回 <c>null</c>。</returns>
    public static string Fix(ConfigLocator loc)
    {
        if (loc is null || !loc.IsUsable) return null;

        string path = loc.MenuConf;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

        string assoc = ResolveAssoc(loc);
        if (assoc.Length == 0) return null;   // 没装 Assoc.exe 就别写一条指向空气的路径

        string raw;
        try
        {
            raw = File.ReadAllText(path, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            return null;
        }

        bool hasBom = raw.Length > 0 && raw[0] == '\uFEFF';
        if (hasBom) raw = raw.Substring(1);

        string nl = raw.Contains("\r\n") ? "\r\n" : "\n";
        var lines = new List<string>(raw.Replace("\r\n", "\n").Split('\n'));

        string want = "run \"" + assoc.Replace('\\', '/') + "\" " + Marker;
        string old = null;

        int at = lines.FindIndex(l => l.IndexOf(Marker, StringComparison.Ordinal) >= 0
                                      && l.IndexOf(ExeName, StringComparison.OrdinalIgnoreCase) >= 0);
        if (at >= 0)
        {
            string line = lines[at];
            int run = line.IndexOf("run ", StringComparison.Ordinal);
            if (run < 0) return null;
            string rebuilt = line.Substring(0, run) + want;
            if (!hasBom && string.Equals(rebuilt, line, StringComparison.Ordinal)) return null;
            old = line.Substring(run);
            lines[at] = rebuilt;
        }
        else
        {
            // 整行都没了（旧包 / 用户删过）：补到第一行，功能比位置重要。
            lines.Insert(0, Label + "\t" + want);
        }

        string output = string.Join(nl, lines);
        try
        {
            File.WriteAllText(path, output, new UTF8Encoding(false));
        }
        catch (Exception)
        {
            return null;
        }

        return old is null
            ? "右键菜单：补回「" + Label + "」→ " + assoc
            : "右键菜单：「" + Label + "」由 " + old + " 改为 " + want;
    }

    /// <summary>
    /// Assoc.exe 的位置：安装根目录是权威来源（与 setting.ahk、ConfigLocator 同一套），
    /// 只有它下面没有时才退回 <see cref="ConfigLocator.Root"/>（开发包 / --config-dir 验证）。
    /// </summary>
    private static string ResolveAssoc(ConfigLocator loc)
    {
        foreach (string root in new[] { loc.InstallRoot, loc.Root })
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string candidate = Path.Combine(root, ExeName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return string.Empty;
    }
}

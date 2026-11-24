using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using MpvSettings.Services;

namespace MpvSettings.Models;

/// <summary>
/// 一个 key=value 配置文件的只读快照。
/// 保留原始行、编码与换行风格：阶段 ③ 只写 diff 键、逐行替换时要靠这些原样信息。
/// 阶段 ② 只用它做反推，绝不写回。
/// </summary>
public sealed class ConfFile
{
    public string Path { get; private set; }
    public bool Exists { get; private set; }
    public bool HadBom { get; private set; }
    public bool Crlf { get; private set; }
    public string RawText { get; private set; } = string.Empty;
    public List<string> Lines { get; private set; } = new();

    // 同名键 mpv 取最后一个；GetAll 用于 include= 这类可重复出现的键
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _all = new(StringComparer.OrdinalIgnoreCase);

    private ConfFile(string path) => Path = path;

    public static ConfFile Load(string path)
    {
        var f = new ConfFile(path);
        if (!File.Exists(path)) return f;

        f.Exists = true;
        byte[] bytes = File.ReadAllBytes(path);
        f.HadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        f.RawText = Encoding.UTF8.GetString(bytes, f.HadBom ? 3 : 0, bytes.Length - (f.HadBom ? 3 : 0));
        f.Crlf = f.RawText.Contains("\r\n");
        f.Lines = f.RawText.Replace("\r\n", "\n").Split('\n').ToList();

        foreach (var line in f.Lines)
        {
            var t = line.Trim();
            if (t.Length == 0 || t.StartsWith("#") || t.StartsWith(";") || t.StartsWith("[")) continue;
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            string key = t.Substring(0, eq).Trim();
            string val = t.Substring(eq + 1).Trim();
            if (key.Length == 0) continue;
            f._values[key] = val;
            if (!f._all.TryGetValue(key, out var list)) f._all[key] = list = new List<string>();
            list.Add(val);
        }
        return f;
    }

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>键不存在时返回 fallback（由草稿给出本包出厂默认）。</summary>
    public string Get(string key, string fallback = "")
        => _values.TryGetValue(key, out var v) ? v : fallback;

    public IReadOnlyList<string> GetAll(string key)
        => _all.TryGetValue(key, out var list) ? list : Array.Empty<string>();

    public bool HasInclude(string file)
        => GetAll("include").Any(v => string.Equals(v, "~~/" + file, StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(v, file, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// HKCU\Software\BassMax 六键快照。缺任意一个托盘会整批忽略，所以齐全性本身要显示出来。
/// 值一律按字符串保存（DWORD 已转成十进制），另记原类型给阶段 ④ 写回时用。
/// </summary>
public sealed class BassMaxSnapshot
{
    public static string[] Keys => BassMaxStore.Keys;

    public string KeyPath { get; private set; } = BassMaxStore.KeyPath;
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, RegistryValueKind> Kinds { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Missing { get; } = new();
    public bool KeyExists { get; private set; }
    public bool Complete => KeyExists && Missing.Count == 0;

    public string StatusText => !KeyExists
        ? "注册表键不存在（显示出厂默认，确定时按六键补齐）"
        : Complete ? "6 / 6 齐全" : "缺项：" + string.Join("、", Missing) + "（确定时自动补齐）";

    public bool Has(string key) => Values.TryGetValue(key, out var v) && v.Length > 0;

    public string Get(string key, string fallback = "")
        => Values.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    public static BassMaxSnapshot Load()
    {
        var s = new BassMaxSnapshot();
        try
        {
            using var key = BassMaxStore.OpenRead();
            if (key is null) return s;
            s.KeyExists = true;
            foreach (string k in BassMaxStore.Keys)
            {
                string v = BassMaxStore.ReadRaw(key, k, out RegistryValueKind kind);
                if (string.IsNullOrEmpty(v)) s.Missing.Add(k);
                else { s.Values[k] = v; s.Kinds[k] = kind; }
            }
        }
        catch (Exception)
        {
            // 读不到就当缺项，UI 仍以出厂默认展示，不因权限问题崩窗
        }
        return s;
    }
}

/// <summary>打开窗口时对所有配置源做一次快照（3.3 交互契约第 1 条）。</summary>
public sealed class ConfigSnapshot
{
    public ConfigLocator Locator { get; }
    public ConfFile Mpv { get; }
    public ConfFile Hdr { get; }
    public ConfFile CoverArt { get; }
    public ConfFile AudioInfo { get; }
    public ConfFile MyMpcOpts { get; }
    public ConfFile Fit90 { get; }
    /// <summary>1.0.7：n*m 缩略图参数（script-opts/thumbnails.conf）。</summary>
    public ConfFile Thumbnails { get; }
    public ConfFile Input { get; }
    public BassMaxSnapshot BassMax { get; }

    /// <summary>scripts\MyMPC_ui.lua 的原文；null = 文件不存在。整文件比对用，不解析。</summary>
    public string LuaText { get; }
    public bool LuaExists => LuaText is not null;

    public DateTime LoadedAt { get; } = DateTime.Now;

    public ConfigSnapshot(ConfigLocator loc)
    {
        Locator = loc;
        Mpv = ConfFile.Load(loc.MpvConf);
        Hdr = ConfFile.Load(loc.HdrConf);
        CoverArt = ConfFile.Load(loc.CoverArtConf);
        AudioInfo = ConfFile.Load(loc.AudioInfoConf);
        MyMpcOpts = ConfFile.Load(loc.MyMpcOpts);
        Fit90 = ConfFile.Load(loc.Fit90Conf);
        Thumbnails = ConfFile.Load(loc.ThumbnailsConf);
        Input = ConfFile.Load(loc.InputConf);   // 只取 Lines，阶段 ⑥ 再按 KEY command 解析
        LuaText = File.Exists(loc.MyMpcLua)
            ? Encoding.UTF8.GetString(File.ReadAllBytes(loc.MyMpcLua)).TrimEnd('\r', '\n')
            : null;
        BassMax = BassMaxSnapshot.Load();
    }

    /// <summary>input.conf 是 KEY command 格式（无 = 号），这里只保留原样行。</summary>
    public IReadOnlyList<string> InputLines => Input.Lines;
}

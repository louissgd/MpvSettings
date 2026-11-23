using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using MpvSettings.Services;

namespace MpvSettings.Models;

/// <summary>一个键的写入请求。Group 非空表示它属于联动组，同组键必须一起写。</summary>
public sealed class KeyChange
{
    public string Key { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool Append { get; set; }

    /// <summary>
    /// 只允许追加、绝不替换同名键。只给 include= 这类可重复出现的键用：
    /// mpv.conf 里可能已经有用户自己的 `include=~~/其它.conf`，按普通键替换会把它们全冲掉。
    /// </summary>
    public bool AppendOnly { get; set; }

    /// <summary>
    /// 「写前重读到的值 → 最终值」的变换。只给 af 用：af 是滤镜链，
    /// 补 bassmax 时要基于磁盘上的最新链条合并，而不是整行覆盖掉别人的滤镜。
    /// 不参与 JSON 序列化（--dump-plan 里只留 To 这个按快照算出来的参考值）。
    /// </summary>
    [JsonIgnore]
    public Func<string, string> Transform { get; set; }

    /// <summary>
    /// 删除这一行，而不是改值（阶段 ⑦：关掉无封面信息窗时移除 include=~~/audio-info.conf）。
    /// 与 To 互斥。
    /// </summary>
    public bool Remove { get; set; }

    /// <summary>Remove 时参与匹配的值。include 这类可重复键不能只按键名删，否则会删掉别人的 include。</summary>
    public string MatchValue { get; set; } = string.Empty;

    public string DisplayFrom => From.Length == 0 ? "<缺失>" : From;

    /// <summary>空值行要显式标出来，否则明细会写成「xxx → 」像被截断。af 不再写空值行，但别的键可能用。</summary>
    public string DisplayTo => Remove ? "<移除该行>" : To.Length == 0 ? "<空值行>" : To;
}

/// <summary>注册表一项的写入请求。Fill = 原本缺这个键，属于「补齐」而不是用户改的。</summary>
public sealed class RegChange
{
    public string Key { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool Fill { get; set; }
    public string DisplayFrom => From.Length == 0 ? "<缺失>" : From;
}

/// <summary>HKCU\Software\BassMax 的写盘计划。</summary>
public sealed class RegistryPlan
{
    public string KeyPath { get; set; } = BassMaxStore.KeyPath;
    public List<RegChange> Changes { get; } = new();
    public bool HasChanges => Changes.Count > 0;
    public string DisplayPath => @"HKCU\" + KeyPath;
}

public sealed class FilePlan
{
    public string Path { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public List<KeyChange> Changes { get; } = new();

    /// <summary>
    /// 整文件写入的内容（含换行）。非 null 时忽略 Changes，直接把这串文本落盘。
    /// 只给 cover-art.conf 用：它是 EXE 独占的固定结构 profile 段，逐键替换没有意义，
    /// 而且首次启用时文件根本不存在，必须能新建。
    /// </summary>
    public string NewContent { get; set; }

    /// <summary>写入时该文件是否尚不存在（新建）。由 Build 依据快照填写，供明细文案使用。</summary>
    public bool IsCreate { get; set; }

    /// <summary>整文件重写时，结果明细里括号内那句说明（「封面条件 profile」/「脚本」…）。</summary>
    public string WholeFileKind { get; set; } = "条件 profile";

    /// <summary>
    /// input.conf 专用：按「动作命令串」定位行，只改/加/删这一行。
    /// 与 Changes（key=value）互斥，两者不会同时出现在同一个 FilePlan 上。
    /// </summary>
    public List<InputEdit> InputEdits { get; } = new();

    public bool IsWholeFile => NewContent is not null;
    public bool IsInputFile => InputEdits.Count > 0;
}

/// <summary>input.conf 的一处改键：按规范命令串匹配行，NewKey 为空表示删行（恢复默认）。</summary>
public sealed class InputEdit
{
    /// <summary>规范命令串（页 E 清单里的那个），写进文件时用 Action 对应的原始命令。</summary>
    public string Command { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;

    /// <summary>打开时那一行的键（空 = 原本没有这一行）。</summary>
    public string OldKey { get; set; } = string.Empty;

    /// <summary>要写成的键（空 = 删掉这一行）。</summary>
    public string NewKey { get; set; } = string.Empty;

    public bool IsDelete => string.IsNullOrEmpty(NewKey);

    public string DisplayOld => OldKey.Length == 0 ? "<无此行>" : ShortcutKeys.Display(OldKey);
    public string DisplayNew => NewKey.Length == 0 ? "<删行 · 恢复默认>" : ShortcutKeys.Display(NewKey);
}

/// <summary>
/// 写盘计划：由「打开时的草稿」与「当前草稿」做 diff 得到。
/// 阶段 ③ 只有 key=value 文件，阶段 ④ 增加 HKCU\Software\BassMax 六键。
/// </summary>
public sealed class WritePlan
{
    public List<FilePlan> Files { get; } = new();
    public RegistryPlan Registry { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Blockers { get; } = new();
    public List<string> Deferred { get; } = new();

    /// <summary>配置文件里要写的键数（不含注册表、不含整文件重写、不含 input.conf 改键）。</summary>
    public int KeyCount => Files.Sum(f => f.Changes.Count);
    public int FileCount => Files.Count(f => f.Changes.Count > 0 || f.IsWholeFile || f.IsInputFile);

    /// <summary>整文件重写的文件数（目前只有 cover-art.conf）。</summary>
    public int WholeFileCount => Files.Count(f => f.IsWholeFile);
    public int InputEditCount => Files.Sum(f => f.InputEdits.Count);
    public int RegistryCount => Registry.Changes.Count;
    public bool HasChanges => KeyCount > 0 || WholeFileCount > 0 || InputEditCount > 0 || RegistryCount > 0;
    public bool CanApply => Blockers.Count == 0;

    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (WholeFileCount > 0) parts.Add($"{WholeFileCount} 个文件整文件重写");
            if (KeyCount > 0) parts.Add($"{Files.Count(f => f.Changes.Count > 0)} 个文件 / {KeyCount} 个键");
            if (InputEditCount > 0) parts.Add($"input.conf {InputEditCount} 处改键");
            if (RegistryCount > 0) parts.Add($"注册表 {RegistryCount} 项");
            return parts.Count == 0 ? "没有需要写入的改动" : string.Join(" · ", parts);
        }
    }

    public FilePlan For(string label, string path)
    {
        var f = Files.FirstOrDefault(x => x.Label == label);
        if (f is null) { f = new FilePlan { Label = label, Path = path }; Files.Add(f); }
        return f;
    }
}

public sealed class WriteResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = string.Empty;
    public bool RolledBack { get; set; }
    public int FilesWritten { get; set; }
    public int KeysWritten { get; set; }
    /// <summary>真正落到注册表的项数（值本来就一致的不计）。</summary>
    public int RegistryWritten { get; set; }
    public List<string> Details { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Deferred { get; } = new();
    public List<string> Blockers { get; } = new();
    public string Summary { get; set; } = string.Empty;

    public static WriteResult Blocked(WritePlan plan)
    {
        var r = new WriteResult { Success = false, Error = "校验未通过，未写入任何文件", Summary = "已阻止写入" };
        r.Blockers.AddRange(plan.Blockers);
        r.Warnings.AddRange(plan.Warnings);
        r.Deferred.AddRange(plan.Deferred);
        return r;
    }
}

/// <summary>
/// 草稿的「打开时」副本。点确定时只比较这些可编辑属性，
/// 没改过的键一律不进写盘计划 —— 这是「只写 diff 键、不覆盖别人刚写的键」的关键。
/// 用字符串保存，避免 double 的 1.0 / 1 之类表示差异造成误判。
/// </summary>
public sealed class DraftBaseline
{
    /// <summary>
    /// 全量受管属性表。恢复默认（Force / ForceAll）与导入导出共用这一份，
    /// 不要另抄一份键表——6.15 已经用这个理由干掉过四个 *_default.conf 模板。
    /// ⑩b 起公开给 BackupService 序列化 manifest.profile。
    /// </summary>
    public static readonly string[] ManagedProps =
    {
        // 页 A
        "HwdecIndex", "HwdecCustom", "Deinterlace", "LoopFile", "SavePosition",
        "SubsMatchOsLanguage", "SubsFallbackForced", "SubAutoReload", "OsdProgress", "CoverOriginal", "CoverMax",
        "NoArtUi", "FitRatio", "AllowMultiInstance",
        // 页 A 弹窗「字幕样式」（只写 SRT 等纯文本；sub-ass-override=no 保护 ASS/SUP）
        "SubFont", "SubColor", "SubFontSize", "SubLineSpacing", "SubBold", "SubItalic",
        "SubOutlineSize", "SubShadowOffset", "SubMarginX", "SubMarginY", "SubProtectAss",
        // 页 B（KeepWindowAspect 是 ⑨b 新增的，漏登记会让每次保存都把它当成「改过」）
        "HdrModeIndex", "HdrModeCustom", "ToneIndex", "ToneCustom",
        "VideoLevelsIndex", "VideoLevelsCustom", "OnlyFullscreen", "KeepWindowAspect",
        // 页 B（1.0.2 RTX 增强：漏登记会让每次保存都把它当成「改过」，整文件重写 MyMPC.conf）
        "RtxVsr", "RtxHdr", "RtxVsrLimitIndex", "RtxHdrPeakIndex",
        // 页 C（VolumeCap100 / ExternalAudioAuto 是新增的，漏登记会让每次保存都当成「改过」）
        "AfEnabled", "BassMaxEnabled", "VolumeCap100", "ExternalAudioAuto", "Bass", "Treble", "Drive", "PresetIndex",
        "AudioChannelsIndex", "AudioChannelsCustom",
        // 页 D
        "ShotFormatIndex", "ShotFormatCustom", "ShotDirIndex", "ShotDirPath", "HighBitDepth",
    };

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>
    /// 页 E 的「打开时状态」，按动作 Id 记 "present|key"。
    /// 快捷键不是单个属性，没法走反射，单独存一份。
    /// </summary>
    public Dictionary<string, string> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public static DraftBaseline Capture(SettingsDraft draft)
    {
        var b = new DraftBaseline();
        foreach (var name in ManagedProps)
        {
            var p = typeof(SettingsDraft).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p is null || !p.CanRead) continue;
            b._values[name] = Str(p.GetValue(draft));
        }
        if (draft.Shortcuts is not null)
            foreach (var row in draft.Shortcuts.Rows) b.Shortcuts[row.Id] = row.Snapshot();
        return b;
    }

    /// <summary>
    /// 1.0.5：字幕样式弹窗「完成」直接写盘成功后，只把字幕样式那组属性的 baseline 推进到草稿现值。
    /// 不碰其他 ManagedProps —— 主界面其他页的未保存改动必须保留 diff，等用户点「应用/确定」再写。
    /// 不推进会让主界面结果窗口把字幕键再列一遍（写盘幂等不会真重复写，但明细是假的）。
    /// </summary>
    public void AdvanceSubtitleStyle(SettingsDraft draft)
    {
        string[] names =
        {
            "SubFont", "SubColor", "SubFontSize", "SubLineSpacing", "SubBold", "SubItalic",
            "SubOutlineSize", "SubShadowOffset", "SubMarginX", "SubMarginY", "SubProtectAss",
        };
        foreach (var name in names)
        {
            var p = typeof(SettingsDraft).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p is null || !p.CanRead) continue;
            _values[name] = Str(p.GetValue(draft));
        }
    }

    /// <summary>打开时的 (Present, Key)。没记录过就当作「原本没有这一行」。</summary>
    public (bool Present, string Key) Shortcut(string id)
    {
        if (!Shortcuts.TryGetValue(id, out var v)) return (false, string.Empty);
        int bar = v.IndexOf('|');
        if (bar < 0) return (false, string.Empty);
        return (v[0] == '1', v.Substring(bar + 1));
    }

    public string Get(string name) => _values.TryGetValue(name, out var v) ? v : string.Empty;

    /// <summary>
    /// true = 「假装每个控件都被改过」（`--default` 恢复默认用）。
    /// 这样恢复默认可以走同一条写盘链路，不必再抄一份键表——抄一份迟早和 Build() 走岔。
    /// </summary>
    public bool ForceAll { get; private set; }

    /// <summary>
    /// 恢复默认用的基准：属性一律「改过」，但**快捷键仍取盘上现状**。
    /// 页 E 的增删是按 baseline.Shortcut(id) 算 diff 的，不经过 Changed()；
    /// 把这里清空会让「用户自定义过、但本包不发行该行」的键删不掉。
    /// </summary>
    public static DraftBaseline Force(SettingsDraft diskDraft)
    {
        var b = Capture(diskDraft ?? new SettingsDraft());
        b.ForceAll = true;
        return b;
    }

    public bool Changed(SettingsDraft draft, params string[] names)
    {
        if (ForceAll) return true;
        foreach (var name in names)
        {
            var p = typeof(SettingsDraft).GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p is null) continue;
            if (!string.Equals(Str(p.GetValue(draft)), Get(name), StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>该属性在打开时是否为「自定义 / 不覆盖」态。重置按钮会清掉这个标志，必须在写盘前再拦一次。</summary>
    public bool WasCustom(string name)
        => !ForceAll && string.Equals(Get(name), "True", StringComparison.OrdinalIgnoreCase);

    private static string Str(object v) => v switch
    {
        null => string.Empty,
        bool b => b ? "True" : "False",
        double d => d.ToString("0.############", CultureInfo.InvariantCulture),
        float f => f.ToString("0.############", CultureInfo.InvariantCulture),
        IFormattable fmt => fmt.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };
}

/// <summary>CLI 验证用：按 JSON 里的属性名覆盖草稿，模拟「用户在界面上改了这些控件」。</summary>
public static class DraftPatch
{
    public static List<string> Apply(SettingsDraft draft, System.Text.Json.JsonElement root)
    {
        var applied = new List<string>();
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return applied;

        foreach (var prop in root.EnumerateObject())
        {
            // 页 E 是集合草稿，走专门入口：{"Shortcuts": {"e1.pause": "Ctrl+p", "e4.hdr-restore": ""}}
            if (string.Equals(prop.Name, "Shortcuts", StringComparison.OrdinalIgnoreCase)
                && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var pairs = new List<KeyValuePair<string, string>>();
                foreach (var kv in prop.Value.EnumerateObject())
                    pairs.Add(new KeyValuePair<string, string>(
                        kv.Name,
                        kv.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? kv.Value.GetString() ?? string.Empty
                            : kv.Value.ToString()));
                applied.AddRange(draft.Shortcuts.ApplyPatch(pairs));
                continue;
            }

            var p = typeof(SettingsDraft).GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance);
            if (p is null || !p.CanWrite) continue;
            try
            {
                object val = Read(prop.Value, p.PropertyType);
                if (val is null) continue;
                p.SetValue(draft, val);
                applied.Add(prop.Name);
            }
            catch (Exception)
            {
                // 验证脚本传错了类型就跳过这一项，结果里会体现出来
            }
        }
        return applied;
    }

    private static object Read(System.Text.Json.JsonElement e, Type t)
    {
        if (t == typeof(bool)) return e.ValueKind == System.Text.Json.JsonValueKind.True
            || (e.ValueKind == System.Text.Json.JsonValueKind.Number && e.GetDouble() != 0)
            || (e.ValueKind == System.Text.Json.JsonValueKind.String && bool.TryParse(e.GetString(), out var b) && b);
        if (t == typeof(int)) return e.ValueKind == System.Text.Json.JsonValueKind.Number
            ? e.GetInt32() : int.TryParse(e.GetString(), out var i) ? i : (object)null;
        if (t == typeof(double)) return e.ValueKind == System.Text.Json.JsonValueKind.Number
            ? e.GetDouble() : double.TryParse(e.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (object)null;
        if (t == typeof(string)) return e.ValueKind == System.Text.Json.JsonValueKind.String
            ? e.GetString() : e.ToString();
        return null;
    }
}

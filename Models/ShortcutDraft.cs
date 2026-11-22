using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MpvSettings.Models;

/// <summary>
/// 页 E 的一行（草稿）。
/// 三态语义（整页最容易做错的地方）：
///   · 默认   —— input.conf 里没有这一行，mpv 用编译进二进制的弱绑定；
///   · 自定义 —— input.conf 里有这一行（强绑定，priority 15），改键才写；
///   · 未绑定 —— 既没有行、也没有默认键。
/// 「默认」态改键 = 写一行；「自定义」态清空 = 删掉那一行（语义是恢复默认，不是禁用）。
/// </summary>
public sealed class ShortcutRow : ObservableObject
{
    public ShortcutAction Def { get; }

    private bool _present;
    private string _key = string.Empty;
    private bool _capturing;
    private string _conflictText = string.Empty;
    private string _hintText = string.Empty;

    public ShortcutRow(ShortcutAction def)
    {
        Def = def;
        _key = def.DefaultKey;
    }

    public string Id => Def.Id;
    public string Action => Def.Action;
    public string Command => Def.Command;
    public string Note => Def.Note;
    public bool HasNote => Def.Note.Length > 0;
    public bool HasDefault => Def.HasDefault;
    public string DefaultKey => Def.DefaultKey;

    /// <summary>input.conf 里是否应该有这一行。</summary>
    public bool Present
    {
        get => _present;
        private set
        {
            if (!Set(ref _present, value)) return;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(KeyText));
            OnPropertyChanged(nameof(CanClear));
        }
    }

    /// <summary>Present 时要写进文件的键；不是 Present 时等于默认键（仅供显示）。</summary>
    public string Key
    {
        get => _key;
        private set
        {
            if (!Set(ref _key, value)) return;
            OnPropertyChanged(nameof(KeyText));
            OnPropertyChanged(nameof(State));
        }
    }

    /// <summary>正在等待按键录入。</summary>
    public bool IsCapturing
    {
        get => _capturing;
        set
        {
            if (!Set(ref _capturing, value)) return;
            OnPropertyChanged(nameof(KeyText));
        }
    }

    public string State => Present ? "自定义" : HasDefault ? "默认" : "未绑定";

    public string KeyText => IsCapturing ? "按下组合键… Esc 取消"
        : Present ? ShortcutKeys.Display(Key)
        : HasDefault ? ShortcutKeys.Display(DefaultKey)
        : "未绑定 · 点击设置";

    public bool CanClear => Present;

    /// <summary>同一键被两个动作强绑定（阻止写入）。</summary>
    public string ConflictText
    {
        get => _conflictText;
        set { if (Set(ref _conflictText, value)) OnPropertyChanged(nameof(HasConflict)); }
    }

    public bool HasConflict => _conflictText.Length > 0;

    /// <summary>新键覆盖了别的动作的默认键（只警告，允许保存）。</summary>
    public string HintText
    {
        get => _hintText;
        set { if (Set(ref _hintText, value)) OnPropertyChanged(nameof(HasHint)); }
    }

    public bool HasHint => _hintText.Length > 0;

    /// <summary>实际生效的键（弱绑定也算），用于冲突检测。</summary>
    public string EffectiveKey => Present ? Key : HasDefault ? DefaultKey : string.Empty;

    public void Load(bool present, string key)
    {
        Key = key;
        Present = present;
    }

    /// <summary>捕获提交。空串 = 清除。</summary>
    public void SetKey(string displayKey)
    {
        string k = (displayKey ?? string.Empty).Trim();
        if (k.Length == 0) { Clear(); return; }

        if (!Present && HasDefault &&
            string.Equals(ShortcutKeys.Canonical(k), ShortcutKeys.Canonical(DefaultKey), StringComparison.Ordinal))
        {
            Key = k;          // 改回默认键 = 不写这一行
            Present = false;
            return;
        }

        Key = k;
        Present = true;
    }

    /// <summary>清空：自定义 → 删行（恢复默认）；默认 / 未绑定 → 无操作。</summary>
    public void Clear()
    {
        if (!Present) return;
        Present = false;
        Key = HasDefault ? DefaultKey : string.Empty;
    }

    /// <summary>恢复本包发行时的状态（E4 的 10 行仍然在，其余不写行）。</summary>
    public void ResetToShipped()
    {
        Present = Def.ShipLine;
        Key = HasDefault ? DefaultKey : string.Empty;
    }

    public string Snapshot() => (Present ? "1" : "0") + "|" + Key;
}

public sealed class ShortcutGroupVm
{
    public string Name { get; init; } = string.Empty;
    public ObservableCollection<ShortcutRow> Rows { get; } = new();
}

/// <summary>
/// input.conf 里「不在 38 行清单内、但能明确解析」的绑定。
/// 只读展示：让用户知道这些行存在且不会被程序改写，不给编辑入口（写盘契约第 9 条要求「绝不擅自改写或删除」，
/// 而「看得见」是让这句话可信的前提）。
/// </summary>
public sealed class OtherBinding
{
    public string KeyText { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
}

/// <summary>
/// 快捷键草稿（阶段 ⑥）。打开时读 input.conf 反推出三态，编辑只改内存。
/// 写盘由 WritePlanBuilder 与本类的 Present/Key 做 diff 得到。
/// </summary>
public sealed class ShortcutDraft : ObservableObject
{
    public List<ShortcutGroupVm> Groups { get; } = new();

    private readonly Dictionary<string, ShortcutRow> _byId = new(StringComparer.OrdinalIgnoreCase);

    public ShortcutDraft()
    {
        foreach (var def in ShortcutCatalog.Groups)
        {
            var g = new ShortcutGroupVm { Name = def.Name };
            foreach (var a in def.Rows)
            {
                var row = new ShortcutRow(a);
                g.Rows.Add(row);
                _byId[a.Id] = row;
            }
            Groups.Add(g);
        }
    }

    public IEnumerable<ShortcutRow> Rows => Groups.SelectMany(g => g.Rows);

    public ShortcutRow Find(string id) => _byId.TryGetValue(id, out var r) ? r : null;

    /// <summary>input.conf 里强绑定的行数（状态栏/统计用）。</summary>
    public int CustomCount => Rows.Count(r => r.Present);

    public void LoadFrom(ConfigSnapshot s)
    {
        var map = ParseInput(s.Input.Lines);
        foreach (var row in Rows)
        {
            if (map.TryGetValue(row.Def.NormCommand, out string key))
                row.Load(true, key);
            else
                row.Load(false, row.HasDefault ? row.DefaultKey : string.Empty);
        }
        LoadOthers(s.Input.Lines);
        RefreshChecks();
    }

    /// <summary>
    /// 收集「别人的绑定」：能解析成 KEY command、但命令不在 38 行清单里的行。
    /// 注释、[section]、键序列、分号多命令连收集都不做——那些连解析都过不了，本来就会逐字保留。
    /// </summary>
    private void LoadOthers(IReadOnlyList<string> lines)
    {
        Others.Clear();
        var known = new HashSet<string>(Rows.Select(r => r.Def.NormCommand), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in lines)
        {
            if (!ShortcutKeys.TryParseLine(raw, out string key, out string cmd)) continue;
            string norm = ShortcutKeys.NormCommand(cmd);
            if (known.Contains(norm) || !seen.Add(norm)) continue;
            Others.Add(new OtherBinding { KeyText = ShortcutKeys.Display(key), Command = cmd });
        }
        OnPropertyChanged(nameof(HasOthers));
        OnPropertyChanged(nameof(OthersTitle));
    }

    /// <summary>input.conf 里其余自定义绑定（只读展示，永不参与写盘）。</summary>
    public ObservableCollection<OtherBinding> Others { get; } = new();

    public bool HasOthers => Others.Count > 0;

    public string OthersTitle => $"其他绑定（只读 · {Others.Count} 行）";

    /// <summary>重置本页 = 回到本包发行时的快捷键矩阵。</summary>
    public void Reset()
    {
        foreach (var row in Rows) row.ResetToShipped();
        RefreshChecks();
    }

    /// <summary>验证用：{"id": "键"}，空串表示清除。未知 id 直接忽略。</summary>
    public List<string> ApplyPatch(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var applied = new List<string>();
        foreach (var kv in pairs)
        {
            var row = Find(kv.Key);
            if (row is null) continue;
            if (string.IsNullOrEmpty(kv.Value)) row.Clear();
            else row.SetKey(kv.Value);
            applied.Add(kv.Key);
        }
        RefreshChecks();
        return applied;
    }

    /// <summary>同一键被两个动作强绑定（真冲突）。</summary>
    public sealed class Conflict
    {
        public ShortcutRow A { get; init; }
        public ShortcutRow B { get; init; }
        public string Key { get; init; } = string.Empty;
        public string Text => $"「{A.Action}」与「{B.Action}」都绑定了 {ShortcutKeys.Display(Key)}";
    }

    /// <summary>新键覆盖了别的动作的默认键（只警告）。</summary>
    public sealed class Cover
    {
        public ShortcutRow Row { get; init; }
        public ShortcutRow Target { get; init; }
    }

    public List<Conflict> ConflictList { get; } = new();
    public List<Cover> CoverList { get; } = new();

    /// <summary>
    /// 冲突校验：
    ///   强 × 强 同键 → 真冲突（阻断写入）；
    ///   强 覆盖 弱的默认键 → 只警告（这正是 mpv 强绑定覆盖弱绑定的预期行为）。
    /// </summary>
    public void RefreshChecks()
    {
        foreach (var r in Rows) { r.ConflictText = string.Empty; r.HintText = string.Empty; }
        ConflictList.Clear();
        CoverList.Clear();

        var strong = new Dictionary<string, ShortcutRow>(StringComparer.Ordinal);
        var weak = new Dictionary<string, ShortcutRow>(StringComparer.Ordinal);

        foreach (var r in Rows)
        {
            if (r.Present && r.Key.Length > 0)
            {
                string c = ShortcutKeys.Canonical(r.Key);
                if (strong.TryGetValue(c, out var other))
                {
                    string msg = $"与「{other.Action}」同键，无法保存";
                    if (r.ConflictText.Length == 0) r.ConflictText = msg;
                    if (other.ConflictText.Length == 0) other.ConflictText = msg;
                    ConflictList.Add(new Conflict { A = other, B = r, Key = c });
                }
                else strong[c] = r;
            }
            else if (!r.Present && r.HasDefault)
            {
                string c = ShortcutKeys.Canonical(r.DefaultKey);
                if (!weak.ContainsKey(c)) weak[c] = r;
            }
        }

        foreach (var kv in strong)
        {
            if (!weak.TryGetValue(kv.Key, out var w) || ReferenceEquals(w, kv.Value)) continue;
            kv.Value.HintText = $"会覆盖「{w.Action}」的默认键 {ShortcutKeys.Display(w.DefaultKey)}";
            CoverList.Add(new Cover { Row = kv.Value, Target = w });
        }
    }

    public List<string> Conflicts => ConflictList.Select(c => c.Text).Distinct().ToList();
    public List<string> Hints => CoverList.Select(c => $"{c.Row.Action} 覆盖「{c.Target.Action}」").ToList();

    /// <summary>
    /// 解析 input.conf 的强绑定：只认「单键 + 单命令」的行。
    /// 键序列（a-b）、分号多命令、注释与 [section] 一律跳过 —— 不认识的不猜，交给「其余行原样保留」。
    /// </summary>
    public static Dictionary<string, string> ParseInput(IReadOnlyList<string> lines)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string raw in lines)
        {
            if (!ShortcutKeys.TryParseLine(raw, out string key, out string cmd)) continue;
            string norm = ShortcutKeys.NormCommand(cmd);
            if (!map.ContainsKey(norm)) map[norm] = key;
        }
        return map;
    }
}

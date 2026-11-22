using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;
using MpvSettings.Models;

namespace MpvSettings.Services;

/// <summary>
/// 配置备份 / 恢复（阶段 ⑩b）。2026-11-15 用户拍板：
///   1. 固定文件夹覆盖式导出，**只保留一份**，不留时间戳历史；
///   2. 第三方 / 自定义脚本默认不带，<c>--include-extra</c> 才收；
///   3. BassMax 注册表六键一起导出，换机时用 <c>--keep-registry</c> 跳过。
/// 默认目录是「桌面\MyMPC-MPV-备份」。
///
/// 导入绝不整目录覆盖：A 类共享文件（mpv.conf / input.conf / menu.conf）只按 diff 写受管键，
/// B 类独占文件由 WritePlanBuilder 按目标草稿重新生成，定版资产由 DefaultProfile 补齐。
/// 键表只有 DraftBaseline.ManagedProps / WritePlanBuilder 这一份，与 UI、--default 同源。
/// </summary>
public static class BackupService
{
    public const int FormatVersion = 1;
    public const string ToolName = "MpvSettings";
    public const string FolderName = "MyMPC-MPV-备份";
    public const string SnapshotFolder = "_before_import";

    private const string PrefKey = @"Software\MyMPC\MpvSettings";
    private const string ManifestFile = "manifest.json";

    /// <summary>11 个受管文件（相对 portable_config）。生产目录里其余文件一律不碰。</summary>
    public static readonly string[] ManagedFiles =
    {
        // A 类：共享文件，只按 diff 写受管键
        "mpv.conf", "input.conf", "menu.conf",
        // B 类：EXE 独占生成（备份里的原文只用于审计 / 人工比对）
        "cover-art.conf", "audio-info.conf",
        "script-opts\\hdr-autoswitch.conf", "script-opts\\MyMPC.conf",
        "script-opts\\fit90.conf", "script-opts\\context_menu.conf",
        "scripts\\MyMPC_ui.lua", "scripts\\mpv-settings.lua",
    };

    /// <summary>本机路径类设置，不进备份：换机导过去会把截图目录串到别人的桌面。</summary>
    private static readonly string[] LocalOnly = { "ShotDirIndex", "ShotDirPath" };

    private static readonly string[] ExtraExtensions = { ".lua", ".conf", ".txt", ".json", ".md", ".cfg" };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ==================== 公开模型 ====================

    public sealed class Options
    {
        public string Dir { get; set; } = string.Empty;
        public ConfigLocator Locator { get; set; }
        public bool IncludeExtra { get; set; }
        public bool DryRun { get; set; }
        public bool KeepRegistry { get; set; }
        public bool Snapshot { get; set; } = true;
    }

    public sealed class Result
    {
        public bool Success { get; set; }
        public int Code { get; set; }               // 0 成功 / 1 写盘失败 / 2 清单或配置无效
        public string Message { get; set; } = string.Empty;
        public List<string> Details { get; } = new();
        public WritePlan Plan { get; set; }
        public WriteResult Write { get; set; }
    }

    /// <summary>manifest.json：格式版本 / 来源 / 受管键值 / 注册表六键 / 每个文件的 SHA256。</summary>
    public sealed class Manifest
    {
        public int Format { get; set; } = FormatVersion;
        public string Tool { get; set; } = ToolName;
        public string ExeVersion { get; set; } = string.Empty;
        public string ExportedAt { get; set; } = string.Empty;
        public string ConfigDir { get; set; } = string.Empty;
        public string ConfigSource { get; set; } = string.Empty;
        public bool IncludeExtra { get; set; }
        public Dictionary<string, string> Profile { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Shortcuts { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Registry { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string AfChain { get; set; } = string.Empty;
        /// <summary>相对备份目录根的路径 → SHA256。A/B 类在 portable_config\ 下，C 类在 extra\ 下。</summary>
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // ==================== 备份目录 ====================

    public static string DefaultDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), FolderName);

    /// <summary>记住用户选过的文件夹（HKCU，AHK 以后要接也能直接读）。读不到就回桌面默认目录。</summary>
    public static string RememberedDir()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PrefKey);
            string v = key?.GetValue("BackupDir") as string;
            return string.IsNullOrWhiteSpace(v) ? DefaultDir : v.Trim();
        }
        catch (Exception) { return DefaultDir; }
    }

    public static void RememberDir(string dir)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PrefKey, true);
            key?.SetValue("BackupDir", NormalizeDir(dir), RegistryValueKind.String);
        }
        catch (Exception) { /* 记不住不影响本次导出 */ }
    }

    public static string NormalizeDir(string dir)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(dir) ? RememberedDir() : dir.Trim().Trim('"'));

    private static string VersionText()
    {
        var asm = typeof(BackupService).Assembly;
        return asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString() ?? string.Empty;
    }

    // ==================== 导出 ====================

    public static Result Export(Options o)
    {
        var usable = CheckConfig(o);
        if (usable != null) return usable;

        string dir;
        try { dir = NormalizeDir(o.Dir); Directory.CreateDirectory(dir); }
        catch (Exception ex) { return Fail(1, "无法创建备份目录：" + ex.Message); }

        // 只保留一份：覆盖本次会写的三个位置，不建时间戳子目录。
        // 目录里若已有不属于本工具的东西，直接拒绝，避免把用户文件当旧备份清掉。
        string guard = GuardDir(dir);
        if (guard.Length > 0) return Fail(2, guard);
        try { Clean(dir); }
        catch (Exception ex) { return Fail(1, "清理旧备份失败：" + ex.Message); }

        try
        {
            var s = new ConfigSnapshot(o.Locator);
            var disk = new SettingsDraft();
            disk.LoadFrom(s);
            var baseline = DraftBaseline.Capture(disk);   // 与 UI / --default 同一张受管属性表

            var man = new Manifest
            {
                ExeVersion = VersionText(),
                ExportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                ConfigDir = o.Locator.ConfigDir,
                ConfigSource = o.Locator.Source,
                IncludeExtra = o.IncludeExtra,
                AfChain = s.Mpv.Get("af", string.Empty),
            };

            foreach (string name in DraftBaseline.ManagedProps)
                if (!LocalOnly.Contains(name)) man.Profile[name] = baseline.Get(name);
            foreach (var row in disk.Shortcuts.Rows)
                man.Shortcuts[row.Id] = row.Present ? row.Key : string.Empty;
            foreach (string key in BassMaxStore.Keys)
                man.Registry[key] = s.BassMax.Get(key, BassMaxStore.DefaultOf(key));

            foreach (string rel in ManagedFiles)
            {
                string src = Path.Combine(o.Locator.ConfigDir, rel);
                if (!File.Exists(src)) continue;
                byte[] bytes = File.ReadAllBytes(src);
                string outRel = Path.Combine("portable_config", rel);
                WriteMirror(dir, outRel, bytes);
                man.Files[outRel] = Sha(bytes);
            }

            int extra = 0;
            if (o.IncludeExtra)
                foreach (string src in ExtraFiles(o.Locator))
                {
                    byte[] bytes = File.ReadAllBytes(src);
                    string outRel = Path.Combine("extra", "scripts", Path.GetFileName(src));
                    WriteMirror(dir, outRel, bytes);
                    man.Files[outRel] = Sha(bytes);
                    extra++;
                }

            File.WriteAllText(Path.Combine(dir, ManifestFile),
                JsonSerializer.Serialize(man, JsonOpts), new UTF8Encoding(false));

            var r = Ok($"已导出到 {dir}（固定覆盖，只保留这一份）");
            r.Details.Add($"受管文件 {man.Files.Count - extra} 个" + (extra > 0 ? $" · 第三方脚本 {extra} 个" : " · 第三方脚本默认不带"));
            r.Details.Add($"manifest.json：{man.Profile.Count} 个受管设置项 / {man.Shortcuts.Count} 行快捷键 / 注册表 6 键 / 每文件 SHA256");
            r.Details.Add("af 链：" + (man.AfChain.Length > 0 ? man.AfChain : "<缺失，导入时会补 bassmax>"));
            r.Details.Add("来源：" + man.ConfigDir);
            return r;
        }
        catch (Exception ex) { return Fail(1, "导出失败：" + ex.Message); }
    }

    // ==================== 导入 ====================

    public static Result Import(Options o)
    {
        var usable = CheckConfig(o);
        if (usable != null) return usable;

        string dir;
        try { dir = NormalizeDir(o.Dir); }
        catch (Exception ex) { return Fail(2, "备份目录路径无效：" + ex.Message); }

        string mf = Path.Combine(dir, ManifestFile);
        if (!File.Exists(mf)) return Fail(2, "找不到 manifest.json，这不是一个有效的备份目录：" + dir);

        Manifest man;
        try { man = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(mf), JsonOpts); }
        catch (Exception ex) { return Fail(2, "manifest.json 解析失败：" + ex.Message); }

        if (man is null || man.Format != FormatVersion || !string.Equals(man.Tool, ToolName, StringComparison.Ordinal))
            return Fail(2, $"清单格式不认（format={man?.Format} tool={man?.Tool}），拒绝导入");

        // 任何一个文件缺失或哈希对不上都拒导：避免半截拷贝 / 手工改过 manifest 的目录混进来
        foreach (var kv in man.Files)
        {
            string abs = SafeJoin(dir, kv.Key, out string why);
            if (why.Length > 0) return Fail(2, "清单里有非法路径：" + kv.Key + "（" + why + "）");
            if (!File.Exists(abs)) return Fail(2, "备份缺文件：" + kv.Key);
            string actual = Sha(File.ReadAllBytes(abs));
            if (!string.Equals(actual, kv.Value, StringComparison.OrdinalIgnoreCase))
                return Fail(2, "SHA256 校验失败：" + kv.Key + "（文件被改过或拷贝不完整）");
        }

        try
        {
            var s = new ConfigSnapshot(o.Locator);
            var disk = new SettingsDraft();
            disk.LoadFrom(s);
            var target = new SettingsDraft();
            target.LoadFrom(s);

            int applied = ApplyProfile(target, man);
            ClearCustomFlags(target);   // 与 --default 一致：导入就是要覆盖，别被本机「自定义态」拦住

            var plan = WritePlanBuilder.Build(target, DraftBaseline.Force(disk), s);
            ApplyRegistry(plan, s, man, o.KeepRegistry);
            DefaultProfile.EnsureFixedAssets(plan, s);   // 配色 / 右键菜单两个文件 / menu.conf「选项」行
            if (o.IncludeExtra) AddExtra(plan, dir, man, o.Locator);
            DefaultProfile.TrimPlan(plan);

            var r = new Result { Success = true, Code = 0, Plan = plan };

            if (o.DryRun)
            {
                r.Message = "--dry-run：只算计划，一个字节都没写";
                r.Details.Add($"目标态来自 manifest：{applied} 个受管设置项");
                r.Details.Add(plan.Summary);
                return r;
            }

            // 导入前的现场快照（固定一份，覆盖上一次）。失败就别导入了——没有回滚点比不导更危险。
            if (o.Snapshot)
            {
                string snap = Path.Combine(dir, SnapshotFolder);
                var before = Export(new Options { Dir = snap, Locator = o.Locator, Snapshot = false });
                if (!before.Success)
                    return Fail(1, "导入前快照失败，已取消导入：" + before.Message);
                r.Details.Add("导入前现场快照：" + snap + "（覆盖式，只保留一份）");
            }

            if (!plan.CanApply)
            {
                var bad = Fail(1, "校验未通过，未写入任何文件");
                bad.Details.AddRange(plan.Blockers);
                bad.Plan = plan;
                return bad;
            }

            var write = ConfWriter.Apply(plan);
            r.Write = write;
            r.Details.AddRange(write.Details.Select(d => "  " + d));
            r.Details.Add(write.Summary);
            if (!write.Success)
            {
                r.Success = false;
                r.Code = 1;
                r.Message = "导入失败：" + write.Error;
                return r;
            }

            r.Message = "导入完成：" + (write.Summary.Length > 0 ? write.Summary : plan.Summary) +
                        "（af=bassmax 已强制保留；导入后请重启 MPV 生效）";
            return r;
        }
        catch (Exception ex) { return Fail(2, "导入失败：" + ex.Message); }
    }

    // ==================== 内部：计划构造 ====================

    /// <summary>把 manifest.profile 回填到草稿。只认 ManagedProps 白名单，且跳过本机路径类。</summary>
    private static int ApplyProfile(SettingsDraft target, Manifest man)
    {
        var patch = new Dictionary<string, object>();
        foreach (var kv in man.Profile)
            if (DraftBaseline.ManagedProps.Contains(kv.Key) && !LocalOnly.Contains(kv.Key))
                patch[kv.Key] = kv.Value;

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(patch));
        int applied = DraftPatch.Apply(target, doc.RootElement).Count;

        // 页 E 不走 DraftPatch 的 Shortcuts 分支：那个分支调 SetKey，而 SetKey 把
        // 「键等于内置默认键」当成「不写这一行」，会把本包 E4 里显式写在 input.conf
        // 的 10 行（Present=true 且键就是默认值）误判成删除。直接 Load 保留 present 语义。
        foreach (var kv in man.Shortcuts)
        {
            var row = target.Shortcuts.Find(kv.Key);
            if (row is null) continue;
            bool present = kv.Value.Length > 0;
            row.Load(present, present ? kv.Value : (row.HasDefault ? row.DefaultKey : string.Empty));
            applied++;
        }
        target.Shortcuts.RefreshChecks();
        return applied;
    }

    private static void ClearCustomFlags(SettingsDraft target)
    {
        foreach (var p in typeof(SettingsDraft).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (p.CanWrite && p.PropertyType == typeof(bool) && p.Name.EndsWith("Custom", StringComparison.Ordinal))
                p.SetValue(target, false);
    }

    /// <summary>D 类：六键值以 manifest 为准；--keep-registry 时一项都不写。</summary>
    private static void ApplyRegistry(WritePlan plan, ConfigSnapshot s, Manifest man, bool keep)
    {
        if (keep) { plan.Registry.Changes.Clear(); return; }

        foreach (string key in BassMaxStore.Keys)
        {
            if (!man.Registry.TryGetValue(key, out string want)) continue;
            string have = s.BassMax.Get(key, string.Empty);
            var existing = plan.Registry.Changes.FirstOrDefault(
                c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase));

            if (BassMaxStore.SameValue(have, want))
            {
                if (existing != null) plan.Registry.Changes.Remove(existing);
                continue;
            }
            if (existing != null)
            {
                existing.From = have;
                existing.To = want;
            }
            else
            {
                plan.Registry.Changes.Add(new RegChange
                {
                    Key = key, From = have, To = want,
                    Group = "BassMax 音效", Fill = !s.BassMax.Has(key),
                });
            }
        }
    }

    /// <summary>C 类：只有同时给 --include-extra 才把 extra\scripts\ 下的文本文件整文件写回。</summary>
    private static void AddExtra(WritePlan plan, string dir, Manifest man, ConfigLocator loc)
    {
        const string prefix = "extra\\";
        foreach (var kv in man.Files.Where(k => k.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            string rel = kv.Key.Substring(prefix.Length);
            string dst = Path.Combine(loc.ConfigDir, rel);
            if (rel.Contains("..") || Path.IsPathRooted(rel)) continue;

            string want = File.ReadAllText(Path.Combine(dir, kv.Key), Encoding.UTF8);
            string have = File.Exists(dst) ? File.ReadAllText(dst, Encoding.UTF8) : null;
            if (have is not null && Norm(have) == Norm(want)) continue;

            var f = plan.For(prefix + rel, dst);
            f.NewContent = want;
            f.IsCreate = have is null;
            f.WholeFileKind = "第三方脚本 / 配置";
        }
    }

    // ==================== 内部：文件与目录 ====================

    private static Result CheckConfig(Options o)
        => o?.Locator is null || !o.Locator.IsUsable
            ? Fail(2, "配置不可用：" + (o?.Locator?.Reason ?? "未定位到配置目录"))
            : null;

    private static Result Fail(int code, string msg) => new() { Success = false, Code = code, Message = msg };
    private static Result Ok(string msg) => new() { Success = true, Code = 0, Message = msg };

    /// <summary>只允许覆盖本工具自己的目录内容；目录里有陌生文件就拒绝，避免误删用户资料。</summary>
    private static string GuardDir(string dir)
    {
        if (!Directory.Exists(dir)) return string.Empty;
        if (File.Exists(Path.Combine(dir, ManifestFile))) return string.Empty;

        var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "portable_config", "extra", ManifestFile, SnapshotFolder };
        string[] strange = Directory.GetFileSystemEntries(dir)
            .Where(p => !own.Contains(Path.GetFileName(p))).ToArray();
        return strange.Length == 0
            ? string.Empty
            : "目录里没有本工具的 manifest.json，且有不属于备份的文件（" +
              Path.GetFileName(strange[0]) + " 等 " + strange.Length + " 项）。为了不误删资料，请换一个专用文件夹。";
    }

    /// <summary>覆盖式清理：只删自己生成的两个子目录与 manifest.json，_before_import 保留作回滚点。</summary>
    private static void Clean(string dir)
    {
        foreach (string name in new[] { "portable_config", "extra" })
        {
            string p = Path.Combine(dir, name);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        string mf = Path.Combine(dir, ManifestFile);
        if (File.Exists(mf)) File.Delete(mf);
    }

    private static void WriteMirror(string dir, string rel, byte[] bytes)
    {
        string dst = Path.Combine(dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(dst) ?? dir);
        File.WriteAllBytes(dst, bytes);   // 原样复制，保留 BOM / CRLF
    }

    private static string SafeJoin(string dir, string rel, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..") || Path.IsPathRooted(rel))
        {
            error = "路径越界";
            return string.Empty;
        }
        string abs = Path.GetFullPath(Path.Combine(dir, rel));
        string root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!abs.StartsWith(root, StringComparison.OrdinalIgnoreCase)) error = "指向备份目录之外";
        return abs;
    }

    private static IEnumerable<string> ExtraFiles(ConfigLocator loc)
    {
        string dir = loc.ScriptsDir;
        if (!Directory.Exists(dir)) yield break;
        foreach (string path in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(path);
            if (ManagedFiles.Contains("scripts\\" + name, StringComparer.OrdinalIgnoreCase)) continue;
            if (!ExtraExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) continue;
            if (name.Contains(".bak_") || name.EndsWith("~", StringComparison.Ordinal)) continue;
            if (new FileInfo(path).Length > 1024 * 1024) continue;   // 1 MB 以上多半不是配置
            yield return path;
        }
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Norm(string t) => (t ?? string.Empty).Replace("\r\n", "\n").TrimStart('\uFEFF').TrimEnd('\n');
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MpvSettings.Models;

namespace MpvSettings.Services;

public sealed class ConfWriterOptions
{
    /// <summary>仅验证用：成功写完第 N 个文件后注入失败，检查回滚是否生效。0 = 不注入。</summary>
    public int FailAfter { get; set; }
}

/// <summary>
/// 写盘执行器（第 4 节写盘契约）：
///   0. 注册表（阶段 ④）先写：逐项记原值，失败立刻还原，文件还没动过；
///   1. 写前重新读盘 —— 以磁盘上的最新文本为基底，绝不用打开窗口时的旧快照整文件回写；
///   2. 只替换计划里的 diff 键 —— 注释、空行、未知键、原有顺序全部原样保留；
///   3. 同名键的多行全部同步替换（setting.exe 的 MpvReadKey 读第一行、mpv 取最后一行，
///      留一半旧值会让两个程序反推不一致）；
///   4. 临时文件 + MoveFileEx(REPLACE_EXISTING|WRITE_THROUGH) 原子替换，不长期持锁；
///   5. 多个文件作为一个事务：任一文件失败，把已写的按原字节逐一回滚，注册表同时还原。
///
/// 编码统一 UTF-8 无 BOM；换行沿用各文件原有风格（mpv.conf / hdr 是 CRLF）。
/// 文本没变化时连临时文件都不建，真正做到「零改动零写盘」。
/// </summary>
public static class ConfWriter
{
    private const uint MoveFileReplaceExisting = 0x00000001;
    private const uint MoveFileWriteThrough = 0x00000008;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool MoveFileEx(string existing, string replacement, uint flags);

    private sealed class Pending
    {
        public string Path;
        public string Label;

        /// <summary>原文件字节；null 表示写入前该文件不存在（新建），回滚时要删掉而不是写回。</summary>
        public byte[] Original;
        public byte[] NewBytes;
        public List<KeyChange> Changes;
    }

    public static WriteResult Apply(WritePlan plan, ConfWriterOptions opt = null)
    {
        opt ??= new ConfWriterOptions();
        var result = new WriteResult();
        result.Warnings.AddRange(plan.Warnings);
        result.Deferred.AddRange(plan.Deferred);
        result.Blockers.AddRange(plan.Blockers);
        result.Summary = plan.Summary;

        if (!plan.CanApply) return WriteResult.Blocked(plan);
        if (!plan.HasChanges)
        {
            result.Success = true;
            result.Summary = "没有需要写入的改动，未触碰任何文件";
            return result;
        }

        // ---------- 1) 注册表先写：值小、可逐项还原；失败立刻停，文件一个都还没动 ----------
        var undo = new RegistryUndo();
        if (plan.Registry.HasChanges)
        {
            if (!RegistryWriter.TryApply(plan.Registry, undo, result.Details,
                                         out string regError, out int regWritten))
            {
                undo.Run(result.Warnings);
                result.Success = false;
                result.Error = regError;
                result.Summary = "注册表写入失败，已还原";
                result.Details.Clear();
                result.KeysWritten = 0;
                return result;
            }
            result.RegistryWritten = regWritten;
        }

        // ---------- 2) 写前重读 + 在内存里构造全部新文本 ----------
        // 整文件重写的（cover-art.conf）排在前面：先把被 include 的文件建好，
        // 再往 mpv.conf 里加 include 行，任何中间状态都不会指向一个不存在的文件。
        var pending = new List<Pending>();
        try
        {
            foreach (var file in plan.Files.OrderBy(f => f.IsWholeFile ? 0 : 1))
            {
                if (file.IsWholeFile)
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(file.NewContent);
                    bool existed = File.Exists(file.Path);
                    byte[] original = existed ? File.ReadAllBytes(file.Path) : null;

                    // 文本相同就不写（用户在界面上改回去，或外部已经写成了一样）
                    if (existed && ByteEqual(bytes, original)) continue;

                    pending.Add(new Pending
                    {
                        Path = file.Path, Label = file.Label,
                        Original = original, NewBytes = bytes,
                        Changes = new List<KeyChange>(file.Changes),
                    });
                    result.KeysWritten += file.Changes.Count;
                    result.Details.Add(existed
                        ? $"{file.Label} · 整文件重写（{LineCount(file.NewContent)} 行 · {file.WholeFileKind}）" + GroupTag(file.Changes.FirstOrDefault())
                        : $"{file.Label} · 新建文件（{LineCount(file.NewContent)} 行 · {file.WholeFileKind}）");
                    continue;
                }

                // ---- input.conf：KEY command 格式，按动作命令串定位行（阶段 ⑥）----
                if (file.InputEdits.Count > 0)
                {
                    byte[] disk = File.Exists(file.Path) ? File.ReadAllBytes(file.Path) : null;
                    var outcome = InputConfWriter.Build(file.Path, file.InputEdits, disk);
                    if (outcome.Error.Length > 0) throw new IOException(outcome.Error);
                    if (outcome.Applied == 0) continue;
                    if (disk is not null && ByteEqual(outcome.Bytes, disk)) continue;

                    result.KeysWritten += outcome.Applied;
                    foreach (string d in outcome.Details) result.Details.Add(d);
                    pending.Add(new Pending
                    {
                        Path = file.Path, Label = file.Label,
                        Original = disk, NewBytes = outcome.Bytes,
                        Changes = new List<KeyChange>(),
                    });
                    continue;
                }

                var fresh = ConfFile.Load(file.Path);   // 关键：这里重读，不是用窗口打开时的快照
                if (!fresh.Exists)
                    throw new IOException("写前重读发现文件已不存在：" + file.Path);

                var lines = new List<string>(fresh.Lines);
                var effective = new List<KeyChange>();
                foreach (var change in file.Changes)
                {
                    // 结果明细里的 From 以「写前重读到的值」为准，才是真实的前后对比
                    string current = fresh.Has(change.Key) ? fresh.Get(change.Key) : string.Empty;

                    // ---- 删行（阶段 ⑦：关闭信息窗时移除 include=~~/audio-info.conf）----
                    if (change.Remove)
                    {
                        int removed = RemoveLines(lines, change.Key, change.MatchValue);
                        if (removed == 0) continue;
                        change.From = change.MatchValue;
                        effective.Add(change);
                        result.Details.Add($"{file.Label} · 移除 {change.Key}={change.MatchValue}" + GroupTag(change));
                        continue;
                    }

                    string target = change.Transform is null ? change.To : change.Transform(current);

                    if (fresh.Has(change.Key) && string.Equals(current, target, StringComparison.Ordinal))
                        continue;   // 磁盘上已经是这个值（af 合并后没变化也算）：不写、不计数

                    // include 这类可重复键：磁盘上已经有同值的行就什么都不做，
                    // 否则 ReplaceKey 会把用户自己的其它 include 行一起改写
                    if (change.AppendOnly &&
                        fresh.GetAll(change.Key).Any(v => string.Equals(v, target, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    change.From = current;
                    change.Append = !fresh.Has(change.Key);
                    change.To = target;
                    effective.Add(change);

                    var hit = ReplaceKey(lines, change.Key, change.To, change.AppendOnly);
                    if (hit.Appended)
                        result.Details.Add($"{file.Label} · 追加 {change.Key}={change.To}" + GroupTag(change));
                    else
                        result.Details.Add($"{file.Label} · {change.Key}  {change.DisplayFrom} → {change.DisplayTo}" +
                                           GroupTag(change) + (hit.Replaced > 1 ? $"（重复键 {hit.Replaced} 处同步）" : ""));
                }

                if (effective.Count == 0) continue;   // 这个文件没必要动

                string nl = fresh.Crlf ? "\r\n" : "\n";
                string text = string.Join(nl, lines);
                byte[] bytes2 = new UTF8Encoding(false).GetBytes(text);
                byte[] original2 = File.ReadAllBytes(file.Path);

                if (ByteEqual(bytes2, original2))
                {
                    // 文本相同就不写：别的程序已经写成了这个值，或用户改完又改回去了
                    continue;
                }
                result.KeysWritten += effective.Count;   // 只统计真正落到磁盘的键
                pending.Add(new Pending
                {
                    Path = file.Path, Label = file.Label,
                    Original = original2, NewBytes = bytes2, Changes = effective,
                });
            }
        }
        catch (Exception ex)
        {
            undo.Run(result.Warnings);
            result.Success = false;
            result.Error = "构造写入内容时失败：" + ex.Message;
            result.KeysWritten = 0;
            result.RegistryWritten = 0;
            result.Details.Clear();
            return result;
        }

        if (pending.Count == 0)
        {
            result.Success = true;
            result.FilesWritten = 0;
            result.Summary = result.RegistryWritten > 0
                ? $"已写入注册表 {result.RegistryWritten} 项（配置文件无需改动）"
                : "磁盘上的值已经与草稿一致，未重写文件";
            return result;
        }

        // ---------- 3) 依次原子替换；失败则整体回滚（含刚写的注册表） ----------
        var written = new List<Pending>();
        var temps = new List<string>();
        try
        {
            foreach (var p in pending)
            {
                string dir = Path.GetDirectoryName(p.Path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string tmp = p.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                temps.Add(tmp);
                File.WriteAllBytes(tmp, p.NewBytes);
                AtomicReplace(tmp, p.Path);
                written.Add(p);
                result.FilesWritten++;

                if (opt.FailAfter > 0 && written.Count >= opt.FailAfter)
                    throw new IOException("[验证注入] 已写入 " + written.Count + " 个文件，模拟后续失败");
            }
            result.Success = true;
            var parts = new List<string>();
            if (result.FilesWritten > 0)
            {
                var seg = new List<string>();
                if (plan.WholeFileCount > 0) seg.Add($"{plan.WholeFileCount} 个整文件重写");
                if (result.KeysWritten > 0) seg.Add($"{result.KeysWritten} 个键");
                parts.Add($"已写入 {result.FilesWritten} 个文件" +
                          (seg.Count > 0 ? "（" + string.Join(" · ", seg) + "）" : string.Empty));
            }
            if (result.RegistryWritten > 0) parts.Add($"注册表 {result.RegistryWritten} 项");
            result.Summary = parts.Count > 0 ? string.Join(" · ", parts) : "已完成";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            result.RolledBack = true;
            result.Summary = "写入失败，已回滚";

            // 逆序恢复已替换的文件（同样用原子替换，避免回滚过程再崩出半个文件）
            for (int i = written.Count - 1; i >= 0; i--)
            {
                var p = written[i];
                try
                {
                    if (p.Original is null)
                    {
                        // 本次新建的文件：回滚就是把它删掉，别留一个空壳让 mpv 读到残缺 profile
                        if (File.Exists(p.Path)) File.Delete(p.Path);
                        continue;
                    }
                    string tmp = p.Path + "." + Guid.NewGuid().ToString("N") + ".rollback.tmp";
                    temps.Add(tmp);
                    File.WriteAllBytes(tmp, p.Original);
                    AtomicReplace(tmp, p.Path);
                }
                catch (Exception rollbackEx)
                {
                    result.Warnings.Add("回滚 " + p.Label + " 失败：" + rollbackEx.Message +
                                        "（原文件字节已保留在本次结果里，可手工恢复）");
                }
            }

            // 注册表也一起还原，别留下「文件回滚了、调音却改了」的半截状态
            undo.Run(result.Warnings);

            result.FilesWritten = 0;
            result.KeysWritten = 0;
            result.RegistryWritten = 0;
            result.Details.Add("事务已回滚：以上改动均未保留，磁盘与注册表仍是原有内容");
        }
        finally
        {
            foreach (var tmp in temps)
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }
        return result;
    }

    private static string GroupTag(KeyChange c)
        => c is null || string.IsNullOrEmpty(c.Group) ? string.Empty : " 〔" + c.Group + " 成组〕";

    private static int LineCount(string text)
        => text.Length == 0 ? 0 : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;

    private static void AtomicReplace(string tmp, string target)
    {
        if (MoveFileEx(tmp, target, MoveFileReplaceExisting | MoveFileWriteThrough)) return;
        int err = Marshal.GetLastWin32Error();
        // 少数网络盘 / 特殊文件系统不支持 MoveFileEx，退回 .NET 的覆盖移动
        try { File.Move(tmp, target, true); return; }
        catch (Exception) { throw new Win32Exception(err, "原子替换失败：" + target); }
    }

    /// <summary>
    /// 逐行替换：保留注释行、空行、未知键、原有顺序；键不存在时追加到末尾（末尾空行之前）。
    /// appendOnly = true 时只追加、绝不替换 —— include 可以出现多次，且每一行都可能是别人的。
    /// </summary>
    private static (int Replaced, bool Appended) ReplaceKey(List<string> lines, string key, string value,
                                                            bool appendOnly = false)
    {
        if (!appendOnly)
        {
            int replaced = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                string t = lines[i].Trim();
                if (t.Length == 0 || t[0] is '#' or ';' or '[') continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;

                // 行内注释（如 "yes # 说明"）跟着键一起保留
                string oldValue = t.Substring(eq + 1).Trim();
                lines[i] = key + "=" + value + InlineComment(oldValue);
                replaced++;
            }

            if (replaced > 0) return (replaced, false);
        }

        string added = key + "=" + value;
        if (lines.Count > 0 && lines[^1].Length == 0) lines.Insert(lines.Count - 1, added);
        else lines.Add(added);
        return (0, true);
    }

    /// <summary>
    /// 删行：按「键名 + 可选的值」匹配，从后往前删，避免下标错位。
    /// matchValue 为空时只按键名删；include 这类可重复键必须带值，否则会删掉别人的 include。
    /// </summary>
    private static int RemoveLines(List<string> lines, string key, string matchValue)
    {
        int removed = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            string t = lines[i].Trim();
            if (t.Length == 0 || t[0] is '#' or ';' or '[') continue;
            int eq = t.IndexOf('=');
            if (eq <= 0) continue;
            if (!string.Equals(t.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
            if (matchValue.Length > 0 &&
                !string.Equals(t.Substring(eq + 1).Trim(), matchValue, StringComparison.OrdinalIgnoreCase)) continue;
            lines.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    private static string InlineComment(string oldValue)
    {
        if (string.IsNullOrEmpty(oldValue)) return string.Empty;
        for (int i = 1; i < oldValue.Length; i++)
            if (oldValue[i] == '#' && (oldValue[i - 1] == ' ' || oldValue[i - 1] == '\t'))
                return " " + oldValue.Substring(i).TrimEnd();
        return string.Empty;
    }

    private static bool ByteEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MpvSettings.Models;

namespace MpvSettings.Services;

/// <summary>
/// input.conf 的行级写入器（唯一一处非 key=value 格式）。
/// 三条硬约束在这里落地：
///   · 行格式 `KEY command`，没有等号；
///   · UTF-8 无 BOM，换行沿用文件原有风格（生产是 LF，不是 mpv.conf 的 CRLF）；
///   · `#` 键写 SHARP；键名大小写敏感。
/// 只碰「命令串精确匹配」的那几行：注释、[section]、键序列、分号多命令、别人的绑定全部原样保留。
/// </summary>
public static class InputConfWriter
{
    public sealed class Outcome
    {
        public byte[] Bytes { get; set; }
        public List<string> Details { get; } = new();
        public int Applied { get; set; }
        public bool Created { get; set; }
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>写前重读磁盘（传入的是原始字节，null 表示文件不存在），返回要落盘的新字节。</summary>
    public static Outcome Build(string path, IReadOnlyList<InputEdit> edits, byte[] diskBytes)
    {
        var outcome = new Outcome();
        outcome.Created = diskBytes is null;

        bool bom = diskBytes is not null && diskBytes.Length >= 3
                   && diskBytes[0] == 0xEF && diskBytes[1] == 0xBB && diskBytes[2] == 0xBF;
        string text = diskBytes is null
            ? string.Empty
            : Encoding.UTF8.GetString(diskBytes, bom ? 3 : 0, diskBytes.Length - (bom ? 3 : 0));

        bool trailingNewline = text.Length > 0 && text.EndsWith("\n", StringComparison.Ordinal);
        string nl = text.Contains("\r\n") ? "\r\n" : "\n";

        var lines = text.Length == 0
            ? new List<string>()
            : text.Replace("\r\n", "\n").Split('\n').ToList();

        foreach (var edit in edits)
        {
            if (edit is null || edit.Command.Length == 0) continue;

            var hits = FindLines(lines, edit.Command);

            if (edit.IsDelete)
            {
                if (hits.Count == 0)
                {
                    outcome.Details.Add($"input.conf · {edit.Action}：文件里没有这一行，无需删除");
                    continue;
                }
                var removed = hits.Select(i => KeyOf(lines[i])).ToList();
                for (int i = hits.Count - 1; i >= 0; i--) lines.RemoveAt(hits[i]);
                outcome.Applied++;
                outcome.Details.Add($"input.conf · 删除 {string.Join("、", removed)} {edit.Command}（{edit.Action} 恢复默认）");
                continue;
            }

            string key = ShortcutKeys.ToFile(edit.NewKey);
            if (key.Length == 0) continue;   // 空键不写入，防御性跳过

            if (hits.Count == 0)
            {
                string added = key + " " + edit.Command;
                if (lines.Count > 0 && lines[^1].Length == 0) lines.Insert(lines.Count - 1, added);
                else lines.Add(added);
                trailingNewline = true;
                outcome.Applied++;
                outcome.Details.Add($"input.conf · 新增 {key} {edit.Command}（{edit.Action}）");
                continue;
            }

            // 命令串命中：只换键，命令文本保持磁盘上的原样（不替用户重排版）
            string oldKey = KeyOf(lines[hits[0]]);
            string cmdText = CommandOf(lines[hits[0]]);
            lines[hits[0]] = key + " " + cmdText;

            // 同一命令被写了多行：强绑定只需一行，多余的删掉，避免两个键都生效
            for (int i = hits.Count - 1; i >= 1; i--) lines.RemoveAt(hits[i]);

            outcome.Applied++;
            outcome.Details.Add($"input.conf · {edit.Action}  {ShortcutKeys.Display(oldKey)} → {ShortcutKeys.Display(key)}" +
                               (hits.Count > 1 ? $"（重复行 {hits.Count - 1} 处已合并）" : string.Empty));
        }

        if (outcome.Applied == 0)
        {
            outcome.Bytes = diskBytes ?? Array.Empty<byte>();
            return outcome;
        }

        string outText = string.Join(nl, lines);
        if (!trailingNewline && outText.Length > 0 && text.Length == 0) outText += nl;   // 新建文件补尾换行
        outcome.Bytes = new UTF8Encoding(false).GetBytes(outText);
        return outcome;
    }

    private static List<int> FindLines(List<string> lines, string normCommand)
    {
        var hits = new List<int>();
        for (int i = 0; i < lines.Count; i++)
        {
            if (!ShortcutKeys.TryParseLine(lines[i], out _, out string cmd)) continue;
            if (string.Equals(ShortcutKeys.NormCommand(cmd), normCommand, StringComparison.Ordinal)) hits.Add(i);
        }
        return hits;
    }

    private static string KeyOf(string line)
        => ShortcutKeys.TryParseLine(line, out string k, out _) ? k : string.Empty;

    private static string CommandOf(string line)
        => ShortcutKeys.TryParseLine(line, out _, out string c) ? c : string.Empty;
}

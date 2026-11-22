using System;
using System.Collections.Generic;
using System.Linq;

namespace MpvSettings.Services;

/// <summary>
/// mpv 的 af 是滤镜链（逗号分隔），不是布尔开关。
/// 只能增删 bassmax 这一节，绝不能整行覆盖，
/// 否则用户自己加的 scaletempo2 / lavfi 之类会被一起抹掉。
///
/// 2026-11-15 起 **只用于「补」，不再用于「删」**：MPV 有专属音效，
/// af=bassmax 是本包不变量，音效开关改的是注册表 Enabled。
/// 也就是说生产代码只会调 Merge(chain, true)；Merge 的 false 分支保留，
/// 仅作为滤镜链解析器的对称性，别在写盘链路里用它。
/// </summary>
public static class AfChain
{
    public const string Filter = "bassmax";

    public static bool Contains(string chain)
        => Split(chain).Any(IsBassMax);

    public static string Merge(string chain, bool enable)
    {
        var tokens = Split(chain);
        bool has = tokens.Any(IsBassMax);

        if (enable)
        {
            if (!has) tokens.Add(Filter);
        }
        else if (has)
        {
            tokens.RemoveAll(IsBassMax);
        }

        return string.Join(",", tokens);
    }

    private static bool IsBassMax(string token)
        => token.IndexOf("bassmax", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>按逗号切分，但忽略 [] 内的逗号（lavfi=[...] 里可能有）。</summary>
    private static List<string> Split(string chain)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(chain)) return list;

        int depth = 0, start = 0;
        for (int i = 0; i < chain.Length; i++)
        {
            char c = chain[i];
            if (c == '[') depth++;
            else if (c == ']' && depth > 0) depth--;
            else if (c == ',' && depth == 0)
            {
                Add(chain.Substring(start, i - start));
                start = i + 1;
            }
        }
        Add(chain.Substring(start));

        void Add(string raw)
        {
            string t = raw.Trim();
            if (t.Length > 0) list.Add(t);
        }
        return list;
    }
}

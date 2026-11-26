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

    /// <summary>
    /// 下混节（2026-11-26）：mpv 的 audio-channels=stereo 只改 AO 协商，不往滤镜链插下混。
    /// 必须显式把 aresample 放在 bassmax 之前，BassMax 才拿到 2ch、驱动不降到 60%。
    /// out_chlayout 是 swresample 的标准矩阵，5.1/7.1/2.0 都能进，不依赖具体布局。
    /// </summary>
    public const string DownmixFilter = "lavfi=[aresample=out_chlayout=stereo]";

    public static bool Contains(string chain)
        => Split(chain).Any(IsBassMax);

    /// <summary>是否已有下混节（认 aresample + out_chlayout/ochl，兼容旧缩写写法）。</summary>
    public static bool ContainsDownmix(string chain)
        => Split(chain).Any(IsDownmix);

    /// <summary>
    /// 增删 bassmax 一节，并按 downmix 参数同步下混节的位置：有下混时它紧挨 bassmax 之前。
    /// 只动这两节，用户自己加的 scaletempo2 / lavfi=[...] 一律保留。
    /// 生产代码只会传 enable=true（音效常驻）；false 分支仅留作解析器对称性。
    /// </summary>
    public static string Merge(string chain, bool enable, bool downmix = false)
    {
        var tokens = Split(chain);

        // 先统一摘掉旧下混节（含旧写法），再按需插回，保证不重复、位置正确
        tokens.RemoveAll(IsDownmix);

        bool has = tokens.Any(IsBassMax);
        if (enable)
        {
            if (!has) tokens.Add(Filter);
        }
        else if (has)
        {
            tokens.RemoveAll(IsBassMax);
        }

        if (downmix && enable)
        {
            int idx = tokens.FindIndex(IsBassMax);
            tokens.Insert(idx < 0 ? tokens.Count : idx, DownmixFilter);
        }

        return string.Join(",", tokens);
    }

    private static bool IsBassMax(string token)
        => token.IndexOf("bassmax", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsDownmix(string token)
        => token.IndexOf("aresample", StringComparison.OrdinalIgnoreCase) >= 0
           && (token.IndexOf("out_chlayout", StringComparison.OrdinalIgnoreCase) >= 0
               || token.IndexOf("ochl", StringComparison.OrdinalIgnoreCase) >= 0
               || token.IndexOf("out_channel_layout", StringComparison.OrdinalIgnoreCase) >= 0);

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

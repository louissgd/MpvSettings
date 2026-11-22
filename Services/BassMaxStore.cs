using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;
using MpvSettings.Models;

namespace MpvSettings.Services;

/// <summary>
/// HKCU\Software\BassMax 的读写约定（照抄 Claw\bassmax_dshow\bassmax_settings.cpp）：
///   · 六键 Bass / Treble / Drive / Preset / Enabled / TrayIcon 必须齐全，
///     DShow 侧 ReloadFromRegistry() 缺任意一个会整批忽略（mpv 的 af_bassmax 是逐键读，宽容些）;
///   · 官方写入格式：Bass/Treble "%.1f"、Drive "%.2f"、Preset/Enabled/TrayIcon "%d"，类型 REG_SZ；
///   · 读取同时接受 REG_SZ / REG_EXPAND_SZ / REG_DWORD，所以写回时保留原有类型，不擅自改类型。
///
/// 环境变量 MPVSETTINGS_BASSMAX_KEY 可把键路径改到测试键（默认 Software\BassMax），
/// 自动化验证靠它避免碰用户真实调音值。
/// </summary>
public static class BassMaxStore
{
    public const string DefaultKeyPath = @"Software\BassMax";
    public const string KeyPathEnvVar = "MPVSETTINGS_BASSMAX_KEY";

    public static readonly string[] Keys = { "Bass", "Treble", "Drive", "Preset", "Enabled", "TrayIcon" };

    public static string KeyPath
    {
        get
        {
            string v = Environment.GetEnvironmentVariable(KeyPathEnvVar);
            return string.IsNullOrWhiteSpace(v) ? DefaultKeyPath : v.Trim();
        }
    }

    public static string DisplayPath => @"HKCU\" + KeyPath;

    /// <summary>六个键各自的出厂默认（与 bassmax_settings.cpp 的 s_val 一致）。</summary>
    public static string DefaultOf(string key) => key switch
    {
        "Bass" or "Treble" => "2.0",
        "Drive" => "1.00",
        "Preset" => "4",
        _ => "1",   // Enabled / TrayIcon
    };

    /// <summary>按官方格式序列化：改一位小数就别写成 2 或 2.00，否则和托盘写出来的不一致。</summary>
    public static string Format(string key, double value) => key switch
    {
        "Bass" or "Treble" => value.ToString("0.0", CultureInfo.InvariantCulture),
        "Drive" => value.ToString("0.00", CultureInfo.InvariantCulture),
        "Preset" => ((int)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture),
        "Enabled" or "TrayIcon" => value != 0 ? "1" : "0",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>注册表里存的是字符串，比较要按数值比 —— "2" 与 "2.0" 是同一个调音，不该重写。</summary>
    public static bool SameValue(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            return Math.Abs(x - y) < 0.0005;
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public static double Parse(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

    internal static bool IsInteger(string s)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && Math.Abs(v - Math.Round(v)) < 1e-9;

    /// <summary>读一个值：REG_DWORD 转成十进制字符串，REG_SZ 原样；读不到返回 null（真·缺失）。</summary>
    internal static string ReadRaw(RegistryKey key, string name, out RegistryValueKind kind)
    {
        kind = RegistryValueKind.Unknown;
        try
        {
            object v = key.GetValue(name, null);
            if (v is null) return null;
            kind = key.GetValueKind(name);
            return v switch
            {
                string s => s,
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                string[] arr => arr.Length > 0 ? arr[0] : null,
                _ => Convert.ToString(v, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static RegistryKey OpenRead()
    {
        try { return Registry.CurrentUser.OpenSubKey(KeyPath); }
        catch (Exception) { return null; }
    }
}

/// <summary>一次注册表写入的前后值，用于失败回滚。只记本进程真正动过的项。</summary>
public sealed class RegistryUndo
{
    internal sealed class Item
    {
        public string Name;
        public bool Existed;
        public object Value;
        public RegistryValueKind Kind;
    }

    internal readonly List<Item> Items = new();

    public bool HasWork => Items.Count > 0;

    /// <summary>逆序还原：值原本存在就写回原值与原类型，原本没有就删掉。</summary>
    public void Run(List<string> warnings)
    {
        if (Items.Count == 0) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(BassMaxStore.KeyPath, true);
            if (key is null)
            {
                warnings?.Add("回滚注册表失败：无法打开 " + BassMaxStore.DisplayPath);
                return;
            }
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                try
                {
                    if (it.Existed) key.SetValue(it.Name, it.Value, it.Kind);
                    else key.DeleteValue(it.Name, false);
                }
                catch (Exception ex)
                {
                    warnings?.Add($"回滚注册表项 {it.Name} 失败：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            warnings?.Add("回滚注册表失败：" + ex.Message);
        }
        Items.Clear();
    }
}

/// <summary>注册表写入执行器。先记原值再写，任何一步失败都由调用方回滚。</summary>
public static class RegistryWriter
{
    public static bool TryApply(RegistryPlan plan, RegistryUndo undo, List<string> details,
                                out string error, out int written)
    {
        error = string.Empty;
        written = 0;

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(plan.KeyPath, true);
            if (key is null)
            {
                error = "无法打开或创建注册表键 " + plan.DisplayPath;
                return false;
            }

            foreach (var c in plan.Changes)
            {
                string old = BassMaxStore.ReadRaw(key, c.Key, out RegistryValueKind kind);
                bool existed = old is not null;

                undo.Items.Add(new RegistryUndo.Item { Name = c.Key, Existed = existed, Value = old, Kind = kind });

                if (existed && kind == RegistryValueKind.DWord && BassMaxStore.IsInteger(c.To))
                {
                    // 原来是 DWORD 就继续写 DWORD；但 Bass/Treble 是 0.1 步长，
                    // 带小数时不能塞进 DWORD（会被截断成整数丢掉 0.5），改回 REG_SZ 保精度
                    double n = BassMaxStore.Parse(c.To, 0);
                    key.SetValue(c.Key, (int)Math.Round(n, MidpointRounding.AwayFromZero), RegistryValueKind.DWord);
                }
                else if (!existed)
                {
                    // 缺键时按官方写法补 REG_SZ，别写 DWORD —— 保持和托盘/面板写出来的一致
                    key.SetValue(c.Key, c.To, RegistryValueKind.String);
                }
                else
                {
                    key.SetValue(c.Key, c.To, RegistryValueKind.String);
                }

                written++;
                details?.Add($"{plan.DisplayPath} · {c.Key}  {c.DisplayFrom} → {c.To}" +
                             (c.Fill ? "（补齐缺失键）" : string.Empty) +
                             (string.IsNullOrEmpty(c.Group) ? string.Empty : $" 〔{c.Group} 成组〕"));
            }
            return true;
        }
        catch (Exception ex)
        {
            error = "写入注册表失败：" + ex.Message;
            return false;
        }
    }

    /// <summary>点确定时用：验证键是否可写（键不存在也可写，能 CreateSubKey 就行）。</summary>
    public static bool CanWrite(out string reason)
    {
        reason = string.Empty;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(BassMaxStore.KeyPath, true);
            if (key is null) { reason = "无法创建或打开 " + BassMaxStore.DisplayPath; return false; }
            return true;
        }
        catch (Exception ex)
        {
            reason = "注册表不可写：" + ex.Message;
            return false;
        }
    }
}

using Microsoft.Win32;

namespace MpvSettings.Services;

/// <summary>
/// 读 NVIDIA 驱动注册表，取 NVIDIA App 视频页的两个「硬开关」实际值：
///   · _User_Global_VAL_SuperResolution  0=关，1..4=质量档，5=自动（NVIDIA App 默认）
///   · _User_Global_VAL_TrueHDR          0=关，非 0=开
///
/// 为什么需要它：d3d11vpp 的 nvidia-true-hdr / nvidia-vsr 只是「向驱动请求效果」，
/// 驱动是否真的生效，还取决于 NVIDIA App 里这两个总开关。setting.ahk 早已按这个
/// 机制检测 VSR（IsNvidiaVSRAvailable），这里把同一套判据搬进设置程序。
///
/// 槽位不写死 0000/0001/0002——本机 5070 在 0000，但多显卡/多driver实例时显卡
/// 可能落在 0017 等更高槽位（见 nvidiaProfileInspector#396）。扫全部三位槽号，
/// 认 DriverDesc 含 NVIDIA 的第一块卡，避免误读集成显卡的残留键。
/// </summary>
public static class NvidiaAppState
{
    private const string ClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const string VsrKey = "_User_Global_VAL_SuperResolution";
    private const string HdrKey = "_User_Global_VAL_TrueHDR";

    /// <summary>检测到的 VSR 原始值；-1 = 没读到（无 NVIDIA 驱动 / 键缺失）。</summary>
    public static int ReadVsr()
    {
        int raw = -1;
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\ControlSet001\Control\Class\{ClassGuid}");
            if (root == null) return -1;
            foreach (string slot in root.GetSubKeyNames())
            {
                if (slot.Length != 4 || !int.TryParse(slot, out _)) continue;
                using var sub = root.OpenSubKey(slot);
                if (sub == null) continue;
                string desc = sub.GetValue("DriverDesc") as string ?? "";
                if (desc.IndexOf("NVIDIA", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                object val = sub.GetValue(VsrKey);
                if (val != null) { raw = ToInt(val); break; }
            }
        }
        catch { /* 读不到就当不支持，绝不阻止保存 */ }
        return raw;
    }

    /// <summary>检测到的 RTX Video HDR 原始值；-1 = 没读到。</summary>
    public static int ReadHdr()
    {
        int raw = -1;
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\ControlSet001\Control\Class\{ClassGuid}");
            if (root == null) return -1;
            foreach (string slot in root.GetSubKeyNames())
            {
                if (slot.Length != 4 || !int.TryParse(slot, out _)) continue;
                using var sub = root.OpenSubKey(slot);
                if (sub == null) continue;
                string desc = sub.GetValue("DriverDesc") as string ?? "";
                if (desc.IndexOf("NVIDIA", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                object val = sub.GetValue(HdrKey);
                if (val != null) { raw = ToInt(val); break; }
            }
        }
        catch { /* 同上 */ }
        return raw;
    }

    private static int ToInt(object v)
    {
        try { return System.Convert.ToInt32(v); }
        catch { return -1; }
    }
}

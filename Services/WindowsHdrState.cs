using System;
using System.Runtime.InteropServices;

namespace MpvSettings.Services;

/// <summary>
/// 直接检测 Windows HDR（显示器的「高级颜色 / HDR」开关）是否开启。
/// 判据逐行照搬 00.Core\setting.ahk 的 HdrWin* 函数组（来自 HdrSwitch.ahk，已在本机验证），
/// 不另发明一套：AHK 用 DllCall 直接摆字节，这里用等价的 P/Invoke 结构。
///
/// 两套 API 代际（AHR 的核心经验，别精简掉）：
///   · legacy（Win11 24H2 之前）：GET_ADVANCED_COLOR_INFO type=9，32 字节结构，
///     value bit0=支持 HDR、bit1=当前开启、bit3=策略强制禁用。
///   · modern（24H2 / build≥26100，且 type=15 探测到能力位）：36 字节结构，
///     value bit4=支持 HDR、bit3=策略禁用；是否开启不看 bit，看末尾 activeColorMode==2。
///   24H2 上旧 API 会「查询成功但值不对」，所以要按 build + 探测选代际，不能只用一种。
///
/// 枚举走 CCD：GetDisplayConfigBufferSizes + QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)，
/// 每条 PATH_INFO 的 targetInfo 在偏移 20（adapterId.low/high + id）。任一活动显示器
/// 支持且当前开启 HDR，即判定 Windows HDR 已开。
///
/// 用途：设置程序里给 RTX Video HDR 做「运行前提」提示 + 开关置灰。
/// 注意——Windows HDR 是 Win+Alt+B 随时可切的动态状态，这里只用于界面提示，
/// 绝不写进配置（见 WritePlanBuilder）。真正的运行时判据在 MyMPC_ui.lua：
/// 它按 mpv 协商出的 video-target-params/gamma=pq 判断，比这里的快照更准。
///
/// 检测失败（无显示器 / API 不可用 / 权限问题）一律返回 false，绝不抛异常。
/// </summary>
public static class WindowsHdrState
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    private const int ERROR_SUCCESS = 0;

    // legacy: DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO = 9
    private const int LEGACY_TYPE = 9;
    private const int LEGACY_SIZE = 32;
    // modern: 24H2 新结构（AHK 里 type=15 / 36 字节）
    private const int MODERN_TYPE = 15;
    private const int MODERN_SIZE = 36;

    private const uint LEGACY_SUPPORTED = 0x1;
    private const uint LEGACY_ENABLED = 0x2;
    private const uint FORCE_DISABLED = 0x8;
    private const uint MODERN_SUPPORTED = 0x10;

    private static string _apiMode;   // "modern" / "legacy"，进程内缓存，与 AHK 的 HdrWinApiMode 对应

    /// <summary>true = 至少一块活动显示器当前开启了 HDR。</summary>
    public static bool IsHdrEnabled()
    {
        try
        {
            foreach (var t in EnumTargets())
            {
                if (Query(t, out bool supported, out bool enabled)
                    && supported && enabled)
                    return true;
            }
        }
        catch { /* 检测失败当作未开启，交给界面提示，绝不阻止保存 */ }
        return false;
    }

    /// <summary>枚举当前活动、且支持 HDR 的显示 target（AHK HdrWinEnum）。</summary>
    private static System.Collections.Generic.List<Target> EnumTargets()
    {
        var list = new System.Collections.Generic.List<Target>();
        int rc = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS,
            out uint pathCount, out uint modeCount);
        if (rc != ERROR_SUCCESS || pathCount == 0) return list;

        var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
        // 第 6 参必须为 NULL，否则 QueryDisplayConfig 返回 87（AHK 实测踩过）
        rc = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS,
            ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
        if (rc != ERROR_SUCCESS) return list;

        for (uint i = 0; i < pathCount; i++)
        {
            var t = paths[i].targetInfo;   // PATH_INFO 里 targetInfo 偏移 20，与 AHK 一致
            if (Query(t, out bool supported, out bool enabled) && supported)
                list.Add(t);
        }
        return list;
    }

    /// <summary>按代际查询单块显示器：supported / enabled（AHK HdrWinQuery）。</summary>
    private static bool Query(Target t, out bool supported, out bool enabled)
    {
        supported = false;
        enabled = false;
        string mode = DetectApi(t);

        var hdr = new DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
        hdr.header.type = mode == "modern" ? MODERN_TYPE : LEGACY_TYPE;
        hdr.header.size = (uint)(mode == "modern" ? MODERN_SIZE : LEGACY_SIZE);
        hdr.header.adapterId = t.adapterId;
        hdr.header.id = t.id;

        // modern 结构上多 4 字节，用固定 36 字节缓冲保证末尾 activeColorMode 读得到
        byte[] buf = new byte[MODERN_SIZE];
        WriteHeader(buf, hdr);
        if (DisplayConfigGetDeviceInfo(buf) != ERROR_SUCCESS) return false;

        uint value = BitConverter.ToUInt32(buf, 20);
        if (mode == "modern")
        {
            if ((value & MODERN_SUPPORTED) != 0 && (value & FORCE_DISABLED) == 0) supported = true;
            enabled = BitConverter.ToInt32(buf, 32) == 2;   // activeColorMode==2 = HDR
        }
        else
        {
            if ((value & LEGACY_SUPPORTED) != 0 && (value & FORCE_DISABLED) == 0) supported = true;
            enabled = (value & LEGACY_ENABLED) != 0;
        }
        return true;
    }

    private static void WriteHeader(byte[] buf, DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO h)
    {
        BitConverter.GetBytes(h.header.type).CopyTo(buf, 0);
        BitConverter.GetBytes(h.header.size).CopyTo(buf, 4);
        BitConverter.GetBytes(h.header.adapterId.LowPart).CopyTo(buf, 8);
        BitConverter.GetBytes(h.header.adapterId.HighPart).CopyTo(buf, 12);
        BitConverter.GetBytes(h.header.id).CopyTo(buf, 16);
    }

    /// <summary>build≥26100 且 type=15 探测到能力位 → modern，否则 legacy（AHK HdrWinDetectApi）。</summary>
    private static string DetectApi(Target t)
    {
        if (_apiMode != null) return _apiMode;
        _apiMode = (GetBuildNumber() >= 26100 && ProbeModern(t)) ? "modern" : "legacy";
        return _apiMode;
    }

    private static bool ProbeModern(Target t)
    {
        byte[] buf = new byte[MODERN_SIZE];
        BitConverter.GetBytes(MODERN_TYPE).CopyTo(buf, 0);
        BitConverter.GetBytes((uint)MODERN_SIZE).CopyTo(buf, 4);
        BitConverter.GetBytes(t.adapterId.LowPart).CopyTo(buf, 8);
        BitConverter.GetBytes(t.adapterId.HighPart).CopyTo(buf, 12);
        BitConverter.GetBytes(t.id).CopyTo(buf, 16);
        if (DisplayConfigGetDeviceInfo(buf) != ERROR_SUCCESS) return false;
        return BitConverter.ToUInt32(buf, 20) != 0;   // 24H2 至少会填一个能力位
    }

    private static int GetBuildNumber()
    {
        try
        {
            byte[] osi = new byte[276];
            BitConverter.GetBytes((uint)osi.Length).CopyTo(osi, 0);
            if (RtlGetVersion(osi) == 0) return BitConverter.ToInt32(osi, 12);
        }
        catch { }
        return 0;
    }

    // ---------------- P/Invoke ----------------
    // 全部是 GET，不改变任何显示设置。用裸 byte[] 摆字节，与 AHK 的 NumPut/NumGet 逐字节等价，
    // 避免 24H2 两套结构在 Marshal.SizeOf 上的对齐歧义。

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Target
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public int outputTechnology;
        public int rotation;
        public int scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public int scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public Target targetInfo;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO { }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint value;
        public int colorEncoding;
        public int bitsPerColorChannel;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags,
        out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags,
        ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        IntPtr currentTopologyId);

    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
    private static extern int DisplayConfigGetDeviceInfo([In, Out] byte[] requestPacket);

    [DllImport("ntdll.dll")]
    private static extern int RtlGetVersion([In, Out] byte[] lpVersionInformation);
}

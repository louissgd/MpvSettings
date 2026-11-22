using System;
using System.Runtime.InteropServices;

namespace MpvSettings.Services;

/// <summary>
/// 外框圆角。WindowStyle=None + GlassFrameThickness=0 的自绘窗口没有系统边框，
/// Windows 11 不会替它切圆角，四角是直角。让 DWM 按系统样式裁圆（窗口内容一起裁），
/// 比自绘圆角干净：不用改 Grid 结构，最大化时 DWM 自己会取消圆角、不留缺口。
///
/// Windows 10 没有 DWMWA_WINDOW_CORNER_PREFERENCE：调用失败，窗口保持直角，功能不受影响。
/// </summary>
public static class WindowCorners
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;          // 系统标准圆角（约 8px）
    private const int DWMWCP_ROUNDSMALL = 3;     // 小圆角（约 4px）

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>给窗口套上系统圆角。small=true 用更小的半径（小窗口更好看）。</summary>
    public static void Apply(IntPtr hwnd, bool small = false)
    {
        if (hwnd == IntPtr.Zero) return;
        int pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch (Exception)
        {
            // 老系统 / 精简版没有这个入口：丢掉异常，直角也能用
        }
    }
}

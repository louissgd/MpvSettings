using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MpvSettings.Models;

namespace MpvSettings.Views;

public partial class SubtitleStyleWindow : Window
{
    private readonly SettingsDraft _draft;

    // 打开时快照：点「取消 / ✕」要把草稿还原，避免弹窗里的试探性改动混进主界面写盘计划。
    private readonly string _snapFont, _snapColor;
    private readonly double _snapSize, _snapSpacing, _snapOutline, _snapShadow, _snapMarginX, _snapMarginY;
    private readonly bool _snapBold, _snapItalic, _snapProtect;

    public SubtitleStyleWindow(SettingsDraft draft)
    {
        InitializeComponent();
        _draft = draft ?? throw new ArgumentNullException(nameof(draft));
        DataContext = draft;

        _snapFont = draft.SubFont;
        _snapColor = draft.SubColor;
        _snapSize = draft.SubFontSize;
        _snapSpacing = draft.SubLineSpacing;
        _snapBold = draft.SubBold;
        _snapItalic = draft.SubItalic;
        _snapOutline = draft.SubOutlineSize;
        _snapShadow = draft.SubShadowOffset;
        _snapMarginX = draft.SubMarginX;
        _snapMarginY = draft.SubMarginY;
        _snapProtect = draft.SubProtectAss;

        UpdateColorPreview(draft.SubColor);
    }

    // ---- 系统字体对话框（Win32 ChooseFont，不引 WinForms 依赖）----
    // 字体名 / 字形(粗体+斜体) / 大小都从对话框取；字符集用 CF_NOSCRIPTSEL 直接隐藏（mpv 无对应选项）。
    // 大小换算（1.0.5 修正）：对话框的 point size 按「ASS 脚本 PlayResY=1080 的字号」理解，
    //   mpv sub-font-size 是 PlayResY=720 基准的缩放像素（源码 ass_mp.c:52 scale=res_y/720，
    //   mpv_options.rst:3097 确认单位是 720p 缩放像素）。两者都是相对视频/窗口高度的比例，
    //   跟屏幕 DPI 无关——之前用 96/72 是套错了坐标系。
    //   用户实测 ASS：PlayResY=1080 / STKaiTi 72pt。等效 720p 字号 = 72 * 720/1080 = 48。
    //   故 mpv值 = pt * 720/1080 = pt * 2/3；回显 pt = mpv值 * 1.5（48 → 72pt，正好对上 ASS）。
    //   华文楷体习惯 52 → 对话框填 78pt；微软雅黑习惯 48 → 填 72pt。
    private const int CF_SCREENFONTS = 0x00000001;
    private const int CF_INITTOLOGFONTSTRUCT = 0x00000040;
    private const int CF_NOSCRIPTSEL = 0x00800000;   // 隐藏字符集下拉（mpv 没有这个选项）

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet;
        public byte lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string lfFaceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CHOOSEFONT
    {
        public int lStructSize;
        public IntPtr hwndOwner, hDC, lpLogFont;
        public int iPointSize, Flags;
        public uint rgbColors;
        public IntPtr lCustData, lpfnHook, lpTemplateName, hInstance, lpszStyle;
        public short nFontType, missingAlignment;
        public int nSizeMin, nSizeMax;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChooseFont(ref CHOOSEFONT lpcf);

    private void PickFont_Click(object sender, RoutedEventArgs e)
    {
        string current = string.IsNullOrWhiteSpace(_draft.SubFont) ? "Microsoft YaHei" : _draft.SubFont;   // 静态兜底；--default 的楷体探测在 DefaultProfile 里
        var lf = new LOGFONT
        {
            lfFaceName = current,
            lfWeight = _draft.SubBold ? 700 : 400,   // FW_BOLD / FW_NORMAL：字形栏回显粗体
            lfItalic = _draft.SubItalic ? (byte)1 : (byte)0,
            lfCharSet = 1,                           // DEFAULT_CHARSET（下拉已隐藏）
            // 回显：想让对话框显示 pt = mpv值*1.5（ASS 1080p 基准），而 ChooseFont 按 pt=|H|*72/96 显示，
            // 故 |H| = mpv值*1.5*96/72 = mpv值*2。例：mpv 48 → H=-96 → 对话框显示 72pt。
            lfHeight = -(int)Math.Round(_draft.SubFontSize * 2.0),
        };
        IntPtr lfPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(LOGFONT)));
        try
        {
            Marshal.StructureToPtr(lf, lfPtr, false);
            var cf = new CHOOSEFONT
            {
                lStructSize = Marshal.SizeOf(typeof(CHOOSEFONT)),
                hwndOwner = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                lpLogFont = lfPtr,
                Flags = CF_SCREENFONTS | CF_INITTOLOGFONTSTRUCT | CF_NOSCRIPTSEL,
            };
            if (!ChooseFont(ref cf)) return; // 取消：草稿不动

            var picked = (LOGFONT)Marshal.PtrToStructure(lfPtr, typeof(LOGFONT));
            if (!string.IsNullOrWhiteSpace(picked.lfFaceName))
                _draft.SubFont = picked.lfFaceName;
            _draft.SubBold = picked.lfWeight >= 600;              // FW_BOLD
            _draft.SubItalic = picked.lfItalic != 0;
            // iPointSize 是 1/10 pt。按 ASS PlayResY=1080 解读，换算到 mpv 的 PlayResY=720：
            //   mpv值 = pt * 720/1080 = pt * 2/3。例：72pt → 48；78pt → 52。
            if (cf.iPointSize > 0)
                _draft.SubFontSize = Math.Max(8, Math.Min(200,
                    Math.Round(cf.iPointSize / 10.0 * 720.0 / 1080.0, 1)));
        }
        finally
        {
            Marshal.FreeHGlobal(lfPtr);
        }
    }

    private void ColorBox_TextChanged(object sender, TextChangedEventArgs e)
        => UpdateColorPreview(ColorBox.Text);

    // ---- 系统颜色对话框（Win32 ChooseColor，同样不引 WinForms）----
    // 色块点击弹出原生调色板。mpv 要 #AARRGGBB，Win32 COLORREF 只有 0x00BBGGRR（无 alpha），
    // 所以保留当前 AA，只替换 RGB。需要改透明度时在右侧文本框手输。
    private const int CC_RGBINIT = 0x00000001;
    private const int CC_FULLOPEN = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CHOOSECOLOR
    {
        public int lStructSize;
        public IntPtr hwndOwner, hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public int Flags;
        public IntPtr lCustData, lpfnHook, lpTemplateName;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ChooseColor(ref CHOOSECOLOR lpcc);

    private void ColorPreview_Click(object sender, MouseButtonEventArgs e)
    {
        // 当前色：优先取 ColorBox 文本框，解析不了回落到草稿值，再回落灰。
        Color current;
        try { current = (Color)ColorConverter.ConvertFromString(ColorBox.Text); }
        catch (Exception)
        {
            try { current = (Color)ColorConverter.ConvertFromString(_draft.SubColor); }
            catch (Exception) { current = Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A); }
        }
        // COLORREF = 0x00BBGGRR
        int rgb = current.R | (current.G << 8) | (current.B << 16);

        // ChooseColor 要求 lpCustColors 指向 16 个 DWORD 的可写缓冲。
        IntPtr cust = Marshal.AllocHGlobal(16 * sizeof(int));
        try
        {
            for (int i = 0; i < 16; i++) Marshal.WriteInt32(cust, i * sizeof(int), 0);
            var cc = new CHOOSECOLOR
            {
                lStructSize = Marshal.SizeOf(typeof(CHOOSECOLOR)),
                hwndOwner = new System.Windows.Interop.WindowInteropHelper(this).Handle,
                rgbResult = rgb,
                lpCustColors = cust,
                Flags = CC_RGBINIT | CC_FULLOPEN,   // 回显当前色 + 展开自定义颜色区
            };
            if (!ChooseColor(ref cc)) return;       // 取消：草稿不动

            int r = cc.rgbResult & 0xFF;
            int g = (cc.rgbResult >> 8) & 0xFF;
            int b = (cc.rgbResult >> 16) & 0xFF;
            // AA 保留原值（Win32 调色板无 alpha 概念，默认 FF=不透明）
            string hex = $"#{current.A:X2}{r:X2}{g:X2}{b:X2}";
            _draft.SubColor = hex;
            ColorBox.Text = hex;                    // TwoWay 绑定 + 触发 TextChanged → 色块刷新
        }
        finally
        {
            Marshal.FreeHGlobal(cust);
        }
    }

    private void UpdateColorPreview(string text)
    {
        if (ColorPreview is null) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(text);
            ColorPreview.Background = new SolidColorBrush(c);
        }
        catch (Exception)
        {
            // 手输到一半 / 非 #AARRGGBB（如 mpv 的 r/g/b 写值）：保留上次色块，不崩窗。
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // 1.0.5：「完成」直接写盘，不用再回主界面点「应用/确定」。
        // 只写「字幕默认样式（仅 SRT）」这一组键，主界面其他页的未保存改动不受影响。
        if (Application.Current.MainWindow is MpvSettings.MainWindow main)
            main.ApplySubtitleGroup(this);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _draft.SubFont = _snapFont;
        _draft.SubColor = _snapColor;
        _draft.SubFontSize = _snapSize;
        _draft.SubLineSpacing = _snapSpacing;
        _draft.SubBold = _snapBold;
        _draft.SubItalic = _snapItalic;
        _draft.SubOutlineSize = _snapOutline;
        _draft.SubShadowOffset = _snapShadow;
        _draft.SubMarginX = _snapMarginX;
        _draft.SubMarginY = _snapMarginY;
        _draft.SubProtectAss = _snapProtect;
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (e.ClickCount == 2) { WindowState = WindowState.Normal; return; }
            DragMove();
        }
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        // DWM 小圆角，跟主窗 / 提示窗一致。
        IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int value = 2;
        DwmSetWindowAttribute(hwnd, 33, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}

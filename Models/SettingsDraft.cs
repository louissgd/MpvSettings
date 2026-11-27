using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MpvSettings.Services;

namespace MpvSettings.Models;

/// <summary>INotifyPropertyChanged 基类：所有控件 TwoWay 绑定到草稿，编辑不落到磁盘。</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>
/// 阶段 ②：窗口的所有可编辑值都在这里，打开时由 ConfigSnapshot 反推一次。
/// 「确定 / 写盘」是阶段 ③ 的事，本阶段任何操作都不碰文件。
///
/// 反推总则：
///   · 键缺失 → 本包出厂默认（不是 mpv 原生默认）
///   · 下拉值不在预设内 → 追加「自定义」项、选中它、控件置灰（禁止写入）
///   · 组合键（HDR 模式 / 色调档位）必须整组精确命中，否则一律自定义
///   · EXE 独占文件（cover-art.conf / MyMPC.conf / audio-info.conf）尚未创建时，
///     只有显式写了「关闭」才算关闭，否则按出厂默认展示，include 行由后续阶段补回
/// </summary>
public sealed class SettingsDraft : ObservableObject
{
    // ---------------- 下拉选项（顺序 = 写盘值 / 索引） ----------------
    // internal：阶段 ③ 的 WritePlanBuilder 把下拉索引映射回写盘值时复用同一份表，
    // 避免反推与写入各存一份导致漂移。
    internal static readonly string[] HwdecValues = { "auto-copy", "auto-safe", "auto", "d3d11va", "nvdec", "no" };
    // 2026-11-24：默认改 d3d11va 零拷贝。auto-copy 读回显存再上传，8K60 VP9 实测 8 秒丢 363 帧；
    // 杜比视界不能走零拷贝（P5 黑屏），由 portable_config/dolby-vision.conf 的 auto-profile 单独回退。
    internal static readonly string[] HwdecLabels =
        { "auto-copy（杜比视界兼容）", "auto-safe（零拷贝）", "auto", "d3d11va（推荐·零拷贝）", "nvdec", "no（纯软解）" };
    internal static readonly string[] HdrModeLabels = { "直通显示器（默认）", "HDR 转 SDR" };
    // 2026-11-19 定稿：去掉 0.5 档，0.6=纯 inverse(con0)。峰值 2000nits / DisplayHDR 1400 实测、HDR10/DV 通用。
    internal static readonly string[] ToneLabels = { "关闭（默认）", "0.6", "0.8", "1.0" };
    internal static readonly string[] LevelsValues = { "full", "limited" };
    internal static readonly string[] LevelsLabels = { "PC 0–255（默认）", "TV 16–235" };
    internal static readonly string[] ChannelsValues = { "auto", "auto-safe", "stereo", "mono" };
    internal static readonly string[] ChannelsLabels = { "auto（默认）", "auto-safe", "stereo", "mono" };
    // 只保留 jpg / png：WebP、AVIF、JXL 都依赖 libwebp / libaom / libsvtav1 / libjxl
    // 这些外部编码库，2026-11-16 起 mpv 构建里已全部裁掉（只当播放器用）。
    // 盘上还留着旧值时按 jpg 显示，保存时由 WritePlanBuilder 纠回 jpg。
    internal static readonly string[] FormatValues = { "jpg", "png" };
    internal static readonly string[] FormatLabels = { "JPG（默认）", "PNG" };

    internal static bool IsFormatSupported(string value)
    {
        return Array.FindIndex(FormatValues,
            v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)) >= 0;
    }
    internal static readonly string[] ShotDirLabels = { "桌面", "图片文件夹", "mpv 目录", "自填路径" };

    // ---------------- NVIDIA RTX 视频增强（1.0.2 新增） ----------------
    // 底层是 D3D11 视频处理器扩展 GUID，和 FrankyRenderer / MPC Video Renderer
    // 调的是同一个驱动接口；mpv 0.40+ 通过 vf=d3d11vpp 暴露它。
    // mpv 原生 d3d11vpp 不做「分辨率上限」判断，所以开关与档位写进
    // script-opts/MyMPC.conf，由 MyMPC_ui.lua 按片源分辨率 / SDR 状态动态挂卸 vf。
    // 档位阈值照抄 MPCVR 的 CD3D11VP::SetSuperRes()，长边/短边归一化：
    //   sd = 1024×576，720p = 1280×720，1080p = 2048×1088，1440p = 2560×1440
    internal static readonly string[] RtxVsrLimitValues = { "sd", "720p", "1080p", "1440p" };
    internal static readonly string[] RtxVsrLimitLabels =
    {
        "≤ 480p（1024×576）", "≤ 720p（1280×720）",
        "≤ 1080p（2048×1088）", "≤ 1440p（2560×1440）",
    };
    internal static readonly int[] RtxHdrPeaks = { 400, 600, 800, 1000, 1200, 1400, 1600, 2000 };
    internal static readonly string[] RtxHdrPeakLabels =
    {
        "400 nit", "600 nit", "800 nit", "1000 nit（NVIDIA 默认）",
        "1200 nit", "1400 nit", "1600 nit", "2000 nit",
    };

    // ---------------- 页 A · 播放 ----------------
    private int _hwdecIndex; private bool _hwdecCustom; private string[] _hwdecOptions = HwdecLabels;
    private bool _deinterlace = true, _loopFile = true, _savePosition, _subsMatchOsLanguage = true, _subsFallbackForced;
    // 外挂字幕改动后自动重读（script-opts/MyMPC.conf: sub_autoreload）。
    // 本包出厂默认开；mpv 原生不监视字幕文件，靠 MyMPC_ui.lua 轮询 mtime+size 触发 sub-reload。
    private bool _subAutoReload = true;
    // 顶部细横线进度条（script-opts/MyMPC.conf: osd_progress，2026-11-23 新增）。
    // 移植自 mpv-lazy/uosc 的 progress 视觉：窗口与全屏都显示一条贴顶细线 + 移动游标。
    // 默认开（本包出厂值）；缺键时按开处理，写盘显式补键。纯音频无封面时跳过（信息窗自带进度条）。
    private bool _osdProgress = true;
    // 1.0.8 起封面口径（对齐 MPC-HC）：
    //   打开音频 → 窗口按封面原始尺寸（100%）打开，超过「封面窗口上限」才缩
    //   拖动边框 → 封面随窗口等比缩放（video-unscaled=no），只保比例、不锁窗口比例
    //   倍率键   → 封面状态下全部屏蔽（见 scripts/fit90.lua 的 scale_key）
    //   mpv 无窗口最小尺寸属性，故不设下限，可拖到 0×0（与 MPC-HC 一致）
    // 所以「封面原始大小」默认开启；关闭时窗口不再按封面尺寸自适应（走 mpv 全局默认）。
    private bool _coverOriginal = true; private double _coverMax = 1200;
    private bool _noArtUi = true; private double _fitRatio = 90; private bool _allowMultiInstance;

    // ---------------- 页 A · 字幕默认样式（弹窗「字幕样式」，只作用于 SRT 等纯文本） ----------------
    // 底层是 mpv 的 --sub-*：对 converted 字幕（SRT→ASS）生效；
    // --sub-ass-override=no 时 ASS/SSA 与 SUP 位图字幕完全不受影响。
    // 出厂静态默认 = 微软雅黑 / 48 / 纯白：与 mpv.conf 模板一致，任何机器都能用。
    // 华文楷体 52 是「按本机字体探测后的升级默认」，只在 --default（安装时执行）里
    // 由 DefaultProfile.DefaultDraft() 探测到 STKaiti 后替换这两个值；field 初值本身
    // 保持静态，避免 GUI 打开与盘上模板不一致（见 1.0.5 记录 1.8 节）。
    // 字号单位：720p 缩放像素；48 = ASS 72pt@PlayResY1080 的等效（72×720/1080）。
    private string _subFont = "Microsoft YaHei";
    private string _subColor = "#FFFFFFFF";
    private double _subFontSize = 48;
    private double _subLineSpacing = 0;
    private bool _subBold;             // true → sub-bold=yes
    private bool _subItalic;           // true → sub-italic=yes（字体对话框字形栏的「倾斜 / 粗倾斜」）
    private double _subOutlineSize = 1;   // ASS 常用 1；mpv 自带 1.65 偏粗，这里按作者习惯收细
    private double _subShadowOffset = 0;
    private double _subMarginX = 19;
    private double _subMarginY = 34;
    private bool _subProtectAss = true;   // true → sub-ass-override=no，锁回 ASS 作者样式

    // ---------------- 页 B · 画面 ----------------
    private int _hdrModeIndex; private bool _hdrModeCustom; private string[] _hdrModeOptions = HdrModeLabels;
    // 由 hdr-autoswitch.conf 的 mode 原始值派生：tonemap = HDR转SDR。
    // 该态下色调修复必须置灰 + 显示关闭 + 配置真关闭（2026-11-17 用户拍板）。
    private string _hdrRawMode = "passthrough";
    private int _toneIndex; private bool _toneCustom; private string[] _toneOptions = ToneLabels;
    private int _videoLevelsIndex; private bool _videoLevelsCustom; private string[] _videoLevelsOptions = LevelsLabels;
    private bool _onlyFullscreen;

    // HDR 能力（只读环境快照，不参与 diff / 写盘）。无 HDR 显示器时：
    //   · HDR 模式显示成「HDR转SDR」（盘上原始值不动，lua 启动自己降级）
    //   · HDR 模式 / 色调档位 / 仅全屏切换三个控件置灰
    // 与 setting.ahk 的 HdrSupported + MpvRefresh() 同一套行为。
    private bool _hdrSupported = true;

    // 拖动窗口边框时是否锁住窗口宽高比。
    // false = 窗口自由拉伸、画面按视频比例居中补黑边（本包默认，跟主流播放器一致）；
    // true = mpv 原生行为，窗口被强制保持视频比例，永远没有黑边。
    private bool _keepWindowAspect;

    // NVIDIA RTX 视频增强（1.0.2）：都写 script-opts/MyMPC.conf，由 MyMPC_ui.lua 执行。
    // VSR 默认开、上限 1080p（MPCVR 同款）；RTX HDR 默认关，因为它要求 Windows HDR 开启，
    // 在 SDR 桌面上强开会把 SDR 内容按 PQ 输出导致画面洗白（mpv#17800）。
    private bool _rtxVsr = true, _rtxHdr = true;
    private int _rtxVsrLimitIndex = 2;   // 1080p
    private int _rtxHdrPeakIndex = 3;    // 1000 nit

    // ---------------- 页 C · 音频 ----------------
    private bool _afEnabled = true, _bassMaxEnabled = true;

    // 自动加载同目录同名外部音频。本包出厂默认「开」（写 audio-file-auto=exact），
    // 与 mpv 原生默认 no 相反：缺键时按本包默认显示开，写盘时显式补键，避免界面骗人。
    // 「视频自带音频优先」由 MyMPC_ui.lua 在 file-loaded 把 aid 拨回内嵌轨实现。
    private bool _externalAudioAuto = true;

    // 音量上限 100%（mpv.conf: volume-max）。默认打开：音效驱动 1.0 自身增益已经足够，
    // 再叠超过 100% 的软件音量只会削波破音，所以出厂就把上限压在 100%。
    private bool _volumeCap100 = true;

    private double _bass = 2.0, _treble = 2.0, _drive = 1.0;
    private int _presetIndex = 4;
    private int _audioChannelsIndex; private bool _audioChannelsCustom;
    private string[] _audioChannelsOptions = ChannelsLabels;

    // ---------------- 页 D · 截图 ----------------
    private int _shotFormatIndex; private bool _shotFormatCustom; private string[] _shotFormatOptions = FormatLabels;
    private int _shotDirIndex; private string _shotDirPath = string.Empty;
    private bool _highBitDepth;

    // n*m 缩略图（1.0.7）：写 script-opts/thumbnails.conf，由 thumbnails.lua 执行。
    // 总宽上限反推单格宽、按视频显示比例算高，对齐 MPC-HC 的缩略图行为。
    private int _thumbCols = 3, _thumbRows = 3;
    private double _thumbMaxWidth = 1200;
    private int _thumbGap = 4;

    // ---------------- 页 E · 快捷键（阶段 ⑥，独立草稿树） ----------------
    public ShortcutDraft Shortcuts { get; } = new();

    // ---------------- 只读状态（由快照计算，不参与重置） ----------------
    private bool _spdifActive;
    private string _spdifText = "—";
    private string _bassMaxRegStatus = "—";
    private bool _bassMaxPanelAvailable;
    private string _configSourceText = "—";
    private string _mpvExeText = "—";
    private string _snapshotText = "—";
    private string _coverConfStatus = "—";
    private string _noArtConfStatus = "—";
    private string _scriptConfStatus = "—";

    public SettingsDraft() { }

    #region 属性（页 A）
    public int HwdecIndex
    {
        get => _hwdecIndex;
        set
        {
            if (!Set(ref _hwdecIndex, value)) return;
            OnPropertyChanged(nameof(RtxHwdecHint));
        }
    }
    public bool HwdecCustom { get => _hwdecCustom; set => Set(ref _hwdecCustom, value); }
    public string[] HwdecOptions { get => _hwdecOptions; set => Set(ref _hwdecOptions, value); }

    public bool Deinterlace { get => _deinterlace; set => Set(ref _deinterlace, value); }
    public bool LoopFile { get => _loopFile; set => Set(ref _loopFile, value); }
    public bool SavePosition { get => _savePosition; set => Set(ref _savePosition, value); }
    public bool SubsMatchOsLanguage { get => _subsMatchOsLanguage; set => Set(ref _subsMatchOsLanguage, value); }
    public bool SubsFallbackForced { get => _subsFallbackForced; set => Set(ref _subsFallbackForced, value); }
    /// <summary>外挂字幕文件被修改后自动重读。script-opts/MyMPC.conf: sub_autoreload，默认开。</summary>
    public bool SubAutoReload { get => _subAutoReload; set => Set(ref _subAutoReload, value); }
    /// <summary>顶部细横线进度条。script-opts/MyMPC.conf: osd_progress，默认开。</summary>
    public bool OsdProgress { get => _osdProgress; set => Set(ref _osdProgress, value); }
    public bool CoverOriginal { get => _coverOriginal; set => Set(ref _coverOriginal, value); }
    public double CoverMax { get => _coverMax; set => Set(ref _coverMax, value); }
    public bool NoArtUi { get => _noArtUi; set => Set(ref _noArtUi, value); }
    public double FitRatio { get => _fitRatio; set => Set(ref _fitRatio, value); }
    public bool AllowMultiInstance { get => _allowMultiInstance; set => Set(ref _allowMultiInstance, value); }

    // 字幕默认样式（弹窗页；写 mpv.conf 的 --sub-*）
    public string SubFont { get => _subFont; set => Set(ref _subFont, value); }
    public string SubColor { get => _subColor; set => Set(ref _subColor, value); }
    public double SubFontSize { get => _subFontSize; set => Set(ref _subFontSize, value); }
    public double SubLineSpacing { get => _subLineSpacing; set => Set(ref _subLineSpacing, value); }
    public bool SubBold { get => _subBold; set => Set(ref _subBold, value); }
    public bool SubItalic { get => _subItalic; set => Set(ref _subItalic, value); }
    public double SubOutlineSize { get => _subOutlineSize; set => Set(ref _subOutlineSize, value); }
    public double SubShadowOffset { get => _subShadowOffset; set => Set(ref _subShadowOffset, value); }
    public double SubMarginX { get => _subMarginX; set => Set(ref _subMarginX, value); }
    public double SubMarginY { get => _subMarginY; set => Set(ref _subMarginY, value); }
    public bool SubProtectAss { get => _subProtectAss; set => Set(ref _subProtectAss, value); }
    #endregion

    #region 属性（页 B）
    public int HdrModeIndex
    {
        get => _hdrModeIndex;
        set
        {
            if (!Set(ref _hdrModeIndex, value)) return;
            OnPropertyChanged(nameof(ToneLockedBySdr));
            OnPropertyChanged(nameof(ToneInteractive));
            // 切到 HDR转SDR 时，界面上的色调增强档位强制归「关闭」
            if (_hdrModeIndex == 1) ToneIndex = 0;
        }
    }
    public bool HdrModeCustom { get => _hdrModeCustom; set => Set(ref _hdrModeCustom, value); }
    public string[] HdrModeOptions { get => _hdrModeOptions; set => Set(ref _hdrModeOptions, value); }

    /// <summary>
    /// HDR 能力（只读环境快照）。false = 没有支持 HDR 的活动显示器（或检测失败）。
    /// 此时三个控件置灰、HDR 模式显示「HDR转SDR」，与 setting.ahk 的 HdrSupported 一致。
    /// 不参与 diff / 写盘。
    /// </summary>
    public bool HdrSupported
    {
        get => _hdrSupported;
        set
        {
            if (!Set(ref _hdrSupported, value)) return;
            OnPropertyChanged(nameof(HdrModeDisplayIndex));
            OnPropertyChanged(nameof(HdrModeInteractive));
            OnPropertyChanged(nameof(ToneLockedBySdr));
            OnPropertyChanged(nameof(ToneInteractive));
            OnPropertyChanged(nameof(OnlyFullscreenInteractive));
        }
    }

    /// <summary>
    /// HDR 模式下拉的「显示层」索引。无 HDR 显示器时固定返回 1（HDR转SDR），
    /// 与 AHK 的 mModeOn 覆盖一致；底层 _hdrModeIndex 保持盘上真实值，写盘不受影响
    /// （lua 启动自己按能力降级，GUI 只负责显示得与运行时一致）。
    /// 该态下控件已置灰，setter 理论上不会触发；真触发也直接吞掉，绝不改底层值。
    /// </summary>
    public int HdrModeDisplayIndex
    {
        get => _hdrSupported ? _hdrModeIndex : 1;
        set { if (_hdrSupported) HdrModeIndex = value; }
    }

    /// <summary>HDR 模式下拉是否可交互：无 HDR 显示器或自定义态时置灰。</summary>
    public bool HdrModeInteractive => _hdrSupported && !_hdrModeCustom;

    /// <summary>true = 应显示/写「转 SDR」：无 HDR 显示器、或 HDR 模式为转 SDR。
    /// 此时色调修复档位置灰、显示关闭、配置强制清零（AHK 的 tonemap 分支同行）。</summary>
    public bool ToneLockedBySdr => !_hdrSupported
        || _hdrModeIndex == 1
        || (_hdrModeCustom && string.Equals(_hdrRawMode, "tonemap", StringComparison.OrdinalIgnoreCase));

    /// <summary>色调档位下拉是否可交互：无 HDR 显示器、自定义态、HDR转SDR 时置灰。</summary>
    public bool ToneInteractive => _hdrSupported && !_toneCustom && !ToneLockedBySdr;

    public int ToneIndex { get => _toneIndex; set => Set(ref _toneIndex, value); }
    public bool ToneCustom
    {
        get => _toneCustom;
        set { if (Set(ref _toneCustom, value)) OnPropertyChanged(nameof(ToneInteractive)); }
    }
    public string[] ToneOptions { get => _toneOptions; set => Set(ref _toneOptions, value); }

    public int VideoLevelsIndex { get => _videoLevelsIndex; set => Set(ref _videoLevelsIndex, value); }
    public bool VideoLevelsCustom { get => _videoLevelsCustom; set => Set(ref _videoLevelsCustom, value); }
    public string[] VideoLevelsOptions { get => _videoLevelsOptions; set => Set(ref _videoLevelsOptions, value); }
    public bool OnlyFullscreen { get => _onlyFullscreen; set => Set(ref _onlyFullscreen, value); }

    /// <summary>仅全屏切换 HDR 是否可交互：无 HDR 显示器时无意义，置灰。</summary>
    public bool OnlyFullscreenInteractive => _hdrSupported;

    /// <summary>
    /// mpv.conf: keepaspect-window。true = 拖动边框时窗口保持视频宽高比（mpv 出厂默认）；
    /// false = 窗口自由拉伸，画面按视频比例居中、两侧或上下补黑边。
    /// 注意它只管「窗口矩形」，画面比例由 --keepaspect 另行控制，两者是两件事。
    /// </summary>
    public bool KeepWindowAspect { get => _keepWindowAspect; set => Set(ref _keepWindowAspect, value); }

    /// <summary>NVIDIA RTX 超分辨率：默认开，上限由 RtxVsrLimitIndex 决定。</summary>
    public bool RtxVsr
    {
        get => _rtxVsr;
        set { if (Set(ref _rtxVsr, value)) OnPropertyChanged(nameof(RtxVsrLimitInteractive)); }
    }
    /// <summary>RTX Video HDR（SDR→HDR）：默认开。NVIDIA 驱动只对 SDR 源生效，HDR 源一律跳过，可放心开；仍需 Windows HDR + NVIDIA App 侧启用。</summary>
    public bool RtxHdr
    {
        get => _rtxHdr;
        set { if (Set(ref _rtxHdr, value)) OnPropertyChanged(nameof(RtxHdrPeakInteractive)); }
    }
    public int RtxVsrLimitIndex { get => _rtxVsrLimitIndex; set => Set(ref _rtxVsrLimitIndex, value); }
    public string[] RtxVsrLimitOptions => RtxVsrLimitLabels;
    public int RtxHdrPeakIndex { get => _rtxHdrPeakIndex; set => Set(ref _rtxHdrPeakIndex, value); }
    public string[] RtxHdrPeakOptions => RtxHdrPeakLabels;

    // NVIDIA App 侧两个硬开关的实际值（-1 = 未读到）。只读，不参与 diff / 写盘。
    private int _nvVsrAppRaw = -1, _nvHdrAppRaw = -1;
    // Windows HDR 是 Win+Alt+B 随时可切的动态状态（CCD API 直读，非注册表推测）。
    // 只用于界面置灰 + 提示；绝不写进配置（详见 WritePlanBuilder 的强制写 no 判据）。
    private bool _windowsHdrEnabled;

    private void NotifyRtxState()
    {
        OnPropertyChanged(nameof(NvidiaAppStateText));
        OnPropertyChanged(nameof(RtxVsrEligible));
        OnPropertyChanged(nameof(RtxHdrEligible));
        OnPropertyChanged(nameof(RtxVsrInteractive));
        OnPropertyChanged(nameof(RtxHdrInteractive));
        OnPropertyChanged(nameof(RtxVsrLimitInteractive));
        OnPropertyChanged(nameof(RtxHdrPeakInteractive));
        OnPropertyChanged(nameof(RtxVsrStatusText));
        OnPropertyChanged(nameof(RtxHdrStatusText));
    }

    /// <summary>NVIDIA App「超分辨率」总开关的原始值：0=关，1..4=质量档，5=自动，-1=未检测到。</summary>
    public int NvVsrAppRaw
    {
        get => _nvVsrAppRaw;
        set { if (Set(ref _nvVsrAppRaw, value)) NotifyRtxState(); }
    }
    /// <summary>NVIDIA App「RTX Video HDR」总开关的原始值：0=关，非0=开，-1=未检测到。</summary>
    public int NvHdrAppRaw
    {
        get => _nvHdrAppRaw;
        set { if (Set(ref _nvHdrAppRaw, value)) NotifyRtxState(); }
    }
    /// <summary>Windows HDR 当前是否开启（CCD API 直读；只读，不参与 diff / 写盘）。</summary>
    public bool WindowsHdrEnabled
    {
        get => _windowsHdrEnabled;
        set { if (Set(ref _windowsHdrEnabled, value)) NotifyRtxState(); }
    }

    // ---- RTX 自动禁用判据（2026-11-18 用户拍板）----
    // 硬件不支持（raw<0 = 读不到 NVIDIA 驱动注册表）或 NVIDIA App 侧总开关关闭（raw=0）
    // 都判为不合格：这类前提不会自己变好，必须去 NVIDIA App / 装驱动，所以写盘时强制关。
    // Windows HDR 是动态状态（Win+Alt+B 即切），不参与「写 no」——只置灰 + 提示，
    // 配置保留 yes，等用户开了 HDR，Lua 按 video-target-params 自动生效，不必回来重设。
    /// <summary>VSR 前提是否满足：读到 NVIDIA 驱动且 NVIDIA App 超分辨率总开关 ≥ 1。</summary>
    public bool RtxVsrEligible => _nvVsrAppRaw >= 1;
    /// <summary>RTX HDR 前提是否满足：读到 NVIDIA 驱动且 NVIDIA App TrueHDR 总开关 ≥ 1。</summary>
    public bool RtxHdrEligible => _nvHdrAppRaw >= 1;

    /// <summary>VSR 开关是否可交互：硬件 / NVIDIA App 前提不满足就置灰。</summary>
    public bool RtxVsrInteractive => RtxVsrEligible;
    /// <summary>RTX HDR 开关是否可交互：NVIDIA App 前提 + Windows HDR 两者都要满足。</summary>
    public bool RtxHdrInteractive => RtxHdrEligible && _windowsHdrEnabled;

    /// <summary>VSR 上限下拉是否可交互：开关开着且前提满足。</summary>
    public bool RtxVsrLimitInteractive => RtxVsr && RtxVsrEligible;
    /// <summary>RTX HDR 峰值下拉是否可交互：开关开着且前提满足。</summary>
    public bool RtxHdrPeakInteractive => RtxHdr && RtxHdrInteractive;

    /// <summary>写盘时的 VSR 有效值：草稿开着且前提满足才算开（WritePlanBuilder 与 BuildMyMpcConf 共用）。</summary>
    public bool RtxVsrEnabledForWrite => RtxVsr && RtxVsrEligible;
    /// <summary>写盘时的 RTX HDR 有效值：草稿开着且前提满足才算开。Windows HDR 不参与。</summary>
    public bool RtxHdrEnabledForWrite => RtxHdr && RtxHdrEligible;

    /// <summary>VSR 项的实时状态说明（置灰原因写在这里，不用弹窗）。</summary>
    public string RtxVsrStatusText
    {
        get
        {
            if (_nvVsrAppRaw < 0)
                return "已自动禁用：未检测到 NVIDIA 驱动注册表开关（硬件不支持 / 未装 NVIDIA App）。保存时写入 rtx_vsr=no，恢复前请先在 NVIDIA App 开启。";
            if (_nvVsrAppRaw == 0)
                return "已自动禁用：NVIDIA App 侧「超分辨率」总开关为关。保存时写入 rtx_vsr=no；到 NVIDIA App → 视频里打开后重开本程序即可恢复。";
            return "前提满足：NVIDIA App「超分辨率」总开关为开"
                   + (_nvVsrAppRaw == 5 ? "（自动质量）" : "（质量 " + _nvVsrAppRaw + "）")
                   + "。是否生效仍看片源分辨率上限与窗口放大倍率。";
        }
    }

    /// <summary>RTX HDR 项的实时状态说明：区分「NV App 前提不满足（写 no）」与「Windows HDR 关（仅置灰、保留配置）」。</summary>
    public string RtxHdrStatusText
    {
        get
        {
            if (_nvHdrAppRaw < 0)
                return "已自动禁用：未检测到 NVIDIA 驱动注册表开关（硬件不支持 / 未装 NVIDIA App）。保存时写入 rtx_hdr=no，恢复前请先在 NVIDIA App 开启。";
            if (_nvHdrAppRaw == 0)
                return "已自动禁用：NVIDIA App 侧「RTX Video HDR」总开关为关。保存时写入 rtx_hdr=no；到 NVIDIA App → 视频里打开后重开本程序即可恢复。";
            if (!_windowsHdrEnabled)
                return "Windows HDR 当前未开启：开关暂时置灰，播放时由脚本自动跳过，不会写入 rtx_hdr=no。按 Win+Alt+B 打开 HDR 后即刻生效，无需回来重设。";
            return "前提满足：NVIDIA App「RTX Video HDR」总开关为开，Windows HDR 已开启。"
                   + "驱动只对 SDR 片源生效，HDR 片源一律跳过。";
        }
    }

    /// <summary>把 NVIDIA App 侧两个硬开关 + Windows HDR 翻成人话，显示在设置页。</summary>
    public string NvidiaAppStateText
    {
        get
        {
            string vsr = _nvVsrAppRaw < 0 ? "超分辨率：未检测到 NVIDIA 驱动 / 开关"
                : _nvVsrAppRaw == 0 ? "超分辨率：关"
                : _nvVsrAppRaw == 5 ? "超分辨率：开（自动质量）"
                : "超分辨率：开（质量 " + _nvVsrAppRaw + "）";
            string hdr = _nvHdrAppRaw < 0 ? "RTX HDR：未检测到"
                : _nvHdrAppRaw == 0 ? "RTX HDR：关"
                : "RTX HDR：开";
            string win = _windowsHdrEnabled ? "Windows HDR：开" : "Windows HDR：关";
            return vsr + "；" + hdr + "；" + win
                   + "（前两项来自 NVIDIA 驱动注册表，第三项由 Windows 显示接口直读；改后即刻生效）";
        }
    }

    /// <summary>只读提示：RTX HDR 的硬前提是 NVIDIA App 开关 + Windows HDR，超分无后者要求。</summary>
    public string RtxHwdecHint => "硬件解码不限（实测 d3d11va / auto-copy / 软解均可）；"
                                  + "RTX Video HDR 需 NVIDIA App 侧启用 + Windows HDR 开启（Win+Alt+B）；超分只需 NVIDIA App 侧启用";
    #endregion

    #region 属性（页 C）
    public bool AfEnabled { get => _afEnabled; set => Set(ref _afEnabled, value); }
    public bool BassMaxEnabled { get => _bassMaxEnabled; set => Set(ref _bassMaxEnabled, value); }

    /// <summary>
    /// mpv.conf: volume-max。true = 写 100（音量上限 100%），false = 写 130（mpv 原生默认，可推到 130%）。
    /// 默认打开：音效驱动 1.0 的增益已经足够大，再叠超过 100% 的软件音量只会削波破坏声音。
    /// </summary>
    public bool VolumeCap100 { get => _volumeCap100; set => Set(ref _volumeCap100, value); }

    /// <summary>
    /// mpv.conf: audio-file-auto。true = exact（同目录同名外部音轨自动加载），false = no。
    /// 本包出厂默认开；写入显式值保证往返稳定。
    /// </summary>
    public bool ExternalAudioAuto { get => _externalAudioAuto; set => Set(ref _externalAudioAuto, value); }

    public double Bass { get => _bass; set => Set(ref _bass, value); }
    public double Treble { get => _treble; set => Set(ref _treble, value); }
    public double Drive { get => _drive; set => Set(ref _drive, value); }
    public int PresetIndex { get => _presetIndex; set => Set(ref _presetIndex, value); }
    public int AudioChannelsIndex { get => _audioChannelsIndex; set => Set(ref _audioChannelsIndex, value); }
    public bool AudioChannelsCustom { get => _audioChannelsCustom; set => Set(ref _audioChannelsCustom, value); }
    public string[] AudioChannelsOptions { get => _audioChannelsOptions; set => Set(ref _audioChannelsOptions, value); }
    #endregion

    #region 属性（页 D）
    public int ShotFormatIndex { get => _shotFormatIndex; set => Set(ref _shotFormatIndex, value); }
    public bool ShotFormatCustom { get => _shotFormatCustom; set => Set(ref _shotFormatCustom, value); }
    public string[] ShotFormatOptions { get => _shotFormatOptions; set => Set(ref _shotFormatOptions, value); }
    public int ShotDirIndex { get => _shotDirIndex; set { if (Set(ref _shotDirIndex, value)) OnPropertyChanged(nameof(ShotDirIsCustom)); } }
    public bool ShotDirIsCustom => _shotDirIndex == 3;
    public string ShotDirPath { get => _shotDirPath; set => Set(ref _shotDirPath, value); }
    public string[] ShotDirOptions => ShotDirLabels;
    public bool HighBitDepth { get => _highBitDepth; set => Set(ref _highBitDepth, value); }

    // n*m 缩略图（1.0.7）
    // 网格下拉 1–8：索引 0 = 1 列/行，依次类推。
    internal static readonly string[] ThumbGridLabels =
        { "1", "2", "3", "4", "5", "6", "7", "8" };
    public string[] ThumbGridOptions => ThumbGridLabels;
    public int ThumbColsIndex
    {
        get => Math.Max(0, Math.Min(7, _thumbCols - 1));
        set { int v = Math.Max(1, Math.Min(8, value + 1)); Set(ref _thumbCols, v); }
    }
    public int ThumbRowsIndex
    {
        get => Math.Max(0, Math.Min(7, _thumbRows - 1));
        set { int v = Math.Max(1, Math.Min(8, value + 1)); Set(ref _thumbRows, v); }
    }
    public int ThumbCols { get => _thumbCols; set => Set(ref _thumbCols, value); }
    public int ThumbRows { get => _thumbRows; set => Set(ref _thumbRows, value); }
    public double ThumbMaxWidth { get => _thumbMaxWidth; set => Set(ref _thumbMaxWidth, value); }
    public int ThumbGap { get => _thumbGap; set => Set(ref _thumbGap, value); }

    // TextBox 桥接：输入合法性由 WritePlanBuilder 落盘时钳制，这里只做无损显示。
    public string ThumbMaxWidthText
    {
        get => _thumbMaxWidth.ToString("0", CultureInfo.InvariantCulture);
        set { if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) _thumbMaxWidth = v; }
    }
    public string ThumbGapText
    {
        get => _thumbGap.ToString(CultureInfo.InvariantCulture);
        set { if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) _thumbGap = v; }
    }
    #endregion

    #region 只读状态
    public bool SpdifActive { get => _spdifActive; set => Set(ref _spdifActive, value); }
    public string SpdifText { get => _spdifText; set => Set(ref _spdifText, value); }
    public string BassMaxRegStatus { get => _bassMaxRegStatus; set => Set(ref _bassMaxRegStatus, value); }
    public bool BassMaxPanelAvailable { get => _bassMaxPanelAvailable; set => Set(ref _bassMaxPanelAvailable, value); }
    public string ConfigSourceText { get => _configSourceText; set => Set(ref _configSourceText, value); }
    public string MpvExeText { get => _mpvExeText; set => Set(ref _mpvExeText, value); }
    public string SnapshotText { get => _snapshotText; set => Set(ref _snapshotText, value); }

    /// <summary>封面配置的接入状态：cover-art.conf 与 mpv.conf 的 include 行缺一不可。</summary>
    public string CoverConfStatus { get => _coverConfStatus; set => Set(ref _coverConfStatus, value); }

    /// <summary>无封面信息窗的接入状态：audio-info.conf + include 行（阶段 ⑦）。</summary>
    public string NoArtConfStatus { get => _noArtConfStatus; set => Set(ref _noArtConfStatus, value); }

    /// <summary>脚本与固定键的状态：MyMPC_ui.lua / MyMPC.conf / fit90.conf / input-ipc-server（阶段 ⑦）。</summary>
    public string ScriptConfStatus { get => _scriptConfStatus; set => Set(ref _scriptConfStatus, value); }
    #endregion

    // ==================== 反推 ====================

    /// <summary>
    /// 只刷新「只读状态」文本（配置源、快照时间、直通互斥、注册表齐全性）。
    /// 应用成功后再读一次盘时用它：界面上的编辑值保持不变，只有状态说明跟着更新。
    /// </summary>
    public void RefreshReadOnly(ConfigSnapshot s)
    {
        var m = s.Mpv;
        var reg = s.BassMax;

        ConfigSourceText = s.Locator.Source + " · " + s.Locator.ConfigDir;
        MpvExeText = s.Locator.MpvExe.Length > 0 ? s.Locator.MpvExe : "未找到";
        SnapshotText = $"读取时间 {s.LoadedAt:HH:mm:ss}";
        SpdifActive = !string.IsNullOrWhiteSpace(m.Get("audio-spdif", ""));
        SpdifText = SpdifActive
            ? "已启用直通 · 与音效互斥，按 Ctrl+b 可旁通"
            : "未启用直通 · 音效保持开启";
        BassMaxRegStatus = reg.StatusText;
        BassMaxPanelAvailable = s.Locator.HasBassMaxPanel;

        // 封面：文件与 include 行缺一不可，缺了 profile 就是死配置，必须在界面上说明白
        bool hasCover = s.CoverArt.Exists;
        bool hasInclude = m.HasInclude("cover-art.conf");
        CoverConfStatus = hasCover && hasInclude ? "已启用"
            : hasCover ? "尚未接入 · 保存设置时自动补上"
            : hasInclude ? "配置文件缺失 · 保存设置时自动补上"
            : "尚未创建 · 保存设置时自动补上";

        // 无封面信息窗（阶段 ⑦）：同样是「文件 + include 行」两件套；关掉开关时 include 会被摘掉
        bool hasInfo = s.AudioInfo.Exists;
        bool hasInfoInclude = m.HasInclude("audio-info.conf");
        NoArtConfStatus = hasInfo && hasInfoInclude ? "已启用"
            : hasInfo ? "尚未接入 · 保存设置时自动补上"
            : hasInfoInclude ? "配置文件缺失 · 保存设置时自动补上"
            : "尚未创建 · 保存设置时自动补上";

        // 脚本层：三个文件 + 固定键，缺任何一样单实例或信息窗就会静默失效
        var missing = new List<string>();
        if (!s.LuaExists) missing.Add("MyMPC_ui.lua");
        if (!s.MyMpcOpts.Exists) missing.Add("MyMPC.conf");
        if (!s.Fit90.Exists) missing.Add("fit90.conf");
        if (!m.Has("input-ipc-server")) missing.Add("input-ipc-server");
        // NVIDIA App 侧两个硬开关（VSR / RTX Video HDR）：只读，不落盘。
        // Windows HDR 是动态状态，也一并直读。每次 LoadFrom / 应用后刷新都重读，
        // NVIDIA App / Windows 显示设置里改完回到这里就能看到。
        NvVsrAppRaw = NvidiaAppState.ReadVsr();
        NvHdrAppRaw = NvidiaAppState.ReadHdr();
        WindowsHdrEnabled = WindowsHdrState.IsHdrEnabled();
        HdrSupported = WindowsHdrState.IsHdrSupported();

        ScriptConfStatus = missing.Count == 0
            ? "已就位"
            : "缺 " + string.Join("、", missing) + " · 保存时补齐";
    }

    public void LoadFrom(ConfigSnapshot s)
    {
        var m = s.Mpv;         // mpv.conf
        var h = s.Hdr;         // script-opts\hdr-autoswitch.conf
        var c = s.CoverArt;    // cover-art.conf（EXE 独占，可能尚未创建）
        var o = s.MyMpcOpts;   // script-opts\MyMPC.conf（EXE 独占，可能尚未创建）
        var reg = s.BassMax;

        // ---- 只读状态 ----
        RefreshReadOnly(s);

        // ---- 页 A ----
        ResolveCombo(HwdecValues, HwdecLabels, m.Get("hwdec", "d3d11va"),
            v => HwdecOptions = v, v => HwdecCustom = v, v => HwdecIndex = v);
        Deinterlace = ParseBool(m.Get("deinterlace", "yes"), true);
        LoopFile = m.Get("loop-file", "inf") == "inf";
        SavePosition = ParseBool(m.Get("save-position-on-quit", "no"), false);
        SubsMatchOsLanguage = ParseBool(m.Get("subs-match-os-language", "yes"), true);
        SubsFallbackForced = ParseBool(m.Get("subs-fallback-forced", "no"), false);

        // 字幕默认样式：缺键按出厂默认（面板接管这些键，保存时 PutOrFill 补齐）
        SubFont = m.Get("sub-font", "Microsoft YaHei");
        SubColor = NormalizeColor(m.Get("sub-color", "#FFFFFFFF"));
        SubFontSize = Clamp(ParseDouble(m.Get("sub-font-size", "48"), 48), 8, 200);
        SubLineSpacing = Clamp(ParseDouble(m.Get("sub-line-spacing", "0"), 0), -20, 20);
        SubBold = ParseBool(m.Get("sub-bold", "no"), false);
        SubItalic = ParseBool(m.Get("sub-italic", "no"), false);
        SubOutlineSize = Clamp(ParseDouble(m.Get("sub-outline-size", "1"), 1), 0, 20);
        SubShadowOffset = Clamp(ParseDouble(m.Get("sub-shadow-offset", "0"), 0), 0, 20);
        SubMarginX = Clamp(ParseDouble(m.Get("sub-margin-x", "19"), 19), 0, 200);
        SubMarginY = Clamp(ParseDouble(m.Get("sub-margin-y", "34"), 34), 0, 300);
        // 只有显式 yes/scale/force 才算「不保护」；缺键按出厂的 no（保护）处理
        string sa = m.Get("sub-ass-override", "no");
        SubProtectAss = sa is "no" or "";

        // cover-art.conf 不存在时按出厂默认；include 行缺失由阶段 ③ 补回，不代表开关关闭
        // 1.0.8：video-unscaled 恒为 no（封面随窗口等比缩放），所以开关不能再从它反推，
        //        改看 autofit-larger 在不在 —— 老配置（1.0.6/1.0.7）都有这行，仍判为开启。
        CoverOriginal = c.Has("autofit-larger") || c.Has("autofit");
        CoverMax = SnapCover(c.Get("autofit-larger", "1200x1200"));

        // 外挂字幕热重载：只有显式写 sub_autoreload=no 才算关闭；缺键/文件未创建按出厂默认「开启」
        SubAutoReload = !string.Equals(o.Get("sub_autoreload", "yes"), "no", StringComparison.OrdinalIgnoreCase);
        // 顶部进度条：只有显式写 osd_progress=no 才算关闭；缺键按出厂默认「开启」
        OsdProgress = !string.Equals(o.Get("osd_progress", "yes"), "no", StringComparison.OrdinalIgnoreCase);

        // 只有显式写 no_art_ui=no 才算用户关闭；文件未创建按出厂默认「开启」
        NoArtUi = !string.Equals(o.Get("no_art_ui", "yes"), "no", StringComparison.OrdinalIgnoreCase);
        // 阶段 ⑦ 起比例外置到 fit90.conf（原 fit90.lua 里写死 0.9）；老配置的 MyMPC.conf
        // 若还留着 fit_ratio，按它兜底，免得用户升级后比例突然跳回 90%
        FitRatio = Clamp(ParsePercent(s.Fit90.Get("ratio", o.Get("fit_ratio", "90")), 90), 50, 100);

        // single_instance=no 才表示「允许多个实例」；默认 yes = UI 开关关闭
        AllowMultiInstance = string.Equals(o.Get("single_instance", "yes"), "no", StringComparison.OrdinalIgnoreCase);

        // ---- 页 B：HDR 四键联动，整组精确命中 ----
        string mode = h.Get("mode", "passthrough");
        _hdrRawMode = mode;   // 供 ToneLockedBySdr 判定：自定义态也能认出 tonemap
        string sdr = h.Get("sdr_passthrough", "no");
        string manage = h.Get("manage_system_hdr", "yes");
        string osd = h.Get("osd_notify", "no");
        bool hdrShared = manage == "yes" && osd == "no";
        if (hdrShared && mode == "passthrough" && sdr == "no") SetCombo(ref _hdrModeOptions, ref _hdrModeCustom, ref _hdrModeIndex,
            HdrModeLabels, false, 0, nameof(HdrModeOptions), nameof(HdrModeCustom), nameof(HdrModeIndex));
        else if (hdrShared && mode == "tonemap" && sdr == "no") SetCombo(ref _hdrModeOptions, ref _hdrModeCustom, ref _hdrModeIndex,
            HdrModeLabels, false, 1, nameof(HdrModeOptions), nameof(HdrModeCustom), nameof(HdrModeIndex));
        else SetCombo(ref _hdrModeOptions, ref _hdrModeCustom, ref _hdrModeIndex,
            HdrModeLabels, true, HdrModeLabels.Length, nameof(HdrModeOptions), nameof(HdrModeCustom), nameof(HdrModeIndex),
            $"mode={mode}, sdr_passthrough={sdr}");

        // 色调档位：inverse / contrast / brightness / saturation 四键组合
        string inv = m.Get("inverse-tone-mapping", "no");
        double con = Num(m.Get("contrast", "0")), bri = Num(m.Get("brightness", "0")), sat = Num(m.Get("saturation", "0"));
        int tone = -1;
        if (inv == "no" && con == 0 && bri == 0 && sat == 0) tone = 0;
        else if (inv == "yes" && bri == 0 && sat == 0)
        {
            // 2026-11-19 新体系：0.6=con0 / 0.8=con6 / 1.0=con12
            if (con == 0) tone = 1; else if (con == 6) tone = 2;
            else if (con == 12) tone = 3;
        }
        if (tone >= 0) SetCombo(ref _toneOptions, ref _toneCustom, ref _toneIndex,
            ToneLabels, false, tone, nameof(ToneOptions), nameof(ToneCustom), nameof(ToneIndex));
        else SetCombo(ref _toneOptions, ref _toneCustom, ref _toneIndex,
            ToneLabels, true, ToneLabels.Length, nameof(ToneOptions), nameof(ToneCustom), nameof(ToneIndex),
            $"inverse={inv}, contrast={m.Get("contrast", "0")}");

        // HDR转SDR 时色调修复必须关闭：盘上若有残留档位，界面显示成「关闭」，
        // 写盘时由 WritePlanBuilder 强制把四键清零（用户 2026-11-17 拍板：配置真关闭）。
        if (string.Equals(mode, "tonemap", StringComparison.OrdinalIgnoreCase))
        {
            SetCombo(ref _toneOptions, ref _toneCustom, ref _toneIndex,
                ToneLabels, false, 0, nameof(ToneOptions), nameof(ToneCustom), nameof(ToneIndex));
        }
        OnPropertyChanged(nameof(ToneLockedBySdr));
        OnPropertyChanged(nameof(ToneInteractive));

        ResolveCombo(LevelsValues, LevelsLabels, m.Get("video-output-levels", "full"),
            v => VideoLevelsOptions = v, v => VideoLevelsCustom = v, v => VideoLevelsIndex = v);
        OnlyFullscreen = ParseBool(h.Get("only_fullscreen", "no"), false);

        // 键缺失按本包出厂默认（false = 不锁窗口比例），不是 mpv 原生默认的 yes
        KeepWindowAspect = ParseBool(m.Get("keepaspect-window", "no"), false);

        // NVIDIA RTX 视频增强（1.0.2）：缺键按出厂默认。两个开关默认都开——
        // RTX HDR 只吃 SDR 源（HDR 源驱动自己一律跳过），加上桌面 HDR 判据兜底，可以放心开。
        // 峰值/档位越界一律落回出厂值。
        RtxVsr = ParseBool(o.Get("rtx_vsr", "yes"), true);
        RtxHdr = ParseBool(o.Get("rtx_hdr", "yes"), true);
        int vlim = Array.FindIndex(RtxVsrLimitValues,
            v => string.Equals(v, o.Get("rtx_vsr_limit", "1080p"), StringComparison.OrdinalIgnoreCase));
        RtxVsrLimitIndex = vlim >= 0 ? vlim : 2;
        // 峰值必须在预设表内才认。先按原值查表，查不到才允许 clamp 到表内（如 750→800），
        // 但远超范围的（99999）直接回默认，不吸附到 2000 nit 的极值。
        string peakRaw = o.Get("rtx_hdr_peak", "1000");
        double peakNum = ParseDouble(peakRaw, 1000);
        int peak = Array.FindIndex(RtxHdrPeaks, p => p == (int)peakNum);
        if (peak < 0 && peakNum >= 400 && peakNum <= 2000)
        {
            int snapped = (int)Math.Round(peakNum / 200.0, MidpointRounding.AwayFromZero) * 200;
            peak = Array.IndexOf(RtxHdrPeaks, snapped);
        }
        RtxHdrPeakIndex = peak >= 0 ? peak : 3;
        OnPropertyChanged(nameof(RtxHwdecHint));

        // 自动禁用：NVIDIA App 总开关关 / 硬件不支持 → 草稿开关归零，界面显示「关 + 置灰」。
        // Windows HDR 关不属于这一类（动态状态），按用户拍板只提示、保留配置原值。
        ApplyRtxEligibility();

        // ---- 页 C ----
        // af=bassmax 是本包不变量，不再由磁盘反推成「开关状态」。
        // 盘上缺了 / 被手工删了，由 WritePlanBuilder.EnsureAfChain 在写盘时补回；
        // 这里恒 true，属性保留只为兼容 DraftBaseline 的属性表（界面已不再绑它）。
        AfEnabled = true;
        BassMaxEnabled = ParseBool(reg.Get("Enabled", "1"), true);

        // volume-max 缺键按本包出厂默认「限 100%」；只有明确 >100 才算用户主动放开了上限。
        // 关闭时写的是 130 而不是删行，所以这里能稳定反推回「关闭」，不会每次保存又写一遍。
        VolumeCap100 = ParseDouble(m.Get("volume-max", "100"), 100) <= 100;

        // audio-file-auto 缺键按本包出厂默认「开（exact）」；显式写 no 才算关。
        ExternalAudioAuto = !string.Equals(m.Get("audio-file-auto", "exact"), "no", StringComparison.OrdinalIgnoreCase);

        Bass = Clamp(ParseDouble(reg.Get("Bass", "2.0"), 2.0), -12, 12);
        Treble = Clamp(ParseDouble(reg.Get("Treble", "2.0"), 2.0), -12, 12);
        Drive = Clamp(ParseDouble(reg.Get("Drive", "1.00"), 1.0), 0, 1);
        int preset = (int)Clamp(ParseDouble(reg.Get("Preset", "4"), 4), 0, 5);
        PresetIndex = preset;
        ResolveCombo(ChannelsValues, ChannelsLabels, m.Get("audio-channels", "auto"),
            v => AudioChannelsOptions = v, v => AudioChannelsCustom = v, v => AudioChannelsIndex = v);

        // ---- 页 D ----
        // 盘上若是已不再支持的格式（webp / avif / jxl），一律按 jpg 显示：
        // 这些编码器库已经不在构建里，留着这个下拉只会让人选了也截不出图。
        // 真正落盘改回 jpg 在 WritePlanBuilder 页 D（无条件纠偏，不依赖用户点保存）。
        string diskFmt = m.Get("screenshot-format", "jpg");
        if (!IsFormatSupported(diskFmt)) diskFmt = FormatValues[0];
        ResolveCombo(FormatValues, FormatLabels, diskFmt,
            v => ShotFormatOptions = v, v => ShotFormatCustom = v, v => ShotFormatIndex = v);
        HighBitDepth = ParseBool(m.Get("screenshot-high-bit-depth", "no"), false);
        ResolveShotDir(m.Get("screenshot-dir", ""), s.Locator.ConfigDir);

        // n*m 缩略图（1.0.7）：从 thumbnails.conf 反推，缺键按出厂默认。
        var th = s.Thumbnails;
        ThumbCols = (int)Clamp(ParseDouble(th.Get("cols", "3"), 3), 1, 8);
        ThumbRows = (int)Clamp(ParseDouble(th.Get("rows", "3"), 3), 1, 8);
        ThumbMaxWidth = Clamp(ParseDouble(th.Get("max_width", "1200"), 1200), 300, 4000);
        ThumbGap = (int)Clamp(ParseDouble(th.Get("gap", "4"), 4), 0, 20);

        // ---- 页 E ----
        Shortcuts.LoadFrom(s);
    }

    // ==================== 重置 ====================

    /// <summary>全部重置 = 回到出厂默认；只读计算状态（配置源、注册表状态）保留。</summary>
    public void ResetAll()
    {
        SetPlaybackDefaults(); SetVideoDefaults(); SetAudioDefaults(); SetScreenshotDefaults();
        Shortcuts.Reset();
    }

    /// <summary>重置页面 = 只把当前页的键恢复出厂默认。</summary>
    public void ResetPage(string page)
    {
        switch (page)
        {
            case "播放": SetPlaybackDefaults(); break;
            case "画面": SetVideoDefaults(); break;
            case "音频": SetAudioDefaults(); break;
            case "截图": SetScreenshotDefaults(); break;
            case "快捷键": Shortcuts.Reset(); break;
        }
    }

    private void SetPlaybackDefaults()
    {
        HwdecOptions = HwdecLabels; HwdecCustom = false; HwdecIndex = 3; // d3d11va 零拷贝
        Deinterlace = true; LoopFile = true; SavePosition = false;
        SubsMatchOsLanguage = true; SubsFallbackForced = false; SubAutoReload = true; OsdProgress = true;
        SubFont = "Microsoft YaHei"; SubColor = "#FFFFFFFF";
        SubFontSize = 48; SubLineSpacing = 0; SubBold = false; SubItalic = false;
        SubOutlineSize = 1; SubShadowOffset = 0; SubMarginX = 19; SubMarginY = 34;
        SubProtectAss = true;
        CoverOriginal = true; CoverMax = 1200;
        NoArtUi = true; FitRatio = 90; AllowMultiInstance = false;
    }

    private void SetVideoDefaults()
    {
        HdrModeOptions = HdrModeLabels; HdrModeCustom = false; HdrModeIndex = 0;
        ToneOptions = ToneLabels; ToneCustom = false; ToneIndex = 0;
        VideoLevelsOptions = LevelsLabels; VideoLevelsCustom = false; VideoLevelsIndex = 0;
        OnlyFullscreen = false;
        KeepWindowAspect = false;
        RtxVsr = true; RtxHdr = true; RtxVsrLimitIndex = 2; RtxHdrPeakIndex = 3;
        _hdrRawMode = "passthrough";
        OnPropertyChanged(nameof(ToneLockedBySdr));
        OnPropertyChanged(nameof(RtxHwdecHint));
        // 恢复默认也要遵守硬前提：NV App 关 / 硬件不支持时开关显示关 + 置灰。
        // Windows HDR 关不在此列（动态状态），保留出厂的开，只靠状态文本提示。
        ApplyRtxEligibility();
    }

    private void SetAudioDefaults()
    {
        AfEnabled = true; BassMaxEnabled = true; VolumeCap100 = true; ExternalAudioAuto = true;
        Bass = 2.0; Treble = 2.0; Drive = 1.0; PresetIndex = 4;
        AudioChannelsOptions = ChannelsLabels; AudioChannelsCustom = false; AudioChannelsIndex = 0;
    }

    private void SetScreenshotDefaults()
    {
        ShotFormatOptions = FormatLabels; ShotFormatCustom = false; ShotFormatIndex = 0;
        HighBitDepth = false;
        ShotDirPath = string.Empty; ShotDirIndex = 0;
        ThumbCols = 3; ThumbRows = 3; ThumbMaxWidth = 1200; ThumbGap = 4;
    }

    // ==================== 辅助 ====================

    /// <summary>
    /// 硬前提不满足时把草稿开关归零，让界面显示「关 + 置灰」与写盘结果一致。
    /// 只归零 NV App 总开关 / 硬件不支持这两项——它们是静态前提，写盘会强制 no；
    /// Windows HDR 是动态的，按用户拍板保留配置原值，只置灰 + 提示。
    /// </summary>
    private void ApplyRtxEligibility()
    {
        if (!RtxVsrEligible && _rtxVsr) { _rtxVsr = false; OnPropertyChanged(nameof(RtxVsr)); OnPropertyChanged(nameof(RtxVsrLimitInteractive)); }
        if (!RtxHdrEligible && _rtxHdr) { _rtxHdr = false; OnPropertyChanged(nameof(RtxHdr)); OnPropertyChanged(nameof(RtxHdrPeakInteractive)); }
    }

    /// <summary>把实际值映射到下拉；不在预设内时追加「自定义」项并选中，由控件绑定负责置灰。</summary>
    private static void ResolveCombo(string[] values, string[] labels, string raw,
        Action<string[]> setOptions, Action<bool> setCustom, Action<int> setIndex)
    {
        int i = Array.FindIndex(values, v => string.Equals(v, raw, StringComparison.OrdinalIgnoreCase));
        if (i >= 0)
        {
            setOptions(labels);
            setCustom(false);
            setIndex(i);
        }
        else
        {
            setOptions(WithCustom(labels, raw));
            setCustom(true);
            setIndex(labels.Length);
        }
    }

    /// <summary>HDR / 档位这类组合反推：直接操作 backing field 后统一发通知，避免多次重绘。</summary>
    private void SetCombo(ref string[] options, ref bool custom, ref int index,
        string[] labels, bool isCustom, int idx, string nOpt, string nCus, string nIdx, string rawLabel = null)
    {
        options = isCustom ? WithCustom(labels, rawLabel ?? "未知") : labels;
        custom = isCustom;
        index = idx;
        OnPropertyChanged(nOpt);
        OnPropertyChanged(nCus);
        OnPropertyChanged(nIdx);
    }

    private static string[] WithCustom(string[] labels, string raw)
    {
        var a = new string[labels.Length + 1];
        Array.Copy(labels, a, labels.Length);
        a[^1] = "自定义（当前值：" + raw + "）· 不覆盖";
        return a;
    }

    private void ResolveShotDir(string raw, string configDir)
    {
        ShotDirPath = raw;
        string desktop = Norm(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        string pictures = Norm(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        string mpvdir = Norm(configDir);
        string v = Norm(raw);
        if (v.Length == 0) { ShotDirIndex = 0; return; }
        if (string.Equals(v, desktop, StringComparison.OrdinalIgnoreCase)) ShotDirIndex = 0;
        else if (string.Equals(v, pictures, StringComparison.OrdinalIgnoreCase)) ShotDirIndex = 1;
        else if (string.Equals(v, mpvdir, StringComparison.OrdinalIgnoreCase)) ShotDirIndex = 2;
        else ShotDirIndex = 3;
    }

    /// <summary>路径归一化：正斜杠 + 结尾斜杠 + 忽略大小写（与 MpvNormPath 一致）。</summary>
    private static string Norm(string p)
        => string.IsNullOrWhiteSpace(p) ? string.Empty
           : p.Replace('\\', '/').TrimEnd('/') + "/";

    private static bool ParseBool(string v, bool def)
    {
        if (string.IsNullOrWhiteSpace(v)) return def;
        v = v.Trim().ToLowerInvariant();
        if (v is "yes" or "true" or "1" or "on") return true;
        if (v is "no" or "false" or "0" or "off") return false;
        return def;
    }

    private static double ParseDouble(string v, double def)
        => double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;

    /// <summary>
    /// mpv 颜色 → UI 的 #AARRGGBB。mpv 接受 #RRGGBB / #AARRGGBB / r/g/b[/a]；
    /// 后两种小数形式不在这里转换（保持原样，避免精度往返漂移），只在样式弹窗里放弃色块预览。
    /// </summary>
    private static string NormalizeColor(string v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "#FFFFFFFF";
        string s = v.Trim().Trim('\'', '"');
        if (s.StartsWith("#"))
        {
            string hex = s.Substring(1);
            if (hex.Length == 6) return "#FF" + hex.ToUpperInvariant();
            if (hex.Length == 8) return "#" + hex.ToUpperInvariant();
        }
        return v;
    }

    private static double Num(string v) => ParseDouble(v, double.NaN);

    private static double ParsePercent(string v, double def)
    {
        v = v.Trim().TrimEnd('%');
        return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : def;
    }

    private static double Clamp(double v, double lo, double hi)
        => double.IsNaN(v) ? lo : Math.Max(lo, Math.Min(hi, v));

    /// <summary>autofit-larger 形如 1200x1200；非数字（如 60%x60%）回落到出厂默认。</summary>
    private static double SnapCover(string v)
    {
        string first = v.Split('x', 'X')[0].Trim();
        if (!double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return 1200;
        d = Clamp(d, 400, 1600);
        return Math.Round(d / 100) * 100;
    }
}

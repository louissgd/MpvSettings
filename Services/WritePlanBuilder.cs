using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using MpvSettings.Models;

namespace MpvSettings.Services;

/// <summary>
/// 把「打开时草稿」与「当前草稿」的 diff 翻译成写盘计划，并做联动校验。
///
/// 已接入：
///   · mpv.conf                          页 A 常规播放键 / 页 B 档位四键 / 页 C af 与 audio-channels / 页 D 截图键
///   · script-opts\hdr-autoswitch.conf  页 B HDR 模式四键 + only_fullscreen
///   · HKCU\Software\BassMax            页 C 音效六键（阶段 ④）
///   · cover-art.conf（整文件）          页 A 纯音频封面 + mpv.conf 的 include 行（阶段 ⑤）
///   · input.conf（行级改键）            页 E 快捷键（阶段 ⑥）
///   · audio-info.conf / MyMPC.conf / fit90.conf / MyMPC_ui.lua（整文件）+ input-ipc-server 固定键（阶段 ⑦）
/// </summary>
public static class WritePlanBuilder
{
    private const string MpvLabel = "mpv.conf";
    private const string HdrLabel = "script-opts\\hdr-autoswitch.conf";
    private const string CoverLabel = "cover-art.conf";
    private const string InputLabel = "input.conf";
    private const string AudioInfoLabel = "audio-info.conf";
    private const string MyMpcLabel = "script-opts\\MyMPC.conf";
    private const string FitLabel = "script-opts\\fit90.conf";
    private const string LuaLabel = "scripts\\MyMPC_ui.lua";
    private const string ThumbLabel = "script-opts\\thumbnails.conf";
    private const string ThumbLuaLabel = "scripts\\thumbnails.lua";

    /// <summary>单实例命名管道。必须与 MyMPC_ui.lua 的 pipe_name 默认值一致。</summary>
    private const string PipeName = "MyMPC_mpv";

    // 档位联动（与 setting.ahk 的 MpvApplyTone 一致）：关闭 / 0.6 / 0.8 / 1.0
    // 2026-11-19 定稿新体系：提亮主力是 inverse-tone-mapping 本身（libplacebo spline
    // 逆映射把 PQ 朝显示器峰值拉伸），contrast 只做梯度微调：0.6=纯inverse(con0) /
    // 0.8=con6 / 1.0=con12。取消旧 0.5 档（mpv 默认提亮已较大，与原片差别太小）。
    // 档位在峰值 2000nits / DisplayHDR 1400 显示器上人眼实测标定，HDR10 / 杜比视界通用同一套，不分片源。
    // 详见 setting.ahk 注释与 1.0.2 记录第十一节。
    private static readonly string[][] Tone =
    {
        new[] { "no",  "0",  "0", "0" },   // inverse / contrast / brightness / saturation  关闭
        new[] { "yes", "0",  "0", "0" },   // 0.6  纯 inverse 基线
        new[] { "yes", "6",  "0", "0" },   // 0.8
        new[] { "yes", "12", "0", "0" },   // 1.0
    };
    private static readonly string[] ToneKeys =
        { "inverse-tone-mapping", "contrast", "brightness", "saturation" };

    // HDR 模式联动（照抄 MpvAction 1271–1286）
    private static readonly string[][] HdrModes =
    {
        new[] { "passthrough", "no",  "yes", "no" },  // mode / sdr_passthrough / manage_system_hdr / osd_notify
        new[] { "tonemap",     "no",  "yes", "no" },
    };
    private static readonly string[] HdrKeys =
        { "mode", "sdr_passthrough", "manage_system_hdr", "osd_notify" };

    public static WritePlan Build(SettingsDraft d, DraftBaseline baseline, ConfigSnapshot s)
    {
        var plan = new WritePlan();
        var m = s.Mpv;
        var h = s.Hdr;
        string mpvPath = s.Locator.MpvConf;
        string hdrPath = s.Locator.HdrConf;

        // ---- 前置可用性 ----
        if (!s.Locator.IsUsable)
            plan.Blockers.Add("配置不可用：" + s.Locator.Reason);
        if (!m.Exists)
            plan.Blockers.Add("mpv.conf 不存在，拒绝写入（不悄悄重建配置）");
        if (!h.Exists)
            plan.Blockers.Add("script-opts\\hdr-autoswitch.conf 不存在，拒绝写入（首行 BOM 保护注释不可丢）");

        // 局部辅助：统一处理「没改就跳过 / 自定义态禁止覆盖 / 快照取值 / 缺键追加」
        void Put(string label, string path, ConfFile conf, string key, string value,
                 string prop, string customProp = null, string group = "", bool force = false)
        {
            if (!force && !baseline.Changed(d, prop)) return;
            if (customProp is not null && Guarded(d, baseline, plan, customProp, key)) return;
            plan.For(label, path).Changes.Add(new KeyChange
            {
                Key = key,
                To = value,
                Group = group,
                From = conf.Has(key) ? conf.Get(key) : string.Empty,
                Append = !conf.Has(key),
            });
        }

        // 「改了就写；没改但盘上缺这个键，也写（追加）」。给那些「缺键会导致界面显示与实际不一致」
        // 的键用——目前只有 volume-max：缺键按「上限 100%」显示，mpv 实际却还是 130。
        // 注册表六键里那套 missing 逻辑对文件键的对应物。
        void PutOrFill(string label, string path, ConfFile conf, string key, string value, string prop)
        {
            bool missing = !conf.Has(key);
            if (!missing && !baseline.Changed(d, prop)) return;
            plan.For(label, path).Changes.Add(new KeyChange
            {
                Key = key,
                To = value,
                From = missing ? string.Empty : conf.Get(key),
                Append = missing,
            });
        }

        void Group(string label, string path, ConfFile conf, string groupName,
                   string[] keys, string[] values, string prop, string customProp, bool force = false)
        {
            if (!force && !baseline.Changed(d, prop, customProp)) return;
            if (!force && customProp is not null && Guarded(d, baseline, plan, customProp, groupName)) return;
            var file = plan.For(label, path);
            for (int i = 0; i < keys.Length; i++)
                file.Changes.Add(new KeyChange
                {
                    Key = keys[i],
                    To = values[i],
                    Group = groupName,
                    From = conf.Has(keys[i]) ? conf.Get(keys[i]) : string.Empty,
                    Append = !conf.Has(keys[i]),
                });
        }

        // ==================== 页 A · 播放（mpv.conf） ====================
        int hw = d.HwdecIndex;
        if (baseline.Changed(d, "HwdecIndex", "HwdecCustom") && !Guarded(d, baseline, plan, "HwdecCustom", "hwdec"))
        {
            if (hw < 0 || hw >= SettingsDraft.HwdecValues.Length)
                plan.Blockers.Add("硬件解码下拉索引越界，已阻止写入");
            else
                Put(MpvLabel, mpvPath, m, "hwdec", SettingsDraft.HwdecValues[hw], "HwdecIndex", force: true);
        }
        // 开 = auto（逐行片源不挂反交错，省一层）；关 = no。原来写 yes 会让 60fps 逐行也白做一层。
        Put(MpvLabel, mpvPath, m, "deinterlace", d.Deinterlace ? "auto" : "no", "Deinterlace");
        Put(MpvLabel, mpvPath, m, "loop-file", d.LoopFile ? "inf" : "no", "LoopFile");
        Put(MpvLabel, mpvPath, m, "save-position-on-quit", YesNo(d.SavePosition), "SavePosition");
        Put(MpvLabel, mpvPath, m, "subs-match-os-language", YesNo(d.SubsMatchOsLanguage), "SubsMatchOsLanguage");
        Put(MpvLabel, mpvPath, m, "subs-fallback-forced", YesNo(d.SubsFallbackForced), "SubsFallbackForced");

        // ---- 页 A 弹窗「字幕默认样式」：整组写（缺键补齐，保证面板显示 = 实际生效） ----
        // 只作用于 SRT 等纯文本（converted）；sub-ass-override=no 时 ASS/SSA 与 SUP 位图不受影响。
        // 数字统一 invariant culture，避免中文系统把小数点写成逗号。
        string F(double v) => v.ToString("0.############", CultureInfo.InvariantCulture);
        string color = (d.SubColor ?? string.Empty).Trim().Trim('\'', '"');
        if (color.Length == 0) color = "#FFFFFFFF";
        // mpv.conf 里 # 是注释符：#RRGGBB 必须加引号，否则整行被忽略（实测 sub-color 回落到白色）。
        if (color.StartsWith("#")) color = "'" + color + "'";
        string[] subKeys =
        {
            "sub-font", "sub-color", "sub-font-size", "sub-line-spacing", "sub-bold", "sub-italic",
            "sub-outline-size", "sub-shadow-offset", "sub-margin-x", "sub-margin-y", "sub-ass-override",
        };
        string[] subVals =
        {
            (d.SubFont ?? string.Empty).Trim().Length == 0 ? "Microsoft YaHei" : d.SubFont.Trim(),
            color,
            F(d.SubFontSize), F(d.SubLineSpacing), YesNo(d.SubBold), YesNo(d.SubItalic),
            F(d.SubOutlineSize), F(d.SubShadowOffset), F(d.SubMarginX), F(d.SubMarginY),
            d.SubProtectAss ? "no" : "scale",
        };
        string[] subProps =
        {
            "SubFont", "SubColor", "SubFontSize", "SubLineSpacing", "SubBold", "SubItalic",
            "SubOutlineSize", "SubShadowOffset", "SubMarginX", "SubMarginY", "SubProtectAss",
        };
        // 逐键 diff：改过、或盘上缺这个键（补齐保证面板显示 = 实际生效）才写。
        // 没改且盘上有值的不动，遵守「不覆盖别人刚写的键」的全局契约。
        FilePlan sf = null;
        for (int i = 0; i < subKeys.Length; i++)
        {
            bool missing = !m.Has(subKeys[i]);
            if (!missing && !baseline.Changed(d, subProps[i])) continue;
            sf ??= plan.For(MpvLabel, mpvPath);
            sf.Changes.Add(new KeyChange
            {
                Key = subKeys[i],
                To = subVals[i],
                Group = "字幕默认样式（仅 SRT）",
                From = missing ? string.Empty : m.Get(subKeys[i]),
                Append = missing,
            });
        }

        // ==================== 页 B · 画面（两个文件，四键成组） ====================
        int hdr = d.HdrModeIndex;
        if (baseline.Changed(d, "HdrModeIndex", "HdrModeCustom") && !Guarded(d, baseline, plan, "HdrModeCustom", "HDR 模式四键"))
        {
            if (hdr < 0 || hdr >= HdrModes.Length)
                plan.Blockers.Add("HDR 模式下拉索引越界，已阻止写入");
            else
                Group(HdrLabel, hdrPath, h, "HDR 模式", HdrKeys, HdrModes[hdr],
                      "HdrModeIndex", "HdrModeCustom");
        }

        int tone = d.ToneIndex;
        // HDR转SDR 开启时色调修复必须真的关闭（2026-11-17 用户拍板）：
        // 界面档位置灰显示关闭，反推时已被归零，diff 看不到盘上残留档位，
        // 所以盘上四键只要有一项不是关闭值，就无条件写成关闭（Tone[0] = no/0/0/0）。
        // 盘上本来就干净时不动，避免「打开看一眼点确定」也制造计划条目。
        bool toneLocked = d.ToneLockedBySdr;
        bool toneDirty = false;
        if (toneLocked)
            for (int i = 0; i < ToneKeys.Length; i++)
                if (!string.Equals(m.Get(ToneKeys[i], Tone[0][i]), Tone[0][i], StringComparison.Ordinal))
                { toneDirty = true; break; }

        if ((toneLocked && toneDirty) ||
            (!toneLocked && baseline.Changed(d, "ToneIndex", "ToneCustom") && !Guarded(d, baseline, plan, "ToneCustom", "色调档位四键")))
        {
            if (!toneLocked && (tone < 0 || tone >= Tone.Length))
                plan.Blockers.Add("色调档位下拉索引越界，已阻止写入");
            else
                Group(MpvLabel, mpvPath, m, toneLocked ? "色调档位（转SDR已关闭）" : "色调档位",
                      ToneKeys, toneLocked ? Tone[0] : Tone[tone],
                      "ToneIndex", "ToneCustom", force: toneLocked && toneDirty);
        }

        // ==================== 真直通：tone-mapping 与「直通 / 色调档位」互斥 ====================
        // 与 setting.ahk 的 MpvSyncToneMapping() 同一套规则：MPCVR 的 Passthrough
        // 和各色调映射算法在同一个互斥下拉里，选直通就完全不做处理。mpv 里
        // clip = 不做曲线压缩（PQ 原样送显示器），spline 才是「对 HDR 内容做处理」。
        // 旧版把 spline 写死在 mpv.conf 且两组控件都不碰它，导致「色调修复关闭 +
        // 直通显示器」仍在被压暗。两个下拉共同决定这一个键，所以无条件写（force），
        // ConfWriter 自己跳过同值；force 也保证「恢复默认」一定把盘上残留的
        // spline 归位成 clip。自定义态视为需要处理，避免误判成零处理。
        // 无 HDR 显示器时 lua 启动会降级成 tonemap，必须 spline（与 setting.ahk
        // MpvSyncToneMapping 的 !HdrSupported 分支一致）；否则界面显示「HDR转SDR」
        // 却写 clip（PQ 原样送 SDR 屏），自相矛盾。
        string tm = d.HdrSupported &&
                    d.HdrModeIndex == 0 && d.ToneIndex == 0 &&
                    !d.HdrModeCustom && !d.ToneCustom ? "clip" : "spline";
        Put(MpvLabel, mpvPath, m, "tone-mapping", tm, "ToneMapping", force: true);

        int lv = d.VideoLevelsIndex;
        if (baseline.Changed(d, "VideoLevelsIndex", "VideoLevelsCustom") && !Guarded(d, baseline, plan, "VideoLevelsCustom", "video-output-levels"))
        {
            if (lv < 0 || lv >= SettingsDraft.LevelsValues.Length)
                plan.Blockers.Add("视频输出范围下拉索引越界，已阻止写入");
            else
                Put(MpvLabel, mpvPath, m, "video-output-levels", SettingsDraft.LevelsValues[lv],
                    "VideoLevelsIndex", force: true);
        }
        Put(HdrLabel, hdrPath, h, "only_fullscreen", YesNo(d.OnlyFullscreen), "OnlyFullscreen");

        // 拖动边框是否锁窗口比例。只管窗口矩形；画面比例由 --keepaspect 另行控制，
        // 所以关掉它不会把画面拉变形，只会出现黑边。
        Put(MpvLabel, mpvPath, m, "keepaspect-window", YesNo(d.KeepWindowAspect), "KeepWindowAspect");

        // 1.0.2：RTX 增强的硬前提（2026-11-18 补全——之前只弹警告、不真正禁用）。
        // d3d11vpp 在 mpv 0.42 + RTX 5070 上，d3d11va / auto-copy / 纯软解都能启用驱动扩展，
        // 所以不按 hwdec 拦截。真正的硬前提按「是否静态」分两类处理：
        //   ① 静态前提=NVIDIA App 总开关 + 硬件存在（注册表 SuperResolution / TrueHDR，扫全槽位）。
        //      关了不会自己变好，必须去 NVIDIA App 开——所以写盘强制写 no，不只是提示。
        //   ② 动态前提=Windows HDR（Win+Alt+B 即切，CCD API 直读）。只置灰 + 提示，
        //      绝不写 no：用户开了 HDR 后 Lua 按 video-target-params 自动生效，不必回来重设。
        // 具体写盘值由 SettingsDraft.Rtx{Vsr,Hdr}EnabledForWrite 统一给出（草稿开关 && 静态前提），
        // 写 MyMPC.conf 时用它，保证界面置灰、配置值、警告三方一致。
        if (!d.RtxVsrEligible && d.RtxVsr)
            plan.Warnings.Add(d.NvVsrAppRaw < 0
                ? "RTX 超分辨率已自动禁用：未检测到 NVIDIA 驱动注册表开关（硬件不支持 / 未装 NVIDIA App），将写入 rtx_vsr=no"
                : "RTX 超分辨率已自动禁用：NVIDIA App「超分辨率」总开关为关，将写入 rtx_vsr=no；到 NVIDIA App → 视频里打开后重开设置程序即可恢复");
        if (!d.RtxHdrEligible && d.RtxHdr)
            plan.Warnings.Add(d.NvHdrAppRaw < 0
                ? "RTX Video HDR 已自动禁用：未检测到 NVIDIA 驱动注册表开关（硬件不支持 / 未装 NVIDIA App），将写入 rtx_hdr=no"
                : "RTX Video HDR 已自动禁用：NVIDIA App「RTX Video HDR」总开关为关，将写入 rtx_hdr=no；到 NVIDIA App → 视频里打开后重开设置程序即可恢复");
        // Windows HDR 关：只提示，不写 no（动态状态，Lua 运行时自己判）
        if (d.RtxHdr && d.RtxHdrEligible && !d.WindowsHdrEnabled)
            plan.Warnings.Add("Windows HDR 当前未开启：RTX Video HDR 开关已置灰，播放时由脚本自动跳过；配置保留 yes，按 Win+Alt+B 开 HDR 后即刻生效，无需回来重设");

        // ==================== 页 C · 音频 / 音效（阶段 ④） ====================
        int ch = d.AudioChannelsIndex;
        if (baseline.Changed(d, "AudioChannelsIndex", "AudioChannelsCustom") && !Guarded(d, baseline, plan, "AudioChannelsCustom", "audio-channels"))
        {
            if (ch < 0 || ch >= SettingsDraft.ChannelsValues.Length)
                plan.Blockers.Add("声道输出下拉索引越界，已阻止写入");
            else
                Put(MpvLabel, mpvPath, m, "audio-channels", SettingsDraft.ChannelsValues[ch],
                    "AudioChannelsIndex", force: true);
        }

        // 音量上限 100%（2026-11-16 用户要求）：音效驱动 1.0 自身增益已经足够，再叠超过 100%
        // 的软件音量只会削波破音，所以出厂就压上限。
        // 关闭时写 130 而不是删行：删了之后盘上没这个键，打开时只能按「默认打开」反推，
        // 用户关掉的状态下次又被显示成打开，还会每次保存重复写一遍。写显式 130 才能稳定往返。
        // 缺键同样补（照注册表六键的做法）：读盘时缺键按「打开」显示，盘上却还是 mpv 的 130，
        // 等于界面骗人；所以老配置第一次保存就把它补上。
        PutOrFill(MpvLabel, mpvPath, m, "volume-max", d.VolumeCap100 ? "100" : "130", "VolumeCap100");

        // 自动加载同目录同名外部音频：开 = exact，关 = no。缺键也补（本包默认开，
        // 而 mpv 原生默认 no，不补就是界面显示开、实际不加载）。
        PutOrFill(MpvLabel, mpvPath, m, "audio-file-auto", d.ExternalAudioAuto ? "exact" : "no", "ExternalAudioAuto");

        // af=bassmax 是本包的不变量（2026-11-15 用户定调）：MPV 有专属音效，
        // 「音效开关」只改 HKCU\Software\BassMax\Enabled，绝不能把 bassmax 从滤镜链里摘掉。
        // 所以这里没有「关」的分支：每次写盘都无条件补回，--default 与导入同样会走。
        // 导出导入 / 恢复默认 / 用户手工删掉 af 行，都靠这一处兜底。
        EnsureAfChain(plan, s);

        // 注册表六键：改过的才写；缺失的一律补齐（DShow 侧缺任意一个会整批忽略）
        var reg = s.BassMax;
        void Reg(string key, string desired, string prop)
        {
            bool missing = !reg.Has(key);
            bool changed = prop is not null && baseline.Changed(d, prop);
            if (!missing && !changed) return;

            string old = reg.Get(key, string.Empty);
            if (!missing && BassMaxStore.SameValue(old, desired)) return;

            plan.Registry.Changes.Add(new RegChange
            {
                Key = key,
                From = old,
                To = desired,
                Group = "BassMax 音效",
                Fill = missing,
            });
        }

        Reg("Bass", BassMaxStore.Format("Bass", d.Bass), "Bass");
        Reg("Treble", BassMaxStore.Format("Treble", d.Treble), "Treble");
        Reg("Drive", BassMaxStore.Format("Drive", d.Drive), "Drive");
        Reg("Preset", BassMaxStore.Format("Preset", d.PresetIndex), "PresetIndex");
        Reg("Enabled", d.BassMaxEnabled ? "1" : "0", "BassMaxEnabled");
        Reg("TrayIcon", reg.Get("TrayIcon", "1"), null);   // 没有对应控件：只补缺，不改用户设的值

        // ==================== 页 D · 截图 ====================
        int fmt = d.ShotFormatIndex;
        string diskFmt = m.Get("screenshot-format", "jpg");
        if (!SettingsDraft.IsFormatSupported(diskFmt))
        {
            // 盘上还是 webp / avif / jxl：这些格式的编码器库（libwebp / libaom /
            // libsvtav1 / libjxl）2026-11-16 已从构建里裁掉，留着只会让截图直接失败。
            // 无条件写回 jpg，不等用户去动下拉（界面上它已经显示成 JPG 了）。
            Put(MpvLabel, mpvPath, m, "screenshot-format", SettingsDraft.FormatValues[0],
                "ShotFormatIndex", force: true);
        }
        else if (baseline.Changed(d, "ShotFormatIndex", "ShotFormatCustom") && !Guarded(d, baseline, plan, "ShotFormatCustom", "screenshot-format"))
        {
            if (fmt < 0 || fmt >= SettingsDraft.FormatValues.Length)
                plan.Blockers.Add("截图格式下拉索引越界，已阻止写入");
            else
                Put(MpvLabel, mpvPath, m, "screenshot-format", SettingsDraft.FormatValues[fmt],
                    "ShotFormatIndex", force: true);
        }
        Put(MpvLabel, mpvPath, m, "screenshot-high-bit-depth", YesNo(d.HighBitDepth), "HighBitDepth");

        if (baseline.Changed(d, "ShotDirIndex", "ShotDirPath"))
        {
            string dir = ResolveShotDir(d, s.Locator.ConfigDir, out string pathError);
            if (pathError.Length > 0)
                plan.Blockers.Add(pathError);
            else
                Put(MpvLabel, mpvPath, m, "screenshot-dir", dir, "ShotDirIndex", force: true);
        }

        // ==================== 页 A · 纯音频封面（阶段 ⑤） ====================
        // V1 里唯一一处「新建文件 + 整文件重写」：条件 profile 是带段落的结构，逐键替换没有意义，
        // 而且首次启用时 cover-art.conf 根本不存在。include 行只补缺，绝不覆盖用户已有的 include。
        //
        // 放在所有其它键之后：要等这批改动算完，才知道「用户这次是不是真的在保存」。
        // 缺文件就补，是 1.2 节的要求，但不能「打开看一眼就写盘」——所以仅在本次确实有其它改动时才顺带补。
        bool coverChanged = baseline.Changed(d, "CoverOriginal", "CoverMax");
        bool coverMissing = !s.CoverArt.Exists || !m.HasInclude("cover-art.conf");
        bool otherChanges = plan.KeyCount > 0 || plan.Registry.HasChanges;

        if (coverChanged || (coverMissing && otherChanges))
        {
            double raw = Math.Round(d.CoverMax);
            if (raw < 400 || raw > 1600)
            {
                plan.Blockers.Add($"封面窗口上限 {raw:0} 超出 400–1600，已阻止写入");
            }
            else
            {
                int max = (int)(Math.Round(raw / 100.0, MidpointRounding.AwayFromZero) * 100);
                if (max != raw)
                    plan.Warnings.Add($"封面窗口上限 {raw:0} 不在 100 的整数倍上，已吸附为 {max}");

                var cover = plan.For(CoverLabel, s.Locator.CoverArtConf);
                cover.NewContent = BuildCoverArt(d, max, s.CoverArt.Exists && !s.CoverArt.Crlf ? "\n" : "\r\n");
                cover.IsCreate = !s.CoverArt.Exists;
                cover.WholeFileKind = "封面条件 profile";

                // include 是"启用封面 profile"的开关，没有它整个文件都是死配置
                if (!m.HasInclude("cover-art.conf"))
                    plan.For(MpvLabel, mpvPath).Changes.Add(new KeyChange
                    {
                        Key = "include",
                        To = "~~/cover-art.conf",
                        Append = true,
                        AppendOnly = true,   // include 可能已有多行，只追加、不改别人那几行
                    });

                if (!coverChanged)
                    plan.Warnings.Add(s.CoverArt.Exists
                        ? "cover-art.conf 已存在但未接入（mpv.conf 缺 include 行），本次将补回并按其内容重写"
                        : "cover-art.conf 尚未创建，本次将新建并按本窗口设置写入");
            }
        }

        // ==================== 页 E · 快捷键（input.conf，阶段 ⑥） ====================
        // 只写用户改动过的行：默认态不写、留空态不写、清空自定义行 = 删行（恢复默认）。
        // 定位按「动作对应的规范命令串」，其余行（注释 / 键序列 / 分号多命令 / 别人的绑定）一律原样保留。
        string inputPath = s.Locator.InputConf;
        var editedShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in d.Shortcuts.Rows)
        {
            var (wasPresent, wasKey) = baseline.Shortcut(row.Id);
            bool nowPresent = row.Present && row.Key.Length > 0;
            string nowKey = nowPresent ? row.Key : string.Empty;
            string oldKey = wasPresent ? wasKey : string.Empty;

            if (wasPresent == nowPresent && string.Equals(oldKey, nowKey, StringComparison.Ordinal)) continue;

            editedShortcuts.Add(row.Id);
            plan.For(InputLabel, inputPath).InputEdits.Add(new InputEdit
            {
                Command = row.Command,
                Action = row.Action,
                OldKey = oldKey,
                NewKey = nowKey,
            });
        }

        // 冲突：只有本次真的动过其中一行才阻止写入；文件里原本就有的重复键降级成提示，
        // 否则「打开就改个别页」会被别人的历史绑定拖住，整批写不了。
        foreach (var c in d.Shortcuts.ConflictList)
        {
            bool touched = editedShortcuts.Contains(c.A.Id) || editedShortcuts.Contains(c.B.Id);
            string msg = "快捷键冲突：" + c.Text;
            if (touched) plan.Blockers.Add(msg + "，已阻止写入");
            else plan.Warnings.Add(msg + "（input.conf 里原本如此，本次未改动，不阻止写入）");
        }

        foreach (var cover in d.Shortcuts.CoverList)
        {
            if (!editedShortcuts.Contains(cover.Row.Id)) continue;
            plan.Warnings.Add($"快捷键：{cover.Row.Action} 改为 {ShortcutKeys.Display(cover.Row.Key)} 会覆盖「{cover.Target.Action}」的默认键" +
                              "（强绑定覆盖弱绑定，允许保存）");
        }

        // ==================== 页 A · 无封面信息窗 / Alt+4 屏幕比例 / 单实例（阶段 ⑦） ====================
        // 四层落点（1.6 / 6.13）：
        //   窗口   audio-info.conf + mpv.conf 的 include 行
        //   界面   scripts\MyMPC_ui.lua（EXE 整文件生成，字节相同就不写）
        //   开关   script-opts\MyMPC.conf 与 fit90.conf
        //   固定键 mpv.conf 的 input-ipc-server=MyMPC_mpv（不随开关增删）
        //
        // 与 cover-art 一样，放在其它键算完之后：要等这批改动算完，才知道「用户这次是不是真的在保存」。
        bool uiChanged = baseline.Changed(d, "NoArtUi", "FitRatio", "AllowMultiInstance", "SubAutoReload",
                                          "OsdProgress",
                                          "RtxVsr", "RtxHdr", "RtxVsrLimitIndex", "RtxHdrPeakIndex",
                                          // 1.0.7：缩略图参数改动要触发 thumbnails.conf 重写。
                                          "ThumbCols", "ThumbRows", "ThumbMaxWidth", "ThumbGap");
        // 前提不满足时开关在反推阶段已被归零，baseline 捕获到的就是归零值，diff 看不到
        // 「盘上 yes → 应该 no」这一步。这里直接比 MyMPC.conf 的现值：若静态前提不合格
        // 而盘上还写着 yes，即使 diff 为空也要重写，把残留的 yes 纠正成 no。
        // Windows HDR 不在此列——它是动态的，盘上 yes 是合法值。
        bool DiskOptYes(string key)
            => string.Equals(s.MyMpcOpts.Get(key, "yes"), "yes", StringComparison.OrdinalIgnoreCase);
        bool rtxStale =
            (!d.RtxVsrEnabledForWrite && DiskOptYes("rtx_vsr"))
            || (!d.RtxHdrEnabledForWrite && DiskOptYes("rtx_hdr"));
        uiChanged |= rtxStale;
        bool wiringMissing = !s.MyMpcOpts.Exists || !s.Fit90.Exists || !s.LuaExists
                             || !s.Thumbnails.Exists || !s.Locator.ThumbnailsLuaFileExists
                             || (d.NoArtUi && (!s.AudioInfo.Exists || !m.HasInclude("audio-info.conf")))
                             || !m.Has("input-ipc-server");
        bool pendingChanges = plan.KeyCount > 0 || plan.Registry.HasChanges
                              || plan.WholeFileCount > 0 || plan.InputEditCount > 0;

        if (uiChanged || (wiringMissing && pendingChanges))
        {
            double rawRatio = Math.Round(d.FitRatio);
            if (rawRatio < 50 || rawRatio > 100)
            {
                plan.Blockers.Add($"Alt+4 屏幕比例 {rawRatio:0}% 超出 50–100，已阻止写入");
            }
            else
            {
                int ratio = (int)rawRatio;
                if (Math.Abs(d.FitRatio - ratio) > 0.001)
                    plan.Warnings.Add($"Alt+4 屏幕比例 {d.FitRatio:0.##}% 已取整为 {ratio}%");

                // 只有「要显示的」才建 audio-info.conf；关掉时保留已有文件，只摘 include 行
                if (d.NoArtUi)
                    AddWhole(AudioInfoLabel, s.Locator.AudioInfoConf, s.AudioInfo.Exists, s.AudioInfo.RawText,
                             BuildAudioInfo(), "无封面窗口 profile");

                AddWhole(MyMpcLabel, s.Locator.MyMpcOpts, s.MyMpcOpts.Exists, s.MyMpcOpts.RawText,
                         BuildMyMpcConf(d), "脚本开关");
                AddWhole(FitLabel, s.Locator.Fit90Conf, s.Fit90.Exists, s.Fit90.RawText,
                         BuildFit90(ratio), "Alt+4 屏幕比例");
                AddWhole(LuaLabel, s.Locator.MyMpcLua, s.LuaExists, s.LuaText,
                         LuaSource(), "脚本");

                // 1.0.7：n*m 缩略图脚本 + 参数。mpv.exe 真实路径写进 conf，口径与 Assoc 一致。
                AddWhole(ThumbLabel, s.Locator.ThumbnailsConf, s.Thumbnails.Exists, s.Thumbnails.RawText,
                         BuildThumbnailsConf(d, s.Locator.MpvExe), "缩略图参数");
                string thumbLuaOnDisk = s.Locator.ThumbnailsLuaFileExists
                    ? File.ReadAllText(s.Locator.ThumbnailsLua, Encoding.UTF8) : null;
                AddWhole(ThumbLuaLabel, s.Locator.ThumbnailsLua, s.Locator.ThumbnailsLuaFileExists, thumbLuaOnDisk,
                         ThumbnailsLuaSource(), "缩略图脚本");

                bool hasInclude = m.HasInclude("audio-info.conf");
                if (d.NoArtUi && !hasInclude)
                {
                    plan.For(MpvLabel, mpvPath).Changes.Add(new KeyChange
                    {
                        Key = "include",
                        To = "~~/audio-info.conf",
                        Append = true,
                        AppendOnly = true,   // 与 cover-art 同理：只加自己这一行，不碰别人的 include
                    });
                }
                else if (!d.NoArtUi && hasInclude)
                {
                    plan.For(MpvLabel, mpvPath).Changes.Add(new KeyChange
                    {
                        Key = "include",
                        MatchValue = "~~/audio-info.conf",
                        Remove = true,       // 只删这一行；include 是别人的可能有好几行
                    });
                }

                if (!m.Has("input-ipc-server"))
                {
                    plan.For(MpvLabel, mpvPath).Changes.Add(new KeyChange
                    {
                        Key = "input-ipc-server",
                        To = PipeName,
                        Append = true,
                    });
                }

                if (!uiChanged)
                    plan.Warnings.Add("脚本 / 配置文件不齐（缺 MyMPC.conf、fit90.conf、MyMPC_ui.lua 或 include 行），本次顺带补齐");
            }
        }

        // 缺键追加提示，便于结果窗口解释「为什么会多出一行」
        // include 是天然可缺的（0 行也合法），不放进提示区，否则看起来像个问题
        foreach (var f in plan.Files)
            foreach (var c in f.Changes)
                if (c.Append && !c.AppendOnly && !c.Remove)
                    plan.Warnings.Add($"{f.Label} 缺少 {c.Key}，将追加到文件末尾");

        return plan;

        // 整文件写：内容与磁盘一致就什么都不做（用户没改、或外面已经写成一样）
        void AddWhole(string label, string path, bool exists, string have, string desired, string kind)
        {
            if (exists && Same(have, desired)) return;
            var f = plan.For(label, path);
            f.NewContent = desired;
            f.IsCreate = !exists;
            f.WholeFileKind = kind;
        }
    }

    /// <summary>
    /// 保证 mpv.conf 的 af 链里挂着 bassmax。这是「MPV 专属音效」的落点：
    /// 滤镜常驻，开关只改注册表 Enabled，所以这里只有「补」没有「删」。
    ///
    /// 不整行覆盖——af 是逗号分隔的滤镜链，用户可能挂了 scaletempo2 / lavfi=[...]，
    /// 必须走 AfChain.Merge 增删一节。Transform 让 ConfWriter 写前重读磁盘最新值再合并，
    /// 避免打开设置程序之后用户又手工改过 af 时被快照里的旧值冲掉。
    /// </summary>
    private static void EnsureAfChain(WritePlan plan, ConfigSnapshot s)
    {
        var m = s.Mpv;
        string current = m.Get("af", "");
        string target = AfChain.Merge(current, true);

        // 已含 bassmax：一个字节都不动，保证「只改截图格式」这种操作零额外写入
        if (string.Equals(target, current, StringComparison.Ordinal)) return;

        plan.For(MpvLabel, s.Locator.MpvConf).Changes.Add(new KeyChange
        {
            Key = "af",
            To = target,
            Group = "音效滤镜（常驻）",
            From = m.Has("af") ? current : string.Empty,
            Append = !m.Has("af"),
            Transform = fresh => AfChain.Merge(fresh, true),
        });

        if (!string.IsNullOrWhiteSpace(m.Get("audio-spdif", "")))
            plan.Warnings.Add("检测到 audio-spdif 比特流直通：与 BassMax 互斥，但 af=bassmax 仍按本包约定保留；" +
                              "要旁通请按 Ctrl+b 或把注册表 Enabled 置 0");
    }

    /// <summary>自定义态保护：控件已置灰，但「重置」会清掉标志，必须在写盘前再拦一次，绝不覆盖手工标定值。</summary>
    private static bool Guarded(SettingsDraft d, DraftBaseline baseline, WritePlan plan,
                                string customProp, string what)
    {
        bool wasCustom = baseline.WasCustom(customProp);
        bool isCustom = IsCustom(d, customProp);
        if (!wasCustom && !isCustom) return false;
        plan.Warnings.Add($"打开时检测到非预设值（{what}）：已跳过写入，不覆盖手工设置");
        return true;
    }

    private static bool IsCustom(SettingsDraft d, string name)
    {
        var p = typeof(SettingsDraft).GetProperty(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        return p?.GetValue(d) is bool b && b;
    }

    private static string YesNo(bool v) => v ? "yes" : "no";

    /// <summary>
    /// cover-art.conf 的完整内容（EXE 独占，固定结构）。
    /// profile-cond 只认「当前视频轨本身是图」这一条：实测 not video / video-params==nil 在正常视频上也会误触发；
    /// profile-restore=copy 保证从纯音频切到视频时把封面相关键还原，不改视频播放行为。
    ///
    /// 1.0.8 口径（对齐 MPC-HC）：
    ///   video-unscaled=no        封面随窗口等比缩放 —— 拖大窗口封面跟着变大，只保比例
    ///   autofit-larger=上限      打开时窗口按封面原始尺寸（100%），超过上限才缩
    /// 不设 autofit-smaller：mpv 没有窗口最小尺寸属性，完全对齐 MPC-HC 可拖到 0×0。
    /// 「封面原始大小」开关关闭时，不写 autofit-larger：窗口不再按封面尺寸自适应。
    /// </summary>
    private static string BuildCoverArt(SettingsDraft d, int max, string nl)
    {
        var lines = new List<string>
        {
            "# 由 MPV 设置 EXE 管理：纯音频封面显示，勿手工改动段落名",
            "[cover-art]",
            "profile-cond=p[\"current-tracks/video/image\"] == true",
            "profile-restore=copy",
            // 旧值 downscale-big 只缩不放，拖大窗口只多黑边，已弃用（1.0.7 曾误用它）
            "video-unscaled=no",
        };
        if (d.CoverOriginal)
            lines.Add("autofit-larger=" + max + "x" + max);
        return string.Join(nl, lines) + nl;
    }

    /// <summary>
    /// audio-info.conf 的完整内容（EXE 独占，固定结构）。
    /// 两条都是实测结论，别"优化"回去：
    ///   · 条件里不能带 audio-params：loadfile 期间它会短暂变 nil，条件抖动 → profile 被 Restoring，
    ///     force-window 一起回退 → 窗口销毁重建，就是「单实例转发后窗口回到 960x540」的真正成因；
    ///   · 纯音频在 mpv 原生行为下**根本不创建窗口**，所以必须在 profile 里 force-window=yes。
    /// </summary>
    private static string BuildAudioInfo()
    {
        var lines = new[]
        {
            "# 由 MPV 设置 EXE 管理：无封面音频信息窗，勿手工改动段落名",
            "[no-art]",
            "profile-cond=p[\"current-tracks/video\"] == nil",
            "profile-restore=copy",
            "force-window=yes",
            "geometry=640x180",
            "osd-scale-by-window=no",
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>script-opts\MyMPC.conf：MyMPC_ui.lua 的开关。</summary>
    private static string BuildMyMpcConf(SettingsDraft d)
    {
        int vlim = d.RtxVsrLimitIndex >= 0 && d.RtxVsrLimitIndex < SettingsDraft.RtxVsrLimitValues.Length
            ? d.RtxVsrLimitIndex : 2;
        int hmin = d.RtxMinResIndex >= 0 && d.RtxMinResIndex < SettingsDraft.RtxMinResValues.Length
            ? d.RtxMinResIndex : 2;
        int peakIdx = d.RtxHdrPeakIndex >= 0 && d.RtxHdrPeakIndex < SettingsDraft.RtxHdrPeaks.Length
            ? d.RtxHdrPeakIndex : 3;
        var lines = new[]
        {
            "# 由 MPV 设置 EXE 管理，勿手工改动（改设置请用设置程序）",
            "no_art_ui=" + YesNo(d.NoArtUi),
            "single_instance=" + YesNo(!d.AllowMultiInstance),   // UI 是「允许同时运行多个」，脚本要反过来
            "sub_autoreload=" + YesNo(d.SubAutoReload),          // 外挂字幕改动后自动重读（MyMPC_ui.lua 轮询实现）
            "osd_progress=" + YesNo(d.OsdProgress),              // 顶部细横线进度条（MyMPC_ui.lua overlay，默认开）
            // 1.0.2：NVIDIA RTX 视频增强。Lua 侧判断 Windows HDR + 分辨率上限后才真正挂 vf。
            // 静态前提（NVIDIA App 总开关 / 硬件）不满足时用 EnabledForWrite 强制写 no——
            // 草稿层已把开关归零，这里再兜一次底，防止 --default / 导入 / patch 绕过 GUI 时漏写。
            // Windows HDR 不参与：它是动态的，保留 yes，Lua 按 video-target-params 自己判。
            "rtx_vsr=" + YesNo(d.RtxVsrEnabledForWrite),
            "rtx_hdr=" + YesNo(d.RtxHdrEnabledForWrite),
            "rtx_vsr_limit=" + SettingsDraft.RtxVsrLimitValues[vlim],
            "rtx_min_res=" + SettingsDraft.RtxMinResValues[hmin],
            "rtx_hdr_peak=" + SettingsDraft.RtxHdrPeaks[peakIdx].ToString(CultureInfo.InvariantCulture),
            "pipe_name=" + PipeName,
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// script-opts\fit90.conf：Alt+4 的屏幕比例（原 fit90.lua 把 0.9 写死在代码里）。
    /// 只有 ratio 一项：这个键只在按 Alt+4 时用到，不参与「打开文件时的窗口尺寸」。
    /// </summary>
    private static string BuildFit90(int ratio)
    {
        var lines = new[]
        {
            "# 由 MPV 设置 EXE 管理，勿手工改动（改设置请用设置程序）",
            "# Alt+4：窗口 = 屏幕可用区域的 ratio%，只在按键时生效",
            "ratio=" + ratio.ToString(CultureInfo.InvariantCulture),
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string _lua;
    private static string _thumbLua;

    /// <summary>1.0.7：内嵌的 scripts\thumbnails.lua，整文件写回（同 MyMPC_ui.lua 手法）。</summary>
    private static string ThumbnailsLuaSource()
    {
        if (_thumbLua is not null) return _thumbLua;
        var asm = typeof(WritePlanBuilder).Assembly;
        using var st = asm.GetManifestResourceStream("MpvSettings.thumbnails.lua")
            ?? throw new InvalidOperationException("内嵌的 thumbnails.lua 丢失（打包问题），拒绝写空脚本");
        using var sr = new StreamReader(st, Encoding.UTF8);
        _thumbLua = sr.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        return _thumbLua;
    }

    /// <summary>1.0.7：script-opts\thumbnails.conf。mpv.exe 路径由 ConfigLocator 探测写入。</summary>
    private static string BuildThumbnailsConf(SettingsDraft d, string mpvExe)
    {
        string norm = (mpvExe ?? string.Empty).Replace("\\", "/");
        var lines = new[]
        {
            "# 由 MPV 设置 EXE 管理，勿手工改动（改设置请用设置程序）",
            "# n*m 缩略图：总宽上限反推单格宽，按视频显示比例算高（对齐 MPC-HC）",
            "cols=" + Math.Max(1, Math.Min(8, d.ThumbCols)).ToString(CultureInfo.InvariantCulture),
            "rows=" + Math.Max(1, Math.Min(8, d.ThumbRows)).ToString(CultureInfo.InvariantCulture),
            "max_width=" + Math.Round(Math.Max(300, Math.Min(4000, d.ThumbMaxWidth))).ToString(CultureInfo.InvariantCulture),
            "gap=" + Math.Max(0, Math.Min(20, d.ThumbGap)).ToString(CultureInfo.InvariantCulture),
            "thumb_dir=",
            "font=",
            // 探测到的真实路径，口径与 Assoc 的 <App Paths 根>\MediaPlayer\mpv\mpv.exe 一致；
            // 空时 Lua 回退 ~~/../mpv.exe。
            "mpv_exe=" + norm,
        };
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>
    /// 内嵌的 scripts\MyMPC_ui.lua。整文件生成，不做 diff：它是 EXE 独占的脚本，
    /// 逐行替换反而会把「脚本被手工改过」这种状态带进下一次运行。
    /// </summary>
    private static string LuaSource()
    {
        if (_lua is not null) return _lua;
        var asm = typeof(WritePlanBuilder).Assembly;
        using var st = asm.GetManifestResourceStream("MpvSettings.MyMPC_ui.lua")
            ?? throw new InvalidOperationException("内嵌的 MyMPC_ui.lua 丢失（打包问题），拒绝写空脚本");
        using var sr = new StreamReader(st, Encoding.UTF8);
        _lua = sr.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n') + "\n";
        return _lua;
    }

    /// <summary>换行与结尾空行不计入差异：外部编辑器另存为 CRLF 不该触发重写。</summary>
    private static bool Same(string a, string b)
        => string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);

    private static string Norm(string t) => (t ?? string.Empty).Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>正斜杠 + 结尾斜杠（与 MpvNormPath 一致）。桌面 / 图片走系统 API，兼容 OneDrive 重定向。</summary>
    private static string ResolveShotDir(SettingsDraft d, string configDir, out string error)
    {
        error = string.Empty;
        string raw;
        switch (d.ShotDirIndex)
        {
            case 0: raw = Environment.GetFolderPath(Environment.SpecialFolder.Desktop); break;
            case 1: raw = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures); break;
            case 2: raw = configDir; break;
            case 3: raw = d.ShotDirPath; break;
            default:
                error = "截图保存位置下拉索引越界，已阻止写入";
                return string.Empty;
        }

        raw = (raw ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            error = "截图保存位置为空，已阻止写入";
            return string.Empty;
        }
        if (!Path.IsPathFullyQualified(raw))
        {
            error = "截图保存位置必须是绝对路径（mpv 不展开 ~~desktop/ 之类的相对路径）";
            return string.Empty;
        }
        return raw.Replace('\\', '/').TrimEnd('/') + "/";
    }

    internal static string NormPath(string p)
        => string.IsNullOrWhiteSpace(p) ? string.Empty : p.Replace('\\', '/').TrimEnd('/') + "/";
}

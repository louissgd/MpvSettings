using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MpvSettings.Controls;
using MpvSettings.Models;
using MpvSettings.Services;
using MpvSettings.Views;

namespace MpvSettings;

public partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _pages = new();
    private readonly ConfigLocator _locator;
    private readonly SettingsDraft _draft;
    private ConfigSnapshot _snapshot;       // 「应用」成功后重新读盘，这里要能换
    private DraftBaseline _baseline;        // 同上：写完把基线推进到磁盘现状
    private string _currentPage = "播放";

    private static readonly Dictionary<string, (string Hint, string Config)> PageMeta = new()
    {
        ["播放"]   = ("常规播放、纯音频封面 / 信息窗、Alt+4 屏幕缩放与单实例", "mpv.conf · audio-info.conf"),
        ["画面"]   = ("HDR 与色调映射、拖动窗口时的比例行为", "mpv.conf · hdr-autoswitch.conf"),
        ["音频"]   = ("BassMax 音效：滤镜常驻，开关只改注册表", "mpv.conf · HKCU\\Software\\BassMax"),
        ["截图"]   = ("截图格式、位置与高位深", "mpv.conf"),
        ["快捷键"] = ("点击键位框录键 · 只写改动过的行", "input.conf"),
        ["关于"]   = ("版本与配置备份", "MyMPC · MPV 简单设置"),
    };

    public MainWindow()
    {
        InitializeComponent();

        // 标题栏副标题显示版本号（csproj 的 InformationalVersion）。
        VersionLabel.Text = "· v" + AppVersion;

        // 打开时：定位 → 快照 → 反推 → 记下「打开时的草稿」。
        // 阶段 ③ 的写盘计划就是这个 baseline 与当前草稿的 diff。
        string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        _locator = ConfigLocator.Resolve(args);

        // 右键菜单「打开本地文件」的路径自检：menu.conf 随包发的是打包机路径，
        // 换安装目录 / 换机器后 mpv 的 run 会去拉一个不存在的 Assoc.exe，点了没反应。
        // 这里按当前安装根目录静默改回；路径本来就对时不动文件。
        // 放在快照之前：后面读 menu.conf 的地方看到的已经是修正后的内容。
        MenuConfPath.Fix(_locator);

        _snapshot = new ConfigSnapshot(_locator);
        _draft = new SettingsDraft();
        DataContext = _draft;
        _draft.LoadFrom(_snapshot);
        _baseline = DraftBaseline.Capture(_draft);

        // 验证用：窗口显示前预置「用户改过的控件」，便于真实窗口截屏断言结果对话框
        string uiPatch = ArgValue(args, "--patch-ui=");
        if (uiPatch.Length > 0 && File.Exists(uiPatch))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(uiPatch));
            DraftPatch.Apply(_draft, doc.RootElement);
        }

        // 验证用：导出反推结果（只写用户显式指定的调试文件，绝不碰配置目录）
        string dump = ArgValue(args, "--dump-draft=");
        if (dump.Length > 0) WriteJson(dump, _draft);

        _pages["播放"]   = new PlaybackView();
        _pages["画面"]   = new VideoView();
        _pages["音频"]   = new AudioView();
        _pages["截图"]   = new ScreenshotView();
        _pages["快捷键"] = new ShortcutsView();   // 绑定 DataContext.Shortcuts（阶段 ⑥ 起真正参与草稿与写盘）
        _pages["关于"]   = new AboutView();

        if (!_locator.IsUsable)
        {
            DisabledOverlay.Visibility = Visibility.Visible;
            DisabledReason.Text = _locator.Reason;
            PageHost.IsEnabled = false;
            SearchBox.IsEnabled = false;
            ResetAllBtn.IsEnabled = false;
            ResetPageBtn.IsEnabled = false;
            OkBtn.IsEnabled = false;
            ApplyBtn.IsEnabled = false;
        }

        NavPlay.IsChecked = true;   // 触发 ShowPage("播放")

        // 验证用：把窗口内容按当前缩放渲染成 PNG 后退出。
        // 走 RenderTargetBitmap 而不是抓屏：远程 / 锁屏 / 无显示器会话里抓屏拿不到
        // WPF 的合成结果（实测整窗位置只有桌面壁纸），而视觉树渲染是确定的。
        string shot = ArgValue(args, "--shot=");
        if (shot.Length > 0)
        {
            string page = ArgValue(args, "--shot-page=");
            // 验证用：--shot-size=900x600 把窗口改到指定尺寸再渲染（默认按 XAML 的 1000x680）。
            // 用来确认「最小尺寸下排版会不会被裁」，不必手拖窗口。
            ApplyShotSize(ArgValue(args, "--shot-size="));
            Loaded += (_, _) =>
            {
                // 等一帧渲染完再截：Loaded 当下窗口还没画完，直接渲染会拿到半成品
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(400)
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (page.Length > 0) ShowPage(page);
                    UpdateLayout();
                    SaveVisual(shot);
                    Application.Current.Shutdown();
                };
                timer.Start();
            };
        }
    }

    /// <summary>验证用：把窗口改到指定尺寸（WxH，DIP）。小于 MinWidth/MinHeight 时 WPF 会自己夹回去。</summary>
    private void ApplyShotSize(string spec)
    {
        string[] parts = spec.Split('x', 'X');
        if (parts.Length != 2) return;
        if (double.TryParse(parts[0], out double w) && w > 0) Width = w;
        if (double.TryParse(parts[1], out double h) && h > 0) Height = h;
    }

    /// <summary>把窗口内容按实际 DPI 渲染到 PNG（验证用，不参与业务）。</summary>
    private void SaveVisual(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        int w = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        int h = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        var bmp = new RenderTargetBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bmp.Render(this);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path))
            encoder.Save(fs);

        File.WriteAllText(path + ".txt",
            $"actual={ActualWidth}x{ActualHeight} dpi={dpi.DpiScaleX}x{dpi.DpiScaleY} px={w}x{h}",
            new UTF8Encoding(false));
    }

    /// <summary>csproj 里的 InformationalVersion，用于标题栏显版本号。</summary>
    private static string AppVersion
    {
        get
        {
            var asm = typeof(MainWindow).Assembly;
            var attr = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                          .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault();
            string v = attr?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(v)) v = asm.GetName().Version?.ToString() ?? "0.0.0";
            int plus = v.IndexOf('+');
            return (plus > 0 ? v.Substring(0, plus) : v).Trim();
        }
    }

    private static string ArgValue(string[] args, string prefix)
    {
        foreach (var a in args)
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return a.Substring(prefix.Length).Trim().Trim('"');
        return string.Empty;
    }

    private static int ArgInt(string[] args, string prefix)
        => int.TryParse(ArgValue(args, prefix), out var n) ? n : 0;

    private static bool HasFlag(string[] args, string flag)
        => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static void WriteJson(string path, object value)
    {
        var json = JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private void ShowPage(string key)
    {
        if (!_pages.TryGetValue(key, out var page)) return;
        _currentPage = key;

        PageHost.Content = page;
        // 关于页的备份目录是代码填的（XAML 里只是占位），切进来就刷新一次，
        // 不依赖 Loaded 事件——截图钩子会在 Loaded 派发前就渲染。
        if (page is AboutView about) about.RefreshDir();
        // 着色器缓存是实况数据（数量/体积随时在变），切进画面页就重算一次
        if (page is VideoView video) video.RefreshCache();
        PageScroll.ScrollToTop();   // 共享一个 ScrollViewer，切页不能把上一页的滚动位置带过去
        PageTitle.Text = key;
        if (PageMeta.TryGetValue(key, out var meta))
        {
            PageHint.Text = meta.Hint;
            ConfigLabel.Text = meta.Config;
        }

        // 页 E / 关于页没有 OptionRow，搜索对它们无意义（1.3 明确不做页内搜索）
        bool searchable = key is not ("快捷键" or "关于");
        if (_locator.IsUsable) SearchBox.IsEnabled = searchable;
        SearchPlaceholder.Opacity = searchable && _locator.IsUsable ? 1.0 : 0.45;

        // 视觉树刚换，等一次布局再过滤
        Dispatcher.BeginInvoke(new Action(ApplySearch), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ==================== 搜索：只做行过滤 ====================

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (PageHost.Content is not DependencyObject root) return;
        string q = SearchBox.Text?.Trim() ?? string.Empty;

        foreach (var card in FindVisualChildren<SettingCard>(root))
        {
            var rows = FindVisualChildren<OptionRow>(card).ToList();
            if (rows.Count == 0) continue;   // 快捷键页卡片由 Border 行组成，不参与过滤

            foreach (var row in rows)
            {
                bool hit = q.Length == 0 ||
                           (row.Title + " " + row.Description)
                           .IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                row.Visibility = hit ? Visibility.Visible : Visibility.Collapsed;
            }
            card.Visibility = rows.Any(r => r.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }

    // ==================== 重置（只改内存草稿） ====================

    private void ResetAll_Click(object sender, RoutedEventArgs e) => ResetAll();
    private void ResetPage_Click(object sender, RoutedEventArgs e) => ResetPage();

    public void ResetAll() => _draft.ResetAll();
    public void ResetPage() => _draft.ResetPage(_currentPage);

    // ==================== 只读查看动作 ====================

    public void OpenMpvConf() => OpenPath(_locator.MpvConf);
    public void OpenConfigDir() => OpenPath(_locator.ConfigDir);

    /// <summary>逃生口：拉起 BassMax 的 DShow 属性页（路径在 ConfigLocator 里解析，找不到就不动作）。</summary>
    public void OpenBassMaxPanel()
    {
        string ax = _locator.BassMaxPanel;
        if (ax.Length == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", $"\"{ax}\",BassMaxConfig") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 面板拉不起来不影响写盘，静默即可
        }
    }

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开就算了，不因为外壳关联缺失而中断设置窗口
        }
    }

    // ==================== 阶段 ③：diff 写盘 ====================

    public WritePlan BuildPlan() => WritePlanBuilder.Build(_draft, _baseline, _snapshot);

    public WriteResult ApplyDraft(ConfWriterOptions opt = null)
    {
        var plan = BuildPlan();
        return plan.CanApply ? ConfWriter.Apply(plan, opt) : WriteResult.Blocked(plan);
    }

    /// <summary>
    /// 1.0.5 字幕样式弹窗「完成」专用：只把「字幕默认样式（仅 SRT）」这一组键写盘。
    ///
    /// 不刷新 baseline —— 主界面其他页的未保存改动必须保留 diff，等用户自己点「应用/确定」再写。
    /// 过滤其他组很重要：否则弹窗顺手把主界面的改动也写了，用户没机会预览。
    /// </summary>
    public void ApplySubtitleGroup(Window owner = null)
    {
        const string group = "字幕默认样式（仅 SRT）";
        var full = BuildPlan();
        var sub = new WritePlan();
        foreach (var f in full.Files)
        {
            var hits = f.Changes.Where(c => c.Group == group).ToList();
            if (hits.Count == 0) continue;
            var pf = new FilePlan { Path = f.Path, Label = f.Label };
            foreach (var c in hits) pf.Changes.Add(c);
            sub.Files.Add(pf);
        }

        var result = sub.CanApply ? ConfWriter.Apply(sub) : WriteResult.Blocked(sub);
        if (result.Success)
            _baseline.AdvanceSubtitleStyle(_draft);  // 只推进字幕组，其他页 diff 保留
        var dialog = new ApplyResultWindow(result, sub, closeOnSuccess: false)
        {
            Owner = owner ?? this,
        };
        dialog.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => WriteAndExit(closeOnSuccess: true);

    private void Apply_Click(object sender, RoutedEventArgs e) => WriteAndExit(closeOnSuccess: false);

    /// <summary>
    /// 左下角「打开 MPV 播放器」：先关掉所有 mpv.exe，再重新开一个空闲窗口。
    ///
    /// 必须先关干净：MyMPC_ui.lua 的单实例管道会把第二个实例的文件 / 命令转发给第一个，
    /// 旧进程还活着时新实例只会转发然后自己退出，配置也不会重读。先 CloseMainWindow
    /// 让它正常收尾（quit 时不记进度，出厂是 save-position-on-quit=no），1.2s 后仍不退出
    /// 才 Kill（--vo=null / 无窗口时 CloseMainWindow 可能没反应）。
    /// 只关 mpv.exe，不动 MPC-HC / PotPlayer——那是 setting.exe 的职责。
    /// 1.0.4 起同时按 mplayerl.exe 的行为拉起 BassMax 托盘（见 StartBassMaxTray）。
    /// </summary>
    private void RestartMpv_Click(object sender, RoutedEventArgs e) => RestartMpv();

    /// <summary>
    /// 对齐 mplayerl.exe：播放器启动时先把 BassMax 托盘拉起来，带 --follow mpv.exe。
    /// 托盘按进程名跟随，播放器退出后自己收工；重复启动被 Local\BassMaxTraySingleton
    /// 互斥体挡掉，所以连点也只有一个图标。找不到 exe 时安静跳过（与 mplayerl 一致）。
    /// 放在 mpv 之前启动：图标先出现，且托盘有 30 s 等待播放器起来的宽限。
    /// </summary>
    private void StartBassMaxTray()
    {
        string tray = _locator.BassMaxTray;
        if (tray.Length == 0 || !File.Exists(tray)) return;
        try
        {
            Process.Start(new ProcessStartInfo(tray)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(tray) ?? string.Empty,
                Arguments = "--follow \"mpv.exe\"",
            });
        }
        catch (Exception)
        {
            // 拉不起来不影响主流程，与 OpenPath / OpenBassMaxPanel 一致
        }
    }

    /// <summary>诊断入口：用 --no-config 起一个不读任何配置的 mpv，判断问题在配置还是程序本体。</summary>
    public void OpenMpvNoConfig()
    {
        string exe = _locator.MpvExe;
        if (exe.Length == 0 || !File.Exists(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                // --no-config 屏蔽全部配置（含 portable_config 与脚本）；不传 --config-dir。
                // 保留 idle + force-window，没拖片时也有窗口，便于观察默认画面。
                Arguments = "--no-config --idle=yes --force-window=yes",
            });
        }
        catch (Exception)
        {
            // 与 RestartMpv 一致：拉不起来不影响设置窗口
        }
    }

    public void RestartMpv()
    {
        string exe = _locator.MpvExe;
        if (exe.Length == 0 || !File.Exists(exe)) return;

        Process[] running = Process.GetProcessesByName("mpv");
        foreach (var p in running)
        {
            try { if (!p.HasExited) p.CloseMainWindow(); } catch { }
        }
        foreach (var p in running)
        {
            try { if (!p.HasExited && !p.WaitForExit(1200)) p.Kill(); } catch { }
        }

        // 托盘先于播放器启动；缺失 / 启动失败都在 StartBassMaxTray 内部静默处理
        StartBassMaxTray();

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
                // 无文件时也留住窗口；显式传配置目录，避免 --config-dir 指向副本时
                // 新起来的 mpv 却去读真实安装目录里的 portable_config
                Arguments = "--idle=yes --force-window=yes --config-dir=\"" + _locator.ConfigDir + "\"",
            });
        }
        catch (Exception)
        {
            // 拉不起来不影响设置窗口，与 OpenPath / OpenBassMaxPanel 一致
        }
    }

    // ==================== 阶段 ⑩b：配置备份 / 恢复（入口在关于页） ====================

    /// <summary>记住的备份文件夹；首次是桌面\MyMPC-MPV-备份。</summary>
    public string BackupDir => BackupService.RememberedDir();

    private void SetBackupStatus(string text, bool ok)
        => (_pages["关于"] as AboutView)?.SetStatus(text, ok);

    public void BackupPickDir()
    {
        try
        {
            string dir = BackupDir;
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "选择 MPV 配置备份文件夹",
                InitialDirectory = Directory.Exists(dir)
                    ? dir
                    : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            if (dlg.ShowDialog(this) == true)
            {
                BackupService.RememberDir(dlg.FolderName);
                SetBackupStatus("备份文件夹已改为 " + dlg.FolderName + "（导出会覆盖该目录里上一次的备份）", true);
            }
        }
        catch (Exception ex)
        {
            SetBackupStatus("选择文件夹失败：" + ex.Message, false);
            NoticeWindow.Inform(this, "备份文件夹", "无法选择文件夹", ex.Message, null, false);
        }
    }

    public void BackupOpenDir()
    {
        string dir = BackupDir;
        try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
        catch (Exception) { /* 打不开就交给 OpenPath 静默处理 */ }
        OpenPath(dir);
    }

    public void BackupExport()
    {
        var r = BackupService.Export(new BackupService.Options { Dir = BackupDir, Locator = _locator });
        ShowBackupResult("导出", r);
    }

    // ==================== 着色器缓存（画面页那一行） ====================
    // 语义与 00.Core\setting.ahk 完全一致：只碰 cache\shader_* 与 cache\icc_*，
    // 删掉只是下次播放重新编译，不改任何设置。文案里的数字两边必须对得上。

    // 目录不用自己拼：ConfigLocator.CacheDir 就是 <ConfigDir>\cache，
    // 与 setting.ahk 的 MpvCacheDir() 同一个位置。
    public ShaderCacheService.StatResult CacheStat() => ShaderCacheService.Stat(_locator.CacheDir);

    public void ClearShaderCache()
    {
        var stat = CacheStat();
        if (stat.IsEmpty)
        {
            NoticeWindow.Inform(this,
                "MPV 简单设置 · 清理着色器缓存",
                "缓存已经是空的",
                "没有可删除的 shader_* 或 icc_* 文件。",
                new[] { "缓存目录：" + _locator.CacheDir },
                true);
            return;
        }

        bool yes = NoticeWindow.Confirm(this,
            "MPV 简单设置 · 清理着色器缓存",
            "确认删除 " + stat.Files + " 个缓存文件？",
            "共 " + stat.Megabytes.ToString("0.#") + " MB，位于：" + _locator.CacheDir,
            new[]
            {
                "只删 shader_* 与 icc_* 两类文件，cache 目录里其他文件一个都不动。",
                "删掉后下次播放会重新编译着色器，首帧可能略慢。",
                "不改变任何设置；mpv 正在播放时被占用的文件可能删不掉，关掉播放器再清一次即可。",
            }, okLabel: "确认删除");
        if (!yes) return;

        var r = ShaderCacheService.Purge(_locator.CacheDir);
        var details = new List<string>
        {
            "已删除 " + r.Deleted + " 个文件，释放 " + (r.FreedBytes / 1048576.0).ToString("0.#") + " MB",
        };
        if (r.Failed > 0)
            details.Add(r.Failed + " 个文件被占用（mpv 正在播放），关掉播放器后可再清一次");

        NoticeWindow.Inform(this,
            "MPV 简单设置 · 清理着色器缓存",
            r.Failed == 0 ? "清理完成" : "部分文件被占用",
            r.Deleted > 0 ? "缓存已清理，下次播放会自动重新生成。" : "没有文件被删除。",
            details,
            r.Failed == 0);
    }

    public void BackupImport()
    {
        string dir = BackupDir;
        bool yes = NoticeWindow.Confirm(this,
            "MPV 简单设置 · 导入配置",
            "确认覆盖当前 MPV 设置？",
            "备份目录：" + dir + "\n\n将按 diff 只写受管键，不整目录覆盖；窗口里未应用的改动会丢失。",
            new[]
            {
                "导入前自动把现场快照到：" + Path.Combine(dir, BackupService.SnapshotFolder) + "（只保留一份）",
                "清单或任一文件 SHA256 校验不过会直接拒导，一个字节都不写。",
                "af=bassmax 强制保留；导入后请用左下角「打开 MPV 播放器」重启生效。",
            });
        if (!yes) return;

        var r = BackupService.Import(new BackupService.Options { Dir = dir, Locator = _locator });
        if (r.Success) ReloadDraftFromDisk();
        ShowBackupResult("导入", r);
    }

    private void ShowBackupResult(string verb, BackupService.Result r)
    {
        SetBackupStatus(r.Message, r.Success);
        // 用程序自带的墨绿主题窗口，不用系统 MessageBox（白底跟主题不搭）
        NoticeWindow.Inform(this,
            "MPV 简单设置 · " + verb,
            verb + (r.Success ? "完成" : "失败"),
            r.Message,
            r.Details.Take(8),
            r.Success,
            !r.Success ? "本次未完成，配置目录保持原样，可按提示重试。"
            : verb == "导入" ? "导入写入的配置需重启 MPV 生效（左下角「打开 MPV 播放器」）。"
                             : "固定覆盖，只保留这一份；可以关掉本窗口继续编辑。");
    }

    /// <summary>导入成功后把草稿 / 快照 / 基线全部推进到新的磁盘现状。</summary>
    private void ReloadDraftFromDisk()
    {
        _snapshot = new ConfigSnapshot(_locator);
        _draft.LoadFrom(_snapshot);
        _draft.RefreshReadOnly(_snapshot);
        _baseline = DraftBaseline.Capture(_draft);
    }

    /// <summary>
    /// 确定 = 写完关闭；应用 = 写完留着窗口继续改。
    /// 两者共用同一条写盘路径（校验 → 事务写 → 结果窗口），只有「成功之后做什么」不同。
    /// </summary>
    private void WriteAndExit(bool closeOnSuccess)
    {
        var plan = BuildPlan();
        var result = plan.CanApply ? ConfWriter.Apply(plan) : WriteResult.Blocked(plan);

        var dialog = new ApplyResultWindow(result, plan, closeOnSuccess) { Owner = this };
        dialog.ShowDialog();

        // 校验被拦 / 写入失败：留着窗口，改完还能重试（两种按钮都一样）
        if (!result.Success) return;

        if (closeOnSuccess)
        {
            Close();
            return;
        }

        // 应用成功：重新读盘，把「磁盘基线」推进到刚写下的内容。
        // 否则 baseline 还是打开窗口时的旧值，同一次改动会被当成 diff 反复提交。
        // 只刷新只读状态文本，界面上的编辑值一律不动。
        _snapshot = new ConfigSnapshot(_locator);
        _draft.RefreshReadOnly(_snapshot);
        _baseline = DraftBaseline.Capture(_draft);
    }

    /// <summary>无窗口验证入口：不弹任何对话框，结果写进 JSON 供脚本断言。</summary>
    public void RunCli(string[] args)
    {
        // --apply-patch 模拟「用户在界面上改了这些控件」
        string patch = ArgValue(args, "--apply-patch=");
        if (patch.Length > 0 && File.Exists(patch))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(patch));
            DraftPatch.Apply(_draft, doc.RootElement);
        }

        // --apply-delay-ms 用于验证「写前重读」：启动后由外部脚本改盘，延迟结束再写
        int delay = ArgInt(args, "--apply-delay-ms=");
        if (delay > 0) Thread.Sleep(delay);

        var plan = BuildPlan();
        string planOut = ArgValue(args, "--dump-plan=");
        if (planOut.Length > 0) WriteJson(planOut, plan);

        if (!HasFlag(args, "--apply-only")) return;

        var opt = new ConfWriterOptions { FailAfter = ArgInt(args, "--apply-fail-after=") };
        var result = plan.CanApply ? ConfWriter.Apply(plan, opt) : WriteResult.Blocked(plan);
        string resultOut = ArgValue(args, "--dump-result=");
        if (resultOut.Length > 0) WriteJson(resultOut, result);
        if (!result.Success) Environment.ExitCode = 1;
    }

    // ==================== 导航与窗口 ====================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string key) ShowPage(key);
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        // 双击标题栏 = 最大化 / 还原（与系统标题栏行为一致）
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // 最大化时窗口位置由系统托管，直接 DragMove 会把窗口拖到屏幕外。
            // 先还原成 Normal，并把窗口左边缘对齐到「鼠标在标题栏里的相对位置」，
            // 视觉上就等于从标题栏把窗口拽下来。
            Point local = e.GetPosition(this);
            Point screen = PointToScreen(local);
            double ratio = ActualWidth > 0 ? Math.Min(1, Math.Max(0, local.X / ActualWidth)) : 0.5;
            double offsetY = local.Y;

            WindowState = WindowState.Normal;
            Rect rb = RestoreBounds;
            Left = screen.X - rb.Width * ratio;
            Top = screen.Y - offsetY;
        }

        DragMove();
    }

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        bool max = WindowState == WindowState.Maximized;

        IconMax.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
        IconRestore.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
        MaxBtn.ToolTip = max ? "还原" : "最大化";
        // 不要在这里补 Margin：WM_GETMINMAXINFO 已把窗口钉在工作区上，
        // 再内缩一圈只会在四周露出一条 AppBrush 底色（实测顶边就是这一条）。
    }

    // ==================== 最大化边界 ====================
    // WindowStyle=None 的窗口点最大化时，WPF 会按「整个显示器」而不是「工作区」算尺寸，
    // 结果窗口比工作区每边多出约 8px：底下盖住任务栏、四周内容被顶到屏幕外。
    // 接管 WM_GETMINMAXINFO，用 MonitorFromWindow 找到所在显示器的工作区，把
    // 最大化位置与尺寸钉死在工作区里。多显示器 / 任务栏在侧面也一起正确。
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT32
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT32
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT32 Reserved;
        public POINT32 MaxSize;
        public POINT32 MaxPosition;
        public POINT32 MinTrackSize;
        public POINT32 MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT32 rcMonitor;
        public RECT32 rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        // 外框圆角：见 Services/WindowCorners.cs（Win11 生效，Win10 自动忽略）
        WindowCorners.Apply(handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            ApplyWorkAreaBounds(hwnd, lParam);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ApplyWorkAreaBounds(IntPtr hwnd, IntPtr lParam)
    {
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor != IntPtr.Zero)
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                RECT32 work = info.rcWork, screen = info.rcMonitor;
                mmi.MaxPosition.X = work.Left - screen.Left;
                mmi.MaxPosition.Y = work.Top - screen.Top;
                mmi.MaxSize.X = work.Right - work.Left;
                mmi.MaxSize.Y = work.Bottom - work.Top;
                mmi.MaxTrackSize.X = mmi.MaxSize.X;
                mmi.MaxTrackSize.Y = mmi.MaxSize.Y;
            }
        }

        // 最小尺寸必须在这里自己钉。hook 里把 handled 置 true 之后，WPF 对
        // WM_GETMINMAXINFO 的处理整段被跳过——它原本负责把 Window.MinWidth /
        // MinHeight 换算成 ptMinTrackSize，跳过后 XAML 里写的 880x540 形同虚设，
        // 实测能被拖到 600 出头，标题、提示文字和右侧按钮组全被裁掉。
        // MinWidth / MinHeight 是 DIP，ptMinTrackSize 是物理像素，按当前窗口 DPI 换算。
        // 每次 WM_GETMINMAXINFO 都重算：跨显示器拖动 / 改缩放后尺寸会跟着变。
        double scale = CurrentDpiScale(hwnd);
        int minW = (int)Math.Round((double.IsNaN(MinWidth) ? 0 : MinWidth) * scale);
        int minH = (int)Math.Round((double.IsNaN(MinHeight) ? 0 : MinHeight) * scale);
        if (mmi.MinTrackSize.X < minW) mmi.MinTrackSize.X = minW;
        if (mmi.MinTrackSize.Y < minH) mmi.MinTrackSize.Y = minH;
        // 小屏 / 高缩放时工作区可能比下限还小，别让 MaxTrackSize 小于 MinTrackSize
        if (mmi.MaxTrackSize.X < mmi.MinTrackSize.X) mmi.MaxTrackSize.X = mmi.MinTrackSize.X;
        if (mmi.MaxTrackSize.Y < mmi.MinTrackSize.Y) mmi.MaxTrackSize.Y = mmi.MinTrackSize.Y;

        Marshal.StructureToPtr(mmi, lParam, true);
    }

    /// <summary>窗口当前 DPI 缩放（1.0 = 100%）。取不到就走 WPF 自己的 DPI 值。</summary>
    private double CurrentDpiScale(IntPtr hwnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hwnd);
            if (dpi > 0) return dpi / 96.0;
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
        return VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}

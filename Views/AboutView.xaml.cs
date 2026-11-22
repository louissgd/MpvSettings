using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MpvSettings.Services;

namespace MpvSettings.Views;

/// <summary>
/// 关于页的「配置备份 / 恢复」入口。真正的逻辑在 Services\BackupService.cs，
/// 这里只负责把按钮转发给主窗口（目录记忆、消息框、导入后刷新草稿都在那边）。
/// </summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshDir();
    }

    private MainWindow Host => Window.GetWindow(this) as MainWindow;

    /// <summary>
    /// 直接读静态偏好，不依赖 Window.GetWindow：--shot-page=关于 会在 Loaded 事件
    /// 还没派发时就渲染截图，靠 Loaded 刷新的话会留下 XAML 里的占位「—」。
    /// ShowPage 切到关于页时也会主动调一次。
    /// </summary>
    public void RefreshDir() => BackupDirText.Text = BackupService.RememberedDir();

    public void SetStatus(string text, bool ok)
    {
        BackupStatus.Text = text;
        var brush = Application.Current?.TryFindResource(ok ? "AccentBrush" : "DangerBrush") as Brush;
        if (brush is not null) BackupStatus.Foreground = brush;
        RefreshDir();
    }

    private void BackupExport_Click(object sender, RoutedEventArgs e) => Host?.BackupExport();
    private void BackupImport_Click(object sender, RoutedEventArgs e) => Host?.BackupImport();

    private void BackupPick_Click(object sender, RoutedEventArgs e)
    {
        Host?.BackupPickDir();
        RefreshDir();
    }

    private void BackupOpen_Click(object sender, RoutedEventArgs e) => Host?.BackupOpenDir();
}

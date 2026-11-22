using System.Windows;
using System.Windows.Controls;

namespace MpvSettings.Views;

/// <summary>
/// 画面页。除了绑定草稿的部分，只有「着色器缓存」一行是实况数据：
/// 数量与体积每次切进本页时重新统计，清理动作转发给主窗口（弹窗、刷新都在那边）。
/// </summary>
public partial class VideoView : UserControl
{
    public VideoView() => InitializeComponent();

    private MainWindow Host => Window.GetWindow(this) as MainWindow;

    /// <summary>
    /// 重新统计缓存目录。ShowPage 切到本页时由主窗口调用，
    /// 清理完成后也调用一次 —— 不依赖 Loaded，否则 --shot-page 截图会拿到占位文字。
    /// </summary>
    public void RefreshCache()
    {
        var host = Host;
        if (host is null) return;
        var stat = host.CacheStat();
        CacheRow.Description = Services.ShaderCacheService.InfoText(stat);
        // 空的时候按钮灰掉：没有文件可删，点下去只会弹一个「已经是空的」
        ClearCacheBtn.IsEnabled = !stat.IsEmpty;
    }

    private void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        Host?.ClearShaderCache();
        RefreshCache();
    }
}

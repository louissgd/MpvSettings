using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MpvSettings.Models;

namespace MpvSettings;

public partial class ApplyResultWindow : Window
{
    /// <param name="closeOnSuccess">true = 「确定」写完关闭；false = 「应用」写完留着设置窗口</param>
    public ApplyResultWindow(WriteResult result, WritePlan plan, bool closeOnSuccess = true)
    {
        InitializeComponent();

        bool blocked = result.Blockers.Count > 0 || plan.Blockers.Count > 0;
        Headline.Text = blocked ? "校验未通过"
                      : !result.Success ? "写入失败（已回滚）"
                      : plan.HasChanges ? "写入完成" : "无需写入";
        Summary.Text = result.Success ? result.Summary : result.Error;

        if (blocked)
            DetailList.ItemsSource = new List<string> { "校验未通过，本次所有改动均未写入，配置文件保持原样。" };
        else
            DetailList.ItemsSource = result.Details.Count > 0
                ? result.Details
                : new List<string> { "没有需要写入的改动，配置文件保持原样。" };

        // 延后项在首屏再提示一次，避免被明细滚动区挤到看不见
        if (result.Deferred.Count > 0)
        {
            DeferredBanner.Visibility = Visibility.Visible;
            DeferredBannerTitle.Text = $"注意：本次有 {result.Deferred.Count} 类改动未写入";
            DeferredBannerBody.Text = string.Join("；", result.Deferred);
        }

        Bind(WarningSection, WarningList, result.Warnings);
        Bind(DeferredSection, DeferredList, result.Deferred);
        Bind(BlockerSection, BlockerList, result.Blockers.Count > 0 ? result.Blockers : plan.Blockers);

        if (!result.Success && result.RolledBack)
            WarningSection.Visibility = Visibility.Visible;

        // 「应用」不退出设置窗口，底部文案和按钮要说清楚下一步做什么，
        // 免得用户以为点完就关窗了，一直在等窗口消失。
        if (!closeOnSuccess)
        {
            FooterButton.Content = result.Success ? "继续编辑" : "返回修改";
            FooterHint.Text = !result.Success
                ? "本次未写入任何内容，设置窗口保持打开，改完可以直接重试。"
                : plan.HasChanges
                    ? "已应用，设置窗口保持打开：可继续修改后再点「应用」，或点「确定」写完关闭。注册表音效即时生效；af 滤镜与其它配置项需重启播放器。"
                    : "本次没有需要写入的改动，设置窗口保持打开。";
        }
    }

    private static void Bind(UIElement section, ItemsControl list, IEnumerable<string> items)
    {
        var data = items?.ToList() ?? new List<string>();
        section.Visibility = data.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        list.ItemsSource = data;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    /// <summary>小窗口用系统「小圆角」，与主窗口保持同一种观感。</summary>
    private void Window_SourceInitialized(object sender, EventArgs e)
        => Services.WindowCorners.Apply(new System.Windows.Interop.WindowInteropHelper(this).Handle, small: true);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

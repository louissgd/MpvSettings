using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MpvSettings.Services;

namespace MpvSettings;

/// <summary>
/// 通用提示 / 确认窗口。视觉完全沿用 ApplyResultWindow（「写入结果」弹窗）那一套：
/// 自绘标题栏、墨绿底、薄荷强调色、卡片式明细、DWM 小圆角。
///
/// ⑩b 导入导出一开始用的是系统 MessageBox，白底系统主题跟设置程序完全不搭
/// （用户 2026-11-15 22:4x 指出），所以换成这个窗口；确认框也一起换。
/// </summary>
public partial class NoticeWindow : Window
{
    private readonly bool _confirm;

    private NoticeWindow(Window owner, string caption, string headline, string summary,
                         IEnumerable<string> details, bool confirm, bool success, string footer,
                         string okLabel = "继续导入")
    {
        InitializeComponent();
        Owner = owner;
        _confirm = confirm;

        Caption.Text = caption;
        Headline.Text = headline;
        Headline.Foreground = PickBrush(confirm ? "WarnBrush" : success ? "AccentBrush" : "DangerBrush");
        Summary.Text = summary;

        var list = details?.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList()
                   ?? new List<string>();
        DetailCard.Visibility = list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailList.ItemsSource = list;

        FooterHint.Text = footer.Length > 0
            ? footer
            : confirm ? "取消不会改动任何文件。"
            : success ? "可以关掉本窗口继续编辑；配置写入后需重启 MPV 生效。"
                      : "本次未完成，配置目录保持原样，可按提示重试。";

        if (confirm)
        {
            CancelBtn.Visibility = Visibility.Visible;
            OkBtn.Content = okLabel;
        }
    }

    private static Brush PickBrush(string key)
        => Application.Current?.TryFindResource(key) as Brush ?? Brushes.White;

    /// <summary>单按钮的结果弹窗（导出 / 导入完成或失败）。</summary>
    public static void Inform(Window owner, string caption, string headline, string summary,
                              IEnumerable<string> details, bool success, string footer = "")
    {
        var w = new NoticeWindow(owner, caption, headline, summary, details, false, success, footer);
        w.ShowDialog();
    }

    /// <summary>两按钮的确认弹窗。点 ✕ / 取消 / Esc 都返回 false。
    /// okLabel 是确认按钮文字：导入沿用「继续导入」，其他场景（如清理缓存）显式传入。</summary>
    public static bool Confirm(Window owner, string caption, string headline, string summary,
                               IEnumerable<string> details, string footer = "", string okLabel = "继续导入")
    {
        var w = new NoticeWindow(owner, caption, headline, summary, details, true, true, footer, okLabel);
        w.ShowDialog();
        return w.DialogResult == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
        => WindowCorners.Apply(new System.Windows.Interop.WindowInteropHelper(this).Handle, small: true);
}

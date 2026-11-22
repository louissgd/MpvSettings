using System.Windows;
using System.Windows.Controls;

namespace MpvSettings.Views;

public partial class PlaybackView : UserControl
{
    public PlaybackView() => InitializeComponent();

    private void EditSubStyle_Click(object sender, RoutedEventArgs e)
    {
        // 草稿由 MainWindow 注入为 DataContext；把同一个实例交给弹窗，
        // 保证弹窗里的改动立刻反映到主界面草稿，点「确定」时一起进写盘计划。
        if (DataContext is not Models.SettingsDraft draft) return;
        var win = new SubtitleStyleWindow(draft)
        {
            Owner = Window.GetWindow(this),
        };
        win.ShowDialog();
    }
}

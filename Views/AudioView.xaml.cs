using System.Windows;
using System.Windows.Controls;

namespace MpvSettings.Views;

public partial class AudioView : UserControl
{
    public AudioView() => InitializeComponent();

    /// <summary>原厂面板是 DShow 滤镜的属性页，只能由 rundll32 拉起。</summary>
    private void Panel_Click(object sender, RoutedEventArgs e)
        => (Window.GetWindow(this) as MainWindow)?.OpenBassMaxPanel();
}

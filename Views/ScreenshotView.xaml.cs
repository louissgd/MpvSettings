using System.Windows;
using System.Windows.Controls;

namespace MpvSettings.Views;

public partial class ScreenshotView : UserControl
{
    public ScreenshotView() => InitializeComponent();

    private MainWindow Host => Window.GetWindow(this) as MainWindow;

    private void RestoreAll_Click(object sender, RoutedEventArgs e) => Host?.ResetAll();
    private void ResetScreenshot_Click(object sender, RoutedEventArgs e) => Host?.ResetPage();
    private void OpenNoConfig_Click(object sender, RoutedEventArgs e) => Host?.OpenMpvNoConfig();
    private void OpenMpvConf_Click(object sender, RoutedEventArgs e) => Host?.OpenMpvConf();
    private void OpenConfigDir_Click(object sender, RoutedEventArgs e) => Host?.OpenConfigDir();
}

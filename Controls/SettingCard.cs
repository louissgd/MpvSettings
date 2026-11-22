using System.Windows;
using System.Windows.Controls;

namespace MpvSettings.Controls;

/// <summary>
/// 圆角卡片：标题栏带左侧紫色竖条，内容区放若干 OptionRow。
/// </summary>
public class SettingCard : ContentControl
{
    static SettingCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SettingCard),
            new FrameworkPropertyMetadata(typeof(SettingCard)));
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}

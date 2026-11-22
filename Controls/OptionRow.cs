using System.Windows;
using System.Windows.Controls;

namespace MpvSettings.Controls;

/// <summary>
/// 三段式设置行：标题 14px / 灰色副标题 12px / 右侧控件。
/// 阶段 ① 只承载静态内容；阶段 ② 起再接草稿绑定。
/// </summary>
public class OptionRow : ContentControl
{
    static OptionRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(OptionRow),
            new FrameworkPropertyMetadata(typeof(OptionRow)));
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(OptionRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(OptionRow), new PropertyMetadata(string.Empty));

    /// <summary>只读说明行：不显示右侧控件，标题/副标题用弱色，避免看起来能点。</summary>
    public static readonly DependencyProperty IsNoteProperty = DependencyProperty.Register(
        nameof(IsNote), typeof(bool), typeof(OptionRow), new PropertyMetadata(false));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsNote
    {
        get => (bool)GetValue(IsNoteProperty);
        set => SetValue(IsNoteProperty, value);
    }
}

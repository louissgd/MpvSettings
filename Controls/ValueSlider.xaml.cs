using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MpvSettings.Controls;

/// <summary>
/// 滑块 + 数值显示。阶段 ① 只做 UI 内部交互，不写任何配置；
/// 阶段 ② 起把 Value 挂到草稿模型上。
/// 鼠标拖动 / 点击轨道用于快速粗调，键盘用于精确微调：
/// 方向键 = SmallChange，PageUp/PageDown = LargeChange，Home/End = 端点。
/// 值始终吸附到 TickFrequency 档位。
/// </summary>
public partial class ValueSlider : UserControl
{
    public ValueSlider()
    {
        InitializeComponent();
        UpdateText();
        // XAML 里设 IsEditable="True" 时，DP 回调早于子控件创建被跳过；这里补应用一次。
        if (IsEditable && PART_ValueBox is not null)
        {
            PART_ValueText.Visibility = Visibility.Collapsed;
            PART_ValueBox.Visibility = Visibility.Visible;
        }

        // 点击轨道（IsMoveToPointEnabled）时 WPF 不保证把键盘焦点交给 Slider，
        // 这里显式聚焦，否则方向键会被外层 ScrollViewer 吃掉。
        // 可编辑模式：点在数值框上不能抢焦点，否则没法键入。
        PreviewMouseDown += (_, e) =>
        {
            if (IsEditable && e.OriginalSource == PART_ValueBox) return;
            PART_Slider.Focus();
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// 1.0.5：数值框是否可直接键入。默认 false——其他页面行为与旧版完全一致。
    /// 字幕样式弹窗里设 True，让用户精确输入字号/行距/描边/边距，不必拖滑杆凑整。
    /// </summary>
    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(ValueSlider),
        new PropertyMetadata(false, OnIsEditableChanged));

    public bool IsEditable { get => (bool)GetValue(IsEditableProperty); set => SetValue(IsEditableProperty, value); }

    private static void OnIsEditableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (ValueSlider)d;
        if (slider.PART_ValueText is null || slider.PART_ValueBox is null) return;
        bool editable = (bool)e.NewValue;
        slider.PART_ValueText.Visibility = editable ? Visibility.Collapsed : Visibility.Visible;
        slider.PART_ValueBox.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ValueSlider),
        new PropertyMetadata(0d, OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(ValueSlider), new PropertyMetadata(0d));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(ValueSlider), new PropertyMetadata(100d));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(ValueSlider), new PropertyMetadata(1d));

    public static readonly DependencyProperty LargeChangeProperty = DependencyProperty.Register(
        nameof(LargeChange), typeof(double), typeof(ValueSlider), new PropertyMetadata(10d));

    public static readonly DependencyProperty TickFrequencyProperty = DependencyProperty.Register(
        nameof(TickFrequency), typeof(double), typeof(ValueSlider), new PropertyMetadata(1d));

    public static readonly DependencyProperty FormatProperty = DependencyProperty.Register(
        nameof(Format), typeof(string), typeof(ValueSlider),
        new PropertyMetadata("{0}", OnValueChanged));

    public static readonly DependencyProperty ValueTextProperty = DependencyProperty.Register(
        nameof(ValueText), typeof(string), typeof(ValueSlider), new PropertyMetadata(string.Empty));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double SmallChange { get => (double)GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }
    public double LargeChange { get => (double)GetValue(LargeChangeProperty); set => SetValue(LargeChangeProperty, value); }
    public double TickFrequency { get => (double)GetValue(TickFrequencyProperty); set => SetValue(TickFrequencyProperty, value); }
    public string Format { get => (string)GetValue(FormatProperty); set => SetValue(FormatProperty, value); }
    public string ValueText { get => (string)GetValue(ValueTextProperty); set => SetValue(ValueTextProperty, value); }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((ValueSlider)d).UpdateText();

    private void UpdateText()
    {
        try
        {
            ValueText = string.Format(Format ?? "{0}", Value);
        }
        catch (FormatException)
        {
            ValueText = Value.ToString();
        }
    }

    /// <summary>数值框键入提交：解析 → 夹到 [Minimum, Maximum] → 写 Value（不吸附 tick，保留精确值）。</summary>
    private void CommitValueBox()
    {
        if (!IsEditable || PART_ValueBox is null) return;
        string text = (PART_ValueBox.Text ?? string.Empty).Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            Value = Math.Max(Minimum, Math.Min(Maximum, parsed));  // 触发 UpdateText → 回写规范化显示
        }
        else
        {
            PART_ValueBox.Text = ValueText;  // 非法输入：还原，不让脏值留在框里
        }
    }

    private void ValueBox_LostFocus(object sender, RoutedEventArgs e) => CommitValueBox();

    private void ValueBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitValueBox();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            PART_ValueBox.Text = ValueText;
            e.Handled = true;
        }
    }

    /// <summary>
    /// 自己接管方向键，避免依赖 Slider 原生键盘行为（不同模板下不稳定），
    /// 也避免和 Slider 各处理一次造成双倍步进。
    /// 可编辑模式下焦点在数值框时不接管——否则左右键没法在文本里移动光标。
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsEditable && e.OriginalSource == PART_ValueBox) return;
        double delta;
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down: delta = -SmallChange; break;
            case Key.Right:
            case Key.Up: delta = SmallChange; break;
            case Key.PageDown: delta = -LargeChange; break;
            case Key.PageUp: delta = LargeChange; break;
            case Key.Home:
                Value = Minimum;
                e.Handled = true;
                return;
            case Key.End:
                Value = Maximum;
                e.Handled = true;
                return;
            default:
                return;
        }
        Value = Math.Round(Math.Max(Minimum, Math.Min(Maximum, Value + delta)), 4);
        e.Handled = true;
    }
}

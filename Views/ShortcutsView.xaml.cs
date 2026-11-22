using System.Windows.Controls;
using System.Windows.Input;
using MpvSettings.Controls;
using MpvSettings.Models;

namespace MpvSettings.Views;

public partial class ShortcutsView : UserControl
{
    private ShortcutRow _capturing;

    public ShortcutsView()
    {
        InitializeComponent();
    }

    private ShortcutDraft Draft => (DataContext as SettingsDraft)?.Shortcuts;

    private void KeyBox_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ShortcutRow row) return;
        BeginCapture(row, btn);
    }

    private void BeginCapture(ShortcutRow row, Button box)
    {
        if (_capturing is not null) _capturing.IsCapturing = false;
        _capturing = row;
        row.IsCapturing = true;
        box.Focus();
        Keyboard.Focus(box);
    }

    private void EndCapture()
    {
        if (_capturing is not null) _capturing.IsCapturing = false;
        _capturing = null;
        Draft?.RefreshChecks();
    }

    /// <summary>
    /// 捕获态下的按键录入。只按修饰键不算完成，Esc 取消，
    /// 其余一律 Handled=true —— 否则空格会把 Button 当点击、Alt+F4 会关窗口。
    /// </summary>
    private void KeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturing is null) return;
        e.Handled = true;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { EndCapture(); return; }
        if (HotkeyNames.IsModifierOnly(key)) return;

        if (!HotkeyNames.TryBuild(key, Keyboard.Modifiers, out string mpvKey)) return;

        ShortcutRow row = _capturing;
        row.SetKey(mpvKey);
        EndCapture();
    }

    /// <summary>焦点离开 = 放弃这次录入，不吞掉半截输入。</summary>
    private void KeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_capturing is null) return;
        EndCapture();
    }

    private void Clear_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not ShortcutRow row) return;
        if (ReferenceEquals(_capturing, row)) EndCapture();
        row.Clear();
        Draft?.RefreshChecks();
    }
}

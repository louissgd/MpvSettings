using System.Windows.Input;

namespace MpvSettings.Controls;

/// <summary>
/// WPF 按键 → mpv 键名。mpv 的键名不是 WinForms 那套，也不是字符：
///   Space → SPACE、Esc → ESC、Backspace → BS、PageUp → PGUP、F1..F24 原样；
///   字母小写就是那个键，大写字母表示 Shift+字母（mpv 里 s 与 S 是两个键）；
///   `#` 键只能写 SHARP（写 # 会被当注释），由 ShortcutKeys.ToFile 转换。
/// </summary>
public static class HotkeyNames
{
    public static bool TryBuild(Key key, ModifierKeys mods, out string result)
    {
        result = string.Empty;
        if (key == Key.None || IsModifierOnly(key)) return false;

        string main = Map(key);
        if (main.Length == 0) return false;

        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0;

        // 字母：Shift 折进大写，保持 mpv 习惯写法（S 而不是 Shift+s）
        if (main.Length == 1 && main[0] >= 'a' && main[0] <= 'z' && shift)
        {
            main = main.ToUpperInvariant();
            shift = false;
        }

        var parts = new System.Collections.Generic.List<string>(4);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(main);
        result = string.Join("+", parts);
        return true;
    }

    public static bool IsModifierOnly(Key k) => k is Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
        or Key.LWin or Key.RWin or Key.Apps or Key.System;

    private static string Map(Key k)
    {
        int c = (int)k;
        if (c >= (int)Key.A && c <= (int)Key.Z) return ((char)('a' + c - (int)Key.A)).ToString();
        if (c >= (int)Key.D0 && c <= (int)Key.D9) return ((char)('0' + c - (int)Key.D0)).ToString();
        if (c >= (int)Key.NumPad0 && c <= (int)Key.NumPad9) return "KP" + (c - (int)Key.NumPad0);
        if (c >= (int)Key.F1 && c <= (int)Key.F24) return "F" + (c - (int)Key.F1 + 1);

        return k switch
        {
            Key.Space => "SPACE",
        Key.Escape => "ESC",
        Key.Enter => "ENTER",
        Key.Tab => "TAB",
        Key.Back => "BS",
        Key.Delete => "DEL",
        Key.Insert => "INS",
        Key.Home => "HOME",
        Key.End => "END",
        Key.PageUp => "PGUP",
        Key.PageDown => "PGDWN",
        Key.Left => "LEFT",
        Key.Right => "RIGHT",
        Key.Up => "UP",
        Key.Down => "DOWN",

        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemSemicolon => ";",
        Key.OemQuotes => "'",
        Key.OemOpenBrackets => "[",
        Key.OemCloseBrackets => "]",
        Key.OemPipe => "\\",
        Key.OemTilde => "`",
        Key.Multiply => "KP_MULT",
        Key.Add => "KP_ADD",
        Key.Subtract => "KP_SUB",
        Key.Divide => "KP_DIV",
            Key.Decimal => "KP_DEC",
            _ => string.Empty,
        };
    }
}

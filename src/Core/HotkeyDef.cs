using System.Text.Json.Serialization;
using PopupTranslator.Interop;

namespace PopupTranslator.Core;

/// <summary>一組快捷鍵：修飾鍵（MOD_* 旗標）+ 虛擬按鍵碼。</summary>
public sealed class HotkeyDef
{
    public uint Modifiers { get; set; }
    public int Key { get; set; }

    public HotkeyDef() { }

    public HotkeyDef(uint modifiers, Keys key)
    {
        Modifiers = modifiers;
        Key = (int)key;
    }

    [JsonIgnore]
    public bool IsValid
    {
        get
        {
            var k = (Keys)Key;
            if (Key <= 0 || Key > 0xFE) return false;
            if (k is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin
                or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
                or Keys.LMenu or Keys.RMenu) return false;
            // 沒有修飾鍵時只允許 F1~F24，避免吃掉一般打字按鍵
            bool hasMod = (Modifiers & (NativeMethods.MOD_ALT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT | NativeMethods.MOD_WIN)) != 0;
            return hasMod || (k >= Keys.F1 && k <= Keys.F24);
        }
    }

    public HotkeyDef Clone() => new() { Modifiers = Modifiers, Key = Key };

    public bool SameAs(HotkeyDef? other) =>
        other != null && other.Key == Key &&
        (other.Modifiers & ~NativeMethods.MOD_NOREPEAT) == (Modifiers & ~NativeMethods.MOD_NOREPEAT);

    public static HotkeyDef FromKeys(Keys keyData)
    {
        uint mods = 0;
        if ((keyData & Keys.Control) != 0) mods |= NativeMethods.MOD_CONTROL;
        if ((keyData & Keys.Alt) != 0) mods |= NativeMethods.MOD_ALT;
        if ((keyData & Keys.Shift) != 0) mods |= NativeMethods.MOD_SHIFT;
        return new HotkeyDef { Modifiers = mods, Key = (int)(keyData & Keys.KeyCode) };
    }

    public override string ToString()
    {
        if (Key == 0) return "（未設定）";
        var parts = new List<string>();
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName((Keys)Key));
        return string.Join(" + ", parts);
    }

    private static string KeyName(Keys k)
    {
        if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
        if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (k - Keys.NumPad0);
        return k switch
        {
            Keys.Oemtilde => "`",
            Keys.OemMinus => "-",
            Keys.Oemplus => "=",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemPipe => "\\",
            Keys.OemSemicolon => ";",
            Keys.OemQuotes => "'",
            Keys.Oemcomma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.Space => "Space",
            Keys.Return => "Enter",
            Keys.Prior => "PageUp",
            Keys.Next => "PageDown",
            _ => k.ToString(),
        };
    }
}

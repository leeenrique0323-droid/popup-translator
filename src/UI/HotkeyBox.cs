using PopupTranslator.Core;

namespace PopupTranslator.UI;

/// <summary>點選後直接按下組合鍵即可設定快捷鍵的輸入框。</summary>
internal sealed class HotkeyBox : TextBox
{
    private HotkeyDef _value = new();

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
        Cursor = Cursors.Hand;
    }

    public HotkeyDef Value
    {
        get => _value.Clone();
        set
        {
            _value = value.Clone();
            Text = _value.ToString();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        var mods = keyData & Keys.Modifiers;

        // 保留 Tab / Shift+Tab 讓使用者切換欄位
        if (key == Keys.Tab && (mods == Keys.None || mods == Keys.Shift))
            return base.ProcessCmdKey(ref msg, keyData);

        // 只按修飾鍵時不處理（等待主按鍵）
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return true;

        var def = HotkeyDef.FromKeys(keyData);
        if (def.IsValid) Value = def;
        else System.Media.SystemSounds.Beep.Play();
        return true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        e.Handled = true;
    }
}

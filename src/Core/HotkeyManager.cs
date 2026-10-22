using System.Runtime.InteropServices;
using PopupTranslator.Interop;

namespace PopupTranslator.Core;

/// <summary>
/// 以隱藏的 message-only 視窗接收 WM_HOTKEY（RegisterHotKey 全域快捷鍵）。
/// 必須在 UI 執行緒建立與使用。
/// </summary>
internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    public const string WindowCaption = "PopupTranslatorHotkeys_8B3F";
    private static readonly IntPtr HWND_MESSAGE = NativeMethods.HWND_MESSAGE;

    /// <summary>另一個實例（命令列參數）送來的指令：1 = 顯示、2 = 翻譯剪貼簿、3 = 截圖翻譯。</summary>
    public event Action<int>? CommandReceived;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0x0100;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams { Caption = WindowCaption, Parent = HWND_MESSAGE });
    }

    /// <summary>註冊快捷鍵；失敗（例如被其他程式佔用）時回傳 false。</summary>
    public bool Register(HotkeyDef def, Action action)
    {
        if (!def.IsValid) return false;
        int id = _nextId++;
        uint mods = (def.Modifiers & ~NativeMethods.MOD_NOREPEAT) | NativeMethods.MOD_NOREPEAT;
        if (!NativeMethods.RegisterHotKey(Handle, id, mods, (uint)def.Key))
        {
            // 少數舊系統不支援 MOD_NOREPEAT，去掉後再試一次
            int err = Marshal.GetLastWin32Error();
            if (err == 1409 /* ERROR_HOTKEY_ALREADY_REGISTERED */ ||
                !NativeMethods.RegisterHotKey(Handle, id, mods & ~NativeMethods.MOD_NOREPEAT, (uint)def.Key))
            {
                return false;
            }
        }
        _actions[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys)
            NativeMethods.UnregisterHotKey(Handle, id);
        _actions.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int id = (int)m.WParam;
            if (_actions.TryGetValue(id, out var action))
            {
                try { action(); }
                catch (Exception ex) { Program.ReportError(ex); }
            }
            return;
        }
        if (m.Msg == NativeMethods.WM_APP_COMMAND)
        {
            try { CommandReceived?.Invoke((int)m.WParam); }
            catch (Exception ex) { Program.ReportError(ex); }
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            UnregisterAll();
            DestroyHandle();
        }
    }
}

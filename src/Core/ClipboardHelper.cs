using System.Runtime.InteropServices;
using PopupTranslator.Interop;

namespace PopupTranslator.Core;

internal static class ClipboardHelper
{
    /// <summary>讀取剪貼簿文字（剪貼簿被其他程式鎖住時會重試）。必須在 STA（UI）執行緒呼叫。</summary>
    public static string? GetText()
    {
        for (int i = 0; i < 6; i++)
        {
            try
            {
                if (!Clipboard.ContainsText()) return null;
                var t = Clipboard.GetText(TextDataFormat.UnicodeText);
                return string.IsNullOrWhiteSpace(t) ? null : t;
            }
            catch (ExternalException) { Thread.Sleep(40); }
            catch (ThreadStateException) { return null; }
        }
        return null;
    }

    public static bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int i = 0; i < 6; i++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException) { Thread.Sleep(40); }
        }
        return false;
    }

    // 主控台 / 終端機視窗：Ctrl+C 會中斷程式，不可模擬
    private static readonly string[] UnsafeCopyClasses =
    {
        "ConsoleWindowClass",              // cmd / PowerShell 傳統主控台
        "CASCADIA_HOSTING_WINDOW_CLASS",   // Windows Terminal
        "mintty",                          // Git Bash
        "PuTTY",
        "VirtualConsoleClass",             // ConEmu
    };

    /// <summary>
    /// 對目前前景視窗模擬 Ctrl+C，若剪貼簿有變化就回傳新文字（= 使用者選取的文字）。
    /// 只送出標準鍵盤輸入，不會讀寫其他行程的記憶體。
    /// </summary>
    public static async Task<string?> TryCopySelectionAsync()
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero) return null;
        NativeMethods.GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return null;

        string cls = NativeMethods.GetWindowClass(fg);
        if (UnsafeCopyClasses.Any(c => string.Equals(c, cls, StringComparison.OrdinalIgnoreCase)))
            return null;

        // 等使用者放開快捷鍵的修飾鍵，否則 Ctrl+C 會變成 Ctrl+Alt+C 等其他組合
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (NativeMethods.AnyModifierDown())
        {
            if (sw.ElapsedMilliseconds > 700) return null;
            await Task.Delay(15);
        }

        uint seq = NativeMethods.GetClipboardSequenceNumber();
        if (!NativeMethods.SendCtrlC()) return null;

        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(20);
            if (NativeMethods.GetClipboardSequenceNumber() != seq)
            {
                await Task.Delay(30); // 讓來源程式寫完所有格式
                return GetText();
            }
        }
        return null;
    }
}

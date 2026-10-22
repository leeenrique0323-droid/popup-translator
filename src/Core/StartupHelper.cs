using Microsoft.Win32;

namespace PopupTranslator.Core;

internal static class StartupHelper
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PopupTranslator";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else if (key.GetValue(ValueName) != null)
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch
        {
            // 忽略登錄檔權限問題
        }
    }
}

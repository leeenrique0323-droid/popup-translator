using PopupTranslator.UI;

namespace PopupTranslator;

internal static class Program
{
    private static Mutex? _singleInstance;

    [STAThread]
    private static void Main(string[] args)
    {
        _singleInstance = new Mutex(true, @"Local\PopupTranslator_SingleInstance_8B3F", out bool isNew);
        if (!isNew)
        {
            // 已在執行：把命令列指令轉給執行中的實例
            int cmd = ParseCommand(args);
            IntPtr target = Interop.NativeMethods.FindWindowEx(Interop.NativeMethods.HWND_MESSAGE, IntPtr.Zero, null, Core.HotkeyManager.WindowCaption);
            if (cmd != 0 && target != IntPtr.Zero)
            {
                Interop.NativeMethods.AllowSetForegroundWindow(Interop.NativeMethods.ASFW_ANY);
                Interop.NativeMethods.PostMessage(target, Interop.NativeMethods.WM_APP_COMMAND, (IntPtr)cmd, IntPtr.Zero);
                return;
            }
            MessageBox.Show("快速翻譯已經在執行中（請看右下角系統匣圖示）。", "快速翻譯",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Per-Monitor V2：所有座標都是實體像素，截圖框選才會在高 DPI / 多螢幕下對齊（manifest 也有宣告）
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ReportError(ex);
        };

        try
        {
            Application.Run(new TrayContext(args));
        }
        finally
        {
            _singleInstance.ReleaseMutex();
            _singleInstance.Dispose();
        }
    }

    /// <summary>命令列參數：--show / --clipboard / --ocr（可用來建立桌面捷徑或綁定滑鼠按鍵）。</summary>
    public static int ParseCommand(string[] args)
    {
        foreach (var a in args)
        {
            switch (a.Trim().ToLowerInvariant())
            {
                case "--show": case "/show": return 1;
                case "--clipboard": case "/clipboard": return 2;
                case "--ocr": case "/ocr": return 3;
            }
        }
        return 0;
    }

    public static void ReportError(Exception ex)
    {
        try
        {
            var log = Path.Combine(Core.AppSettings.Folder, "error.log");
            Directory.CreateDirectory(Core.AppSettings.Folder);
            File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch { /* ignore */ }

        MessageBox.Show("發生未預期的錯誤：\n" + ex.Message + "\n\n詳細記錄已寫入 %APPDATA%\\PopupTranslator\\error.log",
            "快速翻譯", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

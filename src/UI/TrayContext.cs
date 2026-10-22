using PopupTranslator.Core;
using PopupTranslator.Ocr;

namespace PopupTranslator.UI;

/// <summary>常駐系統匣的應用程式主體：系統匣圖示、全域快捷鍵、各功能流程。</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly Icon _icon;
    private readonly NotifyIcon _tray;
    private readonly HotkeyManager _hotkeys;
    private readonly PopupForm _popup;
    private SettingsForm? _settingsForm;
    private bool _ocrBusy;
    private bool _popupHotkeyBusy;
    private bool _exiting;

    /// <param name="args">
    /// 命令列參數（可用於建立捷徑）：--show 啟動後立即開啟翻譯視窗；--clipboard 立即翻譯剪貼簿；--ocr 立即開始截圖翻譯。
    /// </param>
    public TrayContext(string[] args)
    {
        _settings = AppSettings.Load();
        _icon = LoadIcon();

        _popup = new PopupForm(_settings, _icon);
        _popup.OcrRequested += () => _ = StartOcrAsync();
        _popup.SettingsRequested += ShowSettings;

        var menu = new ContextMenuStrip { Font = UiFonts.Create(9.5f) };
        menu.Items.Add("顯示", null, (_, _) => ShowPopupFromTray());
        menu.Items.Add("截圖翻譯", null, (_, _) => _ = StartOcrAsync());
        menu.Items.Add("設定/說明", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("結束", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "快速翻譯（右鍵開啟選單）",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowPopupFromTray();
        };

        _hotkeys = new HotkeyManager();
        var failed = RegisterHotkeys();

        if (failed.Count > 0)
        {
            _tray.ShowBalloonTip(8000, "快捷鍵註冊失敗",
                "下列快捷鍵已被其他程式使用：" + string.Join("、", failed) + "\n請從系統匣圖示 → 設定/說明 更改。",
                ToolTipIcon.Warning);
        }
        else if (!_settings.FirstRunTipShown)
        {
            _tray.ShowBalloonTip(6000, "快速翻譯已在背景執行",
                $"{_settings.PopupHotkey}：開啟翻譯視窗\n{_settings.ClipboardHotkey}：翻譯剪貼簿\n{_settings.OcrHotkey}：截圖翻譯",
                ToolTipIcon.Info);
            _settings.FirstRunTipShown = true;
            _settings.Save();
        }

        _hotkeys.CommandReceived += RunCommand;
        int startCommand = Program.ParseCommand(args);
        if (startCommand != 0)
        {
            // 等訊息迴圈開始後再執行
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                RunCommand(startCommand);
            };
            timer.Start();
        }
    }

    private void RunCommand(int command)
    {
        switch (command)
        {
            case 1: ShowPopupFromTray(); break;
            case 2: OnClipboardHotkey(); break;
            case 3: _ = StartOcrAsync(); break;
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            using var s = typeof(TrayContext).Assembly.GetManifestResourceStream("PopupTranslator.app.ico");
            if (s != null) return new Icon(s, SystemInformation.SmallIconSize);
        }
        catch { /* 使用預設圖示 */ }
        return (Icon)SystemIcons.Application.Clone();
    }

    /// <summary>註冊三組快捷鍵，回傳失敗者的名稱。</summary>
    private List<string> RegisterHotkeys()
    {
        _hotkeys.UnregisterAll();
        var failed = new List<string>();
        if (!_hotkeys.Register(_settings.PopupHotkey, OnPopupHotkey)) failed.Add(_settings.PopupHotkey.ToString());
        if (!_hotkeys.Register(_settings.ClipboardHotkey, OnClipboardHotkey)) failed.Add(_settings.ClipboardHotkey.ToString());
        if (!_hotkeys.Register(_settings.OcrHotkey, () => _ = StartOcrAsync())) failed.Add(_settings.OcrHotkey.ToString());
        return failed;
    }

    // ------------------------------------------------------------------ 快捷鍵 A：開啟視窗

    private async void OnPopupHotkey()
    {
        if (_ocrBusy || _popupHotkeyBusy) return;

        // 視窗已在前景 → 再按一次就隱藏
        if (_popup.Visible && Form.ActiveForm == _popup)
        {
            _popup.HidePopup();
            return;
        }

        _popupHotkeyBusy = true;
        try
        {
            string? selected = null;
            if (_settings.CopySelectionOnPopup)
            {
                try { selected = await ClipboardHelper.TryCopySelectionAsync(); }
                catch { selected = null; }
            }

            if (!string.IsNullOrWhiteSpace(selected))
            {
                _popup.ShowNearCursor();
                _popup.SetInput(selected, translate: true);
                return;
            }

            _popup.ShowNearCursor();
            if (_settings.PrefillClipboard)
            {
                var clip = ClipboardHelper.GetText();
                if (!string.IsNullOrWhiteSpace(clip))
                {
                    _popup.SetInput(clip, translate: false);
                    _popup.SetStatus("已帶入剪貼簿文字，按 Enter 翻譯");
                    return;
                }
            }
            _popup.SetStatus("輸入文字後按 Enter 翻譯");
        }
        finally
        {
            _popupHotkeyBusy = false;
        }
    }

    private void ShowPopupFromTray()
    {
        _popup.ShowNearCursor();
    }

    // ------------------------------------------------------------------ 快捷鍵 B：翻譯剪貼簿

    private void OnClipboardHotkey()
    {
        if (_ocrBusy) return;
        var text = ClipboardHelper.GetText();
        _popup.ShowNearCursor();
        if (string.IsNullOrWhiteSpace(text))
        {
            _popup.SetInput("", translate: false);
            _popup.ClearOutput();
            _popup.SetStatus("剪貼簿中沒有文字", error: true);
            return;
        }
        _popup.SetInput(text, translate: true);
    }

    // ------------------------------------------------------------------ 快捷鍵 C：截圖 OCR 翻譯

    private async Task StartOcrAsync()
    {
        if (_ocrBusy) return;
        _ocrBusy = true;
        Bitmap? shot = null;
        Bitmap? region = null;
        try
        {
            _popup.HidePopup();
            await Task.Delay(150); // 讓視窗消失後再截圖

            Rectangle virtualScreen;
            try
            {
                shot = SelectionOverlay.CaptureVirtualScreen(out virtualScreen);
            }
            catch (Exception ex)
            {
                _popup.ShowNearCursor();
                _popup.ShowError("無法擷取螢幕畫面：" + ex.Message);
                return;
            }

            Rectangle selected;
            using (var overlay = new SelectionOverlay(shot, virtualScreen))
            {
                if (overlay.ShowDialog() != DialogResult.OK || overlay.SelectedImage == null) return;
                region = overlay.SelectedImage;
                selected = overlay.SelectedScreenRect;
            }
            shot.Dispose();
            shot = null;

            _popup.ShowNear(selected);
            _popup.SetInput("", translate: false);
            _popup.ClearOutput();
            _popup.SetStatus("文字辨識中…");

            OcrOutput result;
            try
            {
                result = await OcrService.RecognizeAsync(region, _settings.OcrLanguage, _settings.OcrMergeLines);
            }
            catch (OcrException ex)
            {
                _popup.ShowError(ex.Message);
                return;
            }
            catch (Exception ex)
            {
                _popup.ShowError("文字辨識失敗：" + ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                _popup.ShowError("選取範圍內沒有辨識到文字。可嘗試框選大一點的範圍，或到設定指定 OCR 語言。");
                return;
            }

            _popup.SetInput(result.Text, translate: true);
        }
        finally
        {
            shot?.Dispose();
            region?.Dispose();
            _ocrBusy = false;
        }
    }

    // ------------------------------------------------------------------ 設定 / 結束

    private void ShowSettings()
    {
        if (_settingsForm != null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Activate();
            return;
        }

        _popup.HidePopup();
        // 設定期間暫停全域快捷鍵，才能在輸入框中按下相同的組合鍵
        _hotkeys.UnregisterAll();
        _settingsForm = new SettingsForm(_settings, _icon);
        _settingsForm.FormClosed += (_, _) =>
        {
            _settingsForm = null;
            if (_exiting) return;
            _popup.ApplySettings();
            var failed = RegisterHotkeys();
            if (failed.Count > 0)
            {
                MessageBox.Show("下列快捷鍵已被其他程式使用，無法註冊：\n" + string.Join("\n", failed) +
                                "\n\n請重新開啟「設定/說明」更換組合鍵。",
                    "快速翻譯", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _settingsForm?.Close();
        _popup.CloseForExit();
        _popup.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _hotkeys.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}

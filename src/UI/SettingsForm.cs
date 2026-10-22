using System.Diagnostics;
using PopupTranslator.Core;
using PopupTranslator.Ocr;

namespace PopupTranslator.UI;

/// <summary>設定 / 說明視窗。</summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly HotkeyBox _hkPopup = new() { Width = 220 };
    private readonly HotkeyBox _hkClip = new() { Width = 220 };
    private readonly HotkeyBox _hkOcr = new() { Width = 220 };
    private readonly ComboBox _target = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly CheckBox _zhToEn = new() { AutoSize = true, Text = "來源是中文時改翻成英文（目標為繁體中文時）" };
    private readonly CheckBox _copySel = new() { AutoSize = true, Text = "開啟視窗時自動抓取選取的文字（模擬 Ctrl+C）" };
    private readonly CheckBox _prefillClip = new() { AutoSize = true, Text = "沒有選取文字時，帶入剪貼簿內容" };
    private readonly ComboBox _ocrLang = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _ocrMerge = new() { AutoSize = true, Text = "OCR 結果合併換行（適合遊戲對話框）" };
    private readonly ComboBox _engine = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly TextBox _deeplKey = new() { Width = 260, UseSystemPasswordChar = true, PlaceholderText = "選用：DeepL API 金鑰" };
    private readonly CheckBox _startup = new() { AutoSize = true, Text = "開機時自動啟動" };
    private readonly Label _ocrNote = new() { AutoSize = true, ForeColor = SystemColors.GrayText };

    private sealed record Item(string Value, string Text)
    {
        public override string ToString() => Text;
    }

    public SettingsForm(AppSettings settings, Icon? icon)
    {
        _settings = settings;
        SuspendLayout();

        Text = "快速翻譯 － 設定 / 說明";
        if (icon != null) Icon = icon;
        Font = UiFonts.Create(10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var help = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Text =
                "使用方式（程式常駐於右下角系統匣）：\n" +
                $"・{settings.PopupHotkey}：在游標旁開啟翻譯視窗（自動帶入選取文字或剪貼簿）\n" +
                $"・{settings.ClipboardHotkey}：立即翻譯剪貼簿中的文字\n" +
                $"・{settings.OcrHotkey}：框選螢幕區域，辨識文字（OCR）後翻譯\n" +
                "・翻譯視窗中：Enter 翻譯、Shift+Enter 換行、Esc 隱藏\n" +
                "・遊戲請使用「視窗化」或「無邊框視窗」模式；本程式只擷取螢幕畫面，不讀取遊戲記憶體。\n" +
                "・翻譯文字會傳送到 Google（或 DeepL），請勿翻譯機密內容。\n\n" +
                "修改快捷鍵：點一下下方欄位，直接按下新的組合鍵（需含 Ctrl / Alt / Shift，或使用 F1～F24）。\n" +
                "（此設定視窗開啟期間，全域快捷鍵會暫停，關閉後恢復。）",
            Margin = new Padding(0, 0, 0, 10),
        };

        _target.Items.AddRange(Languages.Targets);
        _target.SelectedItem = Languages.Find(settings.TargetLanguage) ?? Languages.Targets[0];

        _engine.Items.Add(new Item("google", "Google 翻譯（免費，不需金鑰）"));
        _engine.Items.Add(new Item("deepl", "DeepL（需要 API 金鑰）"));
        _engine.SelectedIndex = settings.Engine == "deepl" ? 1 : 0;
        _deeplKey.Text = settings.DeepLApiKey;
        _engine.SelectedIndexChanged += (_, _) => _deeplKey.Enabled = _engine.SelectedIndex == 1;
        _deeplKey.Enabled = _engine.SelectedIndex == 1;

        _hkPopup.Value = settings.PopupHotkey;
        _hkClip.Value = settings.ClipboardHotkey;
        _hkOcr.Value = settings.OcrHotkey;
        _zhToEn.Checked = settings.ChineseToEnglish;
        _copySel.Checked = settings.CopySelectionOnPopup;
        _prefillClip.Checked = settings.PrefillClipboard;
        _ocrMerge.Checked = settings.OcrMergeLines;
        _startup.Checked = StartupHelper.IsEnabled();

        _ocrLang.Items.Add(new Item("", "自動（建議）"));
        if (!string.IsNullOrEmpty(settings.OcrLanguage))
            _ocrLang.Items.Add(new Item(settings.OcrLanguage, settings.OcrLanguage));
        _ocrLang.SelectedIndex = _ocrLang.Items.Count - 1;
        _ocrNote.Text = "正在讀取已安裝的 OCR 語言…";

        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Location = new Point(12, 12),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) });
            c.Margin = new Padding(0, 3, 0, 3);
            grid.Controls.Add(c);
        }
        void Full(Control c)
        {
            c.Margin = new Padding(0, 3, 0, 3);
            grid.Controls.Add(c);
            grid.SetColumnSpan(c, 2);
        }

        Full(help);
        Row("開啟翻譯視窗", _hkPopup);
        Row("翻譯剪貼簿", _hkClip);
        Row("截圖翻譯 (OCR)", _hkOcr);
        Row("預設目標語言", _target);
        Full(_zhToEn);
        Full(_copySel);
        Full(_prefillClip);
        Row("OCR 語言", _ocrLang);
        Full(_ocrNote);
        Full(_ocrMerge);
        Row("翻譯引擎", _engine);
        Row("DeepL 金鑰", _deeplKey);
        Full(_startup);

        var folderLink = new LinkLabel { Text = "開啟設定檔資料夾", AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
        folderLink.LinkClicked += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Folder}\"") { UseShellExecute = true });
            }
            catch { /* ignore */ }
        };
        Full(folderLink);

        var ok = new Button { Text = "儲存", AutoSize = true, DialogResult = DialogResult.None, Margin = new Padding(0, 10, 8, 0) };
        var cancel = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 10, 0, 0) };
        ok.Click += (_, _) => OnSave();
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Full(buttons);

        Controls.Add(grid);
        AcceptButton = ok;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        Shown += async (_, _) => await LoadOcrLanguagesAsync();
    }

    private async Task LoadOcrLanguagesAsync()
    {
        try
        {
            var langs = await OcrService.GetLanguagesAsync();
            if (IsDisposed) return;
            string current = (_ocrLang.SelectedItem as Item)?.Value ?? "";
            _ocrLang.BeginUpdate();
            _ocrLang.Items.Clear();
            _ocrLang.Items.Add(new Item("", "自動（建議）"));
            foreach (var l in langs) _ocrLang.Items.Add(new Item(l.Tag, l.ToString()));
            _ocrLang.EndUpdate();
            int idx = 0;
            for (int i = 0; i < _ocrLang.Items.Count; i++)
                if (string.Equals(((Item)_ocrLang.Items[i]!).Value, current, StringComparison.OrdinalIgnoreCase)) idx = i;
            _ocrLang.SelectedIndex = idx;

            _ocrNote.Text = langs.Count == 0
                ? "⚠ 未安裝任何 OCR 語言：請到 設定 > 時間與語言 > 語言與地區 新增語言（含光學字元辨識）。"
                : "已安裝的 OCR 語言：" + string.Join("、", langs.Select(l => l.Tag)) +
                  "\n（要辨識其他語言，請到 設定 > 時間與語言 > 語言與地區 新增該語言）";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _ocrNote.Text = "無法讀取 OCR 語言：" + ex.Message;
        }
    }

    private void OnSave()
    {
        var p = _hkPopup.Value;
        var c = _hkClip.Value;
        var o = _hkOcr.Value;
        if (!p.IsValid || !c.IsValid || !o.IsValid)
        {
            MessageBox.Show(this, "快捷鍵設定不完整，請重新設定。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (p.SameAs(c) || p.SameAs(o) || c.SameAs(o))
        {
            MessageBox.Show(this, "三組快捷鍵不能重複。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string engine = (_engine.SelectedItem as Item)?.Value ?? "google";
        if (engine == "deepl" && string.IsNullOrWhiteSpace(_deeplKey.Text))
        {
            MessageBox.Show(this, "選擇 DeepL 時需要填入 API 金鑰（或改回 Google 翻譯）。", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.PopupHotkey = p;
        _settings.ClipboardHotkey = c;
        _settings.OcrHotkey = o;
        _settings.TargetLanguage = (_target.SelectedItem as LanguageOption)?.Code ?? "zh-TW";
        _settings.ChineseToEnglish = _zhToEn.Checked;
        _settings.CopySelectionOnPopup = _copySel.Checked;
        _settings.PrefillClipboard = _prefillClip.Checked;
        _settings.OcrLanguage = (_ocrLang.SelectedItem as Item)?.Value ?? "";
        _settings.OcrMergeLines = _ocrMerge.Checked;
        _settings.Engine = engine;
        _settings.DeepLApiKey = _deeplKey.Text.Trim();
        _settings.Save();

        if (_startup.Checked != StartupHelper.IsEnabled()) StartupHelper.SetEnabled(_startup.Checked);

        DialogResult = DialogResult.OK;
        Close();
    }
}

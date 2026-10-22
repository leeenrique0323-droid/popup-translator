using PopupTranslator.Core;
using PopupTranslator.Interop;
using PopupTranslator.Translation;

namespace PopupTranslator.UI;

/// <summary>置頂的快速翻譯視窗。Esc 或失去焦點時隱藏（除非已「釘選」）。</summary>
internal sealed class PopupForm : Form
{
    private readonly AppSettings _settings;
    private readonly ComboBox _target;
    private readonly Button _translateBtn;
    private readonly Button _copyBtn;
    private readonly Button _ocrBtn;
    private readonly Button _settingsBtn;
    private readonly CheckBox _pin;
    private readonly TextBox _input;
    private readonly TextBox _output;
    private readonly Label _status;
    private CancellationTokenSource? _cts;
    private bool _allowClose;
    private bool _suppressTargetEvent;

    public event Action? OcrRequested;
    public event Action? SettingsRequested;

    public PopupForm(AppSettings settings, Icon? icon)
    {
        _settings = settings;
        SuspendLayout();

        Text = "快速翻譯";
        if (icon != null) Icon = icon;
        Font = UiFonts.Create(10f);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        MinimumSize = new Size(380, 240);
        ClientSize = new Size(620, 380);
        Padding = new Padding(6);

        // ---- 工具列
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0),
        };
        var lbl = new Label { Text = "翻譯成", AutoSize = true, Margin = new Padding(0, 7, 2, 0) };
        _target = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Margin = new Padding(0, 3, 6, 3) };
        _target.Items.AddRange(Languages.Targets);
        _translateBtn = MakeButton("翻譯 (Enter)");
        _copyBtn = MakeButton("複製結果");
        _ocrBtn = MakeButton("截圖翻譯");
        _settingsBtn = MakeButton("設定");
        _pin = new CheckBox
        {
            Text = "釘選",
            Appearance = Appearance.Button,
            AutoSize = true,
            Margin = new Padding(0, 3, 0, 3),
        };
        var tips = new ToolTip();
        tips.SetToolTip(_pin, "釘選後失去焦點也不會自動隱藏");
        tips.SetToolTip(_ocrBtn, "框選螢幕區域並辨識文字後翻譯");
        bar.Controls.AddRange(new Control[] { lbl, _target, _translateBtn, _copyBtn, _ocrBtn, _settingsBtn, _pin });

        // ---- 輸入 / 輸出
        _input = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            WordWrap = true,
            Dock = DockStyle.Fill,
            PlaceholderText = "輸入或貼上要翻譯的文字，按 Enter 翻譯（Shift+Enter 換行）",
            Margin = new Padding(0, 4, 0, 4),
        };
        _output = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            Dock = DockStyle.Fill,
            BackColor = SystemColors.Window,
            Margin = new Padding(0, 0, 0, 4),
        };
        _status = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true,
            Margin = new Padding(0),
        };

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(bar, 0, 0);
        table.Controls.Add(_input, 0, 1);
        table.Controls.Add(_output, 0, 2);
        table.Controls.Add(_status, 0, 3);
        Controls.Add(table);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        // ---- 事件
        SetTargetSilently(_settings.TargetLanguage);
        _target.SelectedIndexChanged += OnTargetChanged;
        _translateBtn.Click += async (_, _) => await TranslateAsync();
        _copyBtn.Click += (_, _) => CopyResult();
        _ocrBtn.Click += (_, _) => OcrRequested?.Invoke();
        _settingsBtn.Click += (_, _) => SettingsRequested?.Invoke();
        _input.KeyDown += OnInputKeyDown;
        _output.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A) { _output.SelectAll(); e.SuppressKeyPress = true; }
        };
        SetStatus("就緒");
    }

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0, 2, 4, 2),
        Padding = new Padding(4, 0, 4, 0),
        UseVisualStyleBackColor = true,
    };

    public bool IsPinned => _pin.Checked;

    // ------------------------------------------------------------------ 顯示 / 隱藏

    /// <summary>在滑鼠游標附近顯示。</summary>
    public void ShowNearCursor()
    {
        var p = Cursor.Position;
        ShowNear(new Rectangle(p.X, p.Y, 1, 1));
    }

    /// <summary>在指定矩形（螢幕座標）附近顯示，優先放在下方。</summary>
    public void ShowNear(Rectangle anchor)
    {
        Place(anchor);
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Place(anchor); // 跨不同 DPI 螢幕時，顯示後尺寸可能被重新縮放，再校正一次
        BringToFrontAndFocus();
    }

    private void Place(Rectangle anchor)
    {
        var wa = Screen.FromRectangle(anchor).WorkingArea;
        var size = Size;
        const int gap = 12;
        int x = anchor.Left;
        int y = anchor.Bottom + gap;
        if (y + size.Height > wa.Bottom) y = anchor.Top - size.Height - gap;
        if (y < wa.Top) y = wa.Bottom - size.Height;
        x = Math.Clamp(x, wa.Left, Math.Max(wa.Left, wa.Right - size.Width));
        y = Math.Clamp(y, wa.Top, Math.Max(wa.Top, wa.Bottom - size.Height));
        Location = new Point(x, y);
    }

    private void BringToFrontAndFocus()
    {
        TopMost = true;
        Activate();
        NativeMethods.ForceForeground(Handle);
        _input.Focus();
    }

    public void HidePopup()
    {
        if (Visible) Hide();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && !_target.DroppedDown)
        {
            HidePopup();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_pin.Checked) HidePopup();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HidePopup();
            return;
        }
        base.OnFormClosing(e);
    }

    public void CloseForExit()
    {
        _allowClose = true;
        _cts?.Cancel();
        Close();
    }

    // ------------------------------------------------------------------ 內容

    /// <summary>設定輸入文字。translate = true 時立即翻譯；否則全選以便直接打字覆蓋。</summary>
    public void SetInput(string? text, bool translate)
    {
        text ??= "";
        _input.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (translate && !string.IsNullOrWhiteSpace(text))
        {
            _input.SelectionStart = _input.TextLength;
            _ = TranslateAsync();
        }
        else
        {
            _input.SelectAll();
        }
    }

    public void ClearOutput() => _output.Clear();

    public void SetStatus(string text, bool error = false)
    {
        _status.Text = text;
        _status.ForeColor = error ? Color.Firebrick : SystemColors.GrayText;
    }

    /// <summary>顯示錯誤訊息（同時放在結果欄，避免狀態列文字被截斷）。</summary>
    public void ShowError(string message)
    {
        _output.Text = message;
        SetStatus(message, error: true);
    }

    public void ApplySettings()
    {
        SetTargetSilently(_settings.TargetLanguage);
    }

    private void SetTargetSilently(string code)
    {
        _suppressTargetEvent = true;
        try
        {
            var opt = Languages.Find(code) ?? Languages.Targets[0];
            _target.SelectedItem = opt;
        }
        finally { _suppressTargetEvent = false; }
    }

    private async void OnTargetChanged(object? sender, EventArgs e)
    {
        if (_suppressTargetEvent) return;
        if (_target.SelectedItem is LanguageOption opt)
        {
            _settings.TargetLanguage = opt.Code;
            _settings.Save();
        }
        if (!string.IsNullOrWhiteSpace(_input.Text)) await TranslateAsync();
    }

    private async void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift && !e.Alt)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            await TranslateAsync();
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            _input.SelectAll();
            e.SuppressKeyPress = true;
        }
    }

    private void CopyResult()
    {
        if (string.IsNullOrEmpty(_output.Text))
        {
            SetStatus("沒有可複製的翻譯結果");
            return;
        }
        bool ok = ClipboardHelper.SetText(_output.Text);
        SetStatus(ok ? "已複製翻譯結果到剪貼簿" : "無法存取剪貼簿（可能被其他程式佔用），請稍後再試", error: !ok);
    }

    public async Task TranslateAsync()
    {
        string text = _input.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("請輸入要翻譯的文字");
            return;
        }

        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        string target = (_target.SelectedItem as LanguageOption)?.Code ?? "zh-TW";
        var translator = TranslatorFactory.Create(_settings);
        SetStatus($"翻譯中…（{translator.Name}）");
        _translateBtn.Enabled = false;

        try
        {
            var result = await translator.TranslateAsync(text, target, _settings.ChineseToEnglish, cts.Token);
            if (!ReferenceEquals(cts, _cts) || IsDisposed) return;
            _output.Text = result.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            _output.SelectionStart = 0;
            _output.ScrollToCaret();
            SetStatus($"{Languages.DisplayName(result.DetectedSource)} → {Languages.DisplayName(result.Target)}　·　{result.Engine}");
        }
        catch (OperationCanceledException)
        {
            // 被新的翻譯取代
        }
        catch (TranslationException ex)
        {
            if (ReferenceEquals(cts, _cts) && !IsDisposed) ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(cts, _cts) && !IsDisposed) ShowError("翻譯失敗：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(cts, _cts) && !IsDisposed)
            {
                _translateBtn.Enabled = true;
                _cts = null;
            }
            cts.Dispose();
        }
    }
}

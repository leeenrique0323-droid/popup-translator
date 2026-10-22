using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PopupTranslator.Interop;

namespace PopupTranslator.UI;

/// <summary>
/// 全螢幕（所有螢幕的虛擬桌面）框選介面。
/// 先擷取整個虛擬桌面畫面並凍結顯示（變暗），使用者拖曳出矩形後從同一張截圖裁切，
/// 因此框選位置與擷取內容一定逐像素對齊（程式為 Per-Monitor V2 DPI，座標皆為實體像素）。
/// 只做螢幕截圖（GDI BitBlt），不接觸任何遊戲行程。
/// </summary>
internal sealed class SelectionOverlay : Form
{
    private readonly Bitmap _shot;
    private readonly Bitmap _dimmed;
    private readonly Rectangle _virtual;
    private Point _start;
    private Rectangle _sel = Rectangle.Empty;
    private bool _dragging;
    private readonly Font _hintFont;

    /// <summary>選取的區域（虛擬桌面螢幕座標，實體像素）。</summary>
    public Rectangle SelectedScreenRect { get; private set; } = Rectangle.Empty;

    /// <summary>選取區域的截圖（呼叫端負責 Dispose）。</summary>
    public Bitmap? SelectedImage { get; private set; }

    public SelectionOverlay(Bitmap screenshot, Rectangle virtualScreen)
    {
        _shot = screenshot;
        _virtual = virtualScreen;

        _dimmed = new Bitmap(_shot.Width, _shot.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(_dimmed))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImageUnscaled(_shot, 0, 0);
            g.CompositingMode = CompositingMode.SourceOver;
            using var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
            g.FillRectangle(dim, 0, 0, _dimmed.Width, _dimmed.Height);
        }

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Bounds = virtualScreen;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        DoubleBuffered = true;
        Text = "框選翻譯區域";
        _hintFont = new Font("Microsoft JhengHei UI", 12f, FontStyle.Bold, GraphicsUnit.Point);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW：不出現在 Alt+Tab
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 以實體像素強制設定位置與大小（覆蓋整個虛擬桌面，含多螢幕與負座標）
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST,
            _virtual.X, _virtual.Y, _virtual.Width, _virtual.Height, NativeMethods.SWP_SHOWWINDOW);
        Activate();
        NativeMethods.ForceForeground(Handle);
    }

    protected override void WndProc(ref Message m)
    {
        // 視窗跨越不同 DPI 的螢幕時，忽略系統建議的縮放，維持 1:1 實體像素
        if (m.Msg == NativeMethods.WM_DPICHANGED)
        {
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.CompositingMode = CompositingMode.SourceCopy;
        var clip = e.ClipRectangle;
        g.DrawImage(_dimmed, clip, clip, GraphicsUnit.Pixel);

        if (_sel.Width > 0 && _sel.Height > 0)
        {
            var bright = Rectangle.Intersect(_sel, clip);
            if (!bright.IsEmpty) g.DrawImage(_shot, bright, bright, GraphicsUnit.Pixel);
            g.CompositingMode = CompositingMode.SourceOver;
            using var pen = new Pen(Color.FromArgb(255, 0, 170, 255), 2);
            g.DrawRectangle(pen, _sel.X, _sel.Y, _sel.Width - 1, _sel.Height - 1);

            string size = $"{_sel.Width} × {_sel.Height}";
            var sz = g.MeasureString(size, _hintFont);
            float ty = _sel.Y - sz.Height - 4 >= 0 ? _sel.Y - sz.Height - 4 : _sel.Bottom + 4;
            using var bg = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            g.FillRectangle(bg, _sel.X, ty, sz.Width, sz.Height);
            g.DrawString(size, _hintFont, Brushes.White, _sel.X, ty);
        }
        else
        {
            g.CompositingMode = CompositingMode.SourceOver;
            DrawHint(g);
        }
    }

    private Rectangle HintRect()
    {
        // 在游標所在螢幕的上方中央顯示提示
        var screen = Screen.FromPoint(Cursor.Position).Bounds;
        var r = new Rectangle(screen.X - _virtual.X, screen.Y - _virtual.Y, screen.Width, screen.Height);
        float scale = Math.Max(1f, DeviceDpi / 96f);
        int w = Math.Min(r.Width - 20, (int)(640 * scale));
        int h = (int)(48 * scale);
        return new Rectangle(r.X + (r.Width - w) / 2, r.Y + (int)(40 * scale), w, h);
    }

    private void DrawHint(Graphics g)
    {
        const string hint = "拖曳滑鼠框選要翻譯的區域　（Esc 或 右鍵 取消）";
        var r = HintRect();
        using var bg = new SolidBrush(Color.FromArgb(210, 20, 20, 20));
        g.FillRectangle(bg, r);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(hint, _hintFont, Brushes.White, r, sf);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            if (_dragging || !_sel.IsEmpty)
            {
                var old = _sel;
                _dragging = false;
                _sel = Rectangle.Empty;
                Invalidate(Inflate(old));
                Invalidate(HintRect());
            }
            else
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            return;
        }
        if (e.Button == MouseButtons.Left)
        {
            if (_sel.IsEmpty) Invalidate(HintRect());
            _dragging = true;
            _start = e.Location;
            _sel = new Rectangle(e.Location, Size.Empty);
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var old = _sel;
        int x1 = Math.Clamp(Math.Min(_start.X, e.X), 0, _shot.Width);
        int y1 = Math.Clamp(Math.Min(_start.Y, e.Y), 0, _shot.Height);
        int x2 = Math.Clamp(Math.Max(_start.X, e.X), 0, _shot.Width);
        int y2 = Math.Clamp(Math.Max(_start.Y, e.Y), 0, _shot.Height);
        _sel = Rectangle.FromLTRB(x1, y1, x2, y2);
        Invalidate(Rectangle.Union(Inflate(old), Inflate(_sel)));
    }

    private Rectangle Inflate(Rectangle r)
    {
        if (r.IsEmpty) return r;
        // 包含上下方的尺寸標籤
        int pad = (int)(80 * Math.Max(1f, DeviceDpi / 96f));
        var big = Rectangle.FromLTRB(r.Left - 6, r.Top - pad, Math.Max(r.Right + 6, r.Left + pad * 5), r.Bottom + pad);
        return big;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_dragging) return;
        _dragging = false;
        Capture = false;
        if (_sel.Width < 6 || _sel.Height < 6)
        {
            var old = _sel;
            _sel = Rectangle.Empty;
            Invalidate(Inflate(old));
            Invalidate(HintRect());
            return;
        }

        SelectedImage = _shot.Clone(_sel, PixelFormat.Format32bppArgb);
        SelectedScreenRect = new Rectangle(_sel.X + _virtual.X, _sel.Y + _virtual.Y, _sel.Width, _sel.Height);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // 使用者切換到其他視窗（Alt+Tab 等）時取消，避免遮罩卡在畫面上
        if (Visible && DialogResult == DialogResult.None)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dimmed.Dispose();
            _hintFont.Dispose();
        }
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------

    /// <summary>擷取整個虛擬桌面（所有螢幕），回傳實體像素截圖。</summary>
    public static Bitmap CaptureVirtualScreen(out Rectangle virtualScreen)
    {
        virtualScreen = SystemInformation.VirtualScreen;
        // 32bppRgb：沒有 alpha 通道，避免 GDI 擷取時 alpha 為 0 造成透明
        var bmp = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(virtualScreen.Left, virtualScreen.Top, 0, 0, virtualScreen.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }
}

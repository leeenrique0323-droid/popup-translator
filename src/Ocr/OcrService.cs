using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace PopupTranslator.Ocr;

public sealed record OcrLanguage(string Tag, string Name)
{
    public override string ToString() => $"{Name}（{Tag}）";
}

public sealed record OcrOutput(string Text, string LanguageTag);

/// <summary>
/// 使用 Windows 內建 OCR（Windows.Media.Ocr）辨識圖片文字。
/// 只處理我們自己擷取的螢幕畫面點陣圖，不接觸任何其他行程。
/// </summary>
public static class OcrService
{
    // Windows.Media.Ocr / Globalization / Graphics.Imaging / Security.Cryptography 介面 IID（取自 Windows SDK .winmd）
    private static readonly Guid IID_IOcrEngineStatics = new("5BFFA85A-3384-3540-9940-699120D428A8");
    private static readonly Guid IID_ILanguageFactory = new("9B0252AC-0C27-44F8-B792-9793FB66C63E");
    private static readonly Guid IID_ISoftwareBitmapStatics = new("DF0385DB-672F-4A9D-806E-C2442F343E86");
    private static readonly Guid IID_ICryptographicBufferStatics = new("320B7E22-3CB0-4CDF-8663-1D28910065EB");
    private static readonly Guid IID_IAsyncInfo = new("00000036-0000-0000-C000-000000000046");

    private const int BitmapPixelFormat_Bgra8 = 87;

    // IOcrEngineStatics vtable
    private const int OcrStatics_MaxImageDimension = 6;
    private const int OcrStatics_AvailableRecognizerLanguages = 7;
    private const int OcrStatics_TryCreateFromLanguage = 9;
    private const int OcrStatics_TryCreateFromUserProfileLanguages = 10;
    // IOcrEngine
    private const int OcrEngine_RecognizeAsync = 6;
    private const int OcrEngine_RecognizerLanguage = 7;
    // IOcrResult
    private const int OcrResult_Lines = 6;
    // IOcrLine
    private const int OcrLine_Text = 7;
    // ILanguage
    private const int Language_LanguageTag = 6;
    private const int Language_DisplayName = 7;
    // IAsyncInfo
    private const int AsyncInfo_Status = 7;
    private const int AsyncInfo_ErrorCode = 8;
    private const int AsyncInfo_Cancel = 9;
    private const int AsyncInfo_Close = 10;
    // IAsyncOperation<T>
    private const int AsyncOp_GetResults = 8;

    public const string NoLanguageMessage =
        "系統沒有可用的 OCR 語言。請到 Windows「設定 > 時間與語言 > 語言與地區」新增語言（例如 中文(台灣)、English、日本語），" +
        "並確認已安裝該語言的「光學字元辨識（OCR）」功能。";

    // ------------------------------------------------------------------ 執行緒

    /// <summary>在專用的 MTA 執行緒執行 WinRT 呼叫。</summary>
    private static Task<T> RunOnMtaAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            int hr = WinRt.RoInitialize(WinRt.RO_INIT_MULTITHREADED);
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            finally
            {
                if (hr >= 0) WinRt.RoUninitialize(); // S_OK / S_FALSE 都需要配對
            }
        })
        {
            IsBackground = true,
            Name = "OCR",
        };
        t.SetApartmentState(ApartmentState.MTA);
        t.Start();
        return tcs.Task;
    }

    // ------------------------------------------------------------------ 公開 API

    public static Task<List<OcrLanguage>> GetLanguagesAsync() => RunOnMtaAsync(() =>
    {
        using var s = new Session();
        return s.AvailableLanguages();
    });

    /// <summary>辨識圖片文字。</summary>
    /// <param name="image">要辨識的圖片（呼叫端保有所有權；本方法會在呼叫執行緒先轉成像素陣列）</param>
    /// <param name="preferredTag">指定 OCR 語言；空字串 = 自動</param>
    public static Task<OcrOutput> RecognizeAsync(Bitmap image, string preferredTag, bool mergeLines)
    {
        var prepared = PrepareImage(image);
        return RunOnMtaAsync(() =>
        {
            using var s = new Session();
            return s.RecognizeAuto(prepared, preferredTag ?? "", mergeLines);
        });
    }

    // ------------------------------------------------------------------ 圖片前處理

    private sealed record PreparedImage(byte[] Bgra, int Width, int Height);

    // Windows OCR 限制：長寬皆需介於約 40 ~ 2600 像素（MaxImageDimension）
    private const int OcrMaxDim = 2600;
    private const int OcrMinDim = 48;

    private static PreparedImage PrepareImage(Bitmap src)
    {
        int w = src.Width, h = src.Height;
        int longest = Math.Max(w, h);
        // 小字放大能明顯提高 Windows OCR 準確度；過大的範圍則縮小到上限內
        float desired = longest <= 500 ? 3f : longest <= 1300 ? 2f : 1f;
        float scale = Math.Min(desired, (float)OcrMaxDim / longest);
        int nw = Math.Clamp((int)Math.Round(w * scale), 1, OcrMaxDim);
        int nh = Math.Clamp((int)Math.Round(h * scale), 1, OcrMaxDim);

        // 四周留一點邊（OCR 對貼邊的文字較不準），並確保不小於最小尺寸
        int pad = (nw + 16 <= OcrMaxDim && nh + 16 <= OcrMaxDim) ? 8 : 0;
        int cw = Math.Max(nw + pad * 2, OcrMinDim);
        int ch = Math.Max(nh + pad * 2, OcrMinDim);
        int ox = (cw - nw) / 2, oy = (ch - nh) / 2;

        Color background;
        try { background = src.GetPixel(0, 0); } catch { background = Color.White; }
        background = Color.FromArgb(255, background.R, background.G, background.B);

        using var bmp = new Bitmap(cw, ch, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(background);
            g.InterpolationMode = Math.Abs(scale - 1f) < 0.001f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            using var attrs = new ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY); // 避免縮放時邊緣出現半透明框
            g.DrawImage(src, new Rectangle(ox, oy, nw, nh), 0, 0, w, h, GraphicsUnit.Pixel, attrs);
        }
        int nwFinal = cw, nhFinal = ch;

        var data = bmp.LockBits(new Rectangle(0, 0, nwFinal, nhFinal), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = nwFinal * 4;
            var bytes = new byte[rowBytes * nhFinal];
            for (int y = 0; y < nhFinal; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * rowBytes, rowBytes);
            // 強制不透明（BGRA 的 A）
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            return new PreparedImage(bytes, nwFinal, nhFinal);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    // ------------------------------------------------------------------ WinRT 工作階段（只在 MTA 執行緒使用）

    private sealed class Session : IDisposable
    {
        private IntPtr _ocrStatics;
        private IntPtr _langFactory;
        private IntPtr _bitmapStatics;
        private IntPtr _cryptoStatics;

        public Session()
        {
            _ocrStatics = WinRt.GetActivationFactory("Windows.Media.Ocr.OcrEngine", IID_IOcrEngineStatics);
            _langFactory = WinRt.GetActivationFactory("Windows.Globalization.Language", IID_ILanguageFactory);
            _bitmapStatics = WinRt.GetActivationFactory("Windows.Graphics.Imaging.SoftwareBitmap", IID_ISoftwareBitmapStatics);
            _cryptoStatics = WinRt.GetActivationFactory("Windows.Security.Cryptography.CryptographicBuffer", IID_ICryptographicBufferStatics);
        }

        public void Dispose()
        {
            WinRt.Release(_ocrStatics); _ocrStatics = IntPtr.Zero;
            WinRt.Release(_langFactory); _langFactory = IntPtr.Zero;
            WinRt.Release(_bitmapStatics); _bitmapStatics = IntPtr.Zero;
            WinRt.Release(_cryptoStatics); _cryptoStatics = IntPtr.Zero;
        }

        public List<OcrLanguage> AvailableLanguages()
        {
            var list = new List<OcrLanguage>();
            IntPtr vec = WinRt.GetPtr(_ocrStatics, OcrStatics_AvailableRecognizerLanguages, "AvailableRecognizerLanguages");
            try
            {
                uint n = WinRt.VectorSize(vec);
                for (uint i = 0; i < n; i++)
                {
                    IntPtr lang = WinRt.VectorGetAt(vec, i);
                    try
                    {
                        string tag = WinRt.GetString(lang, Language_LanguageTag, "Language.LanguageTag");
                        string name = WinRt.GetString(lang, Language_DisplayName, "Language.DisplayName");
                        if (!string.IsNullOrEmpty(tag)) list.Add(new OcrLanguage(tag, string.IsNullOrEmpty(name) ? tag : name));
                    }
                    finally { WinRt.Release(lang); }
                }
            }
            finally { WinRt.Release(vec); }
            return list;
        }

        private uint MaxImageDimension()
        {
            try { return WinRt.GetUInt32(_ocrStatics, OcrStatics_MaxImageDimension, "MaxImageDimension"); }
            catch { return 10000; }
        }

        /// <summary>建立 OCR 引擎；tag 為 null 時使用使用者設定檔語言。失敗回傳 IntPtr.Zero。</summary>
        private IntPtr CreateEngine(string? tag)
        {
            if (tag == null)
            {
                try { return WinRt.GetPtr(_ocrStatics, OcrStatics_TryCreateFromUserProfileLanguages, "TryCreateFromUserProfileLanguages"); }
                catch { return IntPtr.Zero; }
            }

            if (WinRt.CreateLanguage(_langFactory, tag, out IntPtr lang) < 0 || lang == IntPtr.Zero)
                return IntPtr.Zero;
            try
            {
                int hr = WinRt.GetPtrWithArg(_ocrStatics, OcrStatics_TryCreateFromLanguage, lang, out IntPtr engine);
                return hr < 0 ? IntPtr.Zero : engine;
            }
            finally { WinRt.Release(lang); }
        }

        private string EngineTag(IntPtr engine)
        {
            try
            {
                IntPtr lang = WinRt.GetPtr(engine, OcrEngine_RecognizerLanguage, "RecognizerLanguage");
                try { return WinRt.GetString(lang, Language_LanguageTag, "LanguageTag"); }
                finally { WinRt.Release(lang); }
            }
            catch { return ""; }
        }

        private IntPtr CreateBitmap(PreparedImage img)
        {
            IntPtr buffer = WinRt.CreateBufferFromBytes(_cryptoStatics, img.Bgra);
            try
            {
                return WinRt.CreateSoftwareBitmap(_bitmapStatics, buffer, BitmapPixelFormat_Bgra8, img.Width, img.Height);
            }
            finally { WinRt.Release(buffer); }
        }

        /// <summary>以指定引擎辨識，回傳每一行文字。</summary>
        private static List<string> Recognize(IntPtr engine, IntPtr bitmap)
        {
            int hr = WinRt.GetPtrWithArg(engine, OcrEngine_RecognizeAsync, bitmap, out IntPtr op);
            WinRt.Check(hr, "OcrEngine.RecognizeAsync");
            IntPtr info = IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            try
            {
                info = WinRt.QueryInterface(op, IID_IAsyncInfo);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                int status;
                while ((status = WinRt.GetInt32(info, AsyncInfo_Status, "IAsyncInfo.Status")) == 0 /* Started */)
                {
                    if (sw.ElapsedMilliseconds > 20000)
                    {
                        WinRt.CallVoid(info, AsyncInfo_Cancel);
                        throw new OcrException("文字辨識逾時。");
                    }
                    Thread.Sleep(5);
                }
                if (status == 3 /* Error */)
                {
                    int err = WinRt.GetInt32(info, AsyncInfo_ErrorCode, "IAsyncInfo.ErrorCode");
                    throw new OcrException($"文字辨識失敗（0x{err:X8}）。", Marshal.GetExceptionForHR(err));
                }
                if (status == 2 /* Canceled */)
                    throw new OcrException("文字辨識已取消。");

                result = WinRt.GetPtr(op, AsyncOp_GetResults, "IAsyncOperation.GetResults");
                var lines = new List<string>();
                if (result == IntPtr.Zero) return lines;

                IntPtr vec = WinRt.GetPtr(result, OcrResult_Lines, "OcrResult.Lines");
                try
                {
                    uint n = WinRt.VectorSize(vec);
                    for (uint i = 0; i < n; i++)
                    {
                        IntPtr line = WinRt.VectorGetAt(vec, i);
                        try { lines.Add(WinRt.GetString(line, OcrLine_Text, "OcrLine.Text")); }
                        finally { WinRt.Release(line); }
                    }
                }
                finally { WinRt.Release(vec); }
                return lines;
            }
            finally
            {
                WinRt.Release(result);
                if (info != IntPtr.Zero)
                {
                    WinRt.CallVoid(info, AsyncInfo_Close);
                    WinRt.Release(info);
                }
                WinRt.Release(op);
            }
        }

        private (string Text, string Tag)? Run(string? tag, IntPtr bitmap, bool mergeLines)
        {
            IntPtr engine = CreateEngine(tag);
            if (engine == IntPtr.Zero) return null;
            try
            {
                var lines = Recognize(engine, bitmap);
                return (TextUtil.Join(lines, mergeLines), EngineTag(engine));
            }
            finally { WinRt.Release(engine); }
        }

        public OcrOutput RecognizeAuto(PreparedImage img, string preferredTag, bool mergeLines)
        {
            var langs = AvailableLanguages();
            if (langs.Count == 0) throw new OcrException(NoLanguageMessage);

            uint maxDim = MaxImageDimension();
            if (img.Width > maxDim || img.Height > maxDim)
                throw new OcrException($"選取範圍太大（OCR 上限 {maxDim} 像素），請框選小一點的區域。");

            IntPtr bitmap = CreateBitmap(img);
            try
            {
                string? Find(params string[] prefixes)
                {
                    foreach (var p in prefixes)
                    {
                        var hit = langs.FirstOrDefault(l => l.Tag.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                        if (hit != null) return hit.Tag;
                    }
                    return null;
                }

                // 1) 使用者指定語言
                if (!string.IsNullOrWhiteSpace(preferredTag))
                {
                    var r = Run(preferredTag, bitmap, mergeLines);
                    if (r != null) return new OcrOutput(r.Value.Text, r.Value.Tag);
                    // 指定的語言已不存在 → 改用自動
                }

                // 2) 自動：先繁中，再使用者設定檔語言，再英文 / 第一個可用語言
                string? zhHant = Find("zh-Hant", "zh-TW", "zh-HK", "zh-MO");
                string? en = Find("en-US", "en");
                string? ja = Find("ja");
                string? ko = Find("ko");

                (string Text, string Tag)? primary = null;
                if (zhHant != null) primary = Run(zhHant, bitmap, mergeLines);
                primary ??= Run(null, bitmap, mergeLines);
                if (primary == null && en != null) primary = Run(en, bitmap, mergeLines);
                primary ??= Run(langs[0].Tag, bitmap, mergeLines);
                if (primary == null) throw new OcrException(NoLanguageMessage);

                var best = primary.Value;
                string primaryTag = best.Tag;
                bool Is(string? tag, string prefix) => tag != null && tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

                // 3a) 幾乎都是拉丁字母 → 用英文引擎重跑（字距 / 標點較準確）
                if (en != null && !Is(primaryTag, "en") && (TextUtil.IsBlank(best.Text) || TextUtil.LatinRatio(best.Text) >= 0.7))
                {
                    var r = Run(en, bitmap, mergeLines);
                    if (r != null && !TextUtil.IsBlank(r.Value.Text)) best = r.Value;
                }

                // 3b) 有安裝日文 → 若辨識出明顯的假名就採用日文結果
                if (ja != null && !Is(primaryTag, "ja") && (TextUtil.IsBlank(best.Text) || TextUtil.CountCjk(best.Text) > 0))
                {
                    var r = Run(ja, bitmap, mergeLines);
                    if (r != null && TextUtil.KanaScore(r.Value.Text) >= 0.1 && TextUtil.CountKana(r.Value.Text) >= 2)
                        best = r.Value;
                }

                // 3c) 有安裝韓文 → 若大部分是韓文字母就採用
                if (ko != null && !Is(primaryTag, "ko") && !Is(best.Tag, "ja"))
                {
                    var r = Run(ko, bitmap, mergeLines);
                    if (r != null && TextUtil.HangulRatio(r.Value.Text) >= 0.5 && TextUtil.CountHangul(r.Value.Text) >= 2)
                        best = r.Value;
                }

                // 4) 仍然沒有結果 → 試其他已安裝語言，取最長者
                if (TextUtil.IsBlank(best.Text))
                {
                    foreach (var l in langs.Take(5))
                    {
                        if (string.Equals(l.Tag, best.Tag, StringComparison.OrdinalIgnoreCase)) continue;
                        var r = Run(l.Tag, bitmap, mergeLines);
                        if (r != null && r.Value.Text.Trim().Length > best.Text.Trim().Length) best = r.Value;
                    }
                }

                return new OcrOutput(best.Text, best.Tag);
            }
            finally { WinRt.Release(bitmap); }
        }
    }
}

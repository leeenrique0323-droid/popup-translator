using System.Text.Encodings.Web;
using System.Text.Json;
using PopupTranslator.Interop;

namespace PopupTranslator.Core;

/// <summary>使用者設定，存放於 %APPDATA%\PopupTranslator\settings.json。</summary>
public sealed class AppSettings
{
    public HotkeyDef PopupHotkey { get; set; } = new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.T);
    public HotkeyDef ClipboardHotkey { get; set; } = new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.C);
    public HotkeyDef OcrHotkey { get; set; } = new(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, Keys.S);

    /// <summary>預設目標語言代碼（zh-TW / en / ja / ko / zh-CN）。</summary>
    public string TargetLanguage { get; set; } = "zh-TW";

    /// <summary>目標為繁中且偵測到來源為中文時，改翻成英文。</summary>
    public bool ChineseToEnglish { get; set; } = true;

    /// <summary>按下「開啟視窗」快捷鍵時，模擬 Ctrl+C 取得目前選取的文字。</summary>
    public bool CopySelectionOnPopup { get; set; } = true;

    /// <summary>沒有選取文字時，以剪貼簿文字預先填入。</summary>
    public bool PrefillClipboard { get; set; } = true;

    /// <summary>OCR 語言標記；空字串 = 自動。</summary>
    public string OcrLanguage { get; set; } = "";

    /// <summary>OCR 結果是否把換行合併成段落（適合遊戲對話框）。</summary>
    public bool OcrMergeLines { get; set; } = true;

    /// <summary>翻譯引擎：google / deepl。</summary>
    public string Engine { get; set; } = "google";

    public string DeepLApiKey { get; set; } = "";

    public bool FirstRunTipShown { get; set; }

    // ---------------------------------------------------------------

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PopupTranslator");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (s != null)
                {
                    s.Normalize();
                    return s;
                }
            }
        }
        catch
        {
            // 設定檔損毀時改用預設值
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // 無法寫入時忽略（例如權限問題）
        }
    }

    private void Normalize()
    {
        var d = new AppSettings();
        if (PopupHotkey is null || !PopupHotkey.IsValid) PopupHotkey = d.PopupHotkey;
        if (ClipboardHotkey is null || !ClipboardHotkey.IsValid) ClipboardHotkey = d.ClipboardHotkey;
        if (OcrHotkey is null || !OcrHotkey.IsValid) OcrHotkey = d.OcrHotkey;
        if (Languages.Find(TargetLanguage) is null) TargetLanguage = d.TargetLanguage;
        OcrLanguage ??= "";
        Engine = Engine?.ToLowerInvariant() == "deepl" ? "deepl" : "google";
        DeepLApiKey ??= "";
    }
}

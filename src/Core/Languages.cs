namespace PopupTranslator.Core;

public sealed record LanguageOption(string Code, string Display)
{
    public override string ToString() => Display;
}

public static class Languages
{
    /// <summary>目標語言下拉選單內容（Google 語言代碼）。</summary>
    public static readonly LanguageOption[] Targets =
    {
        new("zh-TW", "繁體中文"),
        new("en", "English"),
        new("ja", "日本語"),
        new("ko", "한국어"),
        new("zh-CN", "简体中文"),
    };

    public static LanguageOption? Find(string? code) =>
        Targets.FirstOrDefault(t => string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase));

    public static bool IsChinese(string? code) =>
        !string.IsNullOrEmpty(code) && code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 本機快速判斷文字是否為中文：漢字佔多數、幾乎沒有日文假名、沒有韓文。
    /// 用來決定「中文 → 英文」而不必多送一次請求。
    /// </summary>
    public static bool LooksChinese(string text)
    {
        int han = 0, kana = 0, hangul = 0, letters = 0;
        foreach (char c in text)
        {
            if ((c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF)) han++;
            else if (c >= 0x3040 && c <= 0x30FF) kana++;
            else if ((c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F)) hangul++;
            if (char.IsLetter(c)) letters++;
        }
        return han >= 2 && han * 2 >= letters && kana * 20 <= han && hangul == 0;
    }

    /// <summary>把偵測到的來源語言代碼轉成中文名稱。</summary>
    public static string DisplayName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "未知";
        var c = code.Trim().ToLowerInvariant();
        return c switch
        {
            "zh-tw" or "zh-hant" => "繁體中文",
            "zh-cn" or "zh-hans" => "簡體中文",
            "zh" => "中文",
            "en" or "en-us" or "en-gb" => "英文",
            "ja" => "日文",
            "ko" => "韓文",
            "fr" => "法文",
            "de" => "德文",
            "es" => "西班牙文",
            "pt" or "pt-br" or "pt-pt" => "葡萄牙文",
            "ru" => "俄文",
            "it" => "義大利文",
            "vi" => "越南文",
            "th" => "泰文",
            "id" => "印尼文",
            "ms" => "馬來文",
            "ar" => "阿拉伯文",
            "tr" => "土耳其文",
            "pl" => "波蘭文",
            "nl" => "荷蘭文",
            "uk" => "烏克蘭文",
            "auto" => "自動",
            _ => code,
        };
    }
}

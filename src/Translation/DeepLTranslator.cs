using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace PopupTranslator.Translation;

/// <summary>
/// DeepL API（選用，需要 API 金鑰；免費金鑰以 ":fx" 結尾會自動使用 api-free.deepl.com）。
/// </summary>
public sealed class DeepLTranslator : ChunkedTranslatorBase
{
    private readonly string _apiKey;

    public DeepLTranslator(string apiKey) => _apiKey = apiKey.Trim();

    public override string Name => "DeepL";

    protected override int MaxChunkChars => 5000;

    private string Endpoint => _apiKey.EndsWith(":fx", StringComparison.OrdinalIgnoreCase)
        ? "https://api-free.deepl.com/v2/translate"
        : "https://api.deepl.com/v2/translate";

    private static string ToDeepLTarget(string target) => target.ToLowerInvariant() switch
    {
        "zh-tw" => "ZH-HANT",
        "zh-cn" => "ZH-HANS",
        "en" => "EN-US",
        "ja" => "JA",
        "ko" => "KO",
        _ => target.ToUpperInvariant(),
    };

    private static string FromDeepLSource(string src) => src.ToUpperInvariant() switch
    {
        "ZH" => "zh",
        "EN" => "en",
        _ => src.ToLowerInvariant(),
    };

    protected override async Task<(string Text, string Detected)> TranslateChunkAsync(
        string chunk, string? source, string target, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new TranslationException("尚未設定 DeepL API 金鑰，請到「設定/說明」填入，或改用 Google 翻譯。");

        var form = new List<KeyValuePair<string, string>>
        {
            new("text", chunk),
            new("target_lang", ToDeepLTarget(target)),
        };
        if (!string.IsNullOrEmpty(source)) form.Add(new("source_lang", source.ToUpperInvariant()));

        string json = await Http.SendAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new FormUrlEncodedContent(form) };
            req.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + _apiKey);
            return req;
        }, "DeepL", code => code switch
        {
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "DeepL API 金鑰無效或未授權。",
            (HttpStatusCode)456 => "DeepL 本月翻譯額度已用完。",
            HttpStatusCode.BadRequest => "DeepL 拒絕此請求（可能不支援所選的目標語言）。",
            _ => null,
        }, ct).ConfigureAwait(false);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var t = doc.RootElement.GetProperty("translations")[0];
            string text = t.GetProperty("text").GetString() ?? "";
            string det = t.TryGetProperty("detected_source_language", out var d) ? d.GetString() ?? "" : "";
            return (text, FromDeepLSource(det));
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new TranslationException("無法解析 DeepL 的回應。", ex);
        }
    }
}

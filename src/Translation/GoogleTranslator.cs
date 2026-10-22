using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PopupTranslator.Translation;

/// <summary>
/// Google 翻譯免費網頁端點（translate.googleapis.com/translate_a/single?client=gtx）。
/// 非官方 API：不需金鑰，但可能被限流（HTTP 429）。
/// </summary>
public sealed class GoogleTranslator : ChunkedTranslatorBase
{
    public override string Name => "Google";

    // GET 網址長度保守控制在約 5000 字元內
    protected override int MaxChunkChars => 1800;
    protected override Func<string, int>? ChunkCost => s => Uri.EscapeDataString(s).Length;
    protected override int MaxChunkCost => 4500;

    protected override async Task<(string Text, string Detected)> TranslateChunkAsync(
        string chunk, string? source, string target, CancellationToken ct)
    {
        string url = "https://translate.googleapis.com/translate_a/single?client=gtx&ie=UTF-8&oe=UTF-8&dt=t" +
                     "&sl=" + Uri.EscapeDataString(source ?? "auto") +
                     "&tl=" + Uri.EscapeDataString(target) +
                     "&q=" + Uri.EscapeDataString(chunk);

        string json = await Http.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url),
            "Google 翻譯", null, ct).ConfigureAwait(false);

        return Parse(json);
    }

    /// <summary>
    /// 回應格式：[[["譯文","原文",...],["譯文2","原文2",...]], null, "en", ...]
    /// </summary>
    internal static (string Text, string Detected) Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                throw new FormatException();

            var sb = new StringBuilder();
            var segs = root[0];
            if (segs.ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in segs.EnumerateArray())
                {
                    if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 &&
                        seg[0].ValueKind == JsonValueKind.String)
                    {
                        sb.Append(seg[0].GetString());
                    }
                }
            }

            string detected = "";
            if (root.GetArrayLength() > 2 && root[2].ValueKind == JsonValueKind.String)
                detected = root[2].GetString() ?? "";
            // 備援：root[8] = [["zh-TW"], null, [0.98], ["zh-TW"]]
            if (string.IsNullOrEmpty(detected) && root.GetArrayLength() > 8 &&
                root[8].ValueKind == JsonValueKind.Array && root[8].GetArrayLength() > 0 &&
                root[8][0].ValueKind == JsonValueKind.Array && root[8][0].GetArrayLength() > 0 &&
                root[8][0][0].ValueKind == JsonValueKind.String)
                detected = root[8][0][0].GetString() ?? "";

            return (sb.ToString(), detected);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new TranslationException("無法解析 Google 翻譯的回應（服務格式可能已變更）。", ex);
        }
    }
}

using System.Text;
using PopupTranslator.Core;

namespace PopupTranslator.Translation;

/// <summary>
/// 共用邏輯：長文字分段、來源語言偵測、「中文 → 英文」自動切換。
/// 子類別只需實作單段翻譯。
/// </summary>
public abstract class ChunkedTranslatorBase : ITranslator
{
    public abstract string Name { get; }

    protected abstract int MaxChunkChars { get; }

    /// <summary>每段的額外成本限制（例如 URL 長度）；預設不限制。</summary>
    protected virtual Func<string, int>? ChunkCost => null;
    protected virtual int MaxChunkCost => int.MaxValue;

    /// <summary>翻譯單段文字。</summary>
    /// <param name="source">來源語言；null = 自動偵測</param>
    /// <returns>譯文與偵測到的來源語言（小寫 Google 風格代碼，例如 en、zh-CN、ja）</returns>
    protected abstract Task<(string Text, string Detected)> TranslateChunkAsync(
        string chunk, string? source, string target, CancellationToken ct);

    private static string Normalize(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());

    public async Task<TranslationResult> TranslateAsync(string text, string target, bool chineseToEnglish, CancellationToken ct)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (string.IsNullOrWhiteSpace(text))
            throw new TranslationException("沒有可翻譯的文字。");

        var chunks = TextChunker.Split(text, MaxChunkChars, ChunkCost, MaxChunkCost);
        var sb = new StringBuilder();
        string detected = "";
        string actualTarget = target;
        bool swapEnabled = chineseToEnglish && string.Equals(target, "zh-TW", StringComparison.OrdinalIgnoreCase);

        // 預設目標為繁中，但原文本身就是中文 → 直接改翻成英文（本機判斷，不多送請求）
        if (swapEnabled && Languages.LooksChinese(text))
        {
            actualTarget = "en";
            detected = "zh";
            swapEnabled = false;
        }

        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string chunk = chunks[i];
            if (string.IsNullOrWhiteSpace(chunk))
            {
                sb.Append(chunk);
                continue;
            }

            var (translated, det) = await TranslateChunkAsync(chunk, null, actualTarget, ct).ConfigureAwait(false);

            if (swapEnabled)
            {
                // 服務判定來源為中文，或譯文與原文完全相同（原文已是目標語言）→ 改翻英文
                bool unchanged = string.Equals(Normalize(translated), Normalize(chunk), StringComparison.Ordinal);
                if (Languages.IsChinese(det) || (unchanged && chunk.Any(c => c >= 0x3400 && c <= 0x9FFF)))
                {
                    actualTarget = "en";
                    if (!Languages.IsChinese(det)) det = "zh";
                    (translated, _) = await TranslateChunkAsync(chunk, null, actualTarget, ct).ConfigureAwait(false);
                }
                swapEnabled = false; // 只依第一段決定
            }

            if (string.IsNullOrEmpty(detected) && !string.IsNullOrEmpty(det))
                detected = det;

            sb.Append(translated);
            // 服務常會去掉段尾換行 / 空白，補回來以維持段落
            if (chunk.EndsWith('\n') && !translated.EndsWith('\n')) sb.Append('\n');
            else if (i < chunks.Count - 1 && char.IsWhiteSpace(chunk[^1]) && translated.Length > 0 &&
                     !char.IsWhiteSpace(translated[^1]) && !Languages.IsChinese(actualTarget) && actualTarget != "ja")
                sb.Append(' ');
        }

        return new TranslationResult(sb.ToString().TrimEnd(), detected, actualTarget, Name);
    }
}

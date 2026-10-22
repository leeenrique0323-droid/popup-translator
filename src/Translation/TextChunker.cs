namespace PopupTranslator.Translation;

/// <summary>把長文字切成適合單次請求的片段，盡量在換行 / 句號 / 空白處切開。</summary>
public static class TextChunker
{
    /// <param name="maxChars">每段最多字元數</param>
    /// <param name="measure">每段的「成本」計算（例如 URL 編碼後長度）；null 表示用字元數</param>
    /// <param name="maxCost">每段最大成本</param>
    public static List<string> Split(string text, int maxChars, Func<string, int>? measure = null, int maxCost = int.MaxValue)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        int pos = 0;
        while (pos < text.Length)
        {
            int remaining = text.Length - pos;
            int len = Math.Min(maxChars, remaining);

            // 以成本限制縮短長度（二分搜尋）
            if (measure != null && measure(text.Substring(pos, len)) > maxCost)
            {
                int lo = 1, hi = len;
                while (lo < hi)
                {
                    int mid = (lo + hi + 1) / 2;
                    if (measure(text.Substring(pos, mid)) <= maxCost) lo = mid; else hi = mid - 1;
                }
                len = lo;
            }

            if (pos + len < text.Length)
            {
                int cut = FindBreak(text, pos, len);
                if (cut > 0) len = cut;
                // 不要切開 surrogate pair
                if (len > 1 && char.IsHighSurrogate(text[pos + len - 1])) len--;
            }

            result.Add(text.Substring(pos, len));
            pos += len;
        }
        return result;
    }

    private static int FindBreak(string text, int start, int len)
    {
        int minLen = Math.Max(1, len / 3);
        // 依優先順序：換行 > 句末標點 > 空白 > 逗號類
        char[][] tiers =
        {
            new[] { '\n' },
            new[] { '.', '!', '?', '。', '！', '？', '；', ';', '…' },
            new[] { ' ', '\t', '\u3000' },
            new[] { ',', '，', '、', ':', '：', ')', '）', '」', '』' },
        };
        foreach (var tier in tiers)
        {
            for (int i = len - 1; i >= minLen; i--)
            {
                if (Array.IndexOf(tier, text[start + i]) >= 0)
                    return i + 1; // 分隔符留在前一段
            }
        }
        return -1;
    }
}

namespace PopupTranslator.Translation;

public sealed record TranslationResult(string Text, string DetectedSource, string Target, string Engine);

/// <summary>翻譯過程中的錯誤，Message 為給使用者看的繁體中文說明。</summary>
public sealed class TranslationException : Exception
{
    public TranslationException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// 翻譯引擎介面。新增引擎（例如 DeepL）時實作此介面，或繼承 <see cref="ChunkedTranslatorBase"/>。
/// </summary>
public interface ITranslator
{
    string Name { get; }

    /// <param name="text">原文</param>
    /// <param name="target">目標語言（zh-TW / en / ja / ko / zh-CN）</param>
    /// <param name="chineseToEnglish">目標為繁中且來源偵測為中文時，改翻成英文</param>
    Task<TranslationResult> TranslateAsync(string text, string target, bool chineseToEnglish, CancellationToken ct);
}

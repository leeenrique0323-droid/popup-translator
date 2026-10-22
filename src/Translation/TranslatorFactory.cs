using PopupTranslator.Core;

namespace PopupTranslator.Translation;

public static class TranslatorFactory
{
    private static readonly GoogleTranslator Google = new();

    public static ITranslator Create(AppSettings s) =>
        s.Engine == "deepl" && !string.IsNullOrWhiteSpace(s.DeepLApiKey)
            ? new DeepLTranslator(s.DeepLApiKey)
            : Google;
}

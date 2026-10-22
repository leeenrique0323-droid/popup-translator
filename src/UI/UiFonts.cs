namespace PopupTranslator.UI;

internal static class UiFonts
{
    public const string FamilyName = "Microsoft JhengHei UI";

    public static Font Create(float size, FontStyle style = FontStyle.Regular)
    {
        try { return new Font(FamilyName, size, style, GraphicsUnit.Point); }
        catch { return new Font(FontFamily.GenericSansSerif, size, style, GraphicsUnit.Point); }
    }
}

using System.Text;
using System.Text.RegularExpressions;

namespace PopupTranslator.Ocr;

/// <summary>OCR 文字後處理與語系判斷。</summary>
internal static class TextUtil
{
    private const string CjkClass =
        @"\p{IsCJKUnifiedIdeographs}\p{IsCJKUnifiedIdeographsExtensionA}\p{IsHiragana}\p{IsKatakana}" +
        @"\p{IsCJKSymbolsandPunctuation}\p{IsHalfwidthandFullwidthForms}\p{IsCJKCompatibilityIdeographs}ー・";

    // Windows OCR 對中日文會在每個字之間插入空白，移除之
    private static readonly Regex CjkSpace = new($"(?<=[{CjkClass}])\\s+(?=[{CjkClass}])", RegexOptions.Compiled);

    public static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s);

    public static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0x3040 && c <= 0x30FF) || (c >= 0x3000 && c <= 0x303F) || (c >= 0xFF00 && c <= 0xFFEF);

    public static bool IsKana(char c) => c >= 0x3040 && c <= 0x30FF && c != 0x30FB;
    public static bool IsHangul(char c) => (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0x1100 && c <= 0x11FF) || (c >= 0x3130 && c <= 0x318F);

    public static int CountCjk(string s) => s.Count(IsCjk);
    public static int CountKana(string s) => s.Count(IsKana);
    public static int CountHangul(string s) => s.Count(IsHangul);

    private static int Letters(string s) => s.Count(char.IsLetter);

    public static double LatinRatio(string s)
    {
        int letters = Letters(s);
        return letters == 0 ? 0 : (double)s.Count(c => c < 0x250 && char.IsLetter(c)) / letters;
    }

    public static double KanaScore(string s)
    {
        int letters = Letters(s);
        return letters == 0 ? 0 : (double)CountKana(s) / letters;
    }

    public static double HangulRatio(string s)
    {
        int letters = Letters(s);
        return letters == 0 ? 0 : (double)CountHangul(s) / letters;
    }

    public static string CleanLine(string line) => CjkSpace.Replace(line.Trim(), "");

    /// <summary>合併 OCR 各行。mergeLines = true 時把換行接成段落（遊戲對話框常見的自動換行）。</summary>
    public static string Join(List<string> rawLines, bool mergeLines)
    {
        var lines = rawLines.Select(CleanLine).Where(l => l.Length > 0).ToList();
        if (!mergeLines) return string.Join(Environment.NewLine, lines);

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (sb.Length == 0) { sb.Append(line); continue; }
            char prev = sb[^1];
            char next = line[0];
            if (IsCjk(prev) || IsCjk(next))
            {
                sb.Append(line);
            }
            else if (prev == '-' && sb.Length >= 2 && char.IsLetter(sb[^2]) && char.IsLower(next))
            {
                sb.Length--; // 英文斷字連字號
                sb.Append(line);
            }
            else
            {
                sb.Append(' ').Append(line);
            }
        }
        return sb.ToString();
    }
}

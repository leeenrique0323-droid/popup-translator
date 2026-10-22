using PopupTranslator.Ocr;
using PopupTranslator.Translation;

int fails = 0;
void Check(bool cond, string name) { Console.WriteLine((cond ? "PASS " : "FAIL ") + name); if (!cond) fails++; }

// Chunker
var longText = string.Join("\n", Enumerable.Range(0, 300).Select(i => $"This is sentence number {i}. 這是第 {i} 句中文，用來測試切段。"));
var chunks = TextChunker.Split(longText, 1800, s => Uri.EscapeDataString(s).Length, 4500);
Check(string.Concat(chunks) == longText, $"chunker lossless ({chunks.Count} chunks)");
Check(chunks.All(c => Uri.EscapeDataString(c).Length <= 4500), "chunk url cost limit");
Check(chunks.Take(chunks.Count - 1).All(c => c.EndsWith('\n')), "chunks end at newline");
var noBreak = new string('字', 5000);
var c2 = TextChunker.Split(noBreak, 1800, s => Uri.EscapeDataString(s).Length, 4500);
Check(string.Concat(c2) == noBreak && c2.All(c => Uri.EscapeDataString(c).Length <= 4500), $"hard split ({c2.Count})");
var emoji = string.Concat(Enumerable.Repeat("😀", 3000));
var c3 = TextChunker.Split(emoji, 1800, s => Uri.EscapeDataString(s).Length, 4500);
Check(string.Concat(c3) == emoji && c3.All(c => !char.IsHighSurrogate(c[^1])), "surrogate safe");

// TextUtil
Check(TextUtil.CleanLine("這 是 一 個 測 試 。") == "這是一個測試。", "cjk space removal: " + TextUtil.CleanLine("這 是 一 個 測 試 。"));
Check(TextUtil.CleanLine("Hello world") == "Hello world", "latin spaces kept");
Check(TextUtil.Join(new() { "The quick brown", "fox jumps", "over the lazy dog" }, true) == "The quick brown fox jumps over the lazy dog", "merge latin");
Check(TextUtil.Join(new() { "今 日 は い い", "天 気 で す ね" }, true) == "今日はいい天気ですね", "merge ja");
Check(TextUtil.Join(new() { "inter-", "national" }, true) == "international", "hyphen join");
Check(TextUtil.KanaScore("今日はいい天気ですね") > 0.5, "kana score");
Check(TextUtil.LatinRatio("Hello World 你") > 0.8, "latin ratio");

// Google parse
var (t, d) = GoogleTranslator.Parse("[[[\"你好\",\"Hello\",null,null,10],[\"世界\",\"World\",null,null,10]],null,\"en\",null,null,null,1,[],[[\"en\"],null,[1],[\"en\"]]]");
Check(t == "你好世界" && d == "en", "parse");

// Swap logic with fake translator
{
    var fake = new FakeTranslator();
    var a = await fake.TranslateAsync("今天天氣很好", "zh-TW", true, default);
    Check(a.Target == "en" && fake.Calls.Count == 1 && fake.Calls[0] == "en", "local zh detect -> en, single request");
    fake.Calls.Clear();
    var b = await fake.TranslateAsync("Hello there", "zh-TW", true, default);
    Check(b.Target == "zh-TW" && fake.Calls.Count == 1, "en -> zh-TW single request");
    fake.Calls.Clear();
    fake.EchoDetect = "zh-CN";
    var c = await fake.TranslateAsync("天氣 OK 嗎 hello world foo bar baz", "zh-TW", true, default);
    Check(c.Target == "en" && fake.Calls.Count == 2, "service-detected zh -> retry en");
    fake.Calls.Clear(); fake.EchoDetect = null;
    var dd = await fake.TranslateAsync("今天天氣很好", "ja", true, default);
    Check(dd.Target == "ja", "explicit ja target kept");
    var e = await fake.TranslateAsync("今天天氣很好", "zh-TW", false, default);
    Check(e.Target == "zh-TW", "swap disabled");
    Check(!PopupTranslator.Core.Languages.LooksChinese("今日はいい天気ですね"), "ja not chinese");
    Check(!PopupTranslator.Core.Languages.LooksChinese("오늘 날씨가 좋네요"), "ko not chinese");
}

// Live Google
var g = new GoogleTranslator();
var r1 = await g.TranslateAsync("Hello, how are you today?\nThe weather is nice.", "zh-TW", true, default);
Console.WriteLine($"  -> [{r1.DetectedSource}->{r1.Target}] {r1.Text}");
Check(r1.Target == "zh-TW" && r1.DetectedSource == "en" && r1.Text.Length > 0, "live en->zh-TW");
var r2 = await g.TranslateAsync("今天天氣很好，我們去公園散步吧。", "zh-TW", true, default);
Console.WriteLine($"  -> [{r2.DetectedSource}->{r2.Target}] {r2.Text}");
Check(r2.Target == "en" && r2.Text.Any(char.IsAsciiLetter), "live zh->en swap");
var r2b = await g.TranslateAsync("hello", "zh-CN", true, default);
Console.WriteLine($"  -> [{r2b.DetectedSource}->{r2b.Target}] {r2b.Text}");
var r3 = await g.TranslateAsync("おはようございます。今日もよろしくお願いします。", "ko", true, default);
Console.WriteLine($"  -> [{r3.DetectedSource}->{r3.Target}] {r3.Text}");
Check(r3.Target == "ko" && r3.DetectedSource == "ja", "live ja->ko");
var big = string.Join("\n", Enumerable.Range(0, 40).Select(i => $"Paragraph {i}: The adventurer opened the ancient door and found a glowing sword resting on a stone pedestal."));
var r4 = await g.TranslateAsync(big, "zh-TW", true, default);
Check(r4.Text.Split('\n').Length >= 38, $"live long text lines={r4.Text.Split('\n').Length} chars={big.Length}");
var bigZh = string.Concat(Enumerable.Repeat("這是一段很長的中文測試文字，用來確認切段翻譯是否正常運作。", 60));
var r5 = await g.TranslateAsync(bigZh, "zh-TW", true, default);
Check(r5.Target == "en" && r5.Text.Length > 500, $"live long zh -> en ({bigZh.Length} chars, {TextChunker.Split(bigZh, 1800, s => Uri.EscapeDataString(s).Length, 4500).Count} chunks)");

// DeepL missing key error
try { await new DeepLTranslator("").TranslateAsync("hi", "zh-TW", true, default); Check(false, "deepl no key"); }
catch (TranslationException ex) { Check(ex.Message.Contains("DeepL"), "deepl no key msg: " + ex.Message); }
// DeepL invalid key → 403 message
try { await new DeepLTranslator("invalid:fx").TranslateAsync("hi", "zh-TW", true, default); Check(false, "deepl bad key"); }
catch (TranslationException ex) { Console.WriteLine("  deepl bad key -> " + ex.Message); Check(true, "deepl bad key handled"); }

Console.WriteLine(fails == 0 ? "ALL PASS" : $"{fails} FAILED");
return fails;

sealed class FakeTranslator : ChunkedTranslatorBase
{
    public List<string> Calls = new();
    public string? EchoDetect;
    public override string Name => "Fake";
    protected override int MaxChunkChars => 1000;
    protected override Task<(string Text, string Detected)> TranslateChunkAsync(string chunk, string? source, string target, CancellationToken ct)
    {
        Calls.Add(target);
        return Task.FromResult(($"[{target}]{chunk}", EchoDetect ?? (PopupTranslator.Core.Languages.LooksChinese(chunk) ? "zh-TW" : "en")));
    }
}

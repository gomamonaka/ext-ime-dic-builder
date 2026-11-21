using System.Text;
using System.Xml.Linq;
using ExtImeDicBuilder.Core;

namespace ExtImeDicBuilder.Core.Tests;

/// <summary>
/// Ported from poc/test_dctx.py. Every test there has a matching test here
/// with the same intent (class groupings kept as nested classes purely for
/// readability; xUnit does not need them).
/// </summary>
public class DctxBuilderTests
{
    private static readonly XNamespace Ns1 = DctxBuilder.NsUri;
    private const string XsiNs = DctxBuilder.XsiUri;

    private static DictionaryMeta MakeMeta(string guid = "{11111111-1111-1111-1111-111111111111}") =>
        new(
            Guid: guid,
            ShortName: "テスト辞書",
            LongName: "テスト用ロングネーム",
            Description: "unittest 用の説明",
            Copyright: "Copyright (c) 2026 Test");

    private static XElement Parse(byte[] data)
    {
        var bytes = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? data[3..] : data;
        using var ms = new MemoryStream(bytes);
        return XDocument.Load(ms).Root!;
    }

    private static List<string> OutputStrings(XElement root) =>
        root.Elements(Ns1 + "DictionaryEntry")
            .Select(e => e.Element(Ns1 + "OutputString")?.Value ?? string.Empty)
            .ToList();

    // ---- WellFormedTests ----

    [Fact]
    public void WellFormedAndParses()
    {
        var entries = new[] { new Entry("よみ", "変換結果") };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        var root = Parse(data);
        Assert.EndsWith("}Dictionary", root.Name.ToString());
    }

    [Fact]
    public void HeaderFieldOrderAndValues()
    {
        var meta = MakeMeta();
        var entries = new[] { new Entry("よみ", "出力") };
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve);
        var root = Parse(data);
        var header = root.Element(Ns1 + "DictionaryHeader")!;
        var tags = header.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(new[] { "DictionaryGUID", "DictionaryLanguage", "DictionaryVersion", "CommentInsertion", "DictionaryInfo" }, tags);
        Assert.Equal(meta.Guid, header.Element(Ns1 + "DictionaryGUID")!.Value);
        Assert.Equal("ja-jp", header.Element(Ns1 + "DictionaryLanguage")!.Value);
        Assert.Equal("1", header.Element(Ns1 + "DictionaryVersion")!.Value);
        Assert.Equal("true", header.Element(Ns1 + "CommentInsertion")!.Value);
        var info = header.Element(Ns1 + "DictionaryInfo")!;
        Assert.Equal("ja-jp", info.Attribute("Language")!.Value);
        Assert.Equal(meta.ShortName, info.Element(Ns1 + "ShortName")!.Value);
        Assert.Equal(meta.LongName, info.Element(Ns1 + "LongName")!.Value);
        Assert.Equal(meta.Description, info.Element(Ns1 + "Description")!.Value);
        Assert.Equal(meta.Copyright, info.Element(Ns1 + "Copyright")!.Value);
    }

    [Fact]
    public void EntryFieldOrder()
    {
        var entries = new[] { new Entry("よみ", "出力", "Noun", 100, false, false) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        var root = Parse(data);
        var entryEl = root.Element(Ns1 + "DictionaryEntry")!;
        var tags = entryEl.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(new[] { "InputString", "OutputString", "PartOfSpeech", "Priority", "ReverseConversion", "CommonWord" }, tags);
        Assert.Equal("よみ", entryEl.Element(Ns1 + "InputString")!.Value);
        Assert.Equal("出力", entryEl.Element(Ns1 + "OutputString")!.Value);
        Assert.Equal("Noun", entryEl.Element(Ns1 + "PartOfSpeech")!.Value);
        Assert.Equal("100", entryEl.Element(Ns1 + "Priority")!.Value);
        Assert.Equal("false", entryEl.Element(Ns1 + "ReverseConversion")!.Value);
        Assert.Equal("false", entryEl.Element(Ns1 + "CommonWord")!.Value);
    }

    [Fact]
    public void NamespaceIsCorrect()
    {
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", "b") }, NewlineMode.Preserve);
        var root = Parse(data);
        Assert.Equal(XName.Get("Dictionary", DctxBuilder.NsUri), root.Name);
        Assert.Contains($"xmlns:xsi=\"{XsiNs}\"", Encoding.UTF8.GetString(data));
    }

    [Fact]
    public void NoBomStartsWithXmlDeclaration()
    {
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", "b") }, NewlineMode.Preserve, bom: false);
        Assert.StartsWith("<?xml", Encoding.UTF8.GetString(data));
    }

    [Fact]
    public void BomPrefix()
    {
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", "b") }, NewlineMode.Preserve, bom: true);
        Assert.Equal(0xEF, data[0]);
        Assert.Equal(0xBB, data[1]);
        Assert.Equal(0xBF, data[2]);
        Assert.StartsWith("<?xml", Encoding.UTF8.GetString(data, 3, data.Length - 3));
    }

    // ---- NewlineRoundTripTests ----

    private static readonly string[] Lines4 = { "発言者：", "結論：", "補足：", "対応：" };

    private static string Roundtrip(string output, NewlineMode mode)
    {
        var entries = new[] { new Entry("よみ", output) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, mode);
        return OutputStrings(Parse(data))[0];
    }

    [Fact]
    public void CrRoundtripExact()
    {
        var expected = string.Join("\r", Lines4);
        Assert.Equal(expected, Roundtrip(expected, NewlineMode.Preserve));
    }

    [Fact]
    public void LfRoundtripExact()
    {
        var expected = string.Join("\n", Lines4);
        Assert.Equal(expected, Roundtrip(expected, NewlineMode.Preserve));
    }

    [Fact]
    public void CrlfRoundtripExact()
    {
        var expected = string.Join("\r\n", Lines4);
        Assert.Equal(expected, Roundtrip(expected, NewlineMode.Preserve));
    }

    [Fact]
    public void NewlineModeCrForcesCr()
    {
        var source = string.Join("\n", Lines4);
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", source) }, NewlineMode.Cr);
        var text = Encoding.UTF8.GetString(data);
        Assert.Contains("&#13;", text);
        Assert.DoesNotContain("&#10;", text);
        Assert.Equal(string.Join("\r", Lines4), OutputStrings(Parse(data))[0]);
    }

    [Fact]
    public void NewlineModeLfForcesLf()
    {
        var source = string.Join("\r\n", Lines4);
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", source) }, NewlineMode.Lf);
        var text = Encoding.UTF8.GetString(data);
        Assert.Contains("&#10;", text);
        Assert.DoesNotContain("&#13;", text);
        Assert.Equal(string.Join("\n", Lines4), OutputStrings(Parse(data))[0]);
    }

    [Fact]
    public void NewlineModeCrlfForcesCrlf()
    {
        var source = string.Join("\n", Lines4);
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", source) }, NewlineMode.Crlf);
        Assert.Contains("&#13;&#10;", Encoding.UTF8.GetString(data));
        Assert.Equal(string.Join("\r\n", Lines4), OutputStrings(Parse(data))[0]);
    }

    [Fact]
    public void CrBytesContainNumericRefNotRawCr()
    {
        var entries = new[] { new Entry("a", "line1\rline2") };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        var text = Encoding.UTF8.GetString(data);
        Assert.Contains("&#13;", text);
        var bodyStart = text.IndexOf("<ns1:DictionaryEntry>", StringComparison.Ordinal);
        Assert.DoesNotContain("\r", text[bodyStart..]);
    }

    [Fact]
    public void CrlfBytesContainBothNumericRefs()
    {
        var entries = new[] { new Entry("a", "line1\r\nline2") };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        Assert.Contains("&#13;&#10;", Encoding.UTF8.GetString(data));
    }

    // ---- SpecialCharacterTests ----

    [Fact]
    public void EscapeAmpersandLtGtQuotes()
    {
        const string raw = "A&B <tag> \"quote\" 'apos'";
        var entries = new[] { new Entry("a", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        Assert.Equal(raw, OutputStrings(Parse(data))[0]);
        var text = Encoding.UTF8.GetString(data);
        Assert.Contains("&amp;", text);
        Assert.Contains("&lt;tag&gt;", text);
    }

    [Fact]
    public void EscapeTextHelperDirectly()
    {
        Assert.Equal("A&amp;B", DctxBuilder.EscapeText("A&B"));
        Assert.Equal("&lt;x&gt;", DctxBuilder.EscapeText("<x>"));
        Assert.Equal("&quot;q&quot;", DctxBuilder.EscapeText("\"q\""));
        Assert.Equal("&apos;q&apos;", DctxBuilder.EscapeText("'q'"));
        Assert.Equal("&#13;", DctxBuilder.EscapeText("\r"));
        Assert.Equal("&#10;", DctxBuilder.EscapeText("\n"));
        Assert.Equal("&#9;", DctxBuilder.EscapeText("\t"));
    }

    [Fact]
    public void MixedSpecialAndNewlinesRoundtrip()
    {
        const string raw = "A&B\r\n<tag>\r\n\"quote\"\r\n'apos'";
        var entries = new[] { new Entry("a", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        Assert.Equal(raw, OutputStrings(Parse(data))[0]);
    }

    // ---- TabAndBlankLineTests ----

    [Fact]
    public void TabPreserved()
    {
        const string raw = "\t発言者：\r\n\t結論：";
        var entries = new[] { new Entry("a", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        Assert.Contains("&#9;", Encoding.UTF8.GetString(data));
        Assert.Equal(raw, OutputStrings(Parse(data))[0]);
    }

    [Fact]
    public void BlankLinePreserved()
    {
        const string raw = "発言者：\r\n\r\n結論：";
        var entries = new[] { new Entry("a", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        Assert.Equal(raw, OutputStrings(Parse(data))[0]);
    }

    // ---- TrailingNewlineTests ----

    [Fact]
    public void TrailingNewlinePreserved()
    {
        const string raw = "発言者：\r\n結論：\r\n";
        var entries = new[] { new Entry("a", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        var got = OutputStrings(Parse(data))[0];
        Assert.Equal(raw, got);
        Assert.EndsWith("\r\n", got);
    }

    [Fact]
    public void NoTrailingNewlinePreserved()
    {
        const string raw = "発言者：\r\n結論：";
        var entries = new[] { new Entry("a", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        var got = OutputStrings(Parse(data))[0];
        Assert.Equal(raw, got);
        Assert.False(got.EndsWith("\r\n", StringComparison.Ordinal));
        Assert.False(got.EndsWith("\n", StringComparison.Ordinal));
    }

    // ---- UnicodeTests ----

    [Fact]
    public void JapaneseRoundtrip()
    {
        const string raw = "発言者：結論：補足：対応：";
        var entries = new[] { new Entry("よみ", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        var root = Parse(data);
        Assert.Equal("よみ", root.Descendants(Ns1 + "InputString").First().Value);
        Assert.Equal(raw, OutputStrings(root)[0]);
    }

    [Fact]
    public void EmojiSurrogatePairRoundtrip()
    {
        var raw = "テスト\U0001F600\U0001F44D終わり";
        var entries = new[] { new Entry("よみ", raw) };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        Assert.Equal(raw, OutputStrings(Parse(data))[0]);
    }

    // ---- BomTests ----

    [Fact]
    public void BomAbsentByDefault()
    {
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", "b") }, NewlineMode.Preserve);
        Assert.False(data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF);
    }

    [Fact]
    public void BomPresentWhenRequested()
    {
        var data = DctxBuilder.BuildDctx(MakeMeta(), new[] { new Entry("a", "b") }, NewlineMode.Preserve, bom: true);
        Assert.True(data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF);
    }

    [Fact]
    public void BomAndNoBomParseToSameContent()
    {
        var entries = new[] { new Entry("a", "b\r\nc") };
        var noBom = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve, bom: false);
        var withBom = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve, bom: true);
        Assert.Equal(noBom, withBom[3..]);
        Assert.Equal(OutputStrings(Parse(noBom)), OutputStrings(Parse(withBom)));
    }

    // ---- IdempotencyTests ----

    [Fact]
    public void TwoGenerationsAreByteIdentical()
    {
        var meta = MakeMeta();
        var entries = new[]
        {
            new Entry("ぎじしーあーる", "a\rb"),
            new Entry("ぎじえるえふ", "a\nb"),
            new Entry("ぎじしーあーるえるえふ", "a\r\nb"),
        };
        var data1 = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve);
        var data2 = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve);
        Assert.Equal(data1, data2);
    }

    // ---- NormalizeNewlinesTests ----

    [Fact]
    public void PreserveIsIdentity()
    {
        Assert.Equal("a\rb\nc\r\nd", DctxBuilder.NormalizeNewlines("a\rb\nc\r\nd", NewlineMode.Preserve));
    }

    [Fact]
    public void LfCollapsesAllStyles()
    {
        Assert.Equal("a\nb\nc\nd", DctxBuilder.NormalizeNewlines("a\rb\nc\r\nd", NewlineMode.Lf));
    }

    [Fact]
    public void CrCollapsesAllStyles()
    {
        Assert.Equal("a\rb\rc\rd", DctxBuilder.NormalizeNewlines("a\rb\nc\r\nd", NewlineMode.Cr));
    }

    [Fact]
    public void CrlfCollapsesAllStyles()
    {
        Assert.Equal("a\r\nb\r\nc\r\nd", DctxBuilder.NormalizeNewlines("a\rb\nc\r\nd", NewlineMode.Crlf));
    }

    // (No "unknown mode raises" test: NewlineMode is a C# enum, so an
    // invalid value is not constructible without an unsafe cast; the
    // Python test existed only because Python's mode was a bare string.)

    // ---- JoinLinesTests ----

    [Fact]
    public void JoinLinesCr()
    {
        Assert.Equal("a\rb\rc", DctxBuilder.JoinLines(new[] { "a", "b", "c" }, NewlineMode.Cr));
    }

    [Fact]
    public void JoinLinesTrailing()
    {
        Assert.Equal("a\r\nb\r\n", DctxBuilder.JoinLines(new[] { "a", "b" }, NewlineMode.Crlf, trailingNewline: true));
    }

    [Fact]
    public void JoinLinesRejectsPreserve()
    {
        Assert.Throws<ArgumentException>(() => DctxBuilder.JoinLines(new[] { "a" }, NewlineMode.Preserve));
    }

    // ---- Self-verify sanity ----

    [Fact]
    public void SelfVerifyPassesForValidData()
    {
        var entries = new[] { new Entry("よみ", "a\r\nb") };
        var data = DctxBuilder.BuildDctx(MakeMeta(), entries, NewlineMode.Preserve);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Preserve); // must not throw
    }
}

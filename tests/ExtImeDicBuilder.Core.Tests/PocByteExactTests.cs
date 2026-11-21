using System.Security.Cryptography;
using ExtImeDicBuilder.Core;

namespace ExtImeDicBuilder.Core.Tests;

/// <summary>
/// The decisive acceptance test for the C# port: rebuild the same 6 .dctx
/// files that poc/generate_poc.py produces, using the exact same fixed
/// GUIDs, metadata strings and entry content (copied from
/// poc/generate_poc.py), and byte-compare each one against the real file
/// under poc/out/. poc/out/*.dctx were verified against a real Windows 11
/// Microsoft IME, so a byte-for-byte match here is the proof this port is
/// correct.
/// </summary>
public class PocByteExactTests
{
    // Fixed, per-file GUIDs copied verbatim from poc/generate_poc.py.
    private const string GuidMain = "{00d41162-3de9-4a60-8903-e98d815c6bdb}";
    private const string GuidCr = "{6468566d-56e1-4383-aba2-d52eec31bd2a}";
    private const string GuidLf = "{5bb39678-5470-487a-92f0-fdd7fe764e70}";
    private const string GuidCrlf = "{3bb7ca6b-ba78-4f89-8ae4-0dff57a6a6a1}";
    private const string GuidAscii = "{ac5ab976-5c1c-46c0-97a6-d7b4eafd58c7}";
    private const string GuidBom = "{41e14f35-4291-4559-ac46-226df28b0b29}";

    private static readonly string[] BodyLines = { "発言者：", "結論：", "補足：", "対応：" };
    private const string Copyright = "Copyright (c) 2026 PoC";

    private static string Body(NewlineMode mode, bool trailingNewline = false) =>
        DctxBuilder.JoinLines(BodyLines, mode, trailingNewline);

    private static List<Entry> BuildMainEntries() => new()
    {
        new Entry("ぎじしーあーる", Body(NewlineMode.Cr)),
        new Entry("ぎじえるえふ", Body(NewlineMode.Lf)),
        new Entry("ぎじしーあーるえるえふ", Body(NewlineMode.Crlf)),
        new Entry("ぎじたんいつ", "発言者：結論：補足：対応："),
        new Entry("ぎじくうぎょう", DctxBuilder.JoinLines(new[] { "発言者：", "", "結論：", "補足：" }, NewlineMode.Crlf)),
        new Entry("ぎじたぶ", DctxBuilder.JoinLines(new[] { "\t発言者：", "\t結論：", "\t補足：", "\t対応：" }, NewlineMode.Crlf)),
        new Entry("ぎじまつび", Body(NewlineMode.Crlf, trailingNewline: true)),
        new Entry("ぎじとくしゅ", DctxBuilder.JoinLines(new[] { "A&B", "<tag>", "\"quote\"", "'apos'" }, NewlineMode.Crlf)),
    };

    private static List<Entry> BuildAsciiEntries() => new()
    {
        new Entry("ぎじcr", Body(NewlineMode.Cr)),
        new Entry("ぎじlf", Body(NewlineMode.Lf)),
        new Entry("ぎじcrlf", Body(NewlineMode.Crlf)),
    };

    private static string PocOutDir()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var candidate = Path.Combine(dir, "poc", "out");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate poc/out relative to test assembly at " + AppContext.BaseDirectory);
    }

    private static void AssertMatchesPocFile(string fileName, byte[] generated)
    {
        var pocPath = Path.Combine(PocOutDir(), fileName);
        var expected = File.ReadAllBytes(pocPath);

        var expectedHash = Convert.ToHexString(SHA256.HashData(expected));
        var actualHash = Convert.ToHexString(SHA256.HashData(generated));

        Assert.True(
            expected.AsSpan().SequenceEqual(generated),
            $"{fileName}: byte mismatch.\n  expected SHA-256={expectedHash} ({expected.Length} bytes)\n  actual   SHA-256={actualHash} ({generated.Length} bytes)");
    }

    [Fact]
    public void MainDictionaryMatchesPocByteForByte()
    {
        var entries = BuildMainEntries();
        var meta = new DictionaryMeta(
            Guid: GuidMain,
            ShortName: "複数行PoC",
            LongName: "複数行変換候補 PoC 辞書",
            Description: "IME拡張辞書で複数行の変換候補（改行コード CR/LF/CRLF）が機能するかを検証するPoC辞書",
            Copyright: Copyright);
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Preserve);
        AssertMatchesPocFile("ime_multiline_poc.dctx", data);

        // Also pin the exact hash from the task's own acceptance criteria.
        Assert.Equal(
            "B32E939BF34ED93F78060FCC6FB14A24B5FA7BE6632F05C20DC3B84E2C792D7E",
            Convert.ToHexString(SHA256.HashData(data)));
    }

    [Fact]
    public void CrIsolationFileMatchesPocByteForByte()
    {
        var entries = new List<Entry> { new("ぎじしーあーる", Body(NewlineMode.Cr)) };
        var meta = new DictionaryMeta(
            Guid: GuidCr,
            ShortName: "複数行PoC_CR",
            LongName: "複数行PoC 切り分け用 (CR)",
            Description: "CR区切りの複数行候補のみを含む切り分け用PoC辞書",
            Copyright: Copyright);
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Cr);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Cr);
        AssertMatchesPocFile("ime_multiline_poc_cr.dctx", data);
    }

    [Fact]
    public void LfIsolationFileMatchesPocByteForByte()
    {
        var entries = new List<Entry> { new("ぎじえるえふ", Body(NewlineMode.Lf)) };
        var meta = new DictionaryMeta(
            Guid: GuidLf,
            ShortName: "複数行PoC_LF",
            LongName: "複数行PoC 切り分け用 (LF)",
            Description: "LF区切りの複数行候補のみを含む切り分け用PoC辞書",
            Copyright: Copyright);
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Lf);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Lf);
        AssertMatchesPocFile("ime_multiline_poc_lf.dctx", data);
    }

    [Fact]
    public void CrlfIsolationFileMatchesPocByteForByte()
    {
        var entries = new List<Entry> { new("ぎじしーあーるえるえふ", Body(NewlineMode.Crlf)) };
        var meta = new DictionaryMeta(
            Guid: GuidCrlf,
            ShortName: "複数行PoC_CRLF",
            LongName: "複数行PoC 切り分け用 (CRLF)",
            Description: "CRLF区切りの複数行候補のみを含む切り分け用PoC辞書",
            Copyright: Copyright);
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Crlf);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Crlf);
        AssertMatchesPocFile("ime_multiline_poc_crlf.dctx", data);
    }

    [Fact]
    public void AsciiReadingFileMatchesPocByteForByte()
    {
        var entries = BuildAsciiEntries();
        var meta = new DictionaryMeta(
            Guid: GuidAscii,
            ShortName: "複数行PoC_ASCII読み",
            LongName: "複数行PoC ASCII読み受け入れテスト",
            Description: "InputString に ASCII 文字を含む読みが受理されるかを検証するPoC辞書",
            Copyright: Copyright);
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Preserve);
        AssertMatchesPocFile("ime_multiline_poc_ascii.dctx", data);
    }

    [Fact]
    public void BomFileMatchesPocByteForByte()
    {
        var entries = BuildMainEntries();
        var meta = new DictionaryMeta(
            Guid: GuidBom,
            ShortName: "複数行PoC",
            LongName: "複数行変換候補 PoC 辞書 (UTF-8 BOM付き)",
            Description: "本命ファイルと同一内容の UTF-8 BOM 付き版。BOM有無での読み込み可否比較用",
            Copyright: Copyright);
        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve, bom: true);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Preserve);
        AssertMatchesPocFile("ime_multiline_poc.utf8bom.dctx", data);
    }
}

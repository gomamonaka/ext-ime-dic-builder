using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ExtImeDicBuilder.Core;

/// <summary>
/// Newline style used to render an <see cref="Entry.Output"/> string inside a
/// generated .dctx file. "Preserve" leaves the string's existing CR/LF/CRLF
/// mix untouched (used when a caller has already hand-built text that mixes
/// newline styles); the other three modes first collapse every existing
/// newline style to LF and then re-expand to the requested style.
/// </summary>
public enum NewlineMode
{
    Cr,
    Lf,
    Crlf,
    Preserve,
}

/// <summary>One DictionaryEntry.</summary>
public sealed record Entry(
    string Reading,
    string Output,
    string PartOfSpeech = "Noun",
    int Priority = 100,
    bool ReverseConversion = false,
    bool CommonWord = false);

/// <summary>The DictionaryHeader fields.</summary>
public sealed record DictionaryMeta(
    string Guid,
    string ShortName,
    string LongName,
    string Description,
    string Copyright,
    string Language = "ja-jp",
    int Version = 1,
    bool CommentInsertion = true);

/// <summary>
/// Pure, side-effect-free builder for Microsoft IME "Open Extended
/// Dictionary" (.dctx) files. Ported line-for-line from poc/dctx.py so that
/// output is byte-for-byte identical to the Python reference implementation.
///
/// Why not System.Xml.Linq/XmlWriter's own serialization? XML 1.0 end-of-line
/// handling (https://www.w3.org/TR/xml/#sec-line-ends) requires every
/// conforming parser to normalize raw CR and CRLF sequences in the source to
/// a single LF before the text reaches the application, no matter which
/// serializer wrote the file. The only reliable way to transmit CR or CRLF
/// through XML text content is a numeric character reference (&amp;#13; /
/// &amp;#13;&amp;#10;), which is not subject to end-of-line normalization.
/// This class therefore performs its own minimal, centralized text
/// serialization instead of relying on XmlWriter's escaping, funneling every
/// piece of text through the single <see cref="EscapeText"/> method.
/// </summary>
public static class DctxBuilder
{
    public const string NsUri = "http://www.microsoft.com/ime/dctx";
    public const string XsiUri = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly (char Ch, string Ref)[] ControlCharRefs =
    {
        ('\r', "&#13;"),
        ('\n', "&#10;"),
        ('\t', "&#9;"),
    };

    /// <summary>
    /// Escape <paramref name="value"/> for use as XML element (or attribute)
    /// text content. This is the single centralized escaping routine used
    /// everywhere in this class. It escapes <c>&amp; &lt; &gt; " '</c> and
    /// then replaces CR / LF / TAB with numeric character references so that
    /// CR and CRLF line breaks survive XML 1.0 end-of-line normalization
    /// intact. No other code in this class concatenates raw, unescaped text
    /// into the output XML string.
    /// </summary>
    public static string EscapeText(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&apos;"); break;
                case '\r': sb.Append("&#13;"); break;
                case '\n': sb.Append("&#10;"); break;
                case '\t': sb.Append("&#9;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Rewrite line breaks in <paramref name="text"/> to the style named by
    /// <paramref name="newlineMode"/>. <see cref="NewlineMode.Preserve"/>
    /// returns <paramref name="text"/> unchanged. The other modes first
    /// collapse every CRLF/CR/LF already present to a bare LF and then
    /// re-expand to the requested style.
    /// </summary>
    public static string NormalizeNewlines(string text, NewlineMode newlineMode)
    {
        if (newlineMode == NewlineMode.Preserve)
        {
            return text;
        }

        var separator = SeparatorFor(newlineMode);
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return normalized.Replace("\n", separator);
    }

    /// <summary>
    /// Join <paramref name="lines"/> using the raw separator for
    /// <paramref name="newlineMode"/>. <paramref name="newlineMode"/> must be
    /// one of Cr, Lf, Crlf (not Preserve).
    /// </summary>
    public static string JoinLines(IReadOnlyList<string> lines, NewlineMode newlineMode, bool trailingNewline = false)
    {
        if (newlineMode == NewlineMode.Preserve)
        {
            throw new ArgumentException("JoinLines requires an explicit style, got Preserve", nameof(newlineMode));
        }

        var sep = SeparatorFor(newlineMode);
        var text = string.Join(sep, lines);
        if (trailingNewline)
        {
            text += sep;
        }

        return text;
    }

    private static string SeparatorFor(NewlineMode mode) => mode switch
    {
        NewlineMode.Cr => "\r",
        NewlineMode.Lf => "\n",
        NewlineMode.Crlf => "\r\n",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown newline mode"),
    };

    private static string BoolStr(bool value) => value ? "true" : "false";

    private static string Element(string tag, string text) => $"<ns1:{tag}>{EscapeText(text)}</ns1:{tag}>";

    private static string EntryXml(Entry entry, NewlineMode newlineMode)
    {
        var outputText = NormalizeNewlines(entry.Output, newlineMode);
        var lines = new[]
        {
            "  <ns1:DictionaryEntry>",
            $"    {Element("InputString", entry.Reading)}",
            $"    {Element("OutputString", outputText)}",
            $"    {Element("PartOfSpeech", entry.PartOfSpeech)}",
            $"    {Element("Priority", entry.Priority.ToString())}",
            $"    {Element("ReverseConversion", BoolStr(entry.ReverseConversion))}",
            $"    {Element("CommonWord", BoolStr(entry.CommonWord))}",
            "  </ns1:DictionaryEntry>",
        };
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Build a complete .dctx file and return it as bytes.
    /// <paramref name="newlineMode"/> controls how line breaks inside every
    /// <see cref="Entry.Output"/> are represented on disk. Pass
    /// <see cref="NewlineMode.Preserve"/> when entries already contain their
    /// final, intended raw newline characters (e.g. when a single file mixes
    /// entries of different newline styles).
    ///
    /// The returned bytes are UTF-8. Pass <paramref name="bom"/> = true to
    /// prepend a UTF-8 byte order mark (EF BB BF); the default (false)
    /// produces a BOM-less file.
    /// </summary>
    public static byte[] BuildDctx(DictionaryMeta meta, IReadOnlyList<Entry> entries, NewlineMode newlineMode, bool bom = false)
    {
        var header = new[]
        {
            "  <ns1:DictionaryHeader>",
            $"    {Element("DictionaryGUID", meta.Guid)}",
            $"    {Element("DictionaryLanguage", meta.Language)}",
            $"    {Element("DictionaryVersion", meta.Version.ToString())}",
            $"    {Element("CommentInsertion", BoolStr(meta.CommentInsertion))}",
            $"    <ns1:DictionaryInfo Language=\"{EscapeText(meta.Language)}\">",
            $"      {Element("ShortName", meta.ShortName)}",
            $"      {Element("LongName", meta.LongName)}",
            $"      {Element("Description", meta.Description)}",
            $"      {Element("Copyright", meta.Copyright)}",
            "    </ns1:DictionaryInfo>",
            "  </ns1:DictionaryHeader>",
        };

        var body = entries.Select(e => EntryXml(e, newlineMode));

        var doc = new List<string>
        {
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>",
            $"<ns1:Dictionary xmlns:ns1=\"{NsUri}\" xmlns:xsi=\"{XsiUri}\">",
        };
        doc.AddRange(header);
        doc.AddRange(body);
        doc.Add("</ns1:Dictionary>");

        var text = string.Join("\n", doc) + "\n";
        var data = Encoding.UTF8.GetBytes(text);
        if (bom)
        {
            var withBom = new byte[data.Length + 3];
            withBom[0] = 0xEF;
            withBom[1] = 0xBB;
            withBom[2] = 0xBF;
            Buffer.BlockCopy(data, 0, withBom, 3, data.Length);
            return withBom;
        }

        return data;
    }

    /// <summary>
    /// Re-parse <paramref name="data"/> (as produced by <see cref="BuildDctx"/>)
    /// and assert that every OutputString text comes back exactly equal to
    /// the corresponding <paramref name="entries"/> element's Output (after
    /// the same newline normalization <see cref="BuildDctx"/> applied).
    /// Throws <see cref="InvalidOperationException"/> on any mismatch. This
    /// is the C# equivalent of generate_poc.py's self-verification step.
    /// </summary>
    public static void SelfVerify(byte[] data, IReadOnlyList<Entry> entries, NewlineMode newlineMode)
    {
        var xmlBytes = data;
        if (xmlBytes.Length >= 3 && xmlBytes[0] == 0xEF && xmlBytes[1] == 0xBB && xmlBytes[2] == 0xBF)
        {
            xmlBytes = xmlBytes[3..];
        }

        XDocument xdoc;
        using (var ms = new MemoryStream(xmlBytes))
        using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            xdoc = XDocument.Load(reader);
        }

        XNamespace ns = NsUri;
        var parsedOutputs = xdoc.Root!
            .Elements(ns + "DictionaryEntry")
            .Select(el => el.Element(ns + "OutputString")?.Value ?? string.Empty)
            .ToList();

        var expectedOutputs = entries.Select(e => NormalizeNewlines(e.Output, newlineMode)).ToList();

        if (parsedOutputs.Count != expectedOutputs.Count)
        {
            throw new InvalidOperationException(
                $"entry count mismatch: parsed={parsedOutputs.Count} expected={expectedOutputs.Count}");
        }

        for (var i = 0; i < parsedOutputs.Count; i++)
        {
            if (parsedOutputs[i] != expectedOutputs[i])
            {
                throw new InvalidOperationException(
                    $"entry {i} OutputString mismatch\n  expected={expectedOutputs[i]}\n  parsed  ={parsedOutputs[i]}");
            }
        }
    }
}

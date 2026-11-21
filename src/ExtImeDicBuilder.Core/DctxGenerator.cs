namespace ExtImeDicBuilder.Core;

/// <summary>
/// Turns an <see cref="EntryStore"/> (the JSON master data) into a complete
/// .dctx file, using fixed dictionary metadata for this application.
/// </summary>
public static class DctxGenerator
{
    public const string ShortName = "複数行定型文辞書";
    public const string LongName = "ExtIMEDicBuilder 複数行定型文辞書";
    public const string Description = "ExtIMEDicBuilder で登録した複数行の定型文を Microsoft IME の変換候補として提供する拡張辞書";
    public const string Copyright = "Copyright (c) 2026 ExtIMEDicBuilder";

    /// <summary>
    /// Build the .dctx bytes for <paramref name="store"/>. Entry output text
    /// is expected to already be CRLF-normalized (see
    /// <see cref="EntryValidator.NormalizeToCrlf"/>), so newline mode
    /// Preserve is used: each entry's stored text is emitted as-is.
    /// </summary>
    public static byte[] Generate(EntryStore store, bool bom = false)
    {
        var meta = new DictionaryMeta(
            Guid: store.DictionaryGuid,
            ShortName: ShortName,
            LongName: LongName,
            Description: Description,
            Copyright: Copyright,
            Version: store.DictionaryVersion);

        var entries = store.Entries
            .Select(e => new Entry(e.Reading, e.Output))
            .ToList();

        var data = DctxBuilder.BuildDctx(meta, entries, NewlineMode.Preserve, bom);
        DctxBuilder.SelfVerify(data, entries, NewlineMode.Preserve);
        return data;
    }
}

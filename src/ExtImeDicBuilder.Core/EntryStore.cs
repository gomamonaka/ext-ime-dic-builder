using System.Text.Json.Serialization;

namespace ExtImeDicBuilder.Core;

/// <summary>The on-disk shape of entries.json.</summary>
public sealed class EntryStore
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// Fixed per-installation dictionary GUID. Generated once on first save
    /// and never changed afterward: the file the IME installs the .dctx
    /// under is named after this GUID (see poc/README.md), so re-generating
    /// with the same GUID upgrades the existing installed dictionary in
    /// place, while a different GUID would install a second, separate
    /// dictionary alongside it.
    /// </summary>
    [JsonPropertyName("dictionaryGuid")]
    public string DictionaryGuid { get; set; } = string.Empty;

    /// <summary>
    /// The DCTX <c>DictionaryVersion</c> header value to use the next time a
    /// .dctx is generated for the IME update flow. Incremented by one every
    /// time "IME辞書を更新" regenerates and (re-)registers the dictionary
    /// under the same, unchanging <see cref="DictionaryGuid"/>.
    ///
    /// Whether Microsoft IME actually uses this value to detect that an
    /// update has occurred has NOT been measured on real hardware — the PoC
    /// only exercised a single, first-time install at version 1. Always
    /// incrementing is a safety measure adopted in the absence of that
    /// measurement, not a confirmed requirement.
    /// </summary>
    [JsonPropertyName("dictionaryVersion")]
    public int DictionaryVersion { get; set; } = 1;

    [JsonPropertyName("entries")]
    public List<DictionaryEntryRecord> Entries { get; set; } = new();
}

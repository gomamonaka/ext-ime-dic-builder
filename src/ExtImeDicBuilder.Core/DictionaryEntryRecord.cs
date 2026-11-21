using System.Text.Json.Serialization;

namespace ExtImeDicBuilder.Core;

/// <summary>
/// One user-authored entry as stored in the JSON master data file
/// (%APPDATA%\ExtImeDicBuilder\entries.json). This is the "source of truth";
/// the .dctx file is a generated artifact derived from a list of these.
/// </summary>
public sealed class DictionaryEntryRecord
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = System.Guid.NewGuid().ToString();

    [JsonPropertyName("reading")]
    public string Reading { get; set; } = string.Empty;

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }
}

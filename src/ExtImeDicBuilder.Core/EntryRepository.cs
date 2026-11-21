using System.Text.Encodings.Web;
using System.Text.Json;

namespace ExtImeDicBuilder.Core;

/// <summary>
/// Loads and saves the JSON master data file
/// (%APPDATA%\ExtImeDicBuilder\entries.json). Saves are atomic: the new
/// content is written to a temporary file first and then swapped into place
/// with <see cref="File.Replace(string, string, string?)"/> (or a move when
/// no previous file exists yet), so a crash mid-write cannot corrupt or lose
/// existing data.
/// </summary>
public sealed class EntryRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; }

    public EntryRepository(string? filePath = null)
    {
        FilePath = filePath ?? GetDefaultFilePath();
    }

    public static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "ExtImeDicBuilder", "entries.json");
    }

    /// <summary>
    /// Load the store from disk. If the file does not exist yet, returns a
    /// fresh, empty store with a newly generated <see cref="EntryStore.DictionaryGuid"/>
    /// (not yet persisted; call <see cref="Save"/> to persist it).
    /// </summary>
    public EntryStore Load()
    {
        if (!File.Exists(FilePath))
        {
            return new EntryStore
            {
                Version = 1,
                DictionaryGuid = NewDictionaryGuid(),
                Entries = new List<DictionaryEntryRecord>(),
            };
        }

        var json = File.ReadAllText(FilePath);
        var store = JsonSerializer.Deserialize<EntryStore>(json, JsonOptions)
                    ?? new EntryStore { Version = 1, DictionaryGuid = NewDictionaryGuid() };

        if (string.IsNullOrWhiteSpace(store.DictionaryGuid))
        {
            // Defensive: a hand-edited or corrupted file with no GUID still
            // gets a stable one going forward rather than crashing.
            store.DictionaryGuid = NewDictionaryGuid();
        }

        return store;
    }

    /// <summary>Atomically persist <paramref name="store"/> to <see cref="FilePath"/>.</summary>
    public void Save(EntryStore store)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(store, JsonOptions);
        var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, json);

        if (File.Exists(FilePath))
        {
            File.Replace(tempPath, FilePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempPath, FilePath);
        }
    }

    /// <summary>
    /// A fixed-format DCTX DictionaryGUID string: "{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}", lowercase.
    /// </summary>
    public static string NewDictionaryGuid() => "{" + Guid.NewGuid().ToString("D") + "}";
}

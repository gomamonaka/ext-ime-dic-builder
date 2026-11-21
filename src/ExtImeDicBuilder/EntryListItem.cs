using ExtImeDicBuilder.Core;

namespace ExtImeDicBuilder;

/// <summary>
/// DataGrid row wrapper around one <see cref="DictionaryEntryRecord"/>.
/// Exposes a single-line preview of the (possibly multi-line) output, with
/// newlines replaced by "⏎" so the grid never wraps or grows a row.
/// </summary>
public sealed class EntryListItem
{
    public EntryListItem(DictionaryEntryRecord record)
    {
        Record = record;
    }

    public DictionaryEntryRecord Record { get; }

    public string Id => Record.Id;

    public string Reading => Record.Reading;

    public string OutputPreview
    {
        get
        {
            var oneLine = Record.Output.Replace("\r\n", "⏎").Replace("\r", "⏎").Replace("\n", "⏎");
            const int maxLength = 80;
            return oneLine.Length > maxLength ? oneLine[..maxLength] + "…" : oneLine;
        }
    }

    public DateTimeOffset UpdatedAt => Record.UpdatedAt;
}

namespace ExtImeDicBuilder.Core;

/// <summary>One validation problem: either a blocking error or a non-blocking warning.</summary>
public sealed record ValidationIssue(string Message, bool IsError);

/// <summary>
/// Result of validating a single entry (or a candidate entry against the
/// rest of the dictionary). <see cref="IsValid"/> is true when there are no
/// blocking errors; warnings never block registration.
/// </summary>
public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = new();

    public bool IsValid => !Issues.Any(i => i.IsError);

    public IEnumerable<string> Errors => Issues.Where(i => i.IsError).Select(i => i.Message);

    public IEnumerable<string> Warnings => Issues.Where(i => !i.IsError).Select(i => i.Message);

    public void AddError(string message) => Issues.Add(new ValidationIssue(message, true));

    public void AddWarning(string message) => Issues.Add(new ValidationIssue(message, false));
}

/// <summary>
/// Validates a candidate reading/output pair per
/// claude_code_ime_multiline_dictionary_instructions.md §12.
///
/// Deliberately permissive on the two points the instructions call out as
/// unverified: duplicate readings and a character-count ceiling. Neither is
/// confirmed against real Microsoft IME behavior, so both are surfaced as
/// warnings only and never block registration.
/// </summary>
public static class EntryValidator
{
    /// <summary>
    /// Soft, non-blocking character-count ceiling for Output. This number is
    /// NOT an observed Microsoft IME limit — it has not been measured on
    /// real hardware. It exists only to warn a user who pastes an unusually
    /// large block of text; registration is never blocked by it. Do not
    /// raise this to a hard limit without real-device (実機) measurement.
    /// </summary>
    public const int SoftOutputLengthWarningThreshold = 1000;

    /// <summary>Characters XML 1.0 cannot represent at all, even escaped.</summary>
    private static bool IsXmlIllegalChar(char c)
    {
        var code = (int)c;
        if (code is >= 0x00 and <= 0x08) return true;
        if (code == 0x0B || code == 0x0C) return true;
        if (code is >= 0x0E and <= 0x1F) return true;
        if (code == 0xFFFE || code == 0xFFFF) return true;
        return false;
    }

    /// <summary>Normalize any mix of CR / LF / CRLF in <paramref name="text"/> to CRLF.</summary>
    public static string NormalizeToCrlf(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");

    /// <summary>
    /// Validate one candidate entry in isolation (emptiness, illegal XML
    /// characters, soft length warning). Does not know about other entries;
    /// see <see cref="ValidateAgainstExisting"/> for duplicate-reading
    /// checks.
    /// </summary>
    public static ValidationResult Validate(string reading, string output)
    {
        var result = new ValidationResult();

        if (string.IsNullOrEmpty(reading))
        {
            result.AddError("読みを入力してください。");
        }

        if (string.IsNullOrEmpty(output))
        {
            result.AddError("変換結果を入力してください。");
        }

        foreach (var c in reading)
        {
            if (IsXmlIllegalChar(c))
            {
                result.AddError($"読みに使用できない制御文字が含まれています (U+{(int)c:X4})。");
                break;
            }
        }

        foreach (var c in output)
        {
            if (IsXmlIllegalChar(c))
            {
                result.AddError($"変換結果に使用できない制御文字が含まれています (U+{(int)c:X4})。");
                break;
            }
        }

        if (output.Length > SoftOutputLengthWarningThreshold)
        {
            result.AddWarning(
                $"変換結果が{SoftOutputLengthWarningThreshold}文字を超えています。" +
                "IME 側の実測上限は未確認のため登録は可能ですが、長すぎると反映に失敗する可能性があります。");
        }

        return result;
    }

    /// <summary>
    /// Check a candidate reading/output pair against the rest of the
    /// dictionary for duplicate readings. A reading that already exists is
    /// only a warning (Microsoft IME can show multiple candidates for the
    /// same reading; this has not been disproven, so it must not block
    /// registration). An entry whose reading AND output both already exist
    /// exactly is an error, since it would add nothing.
    /// <paramref name="excludeId"/> lets an in-place edit skip comparing an
    /// entry against itself.
    /// </summary>
    public static ValidationResult ValidateAgainstExisting(
        string reading,
        string output,
        IEnumerable<DictionaryEntryRecord> existing,
        string? excludeId = null)
    {
        var result = new ValidationResult();
        var normalizedOutput = NormalizeToCrlf(output);

        foreach (var entry in existing)
        {
            if (excludeId is not null && entry.Id == excludeId)
            {
                continue;
            }

            if (entry.Reading != reading)
            {
                continue;
            }

            if (NormalizeToCrlf(entry.Output) == normalizedOutput)
            {
                result.AddError("読みと変換結果が完全に同じ登録が既に存在します。");
            }
            else
            {
                result.AddWarning($"読み「{reading}」は既に他の登録と重複しています。IME は複数候補として扱える可能性があります。");
            }
        }

        return result;
    }
}

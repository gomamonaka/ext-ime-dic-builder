namespace ExtImeDicBuilder.Core;

/// <summary>
/// Builds the filesystem paths involved in the IME registration workflow
/// (poc/README.md "実機検証手順"). Every path is derived from
/// <see cref="Environment.GetFolderPath"/> / <see cref="Environment.ExpandEnvironmentVariables"/>
/// rather than a hardcoded drive letter, so this works on any Windows
/// installation regardless of where the system/user profile lives.
/// </summary>
public static class ImePaths
{
    /// <summary>
    /// "Microsoft IME Open Extended Dictionary Module" — takes a .dctx path
    /// as its single argument, shows the mandatory user-approval "signature"
    /// dialog, and (on approval) copies the file into
    /// <see cref="GetInstalledDctxPath"/>.
    /// </summary>
    public static string GetImewdbldExePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "IME", "SHARED", "imewdbld.exe");

    /// <summary>
    /// IME JP utility executable used for ADDSYSDICT / CHECKSYSDICT /
    /// REMOVESYSDICT.
    /// </summary>
    public static string GetImjpuexcExePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "IME", "IMEJP", "imjpuexc.exe");

    /// <summary>
    /// Best-effort *expected* install path, built from the GUID exactly as
    /// stored in entries.json (lowercase). This is only a guess: the actual
    /// on-disk casing imewdbld.exe uses has been observed on real hardware
    /// to be uppercase (see <see cref="TryResolveInstalledDctxPath"/>), so
    /// this path must NOT be handed to imjpuexc.exe — ADDSYSDICT writes
    /// whatever string it is given verbatim into the registry, and a
    /// spelling that doesn't match the real file on disk risks creating a
    /// duplicate registry entry instead of updating the existing one. Use
    /// this value only as the fallback "nothing is installed yet" case.
    /// </summary>
    public static string GetInstalledDctxPath(string dictionaryGuid) =>
        Path.Combine(GetInstalledDictsDirectory(), dictionaryGuid + ".dctx");

    /// <summary>
    /// Resolve the *actual* on-disk path of an installed .dctx for
    /// <paramref name="dictionaryGuid"/> by listing the install directory and
    /// matching the file name against the GUID case-insensitively (observed
    /// on real hardware: imewdbld.exe writes the file name in uppercase even
    /// though DictionaryGUID inside the XML is lowercase). Returns the exact
    /// file name reported by the file system — never a case transformation
    /// this code invents — so that callers can pass a string to
    /// imjpuexc.exe that is guaranteed to match the real file, and therefore
    /// the real registry entry, byte for byte.
    /// </summary>
    public static bool TryResolveInstalledDctxPath(string dictionaryGuid, out string? actualPath, string? dictsDirectory = null)
    {
        actualPath = null;
        var dir = dictsDirectory ?? GetInstalledDictsDirectory();
        if (!Directory.Exists(dir))
        {
            return false;
        }

        foreach (var file in Directory.GetFiles(dir, "*.dctx"))
        {
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            if (string.Equals(nameWithoutExtension, dictionaryGuid, StringComparison.OrdinalIgnoreCase))
            {
                actualPath = file;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolve the actual on-disk .dctr runtime index file(s) IME generates
    /// next to an installed .dctx ("{GUID}_{otherGuid}.dctr"), matching the
    /// GUID prefix case-insensitively for the same reason as
    /// <see cref="TryResolveInstalledDctxPath"/>. The second GUID segment is
    /// not predictable ahead of time, so this enumerates rather than
    /// building a single expected path.
    /// </summary>
    public static IReadOnlyList<string> ResolveInstalledDctrPaths(string dictionaryGuid, string? dictsDirectory = null)
    {
        var dir = dictsDirectory ?? GetInstalledDictsDirectory();
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        var prefix = dictionaryGuid + "_";
        return Directory.GetFiles(dir, "*.dctr")
            .Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public static string GetInstalledDictsDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Microsoft", "IME", "15.0", "IMEJP", "Dicts");
    }
}

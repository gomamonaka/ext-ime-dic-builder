using ExtImeDicBuilder.Core;

namespace ExtImeDicBuilder.Core.Tests;

public class ImePathsTests : IDisposable
{
    private readonly string _tempDictsDir;

    public ImePathsTests()
    {
        _tempDictsDir = Path.Combine(Path.GetTempPath(), "ExtImeDicBuilderTests_Dicts_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDictsDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDictsDir))
        {
            Directory.Delete(_tempDictsDir, recursive: true);
        }
    }

    [Fact]
    public void ImewdbldPathIsUnderSystemNotHardcodedDrive()
    {
        var path = ImePaths.GetImewdbldExePath();
        Assert.EndsWith(Path.Combine("IME", "SHARED", "imewdbld.exe"), path);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), path);
    }

    [Fact]
    public void ImjpuexcPathIsUnderSystemNotHardcodedDrive()
    {
        var path = ImePaths.GetImjpuexcExePath();
        Assert.EndsWith(Path.Combine("IME", "IMEJP", "imjpuexc.exe"), path);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.System), path);
    }

    [Fact]
    public void InstalledDctxPathUsesGuidAsFileNameLowercaseGuess()
    {
        var guid = "{00d41162-3de9-4a60-8903-e98d815c6bdb}";
        var path = ImePaths.GetInstalledDctxPath(guid);
        Assert.EndsWith(Path.Combine("Microsoft", "IME", "15.0", "IMEJP", "Dicts", guid + ".dctx"), path);
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path);
    }

    [Fact]
    public void TryResolveInstalledDctxPathReturnsFalseWhenDirectoryMissing()
    {
        var missingDir = Path.Combine(_tempDictsDir, "does-not-exist");
        var found = ImePaths.TryResolveInstalledDctxPath("{00d41162-3de9-4a60-8903-e98d815c6bdb}", out var path, missingDir);
        Assert.False(found);
        Assert.Null(path);
    }

    [Fact]
    public void TryResolveInstalledDctxPathReturnsFalseWhenNoMatchingFile()
    {
        File.WriteAllBytes(Path.Combine(_tempDictsDir, "{unrelated-guid}.dctx"), new byte[] { 1 });
        var found = ImePaths.TryResolveInstalledDctxPath("{00d41162-3de9-4a60-8903-e98d815c6bdb}", out var path, _tempDictsDir);
        Assert.False(found);
        Assert.Null(path);
    }

    [Fact]
    public void TryResolveInstalledDctxPathMatchesCaseInsensitivelyAndReturnsRealOnDiskName()
    {
        // Real hardware observation: the JSON-stored GUID is lowercase, but
        // imewdbld.exe writes the installed file name in uppercase. The
        // resolver must not invent a casing of its own — it must return
        // whatever the file system actually reports.
        var lowercaseGuid = "{00d41162-3de9-4a60-8903-e98d815c6bdb}";
        var uppercaseFileName = "{00D41162-3DE9-4A60-8903-E98D815C6BDB}.dctx";
        var realPath = Path.Combine(_tempDictsDir, uppercaseFileName);
        File.WriteAllBytes(realPath, new byte[] { 1, 2, 3 });

        var found = ImePaths.TryResolveInstalledDctxPath(lowercaseGuid, out var resolvedPath, _tempDictsDir);

        Assert.True(found);
        Assert.Equal(realPath, resolvedPath);
        Assert.EndsWith(uppercaseFileName, resolvedPath); // exact on-disk casing preserved, not lowercased
    }

    [Fact]
    public void ResolveInstalledDctrPathsFindsFilesByGuidPrefixCaseInsensitively()
    {
        var lowercaseGuid = "{00d41162-3de9-4a60-8903-e98d815c6bdb}";
        var matching = Path.Combine(_tempDictsDir, "{00D41162-3DE9-4A60-8903-E98D815C6BDB}_{ABCDEF00-0000-0000-0000-000000000000}.dctr");
        var nonMatching = Path.Combine(_tempDictsDir, "{unrelated}_{other}.dctr");
        File.WriteAllBytes(matching, new byte[] { 1 });
        File.WriteAllBytes(nonMatching, new byte[] { 1 });

        var results = ImePaths.ResolveInstalledDctrPaths(lowercaseGuid, _tempDictsDir);

        Assert.Single(results);
        Assert.Equal(matching, results[0]);
    }

    [Fact]
    public void ResolveInstalledDctrPathsReturnsEmptyWhenDirectoryMissing()
    {
        var missingDir = Path.Combine(_tempDictsDir, "does-not-exist");
        var results = ImePaths.ResolveInstalledDctrPaths("{00d41162-3de9-4a60-8903-e98d815c6bdb}", missingDir);
        Assert.Empty(results);
    }
}

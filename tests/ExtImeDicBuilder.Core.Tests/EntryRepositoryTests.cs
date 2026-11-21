using ExtImeDicBuilder.Core;

namespace ExtImeDicBuilder.Core.Tests;

public class EntryRepositoryTests : IDisposable
{
    private readonly string _tempFile;

    public EntryRepositoryTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), "ExtImeDicBuilderTests_" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void LoadWithNoFileReturnsFreshStoreWithGuid()
    {
        var repo = new EntryRepository(_tempFile);
        var store = repo.Load();
        Assert.False(File.Exists(_tempFile));
        Assert.Matches(@"^\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\}$", store.DictionaryGuid);
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void SaveThenLoadRoundTripsEntries()
    {
        var repo = new EntryRepository(_tempFile);
        var store = repo.Load();
        store.Entries.Add(new DictionaryEntryRecord
        {
            Id = "abc",
            Reading = "ぎじろく",
            Output = "発言者：\r\n結論：\r\n補足：\r\n対応：",
            CreatedAt = DateTimeOffset.Parse("2026-09-16T11:00:00+09:00"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-16T11:00:00+09:00"),
        });
        repo.Save(store);

        Assert.True(File.Exists(_tempFile));

        var reloaded = new EntryRepository(_tempFile).Load();
        Assert.Equal(store.DictionaryGuid, reloaded.DictionaryGuid);
        Assert.Single(reloaded.Entries);
        Assert.Equal("ぎじろく", reloaded.Entries[0].Reading);
        Assert.Equal("発言者：\r\n結論：\r\n補足：\r\n対応：", reloaded.Entries[0].Output);
    }

    [Fact]
    public void DictionaryVersionDefaultsToOneAndRoundTrips()
    {
        var repo = new EntryRepository(_tempFile);
        var store = repo.Load();
        Assert.Equal(1, store.DictionaryVersion);

        store.DictionaryVersion = 5;
        repo.Save(store);

        var reloaded = repo.Load();
        Assert.Equal(5, reloaded.DictionaryVersion);
    }

    [Fact]
    public void DictionaryGuidStaysStableAcrossSaves()
    {
        var repo = new EntryRepository(_tempFile);
        var store = repo.Load();
        var firstGuid = store.DictionaryGuid;
        repo.Save(store);

        var reloaded = repo.Load();
        Assert.Equal(firstGuid, reloaded.DictionaryGuid);

        reloaded.Entries.Add(new DictionaryEntryRecord { Reading = "a", Output = "b" });
        repo.Save(reloaded);

        var reloadedAgain = repo.Load();
        Assert.Equal(firstGuid, reloadedAgain.DictionaryGuid);
    }

    [Fact]
    public void JsonDoesNotEscapeJapaneseCharacters()
    {
        var repo = new EntryRepository(_tempFile);
        var store = repo.Load();
        store.Entries.Add(new DictionaryEntryRecord { Reading = "ぎじろく", Output = "発言者：" });
        repo.Save(store);

        var json = File.ReadAllText(_tempFile);
        Assert.Contains("ぎじろく", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void SaveIsAtomicAndDoesNotLeaveTempFilesBehind()
    {
        var repo = new EntryRepository(_tempFile);
        var store = repo.Load();
        store.Entries.Add(new DictionaryEntryRecord { Reading = "a", Output = "b" });
        repo.Save(store);
        store.Entries.Add(new DictionaryEntryRecord { Reading = "c", Output = "d" });
        repo.Save(store);

        var dir = Path.GetDirectoryName(_tempFile)!;
        var leftoverTemps = Directory.GetFiles(dir, Path.GetFileName(_tempFile) + ".*.tmp");
        Assert.Empty(leftoverTemps);
    }
}

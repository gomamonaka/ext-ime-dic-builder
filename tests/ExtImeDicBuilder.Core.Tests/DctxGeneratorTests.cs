using System.Xml.Linq;
using ExtImeDicBuilder.Core;

namespace ExtImeDicBuilder.Core.Tests;

public class DctxGeneratorTests
{
    private static readonly XNamespace Ns1 = DctxBuilder.NsUri;

    [Fact]
    public void GenerateUsesStoreDictionaryVersion()
    {
        var store = new EntryStore
        {
            DictionaryGuid = "{11111111-1111-1111-1111-111111111111}",
            DictionaryVersion = 3,
            Entries = { new DictionaryEntryRecord { Reading = "よみ", Output = "出力" } },
        };

        var data = DctxGenerator.Generate(store);

        using var ms = new MemoryStream(data);
        var root = XDocument.Load(ms).Root!;
        var version = root.Element(Ns1 + "DictionaryHeader")!.Element(Ns1 + "DictionaryVersion")!.Value;
        Assert.Equal("3", version);
    }

    [Fact]
    public void GenerateDefaultsToVersionOneForFreshStore()
    {
        var store = new EntryStore
        {
            DictionaryGuid = "{11111111-1111-1111-1111-111111111111}",
            Entries = { new DictionaryEntryRecord { Reading = "よみ", Output = "出力" } },
        };

        var data = DctxGenerator.Generate(store);

        using var ms = new MemoryStream(data);
        var root = XDocument.Load(ms).Root!;
        var version = root.Element(Ns1 + "DictionaryHeader")!.Element(Ns1 + "DictionaryVersion")!.Value;
        Assert.Equal("1", version);
    }
}

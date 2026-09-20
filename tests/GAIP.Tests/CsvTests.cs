using GAIP.Core;
using GAIP.Storage;
using Xunit;
using static GAIP.Tests.TestData;

namespace GAIP.Tests;

public sealed class CsvTests
{
    [Fact]
    public void ValidImportCreatesSitesAndNetworks()
    {
        var text = CsvExchange.VlanHeader + "\nLEVANT;Île du Levant;120;SERVEURS;;10.20.120.0/24;10.20.120.1;Firewall\nCOUDON;Coudon;120;ADMIN;;10.30.120.0/24;;";
        var result = CsvExchange.Import(new(), text, CsvKind.Vlans); Assert.Empty(result.Errors); Assert.Equal(2, result.Data!.Sites.Count);
    }
    [Fact]
    public void InvalidImportDoesNotPartiallyMutateSource()
    {
        var source = Example(); var before = JsonData.Hash(JsonData.Serialize(source));
        var text = CsvExchange.AddressHeader + "\nLEVANT;120;10.20.120.25;valid;\nLEVANT;120;10.20.121.1;outside;\nLEVANT;120;10.20.120.255;broadcast;";
        var result = CsvExchange.Import(source, text, CsvKind.Addresses); Assert.Null(result.Data); Assert.True(result.Errors.Count >= 2);
        Assert.Equal(before, JsonData.Hash(JsonData.Serialize(source)));
    }
    [Fact]
    public void CsvEscapingUtf8AndAlternateSeparatorRoundTrip()
    {
        var db = Example(); Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "étoile", Description = "a, b; \"été\"" });
        var exported = CsvExchange.Export(db, CsvKind.Addresses, ',');
        var target = Example(); var imported = CsvExchange.Import(target, "\uFEFF" + exported, CsvKind.Addresses, ',');
        Assert.Empty(imported.Errors); Assert.Equal("a, b; \"été\"", Subnet(imported.Data!).Addresses[0].Description);
    }
    [Fact]
    public void OverlapAndDuplicateRowsPreventWholeImport()
    {
        var text = CsvExchange.VlanHeader + "\nCOUDON;Coudon;120;ADMIN;;10.20.120.0/24;;\nCOUDON;Coudon;120;ADMIN;;10.30.120.0/24;;";
        var result = CsvExchange.Import(Example(), text, CsvKind.Vlans); Assert.Null(result.Data); Assert.True(result.Errors.Count >= 2);
    }
    [Fact]
    public void ImportCannotRemoveNetworkWithAssignedAddresses()
    {
        var db = Example(); Subnet(db).Addresses.Add(new() { Address = "10.20.120.2", Hostname = "host" });
        var result = CsvExchange.Import(db, CsvExchange.VlanHeader + "\nLEVANT;Levant;120;SERVEURS;;;;", CsvKind.Vlans);
        Assert.Null(result.Data); Assert.NotEmpty(result.Errors); Assert.Single(Subnet(db).Addresses);
    }
    [Fact]
    public void ImportCannotChangeNetworkWithAssignedAddresses()
    {
        var db = Example(); Subnet(db).Addresses.Add(new() { Address = "10.20.120.2", Hostname = "host" });
        var text = CsvExchange.VlanHeader + "\nLEVANT;Île du Levant;120;SERVEURS;;10.20.120.0/23;;";
        var result = CsvExchange.Import(db, text, CsvKind.Vlans);
        Assert.Null(result.Data); Assert.Contains(result.Errors, e => e.Contains("Libérez d'abord toutes les adresses IP."));
        Assert.Equal("10.20.120.0/24", Subnet(db).Cidr);
    }
    [Fact]
    public void ExistingIpUpdatesWithoutDuplicatingAndMissingRowsRemain()
    {
        var db = Example(); Subnet(db).Addresses = [new() { Address = "10.20.120.2", Hostname = "old" }, new() { Address = "10.20.120.3", Hostname = "keep" }];
        var result = CsvExchange.Import(db, CsvExchange.AddressHeader + "\nLEVANT;120;10.20.120.2;NEW;", CsvKind.Addresses);
        Assert.Empty(result.Errors); Assert.Equal(2, Subnet(result.Data!).Addresses.Count); Assert.Equal("NEW", Subnet(result.Data!).Addresses[0].Hostname);
    }
    [Fact]
    public void DemoSamplesAreRichAndImportCleanly()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../samples"));
        var vlanImport = CsvExchange.Import(new(), File.ReadAllText(Path.Combine(root, "vlans.csv")), CsvKind.Vlans);
        Assert.Empty(vlanImport.Errors); Assert.NotNull(vlanImport.Data);

        var addressImport = CsvExchange.Import(vlanImport.Data!, File.ReadAllText(Path.Combine(root, "addresses.csv")), CsvKind.Addresses);
        Assert.Empty(addressImport.Errors); Assert.NotNull(addressImport.Data);

        var multicastImport = CsvExchange.Import(addressImport.Data!, File.ReadAllText(Path.Combine(root, "multicast.csv")), CsvKind.Multicast);
        Assert.Empty(multicastImport.Errors); var db = Assert.IsType<Database>(multicastImport.Data);

        Assert.Equal(6, db.Sites.Count);
        Assert.Equal(new[] { 5, 7, 9, 11, 13, 15 }, db.Sites.OrderBy(s => s.Code).Select(s => s.Vlans.Count).ToArray());
        Assert.Equal(60, db.Sites.Sum(s => s.Vlans.Count));
        Assert.Equal(3470, db.Sites.Sum(s => s.Vlans.Sum(v => v.Subnet?.Addresses.Count ?? 0)));
        Assert.All(db.Sites, site => Assert.InRange(site.Vlans.Count, 5, 15));
        Assert.All(db.Sites.SelectMany(s => s.Vlans), vlan => Assert.InRange(vlan.Subnet!.Addresses.Count, 5, 200));
        Assert.Equal(6, db.Sites.SelectMany(s => s.Vlans).Select(v => Ipv4Network.Parse(v.Subnet!.Cidr).Prefix).Distinct().Count());

        Assert.Equal(20, db.MulticastGroups.Count);
        Assert.Equal(258, db.MulticastGroups.Sum(group => group.Flows.Count));
        Assert.Equal(5, db.MulticastGroups.Min(group => group.Flows.Count));
        Assert.Equal(20, db.MulticastGroups.Max(group => group.Flows.Count));
        Assert.All(db.MulticastGroups.SelectMany(group => group.Flows), flow =>
        {
            var usedSites = db.Sites.Count(site => site.Vlans.Any(vlan => flow.VlanIds.Contains(vlan.Id)));
            Assert.InRange(usedSites, 1, 4);
            Assert.NotEmpty(flow.Sources);
        });
        Assert.Empty(ModelValidator.Validate(db));

        var exported = CsvExchange.Export(db, CsvKind.Multicast);
        var withoutMulticast = JsonData.Clone(db); withoutMulticast.MulticastGroups.Clear();
        var roundTrip = CsvExchange.Import(withoutMulticast, exported, CsvKind.Multicast);
        Assert.Empty(roundTrip.Errors);
        Assert.Equal(258, roundTrip.Data!.MulticastGroups.Sum(group => group.Flows.Count));
    }

    [Fact]
    public void MulticastCsvUsesMultilineCellsAndPreservesEmptyGroups()
    {
        var db = Example();
        Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRC-VIDEO" });
        Subnet(db).Addresses.Add(new() { Address = "10.20.120.26", Hostname = "SRC-AUDIO" });
        var audio = new Vlan { Vid = 130, Name = "AUDIO" };
        db.Sites[0].Vlans.Add(audio);
        db.MulticastGroups.Add(new()
        {
            Address = "239.10.20.15", Name = "PROGRAMME", Description = "Plusieurs natures sur la même adresse",
            Flows =
            [
                new()
                {
                    Port = 5004, Content = "Vidéo", Sources = ["10.20.120.26", "10.20.120.25"],
                    VlanIds = [audio.Id, db.Sites[0].Vlans[0].Id]
                },
                new() { Port = 5006, Content = "Audio" }
            ]
        });
        db.MulticastGroups.Add(new()
        {
            Address = "239.10.20.16", Name = "RESERVE", Description = "Métadonnées sans flux"
        });

        var exported = CsvExchange.Export(db, CsvKind.Multicast);
        Assert.DoesNotContain('|', exported);

        var rows = CsvExchange.Parse(exported, ';');
        Assert.Equal(4, rows.Count);
        var video = rows.Single(row => row[0] == "239.10.20.15" && row[3] == "5004");
        Assert.Equal("10.20.120.25\n10.20.120.26", video[6]);
        Assert.Equal("LEVANT/120 — SERVEURS\nLEVANT/130 — AUDIO", video[7]);
        var empty = rows.Single(row => row[0] == "239.10.20.16");
        Assert.All(empty.Skip(3), Assert.Empty);

        var target = JsonData.Clone(db);
        target.MulticastGroups.Clear();
        var imported = CsvExchange.Import(target, exported, CsvKind.Multicast);
        Assert.Empty(imported.Errors);
        var groups = imported.Data!.MulticastGroups.OrderBy(group => group.Address).ToArray();
        Assert.Equal(2, groups.Length);
        Assert.Equal(new[] { "Vidéo", "Audio" }, groups[0].Flows.OrderBy(flow => flow.Port).Select(flow => flow.Content).ToArray());
        Assert.Empty(groups[1].Flows);
        Assert.Equal("Métadonnées sans flux", groups[1].Description);
    }

    [Fact]
    public void MulticastCsvRejectsConflictingGroupMetadata()
    {
        var text = CsvExchange.MulticastHeader +
            "\n239.10.20.15;VIDEO;Description A;5000;Vidéo;;;" +
            "\n239.10.20.15;AUDIO;Description B;5001;Audio;;;";

        var result = CsvExchange.Import(Example(), text, CsvKind.Multicast);

        Assert.Null(result.Data);
        Assert.Contains(result.Errors, error => error.Contains("Métadonnées incohérentes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("site;other\na;b")][InlineData("\"unterminated")][InlineData("a\"b;c")]
    public void InvalidCsvIsRejected(string text) => Assert.NotEmpty(CsvExchange.Import(Example(), text, CsvKind.Addresses).Errors);
}

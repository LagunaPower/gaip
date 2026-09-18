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
    public void ExistingIpUpdatesWithoutDuplicatingAndMissingRowsRemain()
    {
        var db = Example(); Subnet(db).Addresses = [new() { Address = "10.20.120.2", Hostname = "old" }, new() { Address = "10.20.120.3", Hostname = "keep" }];
        var result = CsvExchange.Import(db, CsvExchange.AddressHeader + "\nLEVANT;120;10.20.120.2;NEW;", CsvKind.Addresses);
        Assert.Empty(result.Errors); Assert.Equal(2, Subnet(result.Data!).Addresses.Count); Assert.Equal("NEW", Subnet(result.Data!).Addresses[0].Hostname);
    }
    [Theory]
    [InlineData("site;other\na;b")][InlineData("\"unterminated")][InlineData("a\"b;c")]
    public void InvalidCsvIsRejected(string text) => Assert.NotEmpty(CsvExchange.Import(Example(), text, CsvKind.Addresses).Errors);
}

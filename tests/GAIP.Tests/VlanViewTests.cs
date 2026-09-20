using System.Collections;
using System.Text.Json;
using GAIP.Core;
using GAIP.Storage;
using Xunit;

namespace GAIP.Tests;

public sealed class VlanViewTests
{
    [Fact]
    public void Slash22CalculatesMaskAndEnumeratesAll1022UsableAddresses()
    {
        var n = Ipv4Network.Parse("10.20.122.10/22");
        Assert.Equal("10.20.120.0", Ipv4Network.Format(n.Network));
        Assert.Equal("255.255.252.0", n.DottedMask);
        Assert.Equal("10.20.120.1", Ipv4Network.Format(n.First));
        Assert.Equal("10.20.123.254", Ipv4Network.Format(n.Last));
        Assert.Equal("10.20.123.255", Ipv4Network.Format(n.Broadcast));
        Assert.Equal(1022UL, n.UsableCount);
        var rows = new AddressRows(new() { Cidr = n.ToString() }, true);
        Assert.Equal(1022, rows.Count);
        var values = rows.Select(r => Ipv4Network.ParseAddress(r.Address)).ToArray();
        Assert.Equal(Enumerable.Range(1, 1022).Select(offset => n.Network + (uint)offset), values);
        Assert.Contains("10.20.120.255", rows.Select(r => r.Address));
        Assert.Contains("10.20.121.0", rows.Select(r => r.Address));
        Assert.DoesNotContain(n.Network, values); Assert.DoesNotContain(n.Broadcast, values);
        Assert.DoesNotContain("mask", JsonSerializer.Serialize(TestData.Example(n.ToString()), JsonData.Options), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsedByDefaultIncludesGatewayAndAddressesAcrossWholeRange()
    {
        var subnet = TestData.Subnet(TestData.Example("10.20.120.0/22"));
        subnet.Gateway = new() { Address = "10.20.120.1", Comment = "Firewall" };
        subnet.Addresses = [new() { Address = "10.20.123.254", Hostname = "LAST" }, new() { Address = "10.20.122.10", Description = "Réservée" }];
        var used = new AddressRows(subnet);
        Assert.Equal(new[] { "10.20.120.1", "10.20.122.10", "10.20.123.254" }, used.Select(r => r.Address));
        Assert.All(used, r => Assert.True(r.IsUsed)); Assert.True(used[0].IsGateway);
        var all = new AddressRows(subnet, true);
        Assert.Equal(1022, all.Count); Assert.Equal(3, all.Count(r => r.IsUsed));
        Assert.Equal(1019UL, Queries.FreeCount(subnet)); Assert.Equal("10.20.120.2", Queries.NextFree(subnet));
        Assert.Equal(1021, ((IList)all).IndexOf(all[1021]));
    }

    [Theory]
    [InlineData("10.20.120.0")][InlineData("10.20.123.255")]
    public void NetworkAndBroadcastNeverAppearAsFreeEvenThroughSearch(string ip)
    { Assert.Empty(new AddressRows(new() { Cidr = "10.20.120.0/22" }, true, ip)); }

    [Fact]
    public void PartialSearchIncludesFreeAddressesBeyondFirstSlash24()
    {
        var subnet = new Subnet { Cidr = "10.20.120.0/22" };
        Assert.Empty(new AddressRows(subnet)); Assert.Empty(new AddressRows(subnet, false, "10.20.123."));
        var rows = new AddressRows(subnet, true, "10.20.123.");
        Assert.Equal(255, rows.Count); Assert.Equal("10.20.123.0", rows[0].Address); Assert.Equal("10.20.123.254", rows[^1].Address);
    }

    [Fact]
    public void LargeRangeUsesIndexingWithoutCreatingEveryRow()
    {
        var rows = new AddressRows(new() { Cidr = "10.0.0.0/12" }, true);
        Assert.Equal(1_048_574, rows.Count); Assert.Equal("10.15.255.254", rows[^1].Address);
        Assert.Throws<InvalidOperationException>(() => new AddressRows(new() { Cidr = "0.0.0.0/0" }, true));
        Assert.Single(new AddressRows(new() { Cidr = "0.0.0.0/0" }, true, "255.255.255.254"));
    }

    [Theory]
    [InlineData("10.0.0.0/31")][InlineData("10.0.0.1/32")]
    public void TinyNetworksHaveNoVisibleAvailableAddresses(string cidr) => Assert.Empty(new AddressRows(new() { Cidr = cidr }, true));

    [Fact]
    public void ContextualCsvUsesStableIdAndNeverLeaksOtherSitesOrVlans()
    {
        var db = TestData.Example("10.20.120.0/22"); var subnet = TestData.Subnet(db);
        subnet.Gateway = new() { Address = "10.20.120.1" };
        subnet.Addresses.Add(new() { Address = "10.20.123.254", Hostname = "LOCAL-HOST" });
        var other = TestData.Example("10.30.120.0/24").Sites[0]; other.Code = "OTHER"; other.Name = "OTHER";
        other.Vlans[0].Subnet!.Addresses.Add(new() { Address = "10.30.120.25", Hostname = "OTHER-HOST" }); db.Sites.Add(other);
        var id = db.Sites[0].Vlans[0].Id;
        var vlans = CsvExchange.Export(db, CsvKind.Vlans, ';', id);
        var addresses = CsvExchange.Export(db, CsvKind.Addresses, ';', id);
        Assert.Equal(2, CsvExchange.Parse(vlans, ';').Count); Assert.Equal(2, CsvExchange.Parse(addresses, ';').Count);
        Assert.Contains("10.20.120.1", vlans); Assert.Contains("10.20.123.254", addresses);
        Assert.DoesNotContain("OTHER", vlans); Assert.DoesNotContain("OTHER", addresses);
        Assert.Equal(3, CsvExchange.Parse(CsvExchange.Export(db, CsvKind.Vlans), ';').Count);
    }

    [Fact]
    public void ContextualHistoryMatchesChangesAndRedactsUnrelatedDetails()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository();
        var db = TestData.Example(); var other = TestData.Example("10.30.120.0/24").Sites[0]; other.Code = "OTHER"; db.Sites.Add(other);
        var id = db.Sites[0].Vlans[0].Id;
        var snapshot = repo.Initialize(db);
        var next = JsonData.Clone(snapshot.Data); next.Sites[1].Vlans[0].Name = "OTHER-SECRET";
        snapshot = repo.Commit(next, snapshot.Hash, null, "Unrelated", "VLAN", "120").Snapshot;
        next = JsonData.Clone(snapshot.Data); TestData.Subnet(next).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRV" });
        snapshot = repo.Commit(next, snapshot.Hash, null, "Attribution", "IP", "10.20.120.25").Snapshot;
        next = JsonData.Clone(snapshot.Data); TestData.Subnet(next).Addresses.Clear();
        snapshot = repo.Commit(next, snapshot.Hash, null, "Libération", "IP", "10.20.120.25").Snapshot;
        next = JsonData.Clone(snapshot.Data); next.Sites[0].Vlans[0].Vid = 121;
        repo.Commit(next, snapshot.Hash, null, "Renumber", "VLAN", "121");
        var scoped = repo.History(id);
        Assert.Equal(3, scoped.Count); Assert.Equal(4, repo.History().Count);
        Assert.DoesNotContain(scoped, line => line.Contains("OTHER-SECRET") || line.Contains("Unrelated"));
        var release = JsonSerializer.Deserialize<AuditEntry>(scoped[1], JsonData.Options)!;
        Assert.Equal("Libération", release.Action);
        Assert.Contains(release.Changes, change => change.ObjectType == "IP" && change.Target == "10.20.120.25" &&
            change.Field == "exists" && change.OldValue == "true" && change.NewValue == "false");
        Assert.DoesNotContain(release.Changes, change => change.Target.Contains("OTHER", StringComparison.Ordinal));
    }

    [Fact]
    public void ContextualHistoryFiltersBeforeApplyingDisplayLimit()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var before = TestData.Example();
        var after = JsonData.Clone(before); after.Sites[0].Vlans[0].Name = "Changed";
        var relevant = new AuditEntry(DateTimeOffset.UtcNow, "test", "pc", 1, "Change", "VLAN", "120", AuditDiff.Create(before, after));
        var unrelated = new AuditEntry(DateTimeOffset.UtcNow, "test", "pc", 2, "Other", "Site", "none", []);
        var options = new JsonSerializerOptions(JsonData.Options) { WriteIndented = false };
        File.WriteAllLines(repo.HistoryPath, new[] { JsonSerializer.Serialize(relevant, options) }.Concat(Enumerable.Repeat(JsonSerializer.Serialize(unrelated, options), 1001)));
        Assert.Equal(1000, repo.History().Count);
        Assert.Single(repo.History(before.Sites[0].Vlans[0].Id));
    }
}

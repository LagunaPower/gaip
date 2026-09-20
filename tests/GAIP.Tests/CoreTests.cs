using GAIP.Core;
using GAIP.Storage;
using Xunit;
using static GAIP.Tests.TestData;

namespace GAIP.Tests;

public sealed class CoreTests
{
    [Theory]
    [InlineData("10.20.120.25/24", "10.20.120.0", "10.20.120.1", "10.20.120.254", "10.20.120.255", 254UL)]
    [InlineData("192.168.1.4/30", "192.168.1.4", "192.168.1.5", "192.168.1.6", "192.168.1.7", 2UL)]
    [InlineData("10.10.3.20/23", "10.10.2.0", "10.10.2.1", "10.10.3.254", "10.10.3.255", 510UL)]
    [InlineData("255.255.255.252/30", "255.255.255.252", "255.255.255.253", "255.255.255.254", "255.255.255.255", 2UL)]
    [InlineData("0.0.0.0/0", "0.0.0.0", "0.0.0.1", "255.255.255.254", "255.255.255.255", 4294967294UL)]
    public void NetworkCalculations(string cidr, string network, string first, string last, string broadcast, ulong count)
    {
        var n = Ipv4Network.Parse(cidr);
        Assert.Equal(network, Ipv4Network.Format(n.Network)); Assert.Equal(first, Ipv4Network.Format(n.First));
        Assert.Equal(last, Ipv4Network.Format(n.Last)); Assert.Equal(broadcast, Ipv4Network.Format(n.Broadcast)); Assert.Equal(count, n.UsableCount);
    }
    [Theory]
    [InlineData("10.0.0.0/31")]
    [InlineData("10.0.0.1/32")]
    public void TinyNetworksHaveNoAssignableAddress(string cidr)
    { var n = Ipv4Network.Parse(cidr); Assert.Equal(0UL, n.UsableCount); Assert.False(n.IsUsable(n.Network)); Assert.Null(Queries.NextFree(new() { Cidr = cidr })); }
    [Theory]
    [InlineData("::1")][InlineData("127.1")][InlineData("010.1.1.1")][InlineData("1.2.3.256")][InlineData("1.2.3.-1")][InlineData(" 1.2.3.4")]
    public void StrictIpv4(string address) => Assert.Throws<FormatException>(() => Ipv4Network.ParseAddress(address));
    [Theory]
    [InlineData("10.0.0.0/33")][InlineData("10.0.0.0/-1")][InlineData("10.0.0.0")][InlineData("::/64")]
    public void InvalidCidr(string cidr) => Assert.Throws<FormatException>(() => Ipv4Network.Parse(cidr));
    [Fact]
    public void OverlapDetectsBothContainmentDirectionsAndNotAdjacentNetworks()
    {
        var a = Ipv4Network.Parse("10.0.0.0/16"); var b = Ipv4Network.Parse("10.0.3.0/24");
        Assert.True(a.Overlaps(b)); Assert.True(b.Overlaps(a)); Assert.False(a.Overlaps(Ipv4Network.Parse("10.1.0.0/16")));
    }
    [Theory]
    [InlineData("10.20.120.0")][InlineData("10.20.120.255")][InlineData("10.20.121.1")]
    public void InvalidGatewayAndAddresses(string address)
    {
        var db = Example(); Subnet(db).Gateway = new() { Address = address };
        Assert.NotEmpty(ModelValidator.Validate(db));
        Subnet(db).Gateway = null; Subnet(db).Addresses.Add(new() { Address = address, Hostname = "host" });
        Assert.NotEmpty(ModelValidator.Validate(db));
    }
    [Fact]
    public void GatewayCountsUsedAndCannotBeAssigned()
    {
        var db = Example(); var subnet = Subnet(db); subnet.Gateway = new() { Address = "10.20.120.1", Comment = "Firewall" };
        Assert.Empty(ModelValidator.Validate(db)); Assert.Equal(1, Queries.UsedCount(subnet)); Assert.Equal(253UL, Queries.FreeCount(subnet));
        Assert.Equal("10.20.120.2", Queries.NextFree(subnet)); Assert.True(new AddressRows(subnet)[0].IsGateway);
        subnet.Addresses.Add(new() { Address = "10.20.120.1", Description = "collision" }); Assert.NotEmpty(ModelValidator.Validate(db));
    }
    [Fact]
    public void GlobalUniquenessAndOverlapsAcrossSites()
    {
        var db = Example(); var other = Example("10.20.0.0/16").Sites[0]; other.Code = "COUDON"; db.Sites.Add(other);
        Assert.Contains(ModelValidator.Validate(db), e => e.Contains("chevauche"));
        Subnet(db).Addresses.Add(new() { Address = "10.20.120.10", Hostname = "host" });
        other.Vlans[0].Subnet!.Addresses.Add(new() { Address = "10.20.120.10", Hostname = "host" });
        Assert.Contains(ModelValidator.Validate(db), e => e.Contains("déjà utilisée"));
    }
    [Fact]
    public void SameVidAcrossSitesIsIndependentButDuplicateWithinSiteFails()
    {
        var db = Example(); var other = Example("10.30.120.0/24").Sites[0]; other.Code = "COUDON"; db.Sites.Add(other);
        Assert.Empty(ModelValidator.Validate(db)); other.Vlans[0].Name = "ADMIN"; Assert.Equal("SERVEURS", db.Sites[0].Vlans[0].Name);
        db.Sites[0].Vlans.Add(new() { Vid = 120, Name = "Duplicate" }); Assert.NotEmpty(ModelValidator.Validate(db));
    }
    [Theory]
    [InlineData(0)][InlineData(4095)]
    public void InvalidVid(int vid) { var db = Example(); db.Sites[0].Vlans[0].Vid = vid; Assert.NotEmpty(ModelValidator.Validate(db)); }
    [Fact]
    public void VlanWithoutNetworkAndReservationAreValid()
    {
        var db = Example(); db.Sites[0].Vlans.Add(new() { Vid = 150, Name = "TRANSIT" });
        Subnet(db).Addresses.Add(new() { Address = "10.20.120.50", Description = "Réservée" }); Assert.Empty(ModelValidator.Validate(db));
        Subnet(db).Addresses[0].Description = ""; Assert.NotEmpty(ModelValidator.Validate(db));
    }
    [Fact]
    public void HostnamesPreserveCaseAndMayRepeat()
    {
        var db = Example(); Subnet(db).Addresses = [new() { Address = "10.20.120.2", Hostname = "Srv-APP" }, new() { Address = "10.20.120.3", Hostname = "Srv-APP" }];
        Assert.Empty(ModelValidator.Validate(db)); Assert.Equal("Srv-APP", Subnet(JsonData.Clone(db)).Addresses[0].Hostname);
    }
    [Fact]
    public void NoCascadeGatewayDoesNotBlockVlanDeletionAndCidrShrinkReportsInvalidAddresses()
    {
        var db = Example(); var site = db.Sites[0];
        Assert.Throws<ValidationException>(() => ModelValidator.DeleteSite(db, site.Id));
        Subnet(db).Addresses.Add(new() { Address = "10.20.120.200", Hostname = "host" });
        Assert.Throws<ValidationException>(() => ModelValidator.DeleteVlan(site, site.Vlans[0].Id));
        Subnet(db).Cidr = "10.20.120.0/25"; Assert.Contains(ModelValidator.Validate(db), e => e.Contains("10.20.120.200"));

        var gatewayOnly = Example(); var gatewaySite = gatewayOnly.Sites[0];
        Subnet(gatewayOnly).Gateway = new() { Address = "10.20.120.1", Comment = "Firewall" };
        ModelValidator.DeleteVlan(gatewaySite, gatewaySite.Vlans[0].Id);
        Assert.Empty(gatewaySite.Vlans);
    }
    [Fact]
    public void AssignedAddressesFreezeCidrButGatewayAloneDoesNot()
    {
        var before = Example();
        Subnet(before).Addresses.Add(new() { Address = "10.20.120.2", Hostname = "host" });
        var changed = JsonData.Clone(before);
        Subnet(changed).Cidr = "10.20.120.0/23";
        Assert.Empty(ModelValidator.Validate(changed));
        Assert.Contains(ModelValidator.ValidateTransition(before, changed), e => e.Contains("Libérez d'abord toutes les adresses IP."));

        var gatewayOnly = Example();
        Subnet(gatewayOnly).Gateway = new() { Address = "10.20.120.1", Comment = "Firewall" };
        var moved = JsonData.Clone(gatewayOnly);
        Subnet(moved).Cidr = "10.20.121.0/24";
        Subnet(moved).Gateway!.Address = "10.20.121.1";
        Assert.Empty(ModelValidator.Validate(moved));
        Assert.Empty(ModelValidator.ValidateTransition(gatewayOnly, moved));
    }
    [Fact]
    public void DescriptionsAreSingleLineAndBounded()
    {
        var db = Example(); db.Sites[0].Description = "one\ntwo"; Assert.NotEmpty(ModelValidator.Validate(db));
        db.Sites[0].Description = new string('a', 501); Assert.NotEmpty(ModelValidator.Validate(db));
    }
    [Fact]
    public void NextFreeFindsHolesAndFullNetwork()
    {
        var db = Example("10.20.120.0/30"); var subnet = Subnet(db); subnet.Gateway = new() { Address = "10.20.120.1" };
        Assert.Equal("10.20.120.2", Queries.NextFree(subnet)); subnet.Addresses.Add(new() { Address = "10.20.120.2", Hostname = "host" });
        Assert.Null(Queries.NextFree(subnet)); Assert.Equal(0UL, Queries.FreeCount(subnet));
    }
    [Fact]
    public void HugeNetworkCountsAndNumericalSorting()
    {
        var subnet = new Subnet { Cidr = "0.0.0.0/0", Addresses = [new() { Address = "10.0.0.100", Hostname = "a" }, new() { Address = "10.0.0.2", Hostname = "b" }] };
        Assert.Equal(4294967292UL, Queries.FreeCount(subnet));
        Assert.Equal("10.0.0.2", new AddressRows(subnet)[0].Address);
        Assert.Equal("255.255.255.254", new AddressRows(subnet, true, "255.255.255.254")[0].Address);
        Assert.Empty(new AddressRows(subnet, false, "10.0.0.5"));
        Assert.Single(new AddressRows(subnet, true, "10.0.0.5"));
    }
    [Theory]
    [InlineData("LEVANT")][InlineData("Île")][InlineData("120")][InlineData("SERVEURS")][InlineData("10.20.120.0/24")][InlineData("10.20.120.25")][InlineData("SRV-app")][InlineData("application")]
    public void GlobalSearchCoversAllFields(string query)
    {
        var db = Example(); Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRV-app", Description = "application" });
        Assert.NotEmpty(Queries.Search(db, query));
    }
    [Fact]
    public void MulticastGroupsValidateAddressPortsAndReferences()
    {
        var db = Example();
        Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRC-VIDEO" });
        var vlanId = db.Sites[0].Vlans[0].Id;
        db.MulticastGroups.Add(new()
        {
            Address = "239.10.20.15",
            Name = "VIDEO",
            Flows = [new() { Port = 5004, Content = "Vidéo principale", Sources = ["10.20.120.25"], VlanIds = [vlanId] }]
        });
        Assert.Empty(ModelValidator.Validate(db));

        var bad = JsonData.Clone(db);
        bad.MulticastGroups[0].Address = "10.1.1.1";
        Assert.Contains(ModelValidator.Validate(bad), e => e.Contains("224.0.0.0/4"));

        bad = JsonData.Clone(db);
        bad.MulticastGroups[0].Flows.Add(new() { Port = 5004, Content = "Doublon" });
        Assert.Contains(ModelValidator.Validate(bad), e => e.Contains("port déjà utilisé"));

        bad = JsonData.Clone(db);
        bad.MulticastGroups[0].Flows[0].Sources = ["10.20.120.26"];
        Assert.Contains(ModelValidator.Validate(bad), e => e.Contains("absente des IP attribuées"));

        bad = JsonData.Clone(db);
        bad.MulticastGroups[0].Flows[0].VlanIds = [Guid.NewGuid()];
        Assert.Contains(ModelValidator.Validate(bad), e => e.Contains("VLAN référencé introuvable"));
    }

    [Fact]
    public void MulticastGroupDeletionRequiresNoFlow()
    {
        var db = Example();
        db.MulticastGroups.Add(new() { Address = "239.1.1.1", Name = "TEST", Flows = [new() { Port = 5000, Content = "Test" }] });
        Assert.Throws<ValidationException>(() => ModelValidator.DeleteMulticastGroup(db, "239.1.1.1"));
        ModelValidator.DeleteMulticastFlow(db, "239.1.1.1", 5000);
        ModelValidator.DeleteMulticastGroup(db, "239.1.1.1");
        Assert.Empty(db.MulticastGroups);
    }

}

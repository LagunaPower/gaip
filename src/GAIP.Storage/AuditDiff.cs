using System.Globalization;
using GAIP.Core;

namespace GAIP.Storage;

public sealed record AuditChange(Guid? SiteId, Guid? VlanId, string ObjectType, string Target,
    string Field, string? OldValue, string? NewValue);

public static class AuditDiff
{
    public static List<AuditChange> Create(Database before, Database after)
    {
        var changes = new List<AuditChange>();
        var oldSites = before.Sites.ToDictionary(s => s.Id);
        var newSites = after.Sites.ToDictionary(s => s.Id);
        foreach (var id in oldSites.Keys.Union(newSites.Keys).OrderBy(x => x))
        {
            oldSites.TryGetValue(id, out var oldSite);
            newSites.TryGetValue(id, out var newSite);
            DiffSite(changes, oldSite, newSite);
        }
        return changes;
    }

    private static void DiffSite(List<AuditChange> changes, Site? before, Site? after)
    {
        var site = after ?? before ?? throw new InvalidOperationException();
        var siteId = site.Id;
        var target = after?.Code ?? before!.Code;
        if (before is null || after is null)
            Add(changes, siteId, null, "Site", target, "exists", Bool(before is not null), Bool(after is not null));
        Add(changes, siteId, null, "Site", target, "code", before?.Code, after?.Code);
        Add(changes, siteId, null, "Site", target, "name", before?.Name, after?.Name);
        Add(changes, siteId, null, "Site", target, "description", before?.Description, after?.Description);
        Add(changes, siteId, null, "Site", target, "displayOrder", Number(before?.DisplayOrder), Number(after?.DisplayOrder));

        var oldVlans = (before?.Vlans ?? []).ToDictionary(v => v.Id);
        var newVlans = (after?.Vlans ?? []).ToDictionary(v => v.Id);
        foreach (var id in oldVlans.Keys.Union(newVlans.Keys).OrderBy(x => x))
        {
            oldVlans.TryGetValue(id, out var oldVlan);
            newVlans.TryGetValue(id, out var newVlan);
            DiffVlan(changes, siteId, before?.Code ?? after!.Code, after?.Code ?? before!.Code, oldVlan, newVlan);
        }
    }

    private static void DiffVlan(List<AuditChange> changes, Guid siteId, string oldSiteCode, string newSiteCode, Vlan? before, Vlan? after)
    {
        var vlan = after ?? before ?? throw new InvalidOperationException();
        var vlanId = vlan.Id;
        var oldTarget = before is null ? null : $"{oldSiteCode} / VLAN {before.Vid}";
        var newTarget = after is null ? null : $"{newSiteCode} / VLAN {after.Vid}";
        var target = newTarget ?? oldTarget!;

        if (before is null || after is null)
            Add(changes, siteId, vlanId, "VLAN", target, "exists", Bool(before is not null), Bool(after is not null));
        Add(changes, siteId, vlanId, "VLAN", target, "vid", Number(before?.Vid), Number(after?.Vid));
        Add(changes, siteId, vlanId, "VLAN", target, "name", before?.Name, after?.Name);
        Add(changes, siteId, vlanId, "VLAN", target, "description", before?.Description, after?.Description);

        var oldSubnet = before?.Subnet;
        var newSubnet = after?.Subnet;
        Add(changes, siteId, vlanId, "Sous-réseau", target, "cidr", oldSubnet?.Cidr, newSubnet?.Cidr);

        var oldGateway = oldSubnet?.Gateway;
        var newGateway = newSubnet?.Gateway;
        if (oldGateway is null || newGateway is null)
        {
            if (oldGateway is not null || newGateway is not null)
                Add(changes, siteId, vlanId, "Passerelle", target, "exists", Bool(oldGateway is not null), Bool(newGateway is not null));
        }
        Add(changes, siteId, vlanId, "Passerelle", target, "address", oldGateway?.Address, newGateway?.Address);
        Add(changes, siteId, vlanId, "Passerelle", target, "comment", oldGateway?.Comment, newGateway?.Comment);

        var oldAddresses = (oldSubnet?.Addresses ?? []).ToDictionary(a => a.Address, StringComparer.Ordinal);
        var newAddresses = (newSubnet?.Addresses ?? []).ToDictionary(a => a.Address, StringComparer.Ordinal);
        foreach (var address in oldAddresses.Keys.Union(newAddresses.Keys, StringComparer.Ordinal)
                     .OrderBy(Ipv4Network.ParseAddress))
        {
            oldAddresses.TryGetValue(address, out var oldAddress);
            newAddresses.TryGetValue(address, out var newAddress);
            if (oldAddress is null || newAddress is null)
                Add(changes, siteId, vlanId, "IP", address, "exists", Bool(oldAddress is not null), Bool(newAddress is not null));
            Add(changes, siteId, vlanId, "IP", address, "hostname", oldAddress?.Hostname, newAddress?.Hostname);
            Add(changes, siteId, vlanId, "IP", address, "description", oldAddress?.Description, newAddress?.Description);
        }
    }

    private static void Add(List<AuditChange> changes, Guid? siteId, Guid? vlanId, string objectType, string target,
        string field, string? oldValue, string? newValue)
    {
        if (oldValue == newValue) return;
        changes.Add(new(siteId, vlanId, objectType, target, field, oldValue, newValue));
    }

    private static string Bool(bool value) => value ? "true" : "false";
    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}

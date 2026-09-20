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
        DiffMulticast(changes, before, after);
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

    private static void DiffMulticast(List<AuditChange> changes, Database before, Database after)
    {
        var oldVlanReferences = VlanReferences(before);
        var newVlanReferences = VlanReferences(after);
        var oldGroups = before.MulticastGroups.ToDictionary(g => g.Address, StringComparer.Ordinal);
        var newGroups = after.MulticastGroups.ToDictionary(g => g.Address, StringComparer.Ordinal);
        foreach (var address in oldGroups.Keys.Union(newGroups.Keys, StringComparer.Ordinal).OrderBy(Ipv4Network.ParseAddress))
        {
            oldGroups.TryGetValue(address, out var oldGroup);
            newGroups.TryGetValue(address, out var newGroup);
            if (oldGroup is null || newGroup is null)
                Add(changes, null, null, "Multicast", address, "exists", Bool(oldGroup is not null), Bool(newGroup is not null));
            Add(changes, null, null, "Multicast", address, "name", oldGroup?.Name, newGroup?.Name);
            Add(changes, null, null, "Multicast", address, "description", oldGroup?.Description, newGroup?.Description);

            var oldFlows = (oldGroup?.Flows ?? []).ToDictionary(f => f.Port);
            var newFlows = (newGroup?.Flows ?? []).ToDictionary(f => f.Port);
            foreach (var port in oldFlows.Keys.Union(newFlows.Keys).OrderBy(p => p))
            {
                oldFlows.TryGetValue(port, out var oldFlow);
                newFlows.TryGetValue(port, out var newFlow);
                var target = $"{address}:{port}";
                if (oldFlow is null || newFlow is null)
                    Add(changes, null, null, "Flux multicast", target, "exists", Bool(oldFlow is not null), Bool(newFlow is not null));
                Add(changes, null, null, "Flux multicast", target, "content", oldFlow?.Content, newFlow?.Content);
                Add(changes, null, null, "Flux multicast", target, "description", oldFlow?.Description, newFlow?.Description);
                Add(changes, null, null, "Flux multicast", target, "sources", Sources(oldFlow), Sources(newFlow));

                var oldVlanIds = (oldFlow?.VlanIds ?? []).ToHashSet();
                var newVlanIds = (newFlow?.VlanIds ?? []).ToHashSet();
                foreach (var vlanId in oldVlanIds.Union(newVlanIds).OrderBy(id => id))
                {
                    var wasReferenced = oldVlanIds.Contains(vlanId);
                    var isReferenced = newVlanIds.Contains(vlanId);
                    if (wasReferenced == isReferenced) continue;

                    var reference = isReferenced ? newVlanReferences[vlanId] : oldVlanReferences[vlanId];
                    Add(changes, reference.SiteId, vlanId, "Flux multicast", target, "vlanReference",
                        wasReferenced ? oldVlanReferences[vlanId].Label : null,
                        isReferenced ? newVlanReferences[vlanId].Label : null);
                }
            }
        }
    }

    private static string? Sources(MulticastFlow? flow) => flow is null ? null :
        string.Join(", ", flow.Sources.OrderBy(Ipv4Network.ParseAddress));

    private static Dictionary<Guid, (Guid SiteId, string Label)> VlanReferences(Database db) => db.Sites
        .SelectMany(site => site.Vlans.Select(vlan => (VlanId: vlan.Id, SiteId: site.Id, Label: $"{site.Code}/VLAN {vlan.Vid}")))
        .ToDictionary(item => item.VlanId, item => (item.SiteId, item.Label));

    private static void Add(List<AuditChange> changes, Guid? siteId, Guid? vlanId, string objectType, string target,
        string field, string? oldValue, string? newValue)
    {
        if (oldValue == newValue) return;
        changes.Add(new(siteId, vlanId, objectType, target, field, oldValue, newValue));
    }

    private static string Bool(bool value) => value ? "true" : "false";
    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}

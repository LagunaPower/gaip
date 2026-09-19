namespace GAIP.Core;

public sealed record AddressRow(string Address, string Hostname, string Description, bool IsUsed, bool IsGateway);
public sealed record SearchResult(Guid SiteId, Guid? VlanId, string? Address, string Label);

public static class Queries
{
    public static int UsedCount(Subnet subnet) => subnet.Addresses.Count + (subnet.Gateway is null ? 0 : 1);
    public static ulong FreeCount(Subnet subnet) => Ipv4Network.Parse(subnet.Cidr).UsableCount - (ulong)UsedCount(subnet);
    public static string? NextFree(Subnet subnet)
    {
        var network = Ipv4Network.Parse(subnet.Cidr);
        if (network.UsableCount == 0) return null;
        ulong candidate = network.First;
        foreach (var ip in Used(subnet).OrderBy(x => x))
        {
            if (ip < candidate) continue;
            if (ip > candidate) break;
            candidate++;
        }
        return candidate <= network.Last ? Ipv4Network.Format((uint)candidate) : null;
    }
    private static HashSet<uint> Used(Subnet subnet)
    {
        var used = subnet.Addresses.Select(a => Ipv4Network.ParseAddress(a.Address)).ToHashSet();
        if (subnet.Gateway is { } gw) used.Add(Ipv4Network.ParseAddress(gw.Address));
        return used;
    }

    public static IEnumerable<SearchResult> Search(Database db, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) yield break;
        bool Match(params string[] values) => values.Any(v => v.Contains(query, StringComparison.OrdinalIgnoreCase));
        foreach (var site in db.Sites.OrderBy(s => s.Code))
        {
            if (Match(site.Code, site.Name, site.Description)) yield return new(site.Id, null, null, $"{site.Code} · {site.Name}");
            foreach (var vlan in site.Vlans.OrderBy(v => v.Vid))
            {
                var label = $"{site.Code} / {vlan.Vid} {vlan.Name}";
                if (Match(vlan.Vid.ToString(), vlan.Name, vlan.Description, vlan.Subnet?.Cidr ?? ""))
                    yield return new(site.Id, vlan.Id, null, $"{label} · {vlan.Subnet?.Cidr ?? "sans sous-réseau"}");
                if (vlan.Subnet is not { } subnet) continue;
                if (subnet.Gateway is { } gw && Match(gw.Address, gw.Comment, "PASSERELLE"))
                    yield return new(site.Id, vlan.Id, gw.Address, $"{label} · {gw.Address} PASSERELLE · {gw.Comment}");
                foreach (var ip in subnet.Addresses.OrderBy(a => Ipv4Network.ParseAddress(a.Address)))
                    if (Match(ip.Address, ip.Hostname, ip.Description)) yield return new(site.Id, vlan.Id, ip.Address, $"{label} · {ip.Address} · {ip.Hostname} · {ip.Description}");
            }
        }
    }
}

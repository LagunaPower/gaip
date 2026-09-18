using System.Text.Json;
using GAIP.Core;

namespace GAIP.Storage;

public static class VlanHistory
{
    /// <summary>Works with existing full-database audit snapshots, including multi-site imports.</summary>
    public static AuditEntry? Project(AuditEntry entry, Guid vlanId)
    {
        var before = Find(entry.OldValue, vlanId);
        var after = Find(entry.NewValue, vlanId);
        if (before.Vlan is null && after.Vlan is null) return null;
        if (JsonSerializer.Serialize(before.Vlan, JsonData.Options) == JsonSerializer.Serialize(after.Vlan, JsonData.Options)) return null;
        var label = after.Vlan is not null ? $"{after.Code} / VLAN {after.Vlan.Vid}" : $"{before.Code} / VLAN {before.Vlan!.Vid}";
        // Never expose unrelated sites/VLANs inside the contextual event details.
        return entry with { Target = label, OldValue = before.Vlan, NewValue = after.Vlan };
    }

    private static (string? Code, Vlan? Vlan) Find(object? snapshot, Guid vlanId)
    {
        Database? db = snapshot as Database;
        if (snapshot is JsonElement { ValueKind: JsonValueKind.Object } json && json.TryGetProperty("sites", out _))
            db = json.Deserialize<Database>(JsonData.Options);
        if (db?.Sites is null) return (null, null);
        foreach (var site in db.Sites)
            if (site.Vlans.FirstOrDefault(v => v.Id == vlanId) is { } vlan) return (site.Code, vlan);
        return (null, null);
    }
}

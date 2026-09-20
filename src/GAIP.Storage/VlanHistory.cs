namespace GAIP.Storage;

public static class VlanHistory
{
    public static AuditEntry? Project(AuditEntry entry, Guid vlanId)
    {
        var changes = entry.Changes.Where(change => change.VlanId == vlanId).ToList();
        return changes.Count == 0 ? null : entry with { Changes = changes };
    }
}

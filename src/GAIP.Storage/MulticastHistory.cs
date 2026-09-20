namespace GAIP.Storage;

public static class MulticastHistory
{
    public static AuditEntry? Project(AuditEntry entry, string multicastAddress)
    {
        var prefix = multicastAddress + ":";
        var changes = entry.Changes.Where(change =>
            (change.ObjectType == "Multicast" && change.Target == multicastAddress) ||
            (change.ObjectType == "Flux multicast" && change.Target.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        return changes.Count == 0 ? null : entry with { Changes = changes };
    }
}

using System.Globalization;

namespace GAIP.Core;

public readonly record struct Ipv4Network(uint Network, int Prefix)
{
    public uint Mask => Prefix == 0 ? 0 : uint.MaxValue << (32 - Prefix);
    public string DottedMask => Format(Mask);
    public uint Broadcast => Network | ~Mask;
    // V1 follows the requested network/broadcast exclusion even for /31 and /32.
    public ulong UsableCount => Prefix >= 31 ? 0 : (1UL << (32 - Prefix)) - 2;
    public uint First => UsableCount == 0 ? Network : Network + 1;
    public uint Last => UsableCount == 0 ? Broadcast : Broadcast - 1;
    public bool Contains(uint ip) => ip >= Network && ip <= Broadcast;
    public bool IsUsable(uint ip) => UsableCount > 0 && ip > Network && ip < Broadcast;
    public bool Overlaps(Ipv4Network other) => Network <= other.Broadcast && other.Network <= Broadcast;
    public override string ToString() => $"{Format(Network)}/{Prefix}";

    public static uint ParseAddress(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new FormatException("Adresse IPv4 obligatoire.");
        var parts = value.Split('.');
        if (parts.Length != 4) throw new FormatException($"IPv4 invalide : {value}.");
        uint result = 0;
        foreach (var part in parts)
        {
            if (part.Length is < 1 or > 3 || (part.Length > 1 && part[0] == '0') ||
                part.Any(c => c is < '0' or > '9') || !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                throw new FormatException($"IPv4 invalide : {value}.");
            result = (result << 8) | b;
        }
        return result;
    }

    public static string Format(uint value) => $"{value >> 24}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}";
    public static Ipv4Network Parse(string cidr)
    {
        var parts = (cidr ?? "").Split('/');
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefix) || prefix is < 0 or > 32)
            throw new FormatException($"CIDR IPv4 invalide : {cidr}.");
        var ip = ParseAddress(parts[0]);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return new(ip & mask, prefix);
    }
}

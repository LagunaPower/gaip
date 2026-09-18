using System.Collections;

namespace GAIP.Core;

/// <summary>Read-only indexed source: free rows are created on demand, never preallocated.</summary>
public sealed class AddressRows : IReadOnlyList<AddressRow>, IList
{
    // Beyond /12, keep consultation of stored IPs and exact free-IP lookup available.
    // This is a UI size guard, not a restriction on network calculations or stored CIDRs.
    public const ulong MaxBrowsableAddresses = 1_048_574;
    private readonly Ipv4Network _network;
    private readonly Dictionary<uint, IpAddress> _stored;
    private readonly Gateway? _gateway;
    private readonly uint? _gatewayAddress;
    private readonly uint[]? _indices;
    public int Count { get; }

    public AddressRows(Subnet subnet, bool showFree = false, string search = "")
    {
        _network = Ipv4Network.Parse(subnet.Cidr);
        _stored = subnet.Addresses.ToDictionary(a => Ipv4Network.ParseAddress(a.Address));
        _gateway = subnet.Gateway;
        _gatewayAddress = _gateway is null ? null : Ipv4Network.ParseAddress(_gateway.Address);
        var used = _stored.Keys.Concat(_gatewayAddress is { } gw ? new[] { gw } : []).Order().ToArray();
        search = search.Trim();
        if (!showFree)
            _indices = used.Where(ip => Matches(Row(ip), search)).ToArray();
        else if (search.Length > 0 && TryAddress(search, out var exact))
            _indices = _network.IsUsable(exact) ? [exact] : [];
        else
        {
            if (_network.UsableCount > MaxBrowsableAddresses)
                throw new InvalidOperationException("Ce réseau dépasse 1 048 574 adresses. Consultez les IP utilisées ou recherchez une IP libre complète.");
            if (search.Length > 0)
            {
                // The common no-search path is O(stored IPs), including for large networks.
                // A filtered range contains only numeric indices, not UI rows or controls.
                var matches = new List<uint>();
                for (ulong i = 0; i < _network.UsableCount; i++)
                {
                    var ip = (uint)(_network.First + i);
                    if (Matches(Row(ip), search)) matches.Add(ip);
                }
                _indices = matches.ToArray();
            }
        }
        Count = _indices?.Length ?? checked((int)_network.UsableCount);
    }

    public AddressRow this[int index]
    {
        get
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return Row(_indices is null ? _network.First + (uint)index : _indices[index]);
        }
    }
    private AddressRow Row(uint ip) => ip == _gatewayAddress
        ? new(Ipv4Network.Format(ip), "PASSERELLE", _gateway!.Comment, true, true)
        : _stored.TryGetValue(ip, out var stored) ? new(stored.Address, stored.Hostname, stored.Description, true, false)
        : new(Ipv4Network.Format(ip), "Libre", "", false, false);
    private static bool Matches(AddressRow row, string search) => search.Length == 0 ||
        row.Address.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Hostname.Contains(search, StringComparison.OrdinalIgnoreCase) ||
        row.Description.Contains(search, StringComparison.OrdinalIgnoreCase);
    private static bool TryAddress(string search, out uint address)
    {
        try { address = Ipv4Network.ParseAddress(search); return true; }
        catch (FormatException) { address = 0; return false; }
    }
    public IEnumerator<AddressRow> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    bool IList.IsReadOnly => true;
    bool IList.IsFixedSize => true;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;
    object? IList.this[int index] { get => this[index]; set => throw new NotSupportedException(); }
    int IList.IndexOf(object? value)
    {
        if (value is not AddressRow row || !TryAddress(row.Address, out var ip) || !_network.IsUsable(ip)) return -1;
        return _indices is null ? checked((int)(ip - _network.First)) : Array.BinarySearch(_indices, ip) is var i && i >= 0 ? i : -1;
    }
    bool IList.Contains(object? value) => ((IList)this).IndexOf(value) >= 0;
    void ICollection.CopyTo(Array array, int index) { foreach (var row in this) array.SetValue(row, index++); }
    int IList.Add(object? value) => throw new NotSupportedException();
    void IList.Clear() => throw new NotSupportedException();
    void IList.Insert(int index, object? value) => throw new NotSupportedException();
    void IList.Remove(object? value) => throw new NotSupportedException();
    void IList.RemoveAt(int index) => throw new NotSupportedException();
}

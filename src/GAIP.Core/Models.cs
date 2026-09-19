using System.Text.Json.Serialization;

namespace GAIP.Core;

public sealed class Database
{
    public int SchemaVersion { get; set; } = 1;
    public long Revision { get; set; }
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
    public string LastModifiedBy { get; set; } = "";
    public string LastModifiedFrom { get; set; } = "";
    [JsonRequired] public List<Site> Sites { get; set; } = [];
}

public sealed class Site
{
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayOrder { get; set; }
    [JsonRequired] public List<Vlan> Vlans { get; set; } = [];
}

public sealed class Vlan
{
    [JsonRequired] public Guid Id { get; set; } = Guid.NewGuid();
    public int Vid { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    [JsonRequired] public Subnet? Subnet { get; set; }
}

public sealed class Subnet
{
    public string Cidr { get; set; } = "";
    [JsonRequired] public Gateway? Gateway { get; set; }
    [JsonRequired] public List<IpAddress> Addresses { get; set; } = [];
}

public sealed class Gateway
{
    public string Address { get; set; } = "";
    public string Comment { get; set; } = "";
}

public sealed class IpAddress
{
    public string Address { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class ValidationException(IEnumerable<string> errors) : Exception(string.Join(Environment.NewLine, errors))
{
    public IReadOnlyList<string> Errors { get; } = errors.ToArray();
}

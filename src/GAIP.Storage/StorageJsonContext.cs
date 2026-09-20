using System.Text.Json;
using System.Text.Json.Serialization;
using GAIP.Core;

namespace GAIP.Storage;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    UseStringEnumConverter = true, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(Database))]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(EditLease))]
[JsonSerializable(typeof(AuditEntry))]
[JsonSerializable(typeof(AuditChange))]
[JsonSerializable(typeof(Vlan))]
[JsonSerializable(typeof(MulticastGroup))]
[JsonSerializable(typeof(MulticastFlow))]
public partial class StorageJsonContext : JsonSerializerContext { }

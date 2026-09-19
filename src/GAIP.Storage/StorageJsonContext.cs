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
[JsonSerializable(typeof(Vlan))]
[JsonSerializable(typeof(JsonElement))]
// Audit object values can be Database, Vlan, EditLease or deserialized JsonElement.
public partial class StorageJsonContext : JsonSerializerContext { }

using System.Text.Json.Serialization;

namespace GAIP.Sync;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(CacheInfo))]
internal partial class SyncJsonContext : JsonSerializerContext { }

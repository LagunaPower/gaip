using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GAIP.Core;
using GAIP.Storage;
using Xunit;

namespace GAIP.Tests;

public sealed class JsonCompatibilityTests
{
    private static JsonSerializerOptions Legacy(bool indented = true) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = indented,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(), Converters = { new JsonStringEnumConverter() }
    };
    [Fact]
    public void GeneratedDatabaseAndConfigurationKeepTheExistingJsonFormat()
    {
        var db = TestData.Example("10.20.120.0/22");
        db.Sites[0].DisplayOrder = 2;
        TestData.Subnet(db).Gateway = new() { Address = "10.20.120.1", Comment = "Pare-feu été" };
        TestData.Subnet(db).Addresses.Add(new() { Address = "10.20.123.254", Description = "Réservation" });
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(db, Legacy()), JsonData.Serialize(db));
        Assert.Equal(JsonData.Serialize(db), JsonData.Serialize(JsonData.Read(JsonSerializer.SerializeToUtf8Bytes(db, Legacy()))));
        var config = new AppConfig { Mode = StorageMode.Shared, Theme = AppTheme.Dark, SharedPath = "C:/partage" };
        var old = JsonSerializer.Serialize(config, Legacy());
        Assert.Equal(old, JsonSerializer.Serialize(config, StorageJsonContext.Default.AppConfig));
        Assert.Equal(AppTheme.Dark, JsonSerializer.Deserialize(old, StorageJsonContext.Default.AppConfig)!.Theme);
    }
    [Fact]
    public void ConfigurationWithoutHomeColumnsUsesDefault()
    {
        const string old = """
            {
              "mode": "Local",
              "sharedPath": "",
              "syncSeconds": 60,
              "backupCount": 30,
              "csvSeparator": ";",
              "theme": "System"
            }
            """;
        var config = JsonSerializer.Deserialize(old, StorageJsonContext.Default.AppConfig)!;
        config.Validate();
        Assert.Equal(3, config.MaxHomeColumns);
    }

    [Fact]
    public void GeneratedHistoryUsesCompactFieldDiffFormat()
    {
        var db = TestData.Example();
        var changed = JsonData.Clone(db);
        changed.Sites[0].Name = "Nouveau nom";
        var entry = new AuditEntry(DateTimeOffset.UtcNow, "test", "pc", 1, "Modification", "Site", "LEVANT",
            AuditDiff.Create(db, changed));
        var generated = JsonSerializer.Serialize(entry, JsonData.CompactContext.AuditEntry);
        Assert.Equal(JsonSerializer.Serialize(entry, Legacy(false)), generated);
        using var document = JsonDocument.Parse(generated);
        Assert.False(document.RootElement.TryGetProperty("oldValue", out _));
        Assert.False(document.RootElement.TryGetProperty("newValue", out _));
        Assert.Contains("\"changes\"", generated);
        Assert.Contains("\"field\":\"name\"", generated);
        var restored = JsonSerializer.Deserialize(generated, StorageJsonContext.Default.AuditEntry)!;
        Assert.Single(restored.Changes);
        Assert.DoesNotContain('\n', generated);
    }
}

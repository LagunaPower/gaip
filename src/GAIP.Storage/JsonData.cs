using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using GAIP.Core;

namespace GAIP.Storage;

public static class JsonData
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };
    public static byte[] Serialize(Database db) => JsonSerializer.SerializeToUtf8Bytes(db, Options);
    public static Database Read(byte[] bytes)
    {
        // Do not silently accept an unrelated or truncated-but-valid JSON document.
        using var document = JsonDocument.Parse(bytes);
        foreach (var name in new[] { "schemaVersion", "revision", "lastModified", "lastModifiedBy", "lastModifiedFrom", "sites" })
            if (!document.RootElement.TryGetProperty(name, out _)) throw new InvalidDataException($"JSON : champ {name} absent.");
        var db = JsonSerializer.Deserialize<Database>(bytes, Options) ?? throw new InvalidDataException("Base JSON vide.");
        ModelValidator.EnsureValid(db);
        return db;
    }
    public static Database Clone(Database db) => Read(Serialize(db));
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static byte[] ReadFileBytes(string path)
    {
        // Readers keep the old inode/handle when an atomic replacement occurs.
        // FileShare.Delete is essential on Windows to avoid blocking a publisher.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    public static string HashFile(string path) => Hash(ReadFileBytes(path));
    public static void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (HashFile(temp) != Hash(bytes)) throw new IOException("Échec de vérification du fichier temporaire.");
            // Same-directory rename: old or new complete file, never in-place truncation.
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

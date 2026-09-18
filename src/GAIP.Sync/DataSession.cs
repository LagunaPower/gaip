using System.Text.Json;
using GAIP.Core;
using GAIP.Storage;

namespace GAIP.Sync;

public sealed record CacheInfo(string Hash, string Source, DateTimeOffset CheckedAt);

// All calls are serialized by the desktop controller. No UI dependency.
public sealed class DataSession(AppConfig config, string localRoot, string user, string machine)
{
    public AppConfig Config { get; } = config;
    public string LocalRoot { get; } = localRoot;
    public FileRepository Repository { get; } = new(config.Mode == StorageMode.Shared ? config.SharedPath : Path.Combine(localRoot, "local"), user, machine, config.BackupCount);
    public Database Data { get; private set; } = new();
    public bool HasData { get; private set; }
    public bool IsOffline { get; private set; }
    public bool IsEditing => Config.Mode == StorageMode.Local ? HasData : Lease is not null && !IsOffline;
    public EditLease? Lease { get; private set; }
    public EditLease? RemoteLease { get; private set; }
    public string Hash { get; private set; } = "";
    public string Status { get; private set; } = "Chargement…";
    public string? Warning { get; private set; }
    private string CacheRoot => Path.Combine(LocalRoot, "cache");

    public void Open()
    {
        Config.Validate();
        if (Config.Mode == StorageMode.Local)
        {
            Directory.CreateDirectory(Repository.Root);
            if (!File.Exists(Repository.DataPath)) Repository.Initialize(new());
            Accept(Repository.Read());
            Status = "Local · modifications autorisées";
        }
        else Refresh();
    }
    private void Accept(Snapshot snapshot)
    {
        Data = snapshot.Data; Hash = snapshot.Hash; HasData = true;
    }
    private void Cache(Snapshot snapshot)
    {
        Directory.CreateDirectory(CacheRoot);
        var dataPath = Path.Combine(CacheRoot, "gaip-data.json");
        JsonData.AtomicWrite(dataPath, snapshot.Bytes);
        if (JsonData.HashFile(dataPath) != snapshot.Hash) throw new IOException("Cache non vérifié.");
        JsonData.AtomicWrite(Path.Combine(CacheRoot, "cache.info"), JsonSerializer.SerializeToUtf8Bytes(
            new CacheInfo(snapshot.Hash, Path.GetFullPath(Config.SharedPath), DateTimeOffset.UtcNow), JsonData.Options));
    }
    private Snapshot LoadCache()
    {
        var info = JsonSerializer.Deserialize<CacheInfo>(JsonData.ReadFileBytes(Path.Combine(CacheRoot, "cache.info")), JsonData.Options)
            ?? throw new InvalidDataException("Métadonnées du cache absentes.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(info.Source, Path.GetFullPath(Config.SharedPath), comparison)) throw new InvalidDataException("Cache provenant d'un autre partage.");
        var bytes = JsonData.ReadFileBytes(Path.Combine(CacheRoot, "gaip-data.json"));
        if (JsonData.Hash(bytes) != info.Hash) throw new InvalidDataException("Cache corrompu (SHA-256 incorrect).");
        return new(JsonData.Read(bytes), info.Hash, bytes);
    }
    public void Refresh()
    {
        Warning = null;
        if (Config.Mode == StorageMode.Local) { Accept(Repository.Read()); return; }
        try
        {
            var current = Repository.Read();
            if (Lease is { } lease)
            {
                Repository.Heartbeat(lease.Id);
                if (current.Hash != Hash) throw new IOException("Base modifiée hors de votre session ; édition interrompue.");
            }
            if (!HasData || current.Hash != Hash) { Cache(current); Accept(current); }
            // Heal a damaged disk cache even if this process already has the latest bytes.
            else { try { LoadCache(); } catch { Cache(current); } }
            RemoteLease = Repository.ReadLease();
            IsOffline = false;
            Status = Lease is not null ? "Modification en cours · votre session" : RemoteLease is { } other
                ? $"Modification en cours par {other.User} / {other.Machine}" : "À jour";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ValidationException)
        {
            var oldLease = Lease;
            Lease = null;
            if (oldLease is not null) { try { Repository.Release(oldLease.Id); } catch { /* unavailable: keep central lease for explicit recovery */ } }
            IsOffline = true;
            Warning = ex.Message;
            if (!HasData)
            {
                try { Accept(LoadCache()); }
                catch (Exception cacheError) { throw new IOException($"Base centrale inaccessible et aucun cache valide. {ex.Message}\n{cacheError.Message}", cacheError); }
            }
            Status = "Mode hors ligne — cache local · lecture seule";
        }
    }
    public void BeginEdit()
    {
        if (Config.Mode == StorageMode.Local) return;
        Refresh();
        if (IsOffline) throw new IOException(Warning ?? "Stockage central inaccessible.");
        if (Lease is not null) return;
        var result = Repository.Acquire(Hash);
        Lease = result.Lease;
        Accept(result.Snapshot);
        Status = "Modification en cours · votre session";
    }
    public void EndEdit()
    {
        if (Lease is { } lease) Repository.Release(lease.Id);
        Lease = null;
        Refresh();
    }
    public void Save(Action<Database> mutation, string action, string objectType, string target)
    {
        if (!HasData || !IsEditing) throw new IOException("Passez en mode modification pour enregistrer.");
        var candidate = JsonData.Clone(Data);
        mutation(candidate);
        ModelValidator.EnsureValid(candidate);
        CommitResult result;
        try { result = Repository.Commit(candidate, Hash, Lease?.Id, action, objectType, target); }
        catch
        {
            if (Config.Mode == StorageMode.Shared)
            {
                var old = Lease; Lease = null;
                if (old is not null) { try { Repository.Release(old.Id); } catch { } }
                Status = "Publication interrompue · actualisation nécessaire";
            }
            throw;
        }
        Accept(result.Snapshot);
        Warning = result.Warning;
        if (Config.Mode == StorageMode.Shared)
        {
            try { Cache(result.Snapshot); }
            catch (Exception ex) { Warning = $"Publication réussie ; mise à jour du cache impossible : {ex.Message}"; }
        }
    }
}

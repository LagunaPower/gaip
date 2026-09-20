using System.Text.Json;
using GAIP.Core;
using GAIP.Storage;

namespace GAIP.Sync;

public sealed record CacheInfo(string Hash, string Source, DateTimeOffset CheckedAt, string? HistoryHash = null);
internal sealed record CachedState(Snapshot Snapshot, byte[]? HistoryBytes, CacheInfo Info);

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
    private void Cache(Snapshot snapshot, byte[] historyBytes)
    {
        FileRepository.ValidateHistory(historyBytes);
        Directory.CreateDirectory(CacheRoot);
        var dataPath = Path.Combine(CacheRoot, "gaip-data.json");
        var historyPath = Path.Combine(CacheRoot, "history.jsonl");
        var historyHash = JsonData.Hash(historyBytes);
        JsonData.AtomicWrite(dataPath, snapshot.Bytes);
        if (JsonData.HashFile(dataPath) != snapshot.Hash) throw new IOException("Cache de base non vérifié.");
        JsonData.AtomicWrite(historyPath, historyBytes);
        if (JsonData.HashFile(historyPath) != historyHash) throw new IOException("Cache d’historique non vérifié.");
        JsonData.AtomicWrite(Path.Combine(CacheRoot, "cache.info"), JsonSerializer.SerializeToUtf8Bytes(
            new CacheInfo(snapshot.Hash, Path.GetFullPath(Config.SharedPath), DateTimeOffset.UtcNow, historyHash), SyncJsonContext.Default.CacheInfo));
    }
    private CachedState LoadCache(bool requireHistory = false)
    {
        var info = JsonSerializer.Deserialize(JsonData.ReadFileBytes(Path.Combine(CacheRoot, "cache.info")), SyncJsonContext.Default.CacheInfo)
            ?? throw new InvalidDataException("Métadonnées du cache absentes.");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(info.Source, Path.GetFullPath(Config.SharedPath), comparison)) throw new InvalidDataException("Cache provenant d'un autre partage.");
        var bytes = JsonData.ReadFileBytes(Path.Combine(CacheRoot, "gaip-data.json"));
        if (JsonData.Hash(bytes) != info.Hash) throw new InvalidDataException("Cache de base corrompu (SHA-256 incorrect).");
        var snapshot = new Snapshot(JsonData.Read(bytes), info.Hash, bytes);

        byte[]? historyBytes = null;
        if (!string.IsNullOrWhiteSpace(info.HistoryHash))
        {
            try
            {
                var candidate = JsonData.ReadFileBytes(Path.Combine(CacheRoot, "history.jsonl"));
                if (JsonData.Hash(candidate) != info.HistoryHash) throw new InvalidDataException("Cache d’historique corrompu (SHA-256 incorrect).");
                FileRepository.ValidateHistory(candidate);
                historyBytes = candidate;
            }
            catch (Exception ex) when (!requireHistory && ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ValidationException)
            {
                historyBytes = null;
            }
        }
        if (requireHistory && historyBytes is null)
            throw new InvalidDataException("Aucun historique local validé n’est disponible pour cette restauration. Reconnectez G@IP au partage pour actualiser le cache.");
        return new(snapshot, historyBytes, info);
    }
    private void EnsureCache(Snapshot snapshot, byte[] historyBytes)
    {
        FileRepository.ValidateHistory(historyBytes);
        var historyHash = JsonData.Hash(historyBytes);
        try
        {
            var cached = LoadCache(true);
            if (cached.Snapshot.Hash == snapshot.Hash && cached.Info.HistoryHash == historyHash) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ValidationException) { }
        Cache(snapshot, historyBytes);
    }
    public void Refresh()
    {
        Warning = null;
        if (Config.Mode == StorageMode.Local) { Accept(Repository.Read()); return; }
        try
        {
            var recovery = Repository.ReadRecoverySnapshot();
            var current = recovery.Snapshot;
            if (Lease is { } lease)
            {
                Repository.Heartbeat(lease.Id);
                if (current.Hash != Hash) throw new IOException("Base modifiée hors de votre session ; édition interrompue.");
            }
            if (!HasData || current.Hash != Hash) Accept(current);
            RemoteLease = Repository.ReadLease();
            IsOffline = false;
            Status = Lease is not null ? "Modification en cours · votre session" : RemoteLease is { } other
                ? $"Modification en cours par {other.User} / {other.Machine}" : "À jour";
            try { EnsureCache(current, recovery.HistoryBytes); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ValidationException)
            {
                Warning = "Base centrale accessible, mais cache de récupération non actualisé : " + ex.Message;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or ValidationException)
        {
            var oldLease = Lease;
            Lease = null;
            if (oldLease is not null) { try { Repository.Release(oldLease.Id); } catch { } }
            IsOffline = true;
            Warning = ex.Message;
            if (!HasData)
            {
                try { Accept(LoadCache().Snapshot); }
                catch (Exception cacheError) { throw new IOException($"Base centrale inaccessible et aucun cache valide. {ex.Message}\n{cacheError.Message}", cacheError); }
            }
            Status = "Mode hors ligne — cache local · lecture seule";
        }
    }
    public RestoreResult RestoreSharedFromCache()
    {
        if (Config.Mode != StorageMode.Shared) throw new InvalidOperationException("La restauration du cache n’est disponible qu’en mode partagé.");
        if (Lease is not null) throw new IOException("Terminez la modification en cours avant de restaurer le cache.");
        var cached = LoadCache(true);
        var result = Repository.RestoreMissingData(cached.Snapshot.Bytes, cached.HistoryBytes!);
        Accept(result.Snapshot);
        Warning = null;
        try { Cache(result.Snapshot, cached.HistoryBytes!); }
        catch (Exception ex) { Warning = "Base et historique restaurés ; mise à jour du cache impossible : " + ex.Message; }
        RemoteLease = Repository.ReadLease();
        IsOffline = false;
        Status = "À jour";
        return result;
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
            try
            {
                var recovery = Repository.ReadRecoverySnapshot();
                if (recovery.Snapshot.Hash != result.Snapshot.Hash)
                    throw new IOException("La base centrale a changé avant la mise à jour du cache.");
                Cache(result.Snapshot, recovery.HistoryBytes);
            }
            catch (Exception ex)
            {
                Warning = string.IsNullOrWhiteSpace(Warning)
                    ? $"Publication réussie ; mise à jour du cache impossible : {ex.Message}"
                    : Warning + "\nMise à jour du cache impossible : " + ex.Message;
            }
        }
    }
}

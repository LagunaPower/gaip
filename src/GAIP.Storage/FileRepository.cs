using System.Text;
using System.Text.Json;
using GAIP.Core;

namespace GAIP.Storage;

public sealed record Snapshot(Database Data, string Hash, byte[] Bytes);
public sealed record AuditEntry(DateTimeOffset Date, string User, string Machine, long Revision, string Action,
    string ObjectType, string Target, object? OldValue = null, object? NewValue = null);
public sealed class EditLease
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string User { get; set; } = "";
    public string Machine { get; set; } = "";
    public DateTimeOffset AcquiredAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset Heartbeat { get; set; } = DateTimeOffset.UtcNow;
    public long StartRevision { get; set; }
    public string StartHash { get; set; } = "";
}
public sealed record CommitResult(Snapshot Snapshot, string? Warning);

public sealed class FileRepository(string root, string user, string machine, int backups = 30)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string DataPath => Path.Combine(Root, "gaip-data.json");
    public string LockPath => Path.Combine(Root, "edit.lock");
    public string HistoryPath => Path.Combine(Root, "history.jsonl");

    // Stable coordination inode. Never delete this file: removal could split contenders
    // into different locks. It protects force-unlock vs commit, including on Unix.
    private FileStream Guard()
    {
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Stockage inaccessible : {Root}");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try { return new(Path.Combine(Root, ".gaip-io.guard"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); }
        }
    }
    public Snapshot Read()
    {
        var bytes = JsonData.ReadFileBytes(DataPath);
        return new(JsonData.Read(bytes), JsonData.Hash(bytes), bytes);
    }
    public Snapshot Initialize(Database seed)
    {
        using var guard = Guard();
        if (File.Exists(DataPath)) throw new IOException("Une base existe déjà à cet emplacement ; elle n'a pas été écrasée.");
        ModelValidator.EnsureValid(seed);
        var db = JsonData.Clone(seed);
        db.Revision = 0;
        db.LastModified = DateTimeOffset.UtcNow;
        db.LastModifiedBy = user;
        db.LastModifiedFrom = machine;
        JsonData.AtomicWrite(DataPath, JsonData.Serialize(db));
        return Read();
    }
    public EditLease? ReadLease() => !File.Exists(LockPath) ? null
        : JsonSerializer.Deserialize(JsonData.ReadFileBytes(LockPath), StorageJsonContext.Default.EditLease) ?? throw new InvalidDataException("Verrou illisible.");

    public (EditLease Lease, Snapshot Snapshot) Acquire(string expectedHash)
    {
        using var guard = Guard();
        var existing = ReadLease();
        if (existing is not null) throw new IOException($"Modification en cours par {existing.User} / {existing.Machine}, depuis {existing.AcquiredAt.LocalDateTime:g}.");
        var snapshot = Read();
        if (snapshot.Hash != expectedHash) throw new IOException("La base a changé. Actualisez puis réessayez.");
        var lease = new EditLease { User = user, Machine = machine, StartRevision = snapshot.Data.Revision, StartHash = snapshot.Hash };
        using (var stream = new FileStream(LockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, lease, StorageJsonContext.Default.EditLease);
            stream.Flush(true);
        }
        try
        {
            var after = Read();
            if (after.Hash != snapshot.Hash) throw new IOException("La base a changé pendant la prise du verrou.");
            return (lease, after);
        }
        catch { File.Delete(LockPath); throw; }
    }
    private EditLease RequireLease(Guid id)
    {
        var lease = ReadLease();
        if (lease is null || lease.Id != id) throw new IOException("Verrou perdu ou forcé : publication interdite. Revenez en consultation.");
        return lease;
    }
    public void Heartbeat(Guid id)
    {
        using var guard = Guard();
        var lease = RequireLease(id);
        lease.Heartbeat = DateTimeOffset.UtcNow;
        JsonData.AtomicWrite(LockPath, JsonSerializer.SerializeToUtf8Bytes(lease, StorageJsonContext.Default.EditLease));
    }
    public void Release(Guid id)
    {
        using var guard = Guard();
        if (ReadLease()?.Id == id) File.Delete(LockPath);
    }
    public void ForceRelease(Guid expectedId)
    {
        using var guard = Guard();
        var lease = RequireLease(expectedId);
        var db = Read().Data;
        // Audit failure blocks force-unlock, so the action cannot silently escape history.
        AppendHistory(new(DateTimeOffset.UtcNow, user, machine, db.Revision, "Libération forcée", "Verrou", lease.Id.ToString(), lease, null));
        File.Delete(LockPath);
    }
    public CommitResult Commit(Database candidate, string expectedHash, Guid? leaseId, string action, string objectType, string target)
    {
        using var guard = Guard();
        if (leaseId is { } id) RequireLease(id);
        var old = Read();
        if (old.Hash != expectedHash) throw new IOException("Conflit : la base centrale a changé. Rien n'a été publié ; actualisez.");
        ModelValidator.EnsureValid(candidate);
        ModelValidator.EnsureTransitionValid(old.Data, candidate);
        var db = JsonData.Clone(candidate);
        db.Revision = checked(old.Data.Revision + 1);
        db.LastModified = DateTimeOffset.UtcNow;
        db.LastModifiedBy = user;
        db.LastModifiedFrom = machine;
        var bytes = JsonData.Serialize(db);
        JsonData.Read(bytes);
        var backupDir = Path.Combine(Root, "backup");
        Directory.CreateDirectory(backupDir);
        var backup = Path.Combine(backupDir, $"gaip-data_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fffffff}_rev{old.Data.Revision:D4}_{Guid.NewGuid():N}.json");
        using (var stream = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(old.Bytes); stream.Flush(true); }
        if (JsonData.HashFile(backup) != old.Hash) throw new IOException("Sauvegarde non vérifiée : publication annulée.");
        if (leaseId is { } lastId) RequireLease(lastId);
        if (Read().Hash != old.Hash) throw new IOException("La base a changé pendant la sauvegarde : publication annulée.");
        JsonData.AtomicWrite(DataPath, bytes);
        Snapshot published;
        try
        {
            published = Read();
            if (published.Hash != JsonData.Hash(bytes)) throw new IOException("Hash publié inattendu.");
        }
        catch (Exception ex)
        {
            throw new IOException("Publication potentiellement effectuée, mais vérification impossible. Actualisez avant toute nouvelle modification. " + ex.Message, ex);
        }
        var warnings = new List<string>();
        try { AppendHistory(new(db.LastModified, user, machine, db.Revision, action, objectType, target, old.Data, db)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("Données publiées, historique non écrit : " + ex.Message); }
        try
        {
            foreach (var file in Directory.GetFiles(backupDir, "gaip-data_*.json").OrderByDescending(Path.GetFileName).Skip(Math.Max(1, backups))) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("Nettoyage des sauvegardes impossible : " + ex.Message); }
        return new(published, warnings.Count == 0 ? null : string.Join("\n", warnings));
    }
    private void AppendHistory(AuditEntry entry)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonData.CompactContext.AuditEntry) + "\n");
        using var stream = new FileStream(HistoryPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(true);
    }
    public IReadOnlyList<string> History() => History(null);
    public IReadOnlyList<string> History(Guid? vlanId)
    {
        if (!File.Exists(HistoryPath)) return [];
        using var stream = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var last = new Queue<string>();
        while (reader.ReadLine() is { } line)
        {
            var result = line;
            if (vlanId is { } id)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize(line, StorageJsonContext.Default.AuditEntry);
                    var scoped = entry is null ? null : VlanHistory.Project(entry, id);
                    if (scoped is null) continue;
                    result = JsonSerializer.Serialize(scoped, StorageJsonContext.Default.AuditEntry);
                }
                catch (JsonException) { continue; } // Corrupt lines cannot be attributed reliably.
            }
            last.Enqueue(result); if (last.Count > 1000) last.Dequeue();
        }
        return last.Reverse().ToArray();
    }
    public void TestAccess()
    {
        using var guard = Guard();
        var path = Path.Combine(Root, $".access-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllText(path, "GAIP"); if (File.ReadAllText(path) != "GAIP") throw new IOException("Lecture incohérente."); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

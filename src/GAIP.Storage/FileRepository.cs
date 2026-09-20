using System.Globalization;
using System.Text;
using System.Text.Json;
using GAIP.Core;

namespace GAIP.Storage;

public sealed record Snapshot(Database Data, string Hash, byte[] Bytes);
public sealed record RecoverySnapshot(Snapshot Snapshot, byte[] HistoryBytes);
public sealed record AuditEntry(DateTimeOffset Date, string User, string Machine, long Revision, string Action,
    string ObjectType, string Target, List<AuditChange> Changes);
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
public sealed record RestoreResult(Snapshot Snapshot, int HistoryEntries);

public sealed class FileRepository(string root, string user, string machine, int backups = 30)
{
    public string Root { get; } = Path.GetFullPath(root);
    public string DataPath => Path.Combine(Root, "gaip-data.json");
    public string LockPath => Path.Combine(Root, "edit.lock");
    public string HistoryPath => Path.Combine(Root, "history.jsonl");
    public string BackupPath => Path.Combine(Root, "backup");

    // Stable coordination inode stored outside the business files. Never delete io.guard:
    // removal while clients are active could split contenders into different locks.
    private string TechnicalRoot => Path.Combine(Root, ".gaip");
    private string GuardPath => Path.Combine(TechnicalRoot, "io.guard");

    private void PrepareGuardLayout()
    {
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException($"Stockage inaccessible : {Root}");
        Directory.CreateDirectory(TechnicalRoot);
        if (OperatingSystem.IsWindows())
        {
            try { File.SetAttributes(TechnicalRoot, File.GetAttributes(TechnicalRoot) | FileAttributes.Hidden); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { }
        }

        var legacyPath = Path.Combine(Root, ".gaip-io.guard");
        if (!File.Exists(legacyPath)) return;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                using (new FileStream(legacyPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(legacyPath);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); }
            catch (IOException ex)
            {
                throw new IOException("L’ancienne garde .gaip-io.guard est encore utilisée. Fermez ou mettez à jour les anciens clients G@IP avant de réessayer.", ex);
            }
        }
    }

    private FileStream Guard()
    {
        PrepareGuardLayout();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            try { return new(GuardPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(50); }
        }
    }
    public Snapshot Read()
    {
        var bytes = JsonData.ReadFileBytes(DataPath);
        return new(JsonData.Read(bytes), JsonData.Hash(bytes), bytes);
    }
    private byte[] ReadHistoryBytesRaw() => File.Exists(HistoryPath) ? JsonData.ReadFileBytes(HistoryPath) : [];
    public byte[] ReadHistoryBytes()
    {
        var bytes = ReadHistoryBytesRaw();
        ValidateHistory(bytes);
        return bytes;
    }
    public RecoverySnapshot ReadRecoverySnapshot()
    {
        using var guard = Guard();
        return new(Read(), ReadHistoryBytesRaw());
    }
    public static int ValidateHistory(byte[] bytes)
    {
        var count = 0;
        try
        {
            using var reader = new StreamReader(new MemoryStream(bytes, writable: false), new UTF8Encoding(false, true), true);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) throw new InvalidDataException($"Historique : ligne {count + 1} vide.");
                _ = JsonSerializer.Deserialize(line, StorageJsonContext.Default.AuditEntry)
                    ?? throw new InvalidDataException($"Historique : ligne {count + 1} vide.");
                count++;
            }
        }
        catch (DecoderFallbackException ex) { throw new InvalidDataException("Historique : UTF-8 invalide.", ex); }
        catch (JsonException ex) { throw new InvalidDataException($"Historique : ligne {count + 1} invalide.", ex); }
        return count;
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
        AppendHistory(new(DateTimeOffset.UtcNow, user, machine, db.Revision, "Libération forcée", "Verrou", lease.Id.ToString(),
        [
            new(null, null, "Verrou", lease.Id.ToString(), "holder", $"{lease.User} / {lease.Machine}", null),
            new(null, null, "Verrou", lease.Id.ToString(), "startRevision", lease.StartRevision.ToString(), null),
            new(null, null, "Verrou", lease.Id.ToString(), "startHash", lease.StartHash, null)
        ]));
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
        var backupDir = BackupPath;
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
        try { AppendHistory(new(db.LastModified, user, machine, db.Revision, action, objectType, target, AuditDiff.Create(old.Data, db))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("Données publiées, historique non écrit : " + ex.Message); }
        try
        {
            foreach (var file in Directory.GetFiles(backupDir, "gaip-data_*.json").OrderByDescending(Path.GetFileName).Skip(Math.Max(1, backups))) File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { warnings.Add("Nettoyage des sauvegardes impossible : " + ex.Message); }
        return new(published, warnings.Count == 0 ? null : string.Join("\n", warnings));
    }
    public RestoreResult RestoreMissingData(byte[] cachedBytes, byte[] cachedHistoryBytes)
    {
        using var guard = Guard();
        if (File.Exists(DataPath)) throw new IOException("La base partagée existe déjà : restauration refusée.");
        if (File.Exists(HistoryPath)) throw new IOException("Un historique existe déjà sur le partage : restauration refusée.");
        if (File.Exists(LockPath)) throw new IOException("Un verrou existe sur le partage : restauration refusée.");

        var db = JsonData.Read(cachedBytes);
        var expectedHash = JsonData.Hash(cachedBytes);
        var historyEntries = ValidateHistory(cachedHistoryBytes);
        var expectedHistoryHash = JsonData.Hash(cachedHistoryBytes);
        var latestBackupRevision = LatestBackupRevision();
        if (latestBackupRevision > db.Revision)
            throw new IOException($"Une sauvegarde du partage en révision {latestBackupRevision} est plus récente que le cache (révision {db.Revision}) : restauration automatique refusée.");

        var historyPublished = false;
        try
        {
            AtomicCreate(HistoryPath, cachedHistoryBytes);
            historyPublished = true;
            var restoredHistory = ReadHistoryBytes();
            if (JsonData.Hash(restoredHistory) != expectedHistoryHash)
                throw new IOException("L’historique restauré ne correspond pas au cache validé.");

            AtomicCreate(DataPath, cachedBytes);
            var published = Read();
            if (published.Hash != expectedHash)
                throw new IOException("La base restaurée ne correspond pas au cache validé.");
            return new(published, historyEntries);
        }
        catch
        {
            if (historyPublished && !File.Exists(DataPath))
            {
                try
                {
                    if (File.Exists(HistoryPath) && JsonData.HashFile(HistoryPath) == expectedHistoryHash)
                        File.Delete(HistoryPath);
                }
                catch { }
            }
            throw;
        }
    }
    private static void AtomicCreate(string path, byte[] bytes)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".restore.tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (JsonData.HashFile(temp) != JsonData.Hash(bytes)) throw new IOException("Échec de vérification du fichier temporaire de restauration.");
            File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private long LatestBackupRevision()
    {
        if (!Directory.Exists(BackupPath)) return -1;
        long latest = -1;
        foreach (var file in Directory.EnumerateFiles(BackupPath, "gaip-data_*_rev*_*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var marker = name.LastIndexOf("_rev", StringComparison.Ordinal);
            if (marker < 0) continue;
            var start = marker + 4;
            var end = name.IndexOf('_', start);
            if (end <= start) continue;
            if (long.TryParse(name.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
                latest = Math.Max(latest, revision);
        }
        return latest;
    }
    private void AppendHistory(AuditEntry entry)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, JsonData.CompactContext.AuditEntry) + "\n");
        using var stream = new FileStream(HistoryPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
        stream.Flush(true);
    }
    public IReadOnlyList<string> History() => ReadHistory(null);
    public IReadOnlyList<string> History(Guid? vlanId) =>
        vlanId is null ? History() : ReadHistory(entry => VlanHistory.Project(entry, vlanId.Value));
    public IReadOnlyList<string> History(string multicastAddress) =>
        ReadHistory(entry => MulticastHistory.Project(entry, multicastAddress));

    private IReadOnlyList<string> ReadHistory(Func<AuditEntry, AuditEntry?>? projector)
    {
        if (!File.Exists(HistoryPath)) return [];
        using var stream = new FileStream(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var last = new Queue<string>();
        while (reader.ReadLine() is { } line)
        {
            var result = line;
            if (projector is not null)
            {
                try
                {
                    var entry = JsonSerializer.Deserialize(line, StorageJsonContext.Default.AuditEntry);
                    var scoped = entry is null ? null : projector(entry);
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

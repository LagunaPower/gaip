using System.Text;
using System.Text.Json;
using GAIP.Core;
using GAIP.Storage;
using GAIP.Sync;
using Xunit;
using static GAIP.Tests.TestData;

namespace GAIP.Tests;

public sealed class StorageTests
{
    [Fact]
    public void JsonRoundTripUsesStableIdsAndNoComputedFields()
    {
        var original = Example(); Subnet(original).Gateway = new() { Address = "10.20.120.1", Comment = "Pare-feu" };
        var bytes = JsonData.Serialize(original); var copy = JsonData.Read(bytes);
        Assert.Equal(original.Sites[0].Id, copy.Sites[0].Id); Assert.Equal(original.Sites[0].Vlans[0].Id, copy.Sites[0].Vlans[0].Id);
        var text = Encoding.UTF8.GetString(bytes);
        foreach (var forbidden in new[] { "broadcast", "usableCount", "isUsed", "freeCount", "network", "status" }) Assert.DoesNotContain('"' + forbidden + '"', text);
    }
    [Fact]
    public void Sha256MatchesKnownVector() => Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", JsonData.Hash(Encoding.UTF8.GetBytes("abc")));
    [Theory]
    [InlineData("{}")][InlineData("{\"sites\":[]}")][InlineData("{invalid")]
    public void CorruptOrIncompleteJsonRejected(string text) => Assert.ThrowsAny<Exception>(() => JsonData.Read(Encoding.UTF8.GetBytes(text)));
    [Fact]
    public void MissingStableIdsAndCollectionsRejected()
    {
        var text = Encoding.UTF8.GetString(JsonData.Serialize(Example()));
        using var document = JsonDocument.Parse(text);
        var node = System.Text.Json.Nodes.JsonNode.Parse(text)!; node["sites"]![0]!.AsObject().Remove("id");
        Assert.Throws<JsonException>(() => JsonData.Read(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }
    [Fact]
    public void PublicationIncrementsRevisionBacksUpAndAudits()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example());
        var change = JsonData.Clone(start.Data); change.Sites[0].Description = "Modifiée";
        var result = repo.Commit(change, start.Hash, null, "Modification", "Site", "LEVANT");
        Assert.Equal(1, result.Snapshot.Data.Revision); Assert.NotEqual(start.Hash, result.Snapshot.Hash);
        var backup = Assert.Single(Directory.GetFiles(System.IO.Path.Combine(temp.Path, "backup")));
        Assert.Equal(start.Hash, JsonData.HashFile(backup)); Assert.Single(repo.History()); Assert.Null(result.Warning);
        var entry = JsonSerializer.Deserialize<AuditEntry>(repo.History()[0], JsonData.Options)!;
        Assert.Equal(1, entry.Revision); Assert.NotNull(entry.OldValue); Assert.NotNull(entry.NewValue);
    }
    [Fact]
    public void InvalidModelAndStaleHashDoNotTouchBase()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example());
        var invalid = JsonData.Clone(start.Data); invalid.Sites[0].Vlans[0].Vid = 5000;
        Assert.Throws<ValidationException>(() => repo.Commit(invalid, start.Hash, null, "test", "test", "test"));
        Assert.Throws<IOException>(() => repo.Commit(start.Data, "bad hash", null, "test", "test", "test"));
        Assert.Equal(start.Hash, repo.Read().Hash); Assert.Empty(repo.History());
    }
    [Fact]
    public void FailedBackupNeverReplacesCurrentFile()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example());
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "backup"), "block directory creation");
        Assert.Throws<IOException>(() => repo.Commit(start.Data, start.Hash, null, "test", "test", "test"));
        Assert.Equal(start.Hash, repo.Read().Hash);
    }
    [Fact]
    public void AtomicWriteFailureAndIncompleteTemporaryFilePreserveDatabase()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example());
        File.WriteAllText(repo.DataPath + ".interrupted.tmp", "{\"schemaVersion\":");
        Assert.Equal(start.Hash, repo.Read().Hash);
        var target = temp.Sub("directory-not-file");
        var failure = Record.Exception(() => JsonData.AtomicWrite(target, JsonData.Serialize(start.Data)));
        Assert.True(failure is IOException or UnauthorizedAccessException);
        Assert.Equal(start.Hash, repo.Read().Hash);
    }
    [Fact]
    public void BackupRetentionKeepsNewestVersions()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(backups: 2); var snapshot = repo.Initialize(Example());
        for (int i = 0; i < 5; i++) snapshot = repo.Commit(snapshot.Data, snapshot.Hash, null, "test", "Base", "all").Snapshot;
        var files = Directory.GetFiles(System.IO.Path.Combine(temp.Path, "backup")); Assert.Equal(2, files.Length);
        Assert.Equal(new long[] { 3, 4 }, files.Select(f => JsonData.Read(File.ReadAllBytes(f)).Revision).OrderBy(r => r));
    }
    [Fact]
    public void ExistingCentralDatabaseCannotBeOverwrittenByInitialize()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example());
        Assert.Throws<IOException>(() => repo.Initialize(new())); Assert.Equal(start.Hash, repo.Read().Hash);
    }
    [Fact]
    public void OnlyOneWriterHeartbeatAndForcedLeaseLoss()
    {
        using var temp = new TempDirectory(); var a = temp.Repository("alice"); var b = temp.Repository("bob"); var snapshot = a.Initialize(Example());
        var first = a.Acquire(snapshot.Hash).Lease;
        Assert.Throws<IOException>(() => b.Acquire(snapshot.Hash)); a.Heartbeat(first.Id);
        Assert.True(a.ReadLease()!.Heartbeat >= first.Heartbeat);
        b.ForceRelease(first.Id); var second = b.Acquire(snapshot.Hash).Lease;
        Assert.Throws<IOException>(() => a.Commit(snapshot.Data, snapshot.Hash, first.Id, "stale", "Base", "all"));
        Assert.Throws<IOException>(() => a.Heartbeat(first.Id)); a.Release(first.Id); Assert.Equal(second.Id, b.ReadLease()!.Id);
        var published = b.Commit(snapshot.Data, snapshot.Hash, second.Id, "change", "Base", "all"); Assert.Equal(1, published.Snapshot.Data.Revision);
        Assert.Contains(a.History(), line => line.Contains("Libération forcée") || line.Contains("Lib\\u00E9ration forc\\u00E9e"));
        b.Release(second.Id); Assert.Null(a.ReadLease());
    }
    [Fact]
    public async Task ConcurrentAcquisitionHasExactlyOneWinner()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example());
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        { try { return temp.Repository("writer" + i).Acquire(start.Hash).Lease; } catch (IOException) { return null; } })));
        Assert.Single(results, r => r is not null);
    }
    [Fact]
    public async Task ForceAndPublishAreSerialized()
    {
        using var temp = new TempDirectory(); var repo = temp.Repository(); var start = repo.Initialize(Example()); var lease = repo.Acquire(start.Hash).Lease;
        await Task.WhenAll(Task.Run(() => { try { repo.Commit(start.Data, start.Hash, lease.Id, "change", "Base", "all"); } catch (IOException) { } }, TestContext.Current.CancellationToken),
            Task.Run(() => repo.ForceRelease(lease.Id), TestContext.Current.CancellationToken));
        Assert.Null(repo.ReadLease()); var now = repo.Read();
        Assert.Throws<IOException>(() => repo.Commit(now.Data, now.Hash, lease.Id, "stale", "Base", "all"));
        Assert.Equal(now.Hash, repo.Read().Hash);
    }
    [Fact]
    public void LocalSessionHasNoEditLockAndSurvivesReopen()
    {
        using var temp = new TempDirectory(); var session = new DataSession(new(), temp.Path, "user", "pc"); session.Open();
        session.Save(db => db.Sites.Add(new() { Code = "A", Name = "A" }), "Creation", "Site", "A");
        Assert.False(File.Exists(session.Repository.LockPath)); var other = new DataSession(new(), temp.Path, "user", "pc"); other.Open();
        Assert.Single(other.Data.Sites); Assert.True(other.IsEditing); Assert.Equal(1, other.Data.Revision);
    }
    [Fact]
    public void SharedSessionsSynchronizeAndUseReadOnlyOfflineCache()
    {
        using var temp = new TempDirectory(); var central = temp.Sub("central");
        new FileRepository(central, "seed", "pc").Initialize(Example());
        var config = new AppConfig { Mode = StorageMode.Shared, SharedPath = central };
        var a = new DataSession(config, temp.Sub("alice"), "alice", "pc"); var b = new DataSession(config, temp.Sub("bob"), "bob", "pc");
        a.Open(); b.Open(); Assert.False(a.IsEditing); a.BeginEdit();
        Assert.Throws<IOException>(b.BeginEdit);
        a.Save(db => Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRV" }), "Attribution", "IP", "10.20.120.25");
        b.Refresh(); Assert.Single(Subnet(b.Data).Addresses); Assert.Equal(a.Hash, b.Hash);
        a.EndEdit(); Directory.Move(central, central + "-offline"); b.Refresh();
        Assert.True(b.IsOffline); Assert.False(b.IsEditing); Assert.Single(Subnet(b.Data).Addresses);
        Assert.Throws<IOException>(() => b.Save(db => db.Sites.Clear(), "bad", "Base", "all"));
        var reopened = new DataSession(config, System.IO.Path.Combine(temp.Path, "bob"), "bob", "pc"); reopened.Open();
        Assert.True(reopened.IsOffline); Assert.Single(Subnet(reopened.Data).Addresses);
    }
    [Fact]
    public void CorruptCacheRejectedOfflineAndRepairedOnline()
    {
        using var temp = new TempDirectory(); var central = temp.Sub("central"); var local = temp.Sub("local");
        new FileRepository(central, "seed", "pc").Initialize(Example()); var config = new AppConfig { Mode = StorageMode.Shared, SharedPath = central };
        var a = new DataSession(config, local, "a", "pc"); a.Open();
        var cache = System.IO.Path.Combine(local, "cache", "gaip-data.json"); File.WriteAllText(cache, "{}");
        a.Refresh(); Assert.Equal(a.Hash, JsonData.HashFile(cache));
        File.WriteAllText(cache, "{}"); Directory.Move(central, central + "-offline");
        var b = new DataSession(config, local, "a", "pc"); Assert.Throws<IOException>(b.Open); Assert.False(b.HasData);
    }
    [Fact]
    public void WrongShareCacheCannotBeUsed()
    {
        using var temp = new TempDirectory(); var central = temp.Sub("central"); var local = temp.Sub("local");
        new FileRepository(central, "seed", "pc").Initialize(Example());
        new DataSession(new() { Mode = StorageMode.Shared, SharedPath = central }, local, "a", "pc").Open();
        var wrong = new DataSession(new() { Mode = StorageMode.Shared, SharedPath = System.IO.Path.Combine(temp.Path, "missing") }, local, "a", "pc");
        Assert.Throws<IOException>(wrong.Open);
    }
    [Fact]
    public void ForceUnlockStopsSessionPublishingEvenBeforeHeartbeat()
    {
        using var temp = new TempDirectory(); var central = temp.Sub("central"); var repository = new FileRepository(central, "seed", "pc"); repository.Initialize(Example());
        var session = new DataSession(new() { Mode = StorageMode.Shared, SharedPath = central }, temp.Sub("local"), "a", "pc"); session.Open(); session.BeginEdit();
        repository.ForceRelease(session.Lease!.Id);
        Assert.Throws<IOException>(() => session.Save(db => db.Sites[0].Name = "forbidden", "change", "Site", "LEVANT"));
        Assert.False(session.IsEditing); Assert.Equal("Île du Levant", repository.Read().Data.Sites[0].Name);
    }
    [Fact]
    public void NetworkLossDuringEditingPreventsSaveAndRetainsCache()
    {
        using var temp = new TempDirectory(); var central = temp.Sub("central"); new FileRepository(central, "seed", "pc").Initialize(Example());
        var session = new DataSession(new() { Mode = StorageMode.Shared, SharedPath = central }, temp.Sub("local"), "a", "pc"); session.Open(); session.BeginEdit();
        Directory.Move(central, central + "-offline");
        Assert.Throws<DirectoryNotFoundException>(() => session.Save(db => db.Sites[0].Name = "lost", "change", "Site", "LEVANT"));
        Assert.Equal("Île du Levant", session.Data.Sites[0].Name); Assert.False(session.IsEditing); session.Refresh(); Assert.True(session.IsOffline);
    }
    [Fact]
    public void ConfigRoundTripsAndValidates()
    {
        using var temp = new TempDirectory(); var config = new AppConfig { Theme = AppTheme.Dark, BackupCount = 12, CsvSeparator = ",", SyncSeconds = 90, MaxHomeColumns = 6 };
        UserPaths.SaveConfig(temp.Path, config); var copy = UserPaths.LoadConfig(temp.Path);
        Assert.Equal(AppTheme.Dark, copy.Theme); Assert.Equal(12, copy.BackupCount); Assert.Equal(6, copy.MaxHomeColumns);
        config.MaxHomeColumns = 7; Assert.Throws<InvalidDataException>(config.Validate);
        config.MaxHomeColumns = 3; config.SyncSeconds = 0; Assert.Throws<InvalidDataException>(config.Validate);
    }
}

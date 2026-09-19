using System.Text;
using GAIP.Core;
using GAIP.Storage;
using GAIP.Sync;
using Xunit;

namespace GAIP.Tests;

public sealed class SiteOrderingTests
{
    private static Database Example() => new() { Sites = [new() { Code = "Z", Name = "Zulu" }, new() { Code = "A", Name = "Alpha" }, new() { Code = "M", Name = "Mike" }] };
    private static string[] Codes(Database db) => SiteOrdering.Ordered(db.Sites).Select(s => s.Code).ToArray();

    [Fact]
    public void LegacyJsonKeepsAlphabeticalDisplayWithoutRewritingData()
    {
        var bytes = JsonData.Serialize(Example()); Assert.DoesNotContain("displayOrder", Encoding.UTF8.GetString(bytes));
        var restored = JsonData.Read(bytes); Assert.Equal(new[] { "A", "M", "Z" }, Codes(restored));
        Assert.All(restored.Sites, s => Assert.Null(s.DisplayOrder));
    }
    [Fact]
    public void SavedOrderSurvivesRenameReopenAndNewSitesFollowIt()
    {
        var db = Example(); var ids = db.Sites.Select(s => s.Id).ToArray(); SiteOrdering.Apply(db, ids);
        db.Sites[0].Code = "ZZZ";
        db = JsonData.Clone(db); Assert.Equal(new[] { "ZZZ", "A", "M" }, Codes(db));
        db.Sites.Add(new() { Code = "AAA", Name = "New" }); Assert.Equal("AAA", Codes(db).Last());
        ModelValidator.DeleteSite(db, ids[1]); Assert.Equal(new[] { "ZZZ", "M", "AAA" }, Codes(db));
    }
    [Fact]
    public void StaleOrDuplicateIdsCannotPartiallyReorderSites()
    {
        var db = Example(); var before = JsonData.Serialize(db);
        Assert.Throws<ValidationException>(() => SiteOrdering.Apply(db, [db.Sites[0].Id]));
        Assert.Throws<ValidationException>(() => SiteOrdering.Apply(db, [db.Sites[0].Id, db.Sites[0].Id, db.Sites[2].Id]));
        Assert.Throws<ValidationException>(() => SiteOrdering.Apply(db, [db.Sites[0].Id, db.Sites[1].Id, Guid.NewGuid()]));
        Assert.Equal(before, JsonData.Serialize(db));
    }
    [Fact]
    public void NegativeOrderIsInvalidAndTiesAreDeterministic()
    {
        var db = Example(); foreach (var site in db.Sites) site.DisplayOrder = 0;
        Assert.Equal(new[] { "A", "M", "Z" }, Codes(db)); ModelValidator.EnsureValid(db);
        db.Sites[0].DisplayOrder = -1; Assert.Throws<ValidationException>(() => ModelValidator.EnsureValid(db));
    }
    [Fact]
    public void CsvImportKeepsTheSavedOrderAndAppendsNewSites()
    {
        var db = Example(); SiteOrdering.Apply(db, db.Sites.Select(s => s.Id).ToArray());
        var result = CsvExchange.Import(db, CsvExchange.VlanHeader + "\nA;Alpha;20;LAN;;;;\nB;Bravo;20;LAN;;;;", CsvKind.Vlans);
        Assert.Empty(result.Errors); Assert.Equal(new[] { "Z", "A", "M", "B" }, Codes(result.Data!));
        Assert.Equal(new int?[] { 0, 1, 2 }, db.Sites.Select(s => s.DisplayOrder));
    }
    [Fact]
    public void SharedOrderRequiresLockAndPropagatesToOtherClientsAndCache()
    {
        using var temp = new TempDirectory(); var central = temp.Sub("central");
        new FileRepository(central, "seed", "pc").Initialize(Example());
        var config = new AppConfig { Mode = StorageMode.Shared, SharedPath = central };
        var a = new DataSession(config, temp.Sub("a"), "a", "pc"); var b = new DataSession(config, temp.Sub("b"), "b", "pc");
        a.Open(); b.Open(); var order = a.Data.Sites.Select(s => s.Id).ToArray();
        Assert.Throws<IOException>(() => a.Save(db => SiteOrdering.Apply(db, order), "Ordre d’affichage", "Sites", "Accueil"));
        a.BeginEdit(); a.Save(db => SiteOrdering.Apply(db, order), "Ordre d’affichage", "Sites", "Accueil"); b.Refresh();
        Assert.Equal(new[] { "Z", "A", "M" }, Codes(b.Data)); Assert.Equal(a.Hash, b.Hash);
        Assert.Contains(a.Repository.History(), line => line.Contains("Ordre d") && line.Contains("Accueil"));
        a.EndEdit(); Directory.Move(central, central + "-offline"); b.Refresh();
        Assert.True(b.IsOffline); Assert.Equal(new[] { "Z", "A", "M" }, Codes(b.Data));
    }
}

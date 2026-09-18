using GAIP.Core;
using GAIP.Storage;

namespace GAIP.Tests;

public static class TestData
{
    public static Database Example(string cidr = "10.20.120.0/24") => new()
    {
        Sites = [new Site { Code = "LEVANT", Name = "Île du Levant", Vlans =
        [new Vlan { Vid = 120, Name = "SERVEURS", Subnet = new Subnet { Cidr = cidr } }] }]
    };
    public static Subnet Subnet(Database db) => db.Sites[0].Vlans[0].Subnet!;
}

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GAIP-tests-" + Guid.NewGuid().ToString("N"));
    public TempDirectory() => Directory.CreateDirectory(Path);
    public string Sub(string name) { var p = System.IO.Path.Combine(Path, name); Directory.CreateDirectory(p); return p; }
    public FileRepository Repository(string user = "test", string machine = "pc", int backups = 30) => new(Path, user, machine, backups);
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

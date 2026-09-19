using System.IO.Compression;
using System.Xml.Linq;
using GAIP.Core;
using Xunit;
using static GAIP.Tests.TestData;

namespace GAIP.Tests;

public sealed class ExcelTests
{
    [Fact]
    public void WorkbookContainsIndexHyperlinkNetworkInfoAndAllUsableAddresses()
    {
        var db = Example("10.20.120.0/30");
        var subnet = Subnet(db);
        subnet.Gateway = new Gateway { Address = "10.20.120.1", Comment = "Pare-feu" };
        subnet.Addresses.Add(new IpAddress { Address = "10.20.120.2", Hostname = "srv01", Description = "Serveur" });

        using var stream = new MemoryStream();
        ExcelExchange.Export(db, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.NotNull(archive.GetEntry("xl/worksheets/sheet2.xml"));

        var workbook = ReadXml(archive, "xl/workbook.xml");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheets = workbook.Descendants(main + "sheet").ToArray();
        Assert.Equal(2, sheets.Length);
        Assert.Equal("Sites et VLAN", sheets[0].Attribute("name")!.Value);
        Assert.Equal("LEVANT-VLAN120", sheets[1].Attribute("name")!.Value);

        var index = ReadXml(archive, "xl/worksheets/sheet1.xml");
        var hyperlink = Assert.Single(index.Descendants(main + "hyperlink"));
        Assert.Equal("D4", hyperlink.Attribute("ref")!.Value);
        Assert.Equal("'LEVANT-VLAN120'!A1", hyperlink.Attribute("location")!.Value);

        var network = ReadXml(archive, "xl/worksheets/sheet2.xml");
        var text = network.Descendants(main + "t").Select(x => x.Value).ToArray();
        Assert.Contains("10.20.120.1", text);
        Assert.Contains("10.20.120.2", text);
        Assert.Contains("PASSERELLE", text);
        Assert.Contains("UTILISÉE", text);
        Assert.Contains("255.255.255.252", text);
        Assert.Contains("Pare-feu", text);
        Assert.Contains("srv01", text);

        var ipCells = network.Descendants(main + "c")
            .Where(c => c.Attribute("r")?.Value is "A3" or "A4")
            .ToArray();
        Assert.Equal(2, ipCells.Length);
    }

    [Fact]
    public void VlanWithoutSubnetStaysOnIndexWithoutCreatingNetworkSheet()
    {
        var db = Example();
        db.Sites[0].Vlans.Add(new Vlan { Vid = 121, Name = "SANS-RESEAU", Subnet = null });

        using var stream = new MemoryStream();
        ExcelExchange.Export(db, stream);
        stream.Position = 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var workbook = ReadXml(archive, "xl/workbook.xml");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal(2, workbook.Descendants(main + "sheet").Count());

        var index = ReadXml(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("SANS-RESEAU", index.Descendants(main + "t").Select(x => x.Value));
        Assert.Single(index.Descendants(main + "hyperlink"));
    }

    [Fact]
    public void NetworkLargerThanExcelWorksheetIsRejectedBeforeWriting()
    {
        var db = Example("10.0.0.0/11");
        using var stream = new MemoryStream();

        var error = Assert.Throws<InvalidOperationException>(() => ExcelExchange.Export(db, stream));

        Assert.Contains("Excel ne peut pas", error.Message);
        Assert.Equal(0, stream.Length);
    }

    private static XDocument ReadXml(ZipArchive archive, string path)
    {
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return XDocument.Parse(reader.ReadToEnd());
    }
}

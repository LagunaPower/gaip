using System.IO.Compression;
using System.Text;
using System.Xml;

namespace GAIP.Core;

public static class ExcelExchange
{
    private const string MainSheetName = "Sites et VLAN";
    private const string MulticastSheetName = "Multicast";
    private const int ExcelMaxRows = 1_048_576;

    private sealed record NetworkSheet(Site Site, Vlan Vlan, string Name);

    public static void Export(Database db, Stream output)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite) throw new ArgumentException("Le flux Excel doit être accessible en écriture.", nameof(output));

        var errors = ModelValidator.Validate(db);
        if (errors.Count > 0) throw new ValidationException(errors);

        var networks = BuildNetworkSheets(db);
        var includeMulticast = db.MulticastGroups.Count > 0;
        foreach (var item in networks)
        {
            var network = Ipv4Network.Parse(item.Vlan.Subnet!.Cidr);
            // Row 1 = title, row 2 = headers, usable addresses start on row 3.
            if (network.UsableCount > ExcelMaxRows - 2)
                throw new InvalidOperationException(
                    $"Le réseau {network} du VLAN {item.Vlan.Vid} ({item.Site.Code}) contient {network.UsableCount:N0} adresses utilisables. " +
                    "Excel ne peut pas les contenir dans un seul onglet.");
        }

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var sheetCount = networks.Count + 1 + (includeMulticast ? 1 : 0);
        WriteContentTypes(archive, sheetCount);
        WriteRootRelationships(archive);
        WriteWorkbook(archive, networks, includeMulticast);
        WriteWorkbookRelationships(archive, sheetCount);
        WriteStyles(archive);
        WriteMainWorksheet(archive, db, networks);
        if (includeMulticast) WriteMulticastWorksheet(archive, 2, db);
        var firstNetworkSheet = includeMulticast ? 3 : 2;
        for (var i = 0; i < networks.Count; i++)
            WriteNetworkWorksheet(archive, firstNetworkSheet + i, networks[i]);
    }

    private static List<NetworkSheet> BuildNetworkSheets(Database db)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { MainSheetName };
        var result = new List<NetworkSheet>();
        foreach (var site in SiteOrdering.Ordered(db.Sites))
        {
            foreach (var vlan in site.Vlans.Where(v => v.Subnet is not null).OrderBy(v => v.Vid))
            {
                var baseName = SanitizeSheetName($"{site.Code}-VLAN{vlan.Vid}");
                var name = baseName;
                for (var suffix = 2; !used.Add(name); suffix++)
                {
                    var suffixText = $"-{suffix}";
                    var prefixLength = Math.Max(1, 31 - suffixText.Length);
                    name = baseName[..Math.Min(prefixLength, baseName.Length)] + suffixText;
                }
                result.Add(new(site, vlan, name));
            }
        }
        return result;
    }

    private static string SanitizeSheetName(string value)
    {
        var invalid = new HashSet<char>(['[', ']', ':', '*', '?', '/', '\\']);
        var cleaned = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim().Trim('\'');
        if (cleaned.Length == 0) cleaned = "Réseau";
        return cleaned[..Math.Min(31, cleaned.Length)];
    }

    private static void WriteContentTypes(ZipArchive archive, int sheetCount)
    {
        WriteXml(archive, "[Content_Types].xml", writer =>
        {
            writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
            WriteType("Default", "Extension", "rels", "ContentType", "application/vnd.openxmlformats-package.relationships+xml");
            WriteType("Default", "Extension", "xml", "ContentType", "application/xml");
            WriteType("Override", "PartName", "/xl/workbook.xml", "ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
            WriteType("Override", "PartName", "/xl/styles.xml", "ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
            for (var i = 1; i <= sheetCount; i++)
                WriteType("Override", "PartName", $"/xl/worksheets/sheet{i}.xml", "ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
            writer.WriteEndElement();

            void WriteType(string element, string attr1, string value1, string attr2, string value2)
            {
                writer.WriteStartElement(element);
                writer.WriteAttributeString(attr1, value1);
                writer.WriteAttributeString(attr2, value2);
                writer.WriteEndElement();
            }
        });
    }

    private static void WriteRootRelationships(ZipArchive archive)
    {
        WriteXml(archive, "_rels/.rels", writer =>
        {
            writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", "rId1");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
            writer.WriteAttributeString("Target", "xl/workbook.xml");
            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    private static void WriteWorkbook(ZipArchive archive, IReadOnlyList<NetworkSheet> networks, bool includeMulticast)
    {
        WriteXml(archive, "xl/workbook.xml", writer =>
        {
            writer.WriteStartElement("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
            writer.WriteAttributeString("xmlns", "r", null, "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            writer.WriteStartElement("sheets");
            WriteSheet(MainSheetName, 1, "rId1");
            var offset = 1;
            if (includeMulticast)
            {
                WriteSheet(MulticastSheetName, 2, "rId2");
                offset = 2;
            }
            for (var i = 0; i < networks.Count; i++)
                WriteSheet(networks[i].Name, i + offset + 1, $"rId{i + offset + 1}");
            writer.WriteEndElement();
            writer.WriteEndElement();

            void WriteSheet(string name, int id, string relationship)
            {
                writer.WriteStartElement("sheet");
                writer.WriteAttributeString("name", name);
                writer.WriteAttributeString("sheetId", id.ToString());
                writer.WriteAttributeString("r", "id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships", relationship);
                writer.WriteEndElement();
            }
        });
    }

    private static void WriteWorkbookRelationships(ZipArchive archive, int sheetCount)
    {
        WriteXml(archive, "xl/_rels/workbook.xml.rels", writer =>
        {
            writer.WriteStartElement("Relationships", "http://schemas.openxmlformats.org/package/2006/relationships");
            for (var i = 1; i <= sheetCount; i++)
            {
                writer.WriteStartElement("Relationship");
                writer.WriteAttributeString("Id", $"rId{i}");
                writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet");
                writer.WriteAttributeString("Target", $"worksheets/sheet{i}.xml");
                writer.WriteEndElement();
            }
            writer.WriteStartElement("Relationship");
            writer.WriteAttributeString("Id", $"rId{sheetCount + 1}");
            writer.WriteAttributeString("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles");
            writer.WriteAttributeString("Target", "styles.xml");
            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    private static void WriteStyles(ZipArchive archive)
    {
        WriteXml(archive, "xl/styles.xml", writer =>
        {
            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            writer.WriteStartElement("styleSheet", ns);

            writer.WriteStartElement("fonts");
            writer.WriteAttributeString("count", "7");
            WriteFont(writer, 11, false, null, false);
            WriteFont(writer, 16, true, "1F4E78", false);
            WriteFont(writer, 11, true, null, false);
            WriteFont(writer, 11, true, "FFFFFF", false);
            WriteFont(writer, 11, false, "0563C1", true);
            WriteFont(writer, 11, true, "008000", false);
            WriteFont(writer, 11, true, "C00000", false);
            writer.WriteEndElement();

            writer.WriteStartElement("fills");
            writer.WriteAttributeString("count", "5");
            WriteFill(writer, null);
            writer.WriteStartElement("fill"); writer.WriteStartElement("patternFill"); writer.WriteAttributeString("patternType", "gray125"); writer.WriteEndElement(); writer.WriteEndElement();
            WriteFill(writer, "1F4E78");
            WriteFill(writer, "D9EAF7");
            WriteFill(writer, "F3F6F9");
            writer.WriteEndElement();

            writer.WriteStartElement("borders");
            writer.WriteAttributeString("count", "2");
            writer.WriteStartElement("border");
            foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" }) { writer.WriteStartElement(side); writer.WriteEndElement(); }
            writer.WriteEndElement();
            writer.WriteStartElement("border");
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                writer.WriteStartElement(side);
                writer.WriteAttributeString("style", "thin");
                writer.WriteStartElement("color"); writer.WriteAttributeString("rgb", "FFD9E1F2"); writer.WriteEndElement();
                writer.WriteEndElement();
            }
            writer.WriteStartElement("diagonal"); writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();

            writer.WriteStartElement("cellStyleXfs"); writer.WriteAttributeString("count", "1");
            WriteXf(writer, 0, 0, 0, false, false);
            writer.WriteEndElement();

            writer.WriteStartElement("cellXfs"); writer.WriteAttributeString("count", "10");
            WriteXf(writer, 0, 0, 0, false, false); // 0 default
            WriteXf(writer, 1, 0, 0, true, false);  // 1 title
            WriteXf(writer, 2, 3, 1, true, false);  // 2 label
            WriteXf(writer, 3, 2, 1, true, true);   // 3 header
            WriteXf(writer, 0, 0, 1, false, false); // 4 data
            WriteXf(writer, 0, 4, 1, false, false); // 5 alternate
            WriteXf(writer, 4, 0, 1, false, false); // 6 hyperlink
            WriteXf(writer, 0, 0, 1, false, false); // 7 meta value
            WriteXf(writer, 5, 0, 1, true, true);   // 8 multicast présent
            WriteXf(writer, 6, 0, 1, true, true);   // 9 multicast absent
            writer.WriteEndElement();

            writer.WriteStartElement("cellStyles"); writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("cellStyle"); writer.WriteAttributeString("name", "Normal"); writer.WriteAttributeString("xfId", "0"); writer.WriteAttributeString("builtinId", "0"); writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        });
    }

    private static void WriteFont(XmlWriter writer, int size, bool bold, string? rgb, bool underline)
    {
        writer.WriteStartElement("font");
        if (bold) { writer.WriteStartElement("b"); writer.WriteEndElement(); }
        if (underline) { writer.WriteStartElement("u"); writer.WriteEndElement(); }
        writer.WriteStartElement("sz"); writer.WriteAttributeString("val", size.ToString()); writer.WriteEndElement();
        if (rgb is not null) { writer.WriteStartElement("color"); writer.WriteAttributeString("rgb", "FF" + rgb); writer.WriteEndElement(); }
        writer.WriteStartElement("name"); writer.WriteAttributeString("val", "Calibri"); writer.WriteEndElement();
        writer.WriteStartElement("family"); writer.WriteAttributeString("val", "2"); writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFill(XmlWriter writer, string? rgb)
    {
        writer.WriteStartElement("fill");
        writer.WriteStartElement("patternFill");
        writer.WriteAttributeString("patternType", rgb is null ? "none" : "solid");
        if (rgb is not null)
        {
            writer.WriteStartElement("fgColor"); writer.WriteAttributeString("rgb", "FF" + rgb); writer.WriteEndElement();
            writer.WriteStartElement("bgColor"); writer.WriteAttributeString("indexed", "64"); writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteXf(XmlWriter writer, int fontId, int fillId, int borderId, bool applyFont, bool centered)
    {
        writer.WriteStartElement("xf");
        writer.WriteAttributeString("numFmtId", "0");
        writer.WriteAttributeString("fontId", fontId.ToString());
        writer.WriteAttributeString("fillId", fillId.ToString());
        writer.WriteAttributeString("borderId", borderId.ToString());
        writer.WriteAttributeString("xfId", "0");
        if (applyFont) writer.WriteAttributeString("applyFont", "1");
        if (fillId != 0) writer.WriteAttributeString("applyFill", "1");
        if (borderId != 0) writer.WriteAttributeString("applyBorder", "1");
        if (centered)
        {
            writer.WriteAttributeString("applyAlignment", "1");
            writer.WriteStartElement("alignment");
            writer.WriteAttributeString("horizontal", "center");
            writer.WriteAttributeString("vertical", "center");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteMainWorksheet(ZipArchive archive, Database db, IReadOnlyList<NetworkSheet> networks)
    {
        var byVlan = networks.ToDictionary(n => n.Vlan.Id);
        var ordered = SiteOrdering.Ordered(db.Sites)
            .SelectMany(site => site.Vlans.OrderBy(v => v.Vid).Select(vlan => (site, vlan)))
            .ToList();
        var lastRow = Math.Max(3, ordered.Count + 3);

        WriteXml(archive, "xl/worksheets/sheet1.xml", writer =>
        {
            StartWorksheet(writer, $"A1:J{lastRow}", freezeRows: 3);
            WriteColumns(writer, [14d, 24d, 34d, 10d, 24d, 38d, 20d, 18d, 14d, 14d]);

            writer.WriteStartElement("sheetData");
            writer.WriteStartElement("row"); writer.WriteAttributeString("r", "1"); writer.WriteAttributeString("ht", "24"); writer.WriteAttributeString("customHeight", "1");
            WriteTextCell(writer, "A1", "G@IP — Sites et VLAN", 1);
            writer.WriteEndElement();

            writer.WriteStartElement("row"); writer.WriteAttributeString("r", "3");
            var headers = new[] { "Code site", "Site", "Description site", "VLAN", "Nom VLAN", "Description VLAN", "Réseau", "Passerelle", "IP utilisées", "IP libres" };
            for (var col = 0; col < headers.Length; col++) WriteTextCell(writer, CellRef(col + 1, 3), headers[col], 3);
            writer.WriteEndElement();

            var row = 4;
            foreach (var (site, vlan) in ordered)
            {
                writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString());
                WriteTextCell(writer, $"A{row}", site.Code, 4);
                WriteTextCell(writer, $"B{row}", site.Name, 4);
                WriteTextCell(writer, $"C{row}", site.Description, 4);
                WriteNumberCell(writer, $"D{row}", (ulong)vlan.Vid, byVlan.ContainsKey(vlan.Id) ? 6 : 4);
                WriteTextCell(writer, $"E{row}", vlan.Name, 4);
                WriteTextCell(writer, $"F{row}", vlan.Description, 4);
                WriteTextCell(writer, $"G{row}", vlan.Subnet?.Cidr ?? "", 4);
                WriteTextCell(writer, $"H{row}", vlan.Subnet?.Gateway?.Address ?? "", 4);
                WriteNumberCell(writer, $"I{row}", (ulong)(vlan.Subnet is null ? 0 : Queries.UsedCount(vlan.Subnet)), 4);
                WriteNumberCell(writer, $"J{row}", vlan.Subnet is null ? 0 : Queries.FreeCount(vlan.Subnet), 4);
                writer.WriteEndElement();
                row++;
            }
            writer.WriteEndElement();

            writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A3:J{lastRow}"); writer.WriteEndElement();
            writer.WriteStartElement("mergeCells"); writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("mergeCell"); writer.WriteAttributeString("ref", "A1:J1"); writer.WriteEndElement();
            writer.WriteEndElement();
            if (networks.Count > 0)
            {
                writer.WriteStartElement("hyperlinks");
                row = 4;
                foreach (var (_, vlan) in ordered)
                {
                    if (byVlan.TryGetValue(vlan.Id, out var networkSheet))
                    {
                        writer.WriteStartElement("hyperlink");
                        writer.WriteAttributeString("ref", $"D{row}");
                        writer.WriteAttributeString("location", $"'{networkSheet.Name.Replace("'", "''")}'!A1");
                        writer.WriteAttributeString("tooltip", $"Ouvrir {networkSheet.Name}");
                        writer.WriteEndElement();
                    }
                    row++;
                }
                writer.WriteEndElement();
            }
            EndWorksheet(writer);
        });
    }

    private static void WriteMulticastWorksheet(ZipArchive archive, int sheetIndex, Database db)
    {
        var sites = SiteOrdering.Ordered(db.Sites).ToArray();
        var siteVlans = sites.ToDictionary(
            site => site.Id,
            site => site.Vlans.Select(vlan => vlan.Id).ToHashSet());
        var vlanLabels = db.Sites
            .SelectMany(site => site.Vlans.Select(vlan => (vlan.Id, Label: $"{site.Code}/{vlan.Vid}")))
            .ToDictionary(item => item.Id, item => item.Label);
        var sourceLabels = db.Sites
            .SelectMany(site => site.Vlans.SelectMany(vlan => (vlan.Subnet?.Addresses ?? [])
                .Select(ip => ip)))
            .ToDictionary(
                ip => ip.Address,
                ip => string.IsNullOrWhiteSpace(ip.Hostname) ? ip.Address : $"{ip.Address} — {ip.Hostname}",
                StringComparer.Ordinal);

        var dataRows = db.MulticastGroups.Sum(group => Math.Max(1, group.Flows.Count));
        var lastRow = Math.Max(3, dataRows + 3);
        var lastColumn = 8 + sites.Length;
        var lastColumnName = CellRef(lastColumn, 1);
        lastColumnName = lastColumnName[..^1];

        WriteXml(archive, $"xl/worksheets/sheet{sheetIndex}.xml", writer =>
        {
            StartWorksheet(writer, $"A1:{lastColumnName}{lastRow}", freezeRows: 3);
            var widths = new List<double> { 18d, 24d, 34d, 10d, 28d, 38d, 38d, 34d };
            widths.AddRange(Enumerable.Repeat(14d, sites.Length));
            WriteColumns(writer, widths);

            writer.WriteStartElement("sheetData");

            writer.WriteStartElement("row"); writer.WriteAttributeString("r", "1"); writer.WriteAttributeString("ht", "24"); writer.WriteAttributeString("customHeight", "1");
            WriteTextCell(writer, "A1", "G@IP — Multicast", 1);
            writer.WriteEndElement();

            writer.WriteStartElement("row"); writer.WriteAttributeString("r", "3");
            var headers = new[] { "Adresse multicast", "Groupe", "Description groupe", "Port", "Contenu", "Description flux", "Sources", "VLAN" };
            for (var col = 0; col < headers.Length; col++) WriteTextCell(writer, CellRef(col + 1, 3), headers[col], 3);
            for (var index = 0; index < sites.Length; index++)
                WriteTextCell(writer, CellRef(9 + index, 3), sites[index].Code, 3);
            writer.WriteEndElement();

            var row = 4;
            foreach (var group in db.MulticastGroups.OrderBy(group => Ipv4Network.ParseAddress(group.Address)))
            {
                if (group.Flows.Count == 0)
                {
                    writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString());
                    WriteTextCell(writer, $"A{row}", group.Address, 4);
                    WriteTextCell(writer, $"B{row}", group.Name, 4);
                    WriteTextCell(writer, $"C{row}", group.Description, 4);
                    for (var col = 4; col <= lastColumn; col++) WriteTextCell(writer, CellRef(col, row), "", 4);
                    writer.WriteEndElement();
                    row++;
                    continue;
                }

                foreach (var flow in group.Flows.OrderBy(flow => flow.Port))
                {
                    var style = row % 2 == 0 ? 5 : 4;
                    writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString());
                    WriteTextCell(writer, $"A{row}", group.Address, style);
                    WriteTextCell(writer, $"B{row}", group.Name, style);
                    WriteTextCell(writer, $"C{row}", group.Description, style);
                    WriteNumberCell(writer, $"D{row}", (ulong)flow.Port, style);
                    WriteTextCell(writer, $"E{row}", flow.Content, style);
                    WriteTextCell(writer, $"F{row}", flow.Description, style);
                    WriteTextCell(writer, $"G{row}", string.Join(", ", flow.Sources.Select(source =>
                        sourceLabels.TryGetValue(source, out var label) ? label : source)), style);
                    WriteTextCell(writer, $"H{row}", string.Join(", ", flow.VlanIds.Select(id =>
                        vlanLabels.TryGetValue(id, out var label) ? label : id.ToString())), style);

                    for (var index = 0; index < sites.Length; index++)
                    {
                        var used = flow.VlanIds.Any(siteVlans[sites[index].Id].Contains);
                        WriteTextCell(writer, CellRef(9 + index, row), used ? "✔" : "✖", used ? 8 : 9);
                    }

                    writer.WriteEndElement();
                    row++;
                }
            }

            writer.WriteEndElement();
            writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A3:{lastColumnName}{lastRow}"); writer.WriteEndElement();
            writer.WriteStartElement("mergeCells"); writer.WriteAttributeString("count", "1");
            writer.WriteStartElement("mergeCell"); writer.WriteAttributeString("ref", $"A1:{lastColumnName}1"); writer.WriteEndElement();
            writer.WriteEndElement();
            EndWorksheet(writer);
        });
    }

    private static void WriteNetworkWorksheet(ZipArchive archive, int sheetIndex, NetworkSheet item)
    {
        var subnet = item.Vlan.Subnet!;
        var network = Ipv4Network.Parse(subnet.Cidr);
        var lastRow = checked((int)network.UsableCount + 2);
        var stored = subnet.Addresses.ToDictionary(a => Ipv4Network.ParseAddress(a.Address));
        uint? gateway = subnet.Gateway is null ? null : Ipv4Network.ParseAddress(subnet.Gateway.Address);

        WriteXml(archive, $"xl/worksheets/sheet{sheetIndex}.xml", writer =>
        {
            StartWorksheet(writer, $"A1:G{Math.Max(lastRow, 14)}", freezeRows: 2);
            WriteColumns(writer, [18d, 15d, 28d, 48d, 4d, 24d, 42d]);

            writer.WriteStartElement("sheetData");

            writer.WriteStartElement("row"); writer.WriteAttributeString("r", "1"); writer.WriteAttributeString("ht", "24"); writer.WriteAttributeString("customHeight", "1");
            WriteTextCell(writer, "A1", $"{item.Site.Code} / VLAN {item.Vlan.Vid} — {item.Vlan.Name}", 1);
            WriteTextCell(writer, "F1", "Informations réseau", 1);
            writer.WriteEndElement();

            writer.WriteStartElement("row"); writer.WriteAttributeString("r", "2");
            WriteTextCell(writer, "A2", "Adresse IP", 3);
            WriteTextCell(writer, "B2", "État", 3);
            WriteTextCell(writer, "C2", "Hostname", 3);
            WriteTextCell(writer, "D2", "Description", 3);
            WriteTextCell(writer, "F2", "Site", 2);
            WriteTextCell(writer, "G2", $"{item.Site.Code} — {item.Site.Name}", 7);
            writer.WriteEndElement();

            var meta = new (string Label, string Value)[]
            {
                ("VLAN", $"{item.Vlan.Vid} — {item.Vlan.Name}"),
                ("Description", item.Vlan.Description),
                ("CIDR", network.ToString()),
                ("Masque", network.DottedMask),
                ("Adresse réseau", Ipv4Network.Format(network.Network)),
                ("Broadcast", Ipv4Network.Format(network.Broadcast)),
                ("Première IP utilisable", network.UsableCount == 0 ? "—" : Ipv4Network.Format(network.First)),
                ("Dernière IP utilisable", network.UsableCount == 0 ? "—" : Ipv4Network.Format(network.Last)),
                ("Passerelle", subnet.Gateway?.Address ?? "—"),
                ("Commentaire passerelle", subnet.Gateway?.Comment ?? ""),
                ("IP utilisées", Queries.UsedCount(subnet).ToString()),
                ("IP libres", Queries.FreeCount(subnet).ToString())
            };

            var finalRow = Math.Max(lastRow, meta.Length + 2);
            for (var row = 3; row <= finalRow; row++)
            {
                writer.WriteStartElement("row"); writer.WriteAttributeString("r", row.ToString());

                var offset = row - 3;
                if ((ulong)offset < network.UsableCount)
                {
                    var ip = network.First + (uint)offset;
                    var style = row % 2 == 0 ? 5 : 4;
                    WriteTextCell(writer, $"A{row}", Ipv4Network.Format(ip), style);
                    if (gateway == ip)
                    {
                        WriteTextCell(writer, $"B{row}", "PASSERELLE", style);
                        WriteTextCell(writer, $"C{row}", "", style);
                        WriteTextCell(writer, $"D{row}", subnet.Gateway!.Comment, style);
                    }
                    else if (stored.TryGetValue(ip, out var address))
                    {
                        WriteTextCell(writer, $"B{row}", "UTILISÉE", style);
                        WriteTextCell(writer, $"C{row}", address.Hostname, style);
                        WriteTextCell(writer, $"D{row}", address.Description, style);
                    }
                    else
                    {
                        WriteTextCell(writer, $"B{row}", "LIBRE", style);
                        WriteTextCell(writer, $"C{row}", "", style);
                        WriteTextCell(writer, $"D{row}", "", style);
                    }
                }

                if (offset < meta.Length)
                {
                    WriteTextCell(writer, $"F{row}", meta[offset].Label, 2);
                    WriteTextCell(writer, $"G{row}", meta[offset].Value, 7);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            if (network.UsableCount > 0)
            {
                writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A2:D{lastRow}"); writer.WriteEndElement();
            }
            writer.WriteStartElement("mergeCells"); writer.WriteAttributeString("count", "2");
            writer.WriteStartElement("mergeCell"); writer.WriteAttributeString("ref", "A1:D1"); writer.WriteEndElement();
            writer.WriteStartElement("mergeCell"); writer.WriteAttributeString("ref", "F1:G1"); writer.WriteEndElement();
            writer.WriteEndElement();
            EndWorksheet(writer);
        });
    }

    private static void StartWorksheet(XmlWriter writer, string dimension, int freezeRows)
    {
        writer.WriteStartElement("worksheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        writer.WriteStartElement("dimension"); writer.WriteAttributeString("ref", dimension); writer.WriteEndElement();
        writer.WriteStartElement("sheetViews");
        writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane");
        writer.WriteAttributeString("ySplit", freezeRows.ToString());
        writer.WriteAttributeString("topLeftCell", $"A{freezeRows + 1}");
        writer.WriteAttributeString("activePane", "bottomLeft");
        writer.WriteAttributeString("state", "frozen");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteStartElement("sheetFormatPr"); writer.WriteAttributeString("defaultRowHeight", "15"); writer.WriteEndElement();
    }

    private static void EndWorksheet(XmlWriter writer)
    {
        writer.WriteStartElement("pageMargins");
        writer.WriteAttributeString("left", "0.4");
        writer.WriteAttributeString("right", "0.4");
        writer.WriteAttributeString("top", "0.6");
        writer.WriteAttributeString("bottom", "0.6");
        writer.WriteAttributeString("header", "0.2");
        writer.WriteAttributeString("footer", "0.2");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteColumns(XmlWriter writer, IReadOnlyList<double> widths)
    {
        writer.WriteStartElement("cols");
        for (var i = 0; i < widths.Count; i++)
        {
            writer.WriteStartElement("col");
            writer.WriteAttributeString("min", (i + 1).ToString());
            writer.WriteAttributeString("max", (i + 1).ToString());
            writer.WriteAttributeString("width", widths[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteAttributeString("customWidth", "1");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static void WriteTextCell(XmlWriter writer, string reference, string? value, int style)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("s", style.ToString());
        writer.WriteAttributeString("t", "inlineStr");
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        writer.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
        writer.WriteString(value ?? "");
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteNumberCell(XmlWriter writer, string reference, ulong value, int style)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", reference);
        writer.WriteAttributeString("s", style.ToString());
        writer.WriteStartElement("v");
        writer.WriteString(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static string CellRef(int column, int row)
    {
        var letters = "";
        for (var n = column; n > 0; n = (n - 1) / 26)
            letters = (char)('A' + ((n - 1) % 26)) + letters;
        return letters + row;
    }

    private static void WriteXml(ZipArchive archive, string path, Action<XmlWriter> write)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            CloseOutput = false,
            Indent = false,
            OmitXmlDeclaration = false
        });
        write(writer);
    }
}

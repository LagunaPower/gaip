using System.Text;

namespace GAIP.Core;

public enum CsvKind { Vlans, Addresses, Multicast }
public sealed record ImportResult(Database? Data, IReadOnlyList<string> Errors, int RowCount);

public static class CsvExchange
{
    public const string VlanHeader = "site_code;site_name;vid;vlan_name;vlan_description;cidr;gateway;gateway_comment";
    public const string AddressHeader = "site_code;vid;ip;hostname;description";
    public const string MulticastHeader = "multicast_address;group_name;group_description;port;content;flow_description;sources;vlans";

    public static ImportResult Import(Database source, string text, CsvKind kind, char separator = ';')
    {
        // Manual clone keeps Core independent from serialization and storage libraries.
        var db = Copy(source);
        var errors = new List<string>();
        List<List<string>> rows;
        try { rows = Parse(text, separator); }
        catch (FormatException ex) { return new(null, [ex.Message], 0); }
        if (rows.Count == 0) return new(null, ["CSV vide."], 0);
        var header = Header(kind).Split(';');
        if (!rows[0].SequenceEqual(header)) return new(null, [$"En-tête attendu : {string.Join(separator, header)}"], 0);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            var label = $"Enregistrement {index + 1}";
            if (row.Count != header.Length) { errors.Add($"{label} : {header.Length} colonnes attendues, {row.Count} reçues."); continue; }
            try
            {
                if (kind == CsvKind.Multicast)
                {
                    var address = Ipv4Network.Format(Ipv4Network.ParseAddress(row[0].Trim()));
                    if (!int.TryParse(row[3], out var port)) throw new FormatException("Port UDP invalide.");
                    var key = $"{address}:{port}";
                    if (!duplicates.Add(key)) throw new FormatException($"Entrée répétée dans le fichier : {key}.");

                    var group = db.MulticastGroups.FirstOrDefault(g => g.Address == address);
                    if (group is null)
                    {
                        group = new MulticastGroup { Address = address };
                        db.MulticastGroups.Add(group);
                    }
                    group.Name = row[1];
                    group.Description = row[2];

                    var sources = SplitMulti(row[6]).Select(source =>
                        Ipv4Network.Format(Ipv4Network.ParseAddress(source))).ToList();
                    var vlanIds = new List<Guid>();
                    foreach (var reference in SplitMulti(row[7]))
                    {
                        var slash = reference.LastIndexOf('/');
                        if (slash <= 0 || !int.TryParse(reference[(slash + 1)..], out var vid))
                            throw new FormatException($"Référence VLAN invalide : {reference}. Format attendu SITE/VID.");
                        var code = reference[..slash];
                        var site = db.Sites.FirstOrDefault(site =>
                            string.Equals(site.Code, code, StringComparison.OrdinalIgnoreCase))
                            ?? throw new FormatException($"Site inconnu : {code}. Importez d'abord les VLAN.");
                        var vlan = site.Vlans.FirstOrDefault(vlan => vlan.Vid == vid)
                            ?? throw new FormatException($"VLAN inconnu : {code}/{vid}. Importez d'abord les VLAN.");
                        vlanIds.Add(vlan.Id);
                    }

                    var flow = group.Flows.FirstOrDefault(flow => flow.Port == port);
                    if (flow is null) { flow = new MulticastFlow { Port = port }; group.Flows.Add(flow); }
                    flow.Content = row[4];
                    flow.Description = row[5];
                    flow.Sources = sources;
                    flow.VlanIds = vlanIds;
                }
                else
                {
                    var code = row[0].Trim();
                    var vidIndex = kind == CsvKind.Vlans ? 2 : 1;
                    if (!int.TryParse(row[vidIndex], out var vid)) throw new FormatException("VID invalide.");
                    var key = kind == CsvKind.Vlans ? $"{code}/{vid}" : row[2];
                    if (!duplicates.Add(key)) throw new FormatException($"Entrée répétée dans le fichier : {key}.");
                    var site = db.Sites.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
                    if (kind == CsvKind.Vlans)
                    {
                        if (site is null) { site = new Site { Code = code, Name = row[1] }; db.Sites.Add(site); }
                        var vlan = site.Vlans.FirstOrDefault(v => v.Vid == vid);
                        if (vlan is null) { vlan = new Vlan { Vid = vid }; site.Vlans.Add(vlan); }
                        vlan.Name = row[3]; vlan.Description = row[4];
                        if (string.IsNullOrWhiteSpace(row[5]))
                        {
                            if (vlan.Subnet?.Addresses.Count > 0) throw new FormatException("Impossible de retirer un sous-réseau contenant des IP.");
                            if (!string.IsNullOrWhiteSpace(row[6]) || !string.IsNullOrWhiteSpace(row[7])) throw new FormatException("Passerelle sans sous-réseau.");
                            vlan.Subnet = null;
                        }
                        else
                        {
                            var network = Ipv4Network.Parse(row[5].Trim());
                            vlan.Subnet ??= new();
                            vlan.Subnet.Cidr = network.ToString();
                            if (string.IsNullOrWhiteSpace(row[6]) && !string.IsNullOrWhiteSpace(row[7])) throw new FormatException("Commentaire de passerelle sans adresse.");
                            vlan.Subnet.Gateway = string.IsNullOrWhiteSpace(row[6]) ? null : new Gateway { Address = row[6].Trim(), Comment = row[7] };
                        }
                    }
                    else
                    {
                        var vlan = site?.Vlans.FirstOrDefault(v => v.Vid == vid) ?? throw new FormatException($"VLAN inconnu : {code}/{vid}. Importez d'abord les VLAN.");
                        var subnet = vlan.Subnet ?? throw new FormatException("Ce VLAN n'a pas de sous-réseau.");
                        var ip = row[2].Trim();
                        Ipv4Network.ParseAddress(ip);
                        var address = subnet.Addresses.FirstOrDefault(a => a.Address == ip);
                        if (address is null) { address = new IpAddress { Address = ip }; subnet.Addresses.Add(address); }
                        address.Hostname = row[3]; address.Description = row[4];
                    }
                }
            }
            catch (FormatException ex) { errors.Add($"{label} : {ex.Message}"); }
        }
        errors.AddRange(ModelValidator.Validate(db));
        if (errors.Count == 0) errors.AddRange(ModelValidator.ValidateTransition(source, db));
        return new(errors.Count == 0 ? db : null, errors, rows.Count - 1);
    }

    public static string Export(Database db, CsvKind kind, char separator = ';', Guid? vlanId = null)
    {
        var builder = new StringBuilder();
        Write(Header(kind).Split(';'));
        if (kind == CsvKind.Multicast)
        {
            var vlanLabels = db.Sites.SelectMany(site => site.Vlans.Select(vlan =>
                (vlan.Id, Label: $"{site.Code}/{vlan.Vid}"))).ToDictionary(item => item.Id, item => item.Label);
            foreach (var group in db.MulticastGroups.OrderBy(group => Ipv4Network.ParseAddress(group.Address)))
                foreach (var flow in group.Flows.OrderBy(flow => flow.Port))
                    Write([group.Address, group.Name, group.Description, flow.Port.ToString(), flow.Content,
                        flow.Description, string.Join('|', flow.Sources),
                        string.Join('|', flow.VlanIds.Select(id => vlanLabels.TryGetValue(id, out var label) ? label : id.ToString()))]);
        }
        else
        {
            foreach (var site in db.Sites.OrderBy(s => s.Code))
                foreach (var vlan in site.Vlans.Where(v => vlanId is null || v.Id == vlanId).OrderBy(v => v.Vid))
                {
                    if (kind == CsvKind.Vlans)
                        Write([site.Code, site.Name, vlan.Vid.ToString(), vlan.Name, vlan.Description, vlan.Subnet?.Cidr ?? "", vlan.Subnet?.Gateway?.Address ?? "", vlan.Subnet?.Gateway?.Comment ?? ""]);
                    else if (vlan.Subnet is { } subnet)
                        foreach (var ip in subnet.Addresses.OrderBy(a => Ipv4Network.ParseAddress(a.Address)))
                            Write([site.Code, vlan.Vid.ToString(), ip.Address, ip.Hostname, ip.Description]);
                }
        }
        return builder.ToString();
        void Write(IEnumerable<string> fields) => builder.AppendLine(string.Join(separator, fields.Select(s =>
            s.Contains(separator) || s.Contains('"') || s.Contains('\n') || s.Contains('\r') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s)));
    }

    private static string Header(CsvKind kind) => kind switch
    {
        CsvKind.Vlans => VlanHeader,
        CsvKind.Addresses => AddressHeader,
        CsvKind.Multicast => MulticastHeader,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static IEnumerable<string> SplitMulti(string value) =>
        value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static List<List<string>> Parse(string text, char separator)
    {
        if (separator is '"' or '\n' or '\r' or '\0') throw new FormatException("Séparateur CSV invalide.");
        text = text.TrimStart('\uFEFF');
        var rows = new List<List<string>>();
        var row = new List<string>();
        var value = new StringBuilder();
        bool quoted = false, closed = false, start = true;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else value.Append(c);
                continue;
            }
            if (c == '"')
            {
                if (!start || closed) throw new FormatException($"CSV : guillemet inattendu près de l'enregistrement {rows.Count + 1}.");
                quoted = true; start = false; continue;
            }
            if (c == separator) { row.Add(value.ToString()); value.Clear(); start = true; closed = false; continue; }
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(value.ToString());
                if (row.Count > 1 || row[0].Length > 0) rows.Add(row);
                row = []; value.Clear(); start = true; closed = false; continue;
            }
            if (closed) throw new FormatException("CSV : caractère après un guillemet fermant.");
            value.Append(c); start = false;
        }
        if (quoted) throw new FormatException("CSV : guillemet non fermé.");
        if (row.Count > 0 || value.Length > 0 || closed) { row.Add(value.ToString()); rows.Add(row); }
        return rows;
    }

    private static Database Copy(Database source) => new()
    {
        SchemaVersion = source.SchemaVersion, Revision = source.Revision, LastModified = source.LastModified,
        LastModifiedBy = source.LastModifiedBy, LastModifiedFrom = source.LastModifiedFrom,
        Sites = source.Sites.Select(s => new Site
        {
            Id = s.Id, Code = s.Code, Name = s.Name, Description = s.Description, DisplayOrder = s.DisplayOrder,
            Vlans = s.Vlans.Select(v => new Vlan
            {
                Id = v.Id, Vid = v.Vid, Name = v.Name, Description = v.Description,
                Subnet = v.Subnet is not { } n ? null : new Subnet
                {
                    Cidr = n.Cidr, Gateway = n.Gateway is not { } g ? null : new Gateway { Address = g.Address, Comment = g.Comment },
                    Addresses = n.Addresses.Select(a => new IpAddress { Address = a.Address, Hostname = a.Hostname, Description = a.Description }).ToList()
                }
            }).ToList()
        }).ToList(),
        MulticastGroups = source.MulticastGroups.Select(group => new MulticastGroup
        {
            Address = group.Address, Name = group.Name, Description = group.Description,
            Flows = group.Flows.Select(flow => new MulticastFlow
            {
                Port = flow.Port, Content = flow.Content, Description = flow.Description,
                Sources = [.. flow.Sources], VlanIds = [.. flow.VlanIds]
            }).ToList()
        }).ToList()
    };
}

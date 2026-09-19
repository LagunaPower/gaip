namespace GAIP.Core;

public static class ModelValidator
{
    public static IReadOnlyList<string> Validate(Database db)
    {
        var errors = new List<string>();
        if (db.SchemaVersion != 1) errors.Add("Version du schéma non prise en charge.");
        if (db.Revision < 0) errors.Add("Révision négative.");
        if (db.Sites is null) return ["La liste des sites est absente."];
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<Guid>();
        var ips = new HashSet<uint>();
        var networks = new List<(Ipv4Network Network, string Label)>();
        foreach (var site in db.Sites)
        {
            if (site is null) { errors.Add("Site nul."); continue; }
            var label = $"Site {site.Code}";
            CheckId(site.Id, label);
            Text(site.Code, 50, true, $"{label} / code");
            Text(site.Name, 150, true, $"{label} / nom");
            Text(site.Description, 500, false, $"{label} / description");
            if (site.DisplayOrder < 0) errors.Add($"{label} : ordre d’affichage négatif.");
            if (!codes.Add(site.Code?.Trim() ?? "")) errors.Add($"Code site déjà utilisé : {site.Code}.");
            if (site.Vlans is null) { errors.Add($"{label} : liste des VLAN absente."); continue; }
            var vids = new HashSet<int>();
            foreach (var vlan in site.Vlans)
            {
                if (vlan is null) { errors.Add($"{label} : VLAN nul."); continue; }
                var vl = $"{site.Code} / VLAN {vlan.Vid}";
                CheckId(vlan.Id, vl);
                if (vlan.Vid is < 1 or > 4094) errors.Add($"{vl} : VID attendu entre 1 et 4094.");
                if (!vids.Add(vlan.Vid)) errors.Add($"{vl} : VID déjà utilisé sur ce site.");
                Text(vlan.Name, 150, true, $"{vl} / nom");
                Text(vlan.Description, 500, false, $"{vl} / description");
                if (vlan.Subnet is not { } subnet) continue;
                Ipv4Network network;
                try { network = Ipv4Network.Parse(subnet.Cidr); }
                catch (FormatException ex) { errors.Add($"{vl} : {ex.Message}"); continue; }
                foreach (var other in networks)
                    if (network.Overlaps(other.Network)) errors.Add($"{vl} ({network}) chevauche {other.Label} ({other.Network}).");
                networks.Add((network, vl));
                if (subnet.Gateway is { } gw)
                {
                    CheckAddress(gw.Address, network, $"{vl} / passerelle");
                    Text(gw.Comment, 500, false, $"{vl} / commentaire passerelle");
                }
                if (subnet.Addresses is null) { errors.Add($"{vl} : liste IP absente."); continue; }
                foreach (var ip in subnet.Addresses)
                {
                    if (ip is null) { errors.Add($"{vl} : adresse nulle."); continue; }
                    CheckAddress(ip.Address, network, vl);
                    Text(ip.Hostname, 255, false, $"{vl} / {ip.Address} / hostname");
                    Text(ip.Description, 500, false, $"{vl} / {ip.Address} / description");
                    if (string.IsNullOrWhiteSpace(ip.Hostname) && string.IsNullOrWhiteSpace(ip.Description))
                        errors.Add($"{vl} / {ip.Address} : hostname ou description obligatoire.");
                }
            }
        }
        return errors;

        void CheckId(Guid id, string label)
        {
            if (id == Guid.Empty || !ids.Add(id)) errors.Add($"{label} : identifiant interne invalide ou dupliqué.");
        }
        void Text(string? value, int max, bool required, string label)
        {
            if (value is null || (required && string.IsNullOrWhiteSpace(value))) errors.Add($"{label} : champ obligatoire.");
            if (value is not null && (value.Length > max || value.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029')))
                errors.Add($"{label} : une seule ligne, {max} caractères maximum.");
        }
        void CheckAddress(string value, Ipv4Network network, string label)
        {
            try
            {
                var address = Ipv4Network.ParseAddress(value);
                if (!network.IsUsable(address)) errors.Add($"{label} : {value} n'est pas une adresse utilisable de {network}.");
                if (!ips.Add(address)) errors.Add($"{label} : {value} déjà utilisée (IP ou passerelle).");
            }
            catch (FormatException ex) { errors.Add($"{label} : {ex.Message}"); }
        }
    }

    public static void EnsureValid(Database db)
    {
        var errors = Validate(db);
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    public static void DeleteSite(Database db, Guid siteId)
    {
        var site = db.Sites.Single(s => s.Id == siteId);
        if (site.Vlans.Count > 0) throw new ValidationException(["Supprimez d'abord les VLAN de ce site."]);
        db.Sites.Remove(site);
    }

    public static void DeleteVlan(Site site, Guid vlanId)
    {
        var vlan = site.Vlans.Single(v => v.Id == vlanId);
        if (vlan.Subnet is { } subnet && (subnet.Addresses.Count > 0 || subnet.Gateway is not null))
            throw new ValidationException(["Libérez les IP et retirez la passerelle avant de supprimer le VLAN."]);
        site.Vlans.Remove(vlan);
    }
}

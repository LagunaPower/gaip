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
        var assignedIps = new HashSet<string>(StringComparer.Ordinal);
        var vlanIds = new HashSet<Guid>();
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
                vlanIds.Add(vlan.Id);
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
                    try { assignedIps.Add(Ipv4Network.Format(Ipv4Network.ParseAddress(ip.Address))); } catch (FormatException) { }
                    Text(ip.Hostname, 255, false, $"{vl} / {ip.Address} / hostname");
                    Text(ip.Description, 500, false, $"{vl} / {ip.Address} / description");
                    if (string.IsNullOrWhiteSpace(ip.Hostname) && string.IsNullOrWhiteSpace(ip.Description))
                        errors.Add($"{vl} / {ip.Address} : hostname ou description obligatoire.");
                }
            }
        }

        if (db.MulticastGroups is null)
        {
            errors.Add("La liste des groupes multicast est absente.");
            return errors;
        }

        var multicastAddresses = new HashSet<uint>();
        foreach (var group in db.MulticastGroups)
        {
            if (group is null) { errors.Add("Groupe multicast nul."); continue; }
            var label = $"Multicast {group.Address}";
            try
            {
                var address = Ipv4Network.ParseAddress(group.Address);
                if ((address & 0xF0000000u) != 0xE0000000u)
                    errors.Add($"{label} : adresse attendue dans 224.0.0.0/4.");
                if (!multicastAddresses.Add(address))
                    errors.Add($"{label} : adresse multicast déjà utilisée.");
            }
            catch (FormatException ex) { errors.Add($"{label} : {ex.Message}"); }

            Text(group.Name, 150, true, $"{label} / nom");
            Text(group.Description, 500, false, $"{label} / description");
            if (group.Flows is null) { errors.Add($"{label} : liste des flux absente."); continue; }

            var ports = new HashSet<int>();
            foreach (var flow in group.Flows)
            {
                if (flow is null) { errors.Add($"{label} : flux nul."); continue; }
                var fl = $"{label} / port {flow.Port}";
                if (flow.Port is < 1 or > 65535) errors.Add($"{fl} : port attendu entre 1 et 65535.");
                if (!ports.Add(flow.Port)) errors.Add($"{fl} : port déjà utilisé dans ce groupe.");
                Text(flow.Content, 150, true, $"{fl} / contenu");
                Text(flow.Description, 500, false, $"{fl} / description");

                if (flow.Sources is null) errors.Add($"{fl} : liste des sources absente.");
                else
                {
                    var sources = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var source in flow.Sources)
                    {
                        try
                        {
                            var normalized = Ipv4Network.Format(Ipv4Network.ParseAddress(source));
                            if (!sources.Add(normalized)) errors.Add($"{fl} : source {normalized} dupliquée.");
                            if (!assignedIps.Contains(normalized)) errors.Add($"{fl} : source {normalized} absente des IP attribuées. Retirez d'abord cette référence multicast avant de libérer ou modifier l'adresse.");
                        }
                        catch (FormatException ex) { errors.Add($"{fl} / source : {ex.Message}"); }
                    }
                }

                if (flow.VlanIds is null) errors.Add($"{fl} : liste des VLAN absente.");
                else
                {
                    var references = new HashSet<Guid>();
                    foreach (var vlanId in flow.VlanIds)
                    {
                        if (vlanId == Guid.Empty || !references.Add(vlanId))
                            errors.Add($"{fl} : référence VLAN invalide ou dupliquée.");
                        else if (!vlanIds.Contains(vlanId))
                            errors.Add($"{fl} : VLAN référencé introuvable. Retirez d'abord cette référence multicast avant de supprimer le VLAN.");
                    }
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

    public static IReadOnlyList<string> ValidateTransition(Database before, Database after)
    {
        var errors = new List<string>();
        var currentVlans = after.Sites.SelectMany(s => s.Vlans).ToDictionary(v => v.Id);
        foreach (var site in before.Sites)
            foreach (var vlan in site.Vlans)
            {
                if (vlan.Subnet is not { Addresses.Count: > 0 } previousSubnet) continue;
                if (!currentVlans.TryGetValue(vlan.Id, out var currentVlan) || currentVlan.Subnet is not { } currentSubnet)
                {
                    errors.Add($"Impossible de modifier le sous-réseau de {site.Code} / VLAN {vlan.Vid} : ce VLAN contient des adresses IP attribuées. Libérez d'abord toutes les adresses IP.");
                    continue;
                }
                try
                {
                    if (Ipv4Network.Parse(previousSubnet.Cidr) != Ipv4Network.Parse(currentSubnet.Cidr))
                        errors.Add($"Impossible de modifier le sous-réseau de {site.Code} / VLAN {vlan.Vid} : ce VLAN contient des adresses IP attribuées. Libérez d'abord toutes les adresses IP.");
                }
                catch (FormatException)
                {
                    // La validation structurelle signale séparément les CIDR invalides.
                }
            }
        return errors;
    }

    public static void EnsureValid(Database db)
    {
        var errors = Validate(db);
        if (errors.Count > 0) throw new ValidationException(errors);
    }

    public static void EnsureTransitionValid(Database before, Database after)
    {
        var errors = ValidateTransition(before, after);
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
        if (vlan.Subnet is { Addresses.Count: > 0 })
            throw new ValidationException(["Libérez les IP avant de supprimer le VLAN."]);
        site.Vlans.Remove(vlan);
    }

    public static void DeleteMulticastGroup(Database db, string address)
    {
        var group = db.MulticastGroups.Single(g => g.Address == address);
        if (group.Flows.Count > 0) throw new ValidationException(["Supprimez d'abord les flux de ce groupe multicast."]);
        db.MulticastGroups.Remove(group);
    }

    public static void DeleteMulticastFlow(Database db, string address, int port)
    {
        var group = db.MulticastGroups.Single(g => g.Address == address);
        group.Flows.Remove(group.Flows.Single(f => f.Port == port));
    }
}

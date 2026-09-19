using Avalonia.Controls;
using Avalonia.Media;
using GAIP.Core;
using GAIP.Storage;

namespace GAIP.Desktop;

public sealed partial class MainWindow
{
    private void LiveValidation(FormWindow form, Func<Action<Database>> mutation, params TextBox[] fields)
    {
        void Validate()
        {
            try
            {
                var copy = JsonData.Clone(Db); mutation()(copy); ModelValidator.EnsureValid(copy);
                form.Error.Text = ""; form.Save.IsEnabled = CanWrite;
            }
            catch (Exception ex) { form.Error.Text = ex.Message; form.Save.IsEnabled = false; }
        }
        foreach (var field in fields) field.TextChanged += (_, _) => Validate();
        form.Opened += (_, _) => Validate();
    }
    private async Task EditSite(Site? existing)
    {
        var form = new FormWindow(existing is null ? "Ajouter un site" : "Modifier le site");
        var code = Ui.Input(existing?.Code ?? "", "LEVANT", 50);
        var name = Ui.Input(existing?.Name ?? "", "Île du Levant", 150);
        var description = Ui.Input(existing?.Description ?? "");
        form.Add("Code unique", code); form.Add("Nom", name); form.Add("Description", description);
        Action<Database> Mutation()
        {
            var c = code.Text?.Trim() ?? ""; var n = name.Text ?? ""; var d = description.Text ?? "";
            return db =>
            {
                var site = existing is null ? new Site() : db.Sites.Single(s => s.Id == existing.Id);
                site.Code = c; site.Name = n; site.Description = d;
                if (existing is null) db.Sites.Add(site);
            };
        }
        form.Submit = () => Save(Mutation(), existing is null ? "Création" : "Modification", "Site", code.Text ?? "");
        LiveValidation(form, Mutation, code, name, description);
        if (existing is not null) form.Fields.Children.Add(Ui.Button("Supprimer le site", async () =>
        {
            try
            {
                if (!await Confirm("Supprimer le site", $"Supprimer {existing.Code} ? Le site doit être vide.")) return;
                await Save(db => ModelValidator.DeleteSite(db, existing.Id), "Suppression", "Site", existing.Code); form.Close(true);
            }
            catch (Exception ex) { form.Error.Text = ex.Message; }
        }, CanWrite));
        await form.ShowDialog<bool>(this);
    }

    private sealed record NetworkFields(Guid SiteId, CheckBox Selected, TextBox Cidr, TextBox Gateway, TextBox Comment);
    private async Task EditVlan(Site? owner, Vlan? existing)
    {
        var form = new FormWindow(existing is null ? "Ajouter un VLAN · un ou plusieurs sites" : "Modifier le VLAN / sous-réseau", width: 720);
        var vid = Ui.Input(existing?.Vid.ToString() ?? "", "1 à 4094", 4);
        var name = Ui.Input(existing?.Name ?? "", "SERVEURS", 150);
        var description = Ui.Input(existing?.Description ?? "");
        form.Add("VID", vid); form.Add("Nom", name); form.Add("Description", description);
        form.Fields.Children.Add(Ui.Text("Sites concernés · un sous-réseau facultatif par site", 16, true));
        var networks = new List<NetworkFields>();
        foreach (var site in Db.Sites.Where(s => existing is null || s.Id == owner?.Id).OrderBy(s => s.Code))
        {
            var check = new CheckBox { Content = $"{site.Code} — {site.Name}", IsChecked = site.Id == owner?.Id, IsEnabled = existing is null };
            var cidr = Ui.Input(existing?.Subnet?.Cidr ?? "", "10.20.120.0/24 ou vide", 18);
            var cidrLocked = existing?.Subnet?.Addresses.Count > 0;
            cidr.IsReadOnly = cidrLocked;
            if (cidrLocked) ToolTip.SetTip(cidr, "Libérez toutes les adresses IP avant de modifier le sous-réseau.");
            var gateway = Ui.Input(existing?.Subnet?.Gateway?.Address ?? "", "Facultative", 15);
            var comment = Ui.Input(existing?.Subnet?.Gateway?.Comment ?? "");
            var summary = Ui.Text("Aucun sous-réseau", 12);
            var fields = Ui.Column(Ui.Field("CIDR IPv4", cidr), summary, Ui.Field("Passerelle", gateway), Ui.Field("Commentaire passerelle", comment));
            fields.IsEnabled = check.IsChecked == true;
            check.IsCheckedChanged += (_, _) => fields.IsEnabled = check.IsChecked == true;
            void Summary()
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(cidr.Text)) { summary.Text = "Aucun sous-réseau"; return; }
                    var n = Ipv4Network.Parse(cidr.Text.Trim()); summary.Text = NetworkSummary(n); summary.Foreground = Brushes.SeaGreen;
                    if (!string.IsNullOrWhiteSpace(gateway.Text) && !n.IsUsable(Ipv4Network.ParseAddress(gateway.Text.Trim())))
                        throw new FormatException("Passerelle hors réseau, réseau ou broadcast : enregistrement interdit.");
                }
                catch (FormatException ex) { summary.Text = ex.Message; summary.Foreground = Brushes.IndianRed; }
            }
            cidr.TextChanged += (_, _) => Summary(); gateway.TextChanged += (_, _) => Summary(); Summary();
            networks.Add(new(site.Id, check, cidr, gateway, comment));
            form.Fields.Children.Add(Ui.Card(Ui.Column(check, fields)));
        }
        Action<Database> Mutation()
        {
            if (!int.TryParse(vid.Text, out var v)) throw new FormatException("VID entier attendu entre 1 et 4094.");
            var n = name.Text ?? ""; var d = description.Text ?? "";
            var choices = networks.Where(f => f.Selected.IsChecked == true)
                .Select(f => (f.SiteId, Cidr: f.Cidr.Text?.Trim() ?? "", Gateway: f.Gateway.Text?.Trim() ?? "", Comment: f.Comment.Text ?? "")).ToArray();
            if (choices.Length == 0) throw new FormatException("Sélectionnez au moins un site.");
            return db =>
            {
                foreach (var choice in choices)
                {
                    var site = db.Sites.Single(s => s.Id == choice.SiteId);
                    var vlan = existing is null ? new Vlan() : site.Vlans.Single(x => x.Id == existing.Id);
                    vlan.Vid = v; vlan.Name = n; vlan.Description = d;
                    if (choice.Cidr.Length == 0)
                    {
                        if (vlan.Subnet?.Addresses.Count > 0) throw new ValidationException(["Libérez les adresses avant de retirer le sous-réseau."]);
                        if (choice.Gateway.Length > 0 || choice.Comment.Length > 0) throw new ValidationException(["Passerelle ou commentaire sans sous-réseau."]);
                        vlan.Subnet = null;
                    }
                    else
                    {
                        var network = Ipv4Network.Parse(choice.Cidr);
                        vlan.Subnet ??= new(); vlan.Subnet.Cidr = network.ToString();
                        if (choice.Gateway.Length == 0 && choice.Comment.Length > 0) throw new ValidationException(["Indiquez l'adresse de la passerelle ou effacez son commentaire."]);
                        vlan.Subnet.Gateway = choice.Gateway.Length == 0 ? null : new Gateway { Address = choice.Gateway, Comment = choice.Comment };
                    }
                    if (existing is null) site.Vlans.Add(vlan);
                }
            };
        }
        var textFields = new[] { vid, name, description }.Concat(networks.SelectMany(f => new[] { f.Cidr, f.Gateway, f.Comment })).ToArray();
        LiveValidation(form, Mutation, textFields);
        foreach (var field in networks) field.Selected.IsCheckedChanged += (_, _) =>
        {
            try { var copy = JsonData.Clone(Db); Mutation()(copy); ModelValidator.EnsureValid(copy); form.Error.Text = ""; form.Save.IsEnabled = CanWrite; }
            catch (Exception ex) { form.Error.Text = ex.Message; form.Save.IsEnabled = false; }
        };
        form.Submit = () => Save(Mutation(), existing is null ? "Création multi-sites" : "Modification", "VLAN", vid.Text ?? "");
        if (existing is not null && owner is not null) form.Fields.Children.Add(Ui.Button("Supprimer le VLAN", async () =>
        {
            try
            {
                if (!await Confirm("Supprimer le VLAN", $"Supprimer {owner.Code} / VLAN {existing.Vid} ? Aucune IP ni passerelle ne doit subsister.")) return;
                await Save(db => ModelValidator.DeleteVlan(db.Sites.Single(s => s.Id == owner.Id), existing.Id), "Suppression", "VLAN", $"{owner.Code}/{existing.Vid}");
                _selectedVlan = null; form.Close(true);
            }
            catch (Exception ex) { form.Error.Text = ex.Message; }
        }, CanWrite));
        await form.ShowDialog<bool>(this);
    }

    private async Task EditAddress(Guid siteId, Guid vlanId, AddressRow? row)
    {
        Subnet Subnet(Database db) => db.Sites.Single(s => s.Id == siteId).Vlans.Single(v => v.Id == vlanId).Subnet ?? throw new InvalidOperationException("Sous-réseau supprimé.");
        var form = new FormWindow(row?.IsUsed == true ? "Modifier l’adresse IP" : "Ajouter une adresse IP");
        var address = Ui.Input(row?.Address ?? Queries.NextFree(Subnet(Db)) ?? "", "10.20.120.25", 15);
        var hostname = Ui.Input(row?.IsUsed == true ? row.Hostname : "", "Facultatif si une description est renseignée", 255);
        var description = Ui.Input(row?.Description ?? "");
        form.Add("Adresse IPv4", address); form.Add("Nom / Hostname", hostname); form.Add("Description", description);
        form.Fields.Children.Add(Ui.Button("Prochaine libre", () =>
        {
            address.Text = Queries.NextFree(Subnet(Db)) ?? "";
            if (address.Text.Length == 0) form.Error.Text = "Aucune adresse libre.";
        }));
        Action<Database> Mutation()
        {
            var a = address.Text?.Trim() ?? ""; var h = hostname.Text ?? ""; var d = description.Text ?? "";
            return db =>
            {
                var subnet = Subnet(db);
                if (row?.IsUsed == true) subnet.Addresses.RemoveAll(ip => ip.Address == row.Address);
                subnet.Addresses.Add(new() { Address = a, Hostname = h, Description = d });
            };
        }
        LiveValidation(form, Mutation, address, hostname, description);
        form.Submit = () => Save(Mutation(), row?.IsUsed == true ? "Modification" : "Attribution", "IP", address.Text ?? "");
        if (row?.IsUsed == true) form.Fields.Children.Add(Ui.Button("Libérer l’adresse", async () =>
        {
            try
            {
                if (!await Confirm("Libérer l’adresse", $"Libérer {row.Address} ? Son hostname et sa description seront supprimés.")) return;
                await Save(db => Subnet(db).Addresses.RemoveAll(a => a.Address == row.Address), "Libération", "IP", row.Address); form.Close(true);
            }
            catch (Exception ex) { form.Error.Text = ex.Message; }
        }, CanWrite));
        await form.ShowDialog<bool>(this);
    }
}

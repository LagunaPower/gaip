using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using GAIP.Core;
using GAIP.Storage;

namespace GAIP.Desktop;

public sealed partial class MainWindow
{
    private static int MaxMulticastHomeTiles(int multicastCount) =>
        Math.Max(1, multicastCount / 5);

    private void AddMulticastHomeCard(Grid cards)
    {
        var groups = Db.MulticastGroups.OrderBy(g => Ipv4Network.ParseAddress(g.Address)).ToArray();
        if (groups.Length == 0) return;

        var tileCount = Math.Min(Math.Max(1, _config.MulticastHomeTiles), MaxMulticastHomeTiles(groups.Length));
        var baseSize = groups.Length / tileCount;
        var remainder = groups.Length % tileCount;
        var offset = 0;

        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            var size = baseSize + (tileIndex < remainder ? 1 : 0);
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            header.Children.Add(Ui.Text(tileCount == 1 ? "Multicast" : $"Multicast {tileIndex + 1}/{tileCount}", 18, true));
            var stats = Ui.Text($"{size} groupe(s)", 11);
            Grid.SetColumn(stats, 1); header.Children.Add(stats);

            var details = Ui.Column(header);
            foreach (var group in groups.Skip(offset).Take(size))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,Auto"), ColumnSpacing = 8 };
                row.Children.Add(Ui.Text(group.Address, 13, true));
                var name = Ui.Text(group.Name, 13); Grid.SetColumn(name, 1); row.Children.Add(name);
                var flows = Ui.Text($"{group.Flows.Count} flux", 12); Grid.SetColumn(flows, 2); row.Children.Add(flows);
                var button = Ui.Button("", () => OpenMulticast(group.Address));
                button.Name = "MulticastGroupRow";
                button.Content = row;
                button.Background = Brushes.Transparent;
                button.BorderThickness = new Thickness(0, 0, 0, 1);
                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                details.Children.Add(button);
            }
            offset += size;

            var card = Ui.Card(details);
            card.Name = "MulticastCard";
            card.Margin = new Thickness(0);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            cards.Children.Add(card);
        }
    }

    private void OpenMulticast(string address)
    {
        _selectedSite = null;
        _selectedVlan = null;
        _selectedMulticast = address;
        _showFreeAddresses = false;
        _subnetSearch = "";
        _search.Text = "";
        Render();
    }

    private void RenderMulticast(MulticastGroup group)
    {
        _pageHeading.Content = Ui.Text($"{group.Address} — {group.Name}", 22, true);
        var stack = new StackPanel { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(group.Description)) stack.Children.Add(Ui.Text(group.Description));

        var sites = SiteOrdering.Ordered(Db.Sites).ToArray();
        Grid Row()
        {
            var row = new Grid { ColumnSpacing = 10 };
            row.ColumnDefinitions.Add(new ColumnDefinition(70, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(180, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            foreach (var _ in sites) row.ColumnDefinitions.Add(new ColumnDefinition(90, GridUnitType.Pixel));
            return row;
        }

        var header = Row();
        header.Children.Add(Ui.Text("Port", 12, true));
        var hContent = Ui.Text("Contenu", 12, true); Grid.SetColumn(hContent, 1); header.Children.Add(hContent);
        var hSources = Ui.Text("Sources", 12, true); Grid.SetColumn(hSources, 2); header.Children.Add(hSources);
        for (var index = 0; index < sites.Length; index++)
        {
            var siteHeader = Ui.Text(sites[index].Code, 12, true);
            siteHeader.Name = $"MulticastSiteHeader_{sites[index].Id:N}";
            siteHeader.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(siteHeader, 3 + index); header.Children.Add(siteHeader);
        }
        stack.Children.Add(header);

        var flows = new ListBox
        {
            Name = "MulticastFlowList",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = group.Flows.OrderBy(flow => flow.Port).ToArray(),
            ItemTemplate = new FuncDataTemplate<MulticastFlow>((flow, _) =>
            {
                if (flow is null) return null;
                var row = Row();
                row.Children.Add(Ui.Text(flow.Port.ToString(), 13, true));
                var content = Ui.Text(flow.Content, 13); Grid.SetColumn(content, 1); row.Children.Add(content);
                var sources = Ui.Text(flow.Sources.Count == 0 ? "—" : string.Join(", ", flow.Sources.Select(MulticastSourceLabel)), 12);
                Grid.SetColumn(sources, 2); row.Children.Add(sources);

                for (var index = 0; index < sites.Length; index++)
                {
                    var site = sites[index];
                    var usedVlans = site.Vlans.Where(vlan => flow.VlanIds.Contains(vlan.Id)).OrderBy(vlan => vlan.Vid).ToArray();
                    var mark = Ui.Text(usedVlans.Length > 0 ? "✔" : "✖", 16, true);
                    mark.Name = $"MulticastSite_{flow.Port}_{site.Id:N}";
                    mark.Foreground = usedVlans.Length > 0 ? Brushes.SeaGreen : Brushes.IndianRed;
                    mark.HorizontalAlignment = HorizontalAlignment.Center;
                    if (usedVlans.Length > 0)
                        ToolTip.SetTip(mark, string.Join("\n", usedVlans.Select(vlan => $"VLAN {vlan.Vid} — {vlan.Name}")));
                    else ToolTip.SetTip(mark, "Aucun VLAN de ce site n’utilise ce flux.");
                    Grid.SetColumn(mark, 3 + index); row.Children.Add(mark);
                }

                var item = new Border
                {
                    Name = $"MulticastFlow_{flow.Port}",
                    Child = row,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(8, 3),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Tag = flow
                };
                var edit = new MenuItem { Header = "Modifier", IsEnabled = CanWrite };
                edit.Click += (_, _) => Run(() => EditMulticastFlow(group, flow));
                var delete = new MenuItem { Header = "Supprimer", IsEnabled = CanWrite };
                delete.Click += (_, _) => Run(() => DeleteMulticastFlow(group, flow));
                item.ContextMenu = new ContextMenu { ItemsSource = new Control[] { edit, delete } };
                return item;
            })
        };
        flows.DoubleTapped += (_, _) =>
        {
            if (!CanWrite || flows.SelectedItem is not MulticastFlow flow) return;
            Run(() => EditMulticastFlow(group, flow));
        };
        flows.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters =
        {
            new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
            new Setter(ListBoxItem.MinHeightProperty, 36d)
        } });

        if (group.Flows.Count == 0)
            stack.Children.Add(Ui.Card(Ui.Text("Aucun flux. Ajoutez un port pour décrire le contenu diffusé.")));
        else stack.Children.Add(flows);

        _body.Content = new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private string MulticastSourceLabel(string address)
    {
        var found = Db.Sites
            .SelectMany(site => site.Vlans.SelectMany(vlan => (vlan.Subnet?.Addresses ?? []).Select(ip => (site, vlan, ip))))
            .FirstOrDefault(x => x.ip.Address == address);
        return found.ip is null || string.IsNullOrWhiteSpace(found.ip.Hostname) ? address : $"{address} — {found.ip.Hostname}";
    }

    private string MulticastVlanLabel(Guid id)
    {
        foreach (var site in Db.Sites)
        {
            var vlan = site.Vlans.FirstOrDefault(v => v.Id == id);
            if (vlan is not null) return $"{site.Code}/{vlan.Vid}";
        }
        return id.ToString();
    }

    private async Task EditMulticastGroup(MulticastGroup? existing)
    {
        var form = new FormWindow(existing is null ? "Ajouter un multicast" : "Modifier le multicast");
        var address = Ui.Input(existing?.Address ?? "", "239.10.20.15", 15);
        var name = Ui.Input(existing?.Name ?? "", "VIDEO", 150);
        var description = Ui.Input(existing?.Description ?? "");
        form.Add("Adresse multicast (identifiant)", address);
        form.Add("Nom", name);
        form.Add("Description", description);

        Action<Database> Mutation()
        {
            var parsed = Ipv4Network.ParseAddress(address.Text?.Trim() ?? "");
            var a = Ipv4Network.Format(parsed);
            var n = name.Text ?? "";
            var d = description.Text ?? "";
            return db =>
            {
                var target = existing is null
                    ? new MulticastGroup()
                    : db.MulticastGroups.Single(g => g.Address == existing.Address);
                target.Address = a;
                target.Name = n;
                target.Description = d;
                if (existing is null) db.MulticastGroups.Add(target);
            };
        }

        LiveValidation(form, Mutation, address, name, description);
        form.Submit = async () =>
        {
            var normalized = Ipv4Network.Format(Ipv4Network.ParseAddress(address.Text?.Trim() ?? ""));
            await Save(Mutation(), existing is null ? "Création" : "Modification", "Multicast", normalized);
            _selectedMulticast = normalized;
            Render();
        };

        if (existing is not null)
            form.Fields.Children.Add(Ui.Button("Supprimer le multicast", async () =>
            {
                try
                {
                    if (!await Confirm("Supprimer le multicast", $"Supprimer {existing.Address} ? Le groupe doit être vide.")) return;
                    await Save(db => ModelValidator.DeleteMulticastGroup(db, existing.Address), "Suppression", "Multicast", existing.Address);
                    _selectedMulticast = null;
                    Render();
                    form.Close(true);
                }
                catch (Exception ex) { form.Error.Text = ex.Message; }
            }, CanWrite));

        await form.ShowDialog<bool>(this);
    }

    private sealed record MulticastFilterChoice<T>(T Value, CheckBox Check, string SearchText);

    private static Control MulticastFilterPicker<T>(
        string searchName,
        string searchLabel,
        IReadOnlyList<MulticastFilterChoice<T>> choices,
        string emptyMessage,
        Action selectionChanged)
    {
        var search = Ui.Input("", searchLabel);
        search.Name = searchName;
        var rows = new StackPanel { Spacing = 3 };
        var count = Ui.Text("", 12);

        if (choices.Count == 0) rows.Children.Add(Ui.Text(emptyMessage, 12));
        else foreach (var choice in choices) rows.Children.Add(choice.Check);

        void Refresh()
        {
            var query = search.Text?.Trim() ?? "";
            foreach (var choice in choices)
                choice.Check.IsVisible = choice.Check.IsChecked == true ||
                    query.Length == 0 ||
                    choice.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            count.Text = $"{choices.Count(choice => choice.Check.IsChecked == true)} sélectionné(s)";
        }

        search.TextChanged += (_, _) => Refresh();
        foreach (var choice in choices)
            choice.Check.IsCheckedChanged += (_, _) => { Refresh(); selectionChanged(); };
        Refresh();

        var list = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#65758B"), .35),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6),
            MaxHeight = 220,
            Child = new ScrollViewer
            {
                Content = rows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }
        };
        return Ui.Column(Ui.SearchField(search, searchLabel), count, list);
    }

    private async Task EditMulticastFlow(MulticastGroup group, MulticastFlow? existing)
    {
        var form = new FormWindow(existing is null ? $"Ajouter un flux · {group.Address}" : $"Modifier le flux · {group.Address}", width: 760);
        var port = Ui.Input(existing?.Port.ToString() ?? "", "1 à 65535", 5);
        var content = Ui.Input(existing?.Content ?? "", "Vidéo principale", 150);
        var description = Ui.Input(existing?.Description ?? "");
        form.Add("Port UDP", port);
        form.Add("Contenu", content);
        form.Add("Description", description);

        var sourceChoices = Db.Sites
            .SelectMany(site => site.Vlans.SelectMany(vlan => (vlan.Subnet?.Addresses ?? []).Select(ip => (site, vlan, ip))))
            .OrderBy(item => Ipv4Network.ParseAddress(item.ip.Address))
            .Select(item =>
            {
                var display = $"{item.ip.Address} — {(string.IsNullOrWhiteSpace(item.ip.Hostname) ? item.ip.Description : item.ip.Hostname)} — {item.site.Code}/VLAN {item.vlan.Vid}";
                var searchText = string.Join(" ", item.ip.Address, item.ip.Hostname, item.ip.Description,
                    item.site.Code, item.site.Name, item.vlan.Vid.ToString(), item.vlan.Name, item.vlan.Description);
                var check = new CheckBox
                {
                    Content = display,
                    IsChecked = existing?.Sources.Contains(item.ip.Address) == true
                };
                Avalonia.Automation.AutomationProperties.SetName(check, $"Source multicast {item.ip.Address}");
                return new MulticastFilterChoice<string>(item.ip.Address, check, searchText);
            }).ToList();

        var vlanChoices = SiteOrdering.Ordered(Db.Sites)
            .SelectMany(site => site.Vlans.OrderBy(vlan => vlan.Vid).Select(vlan => (site, vlan)))
            .Select(item =>
            {
                var display = $"{item.site.Code} — VLAN {item.vlan.Vid} — {item.vlan.Name}";
                var searchText = string.Join(" ", item.site.Code, item.site.Name, item.vlan.Vid.ToString(),
                    item.vlan.Name, item.vlan.Description, item.vlan.Subnet?.Cidr ?? "");
                var check = new CheckBox
                {
                    Content = display,
                    IsChecked = existing?.VlanIds.Contains(item.vlan.Id) == true
                };
                Avalonia.Automation.AutomationProperties.SetName(check, $"VLAN multicast {item.site.Code} {item.vlan.Vid}");
                return new MulticastFilterChoice<Guid>(item.vlan.Id, check, searchText);
            }).ToList();

        Action<Database> Mutation()
        {
            if (!int.TryParse(port.Text, out var p)) throw new FormatException("Port entier attendu entre 1 et 65535.");
            var c = content.Text ?? "";
            var d = description.Text ?? "";
            var sources = sourceChoices.Where(choice => choice.Check.IsChecked == true).Select(choice => choice.Value).ToList();
            var vlans = vlanChoices.Where(choice => choice.Check.IsChecked == true).Select(choice => choice.Value).ToList();
            return db =>
            {
                var targetGroup = db.MulticastGroups.Single(g => g.Address == group.Address);
                var flow = existing is null ? new MulticastFlow() : targetGroup.Flows.Single(f => f.Port == existing.Port);
                flow.Port = p;
                flow.Content = c;
                flow.Description = d;
                flow.Sources = sources;
                flow.VlanIds = vlans;
                if (existing is null) targetGroup.Flows.Add(flow);
            };
        }

        void Validate()
        {
            try
            {
                var copy = JsonData.Clone(Db);
                Mutation()(copy);
                ModelValidator.EnsureValid(copy);
                form.Error.Text = "";
                form.Save.IsEnabled = CanWrite;
            }
            catch (Exception ex)
            {
                form.Error.Text = ex.Message;
                form.Save.IsEnabled = false;
            }
        }

        form.Fields.Children.Add(Ui.Field("Source(s) du flux",
            MulticastFilterPicker("MulticastSourceSearch", "Rechercher une IP, un hostname, un site ou un VLAN",
                sourceChoices, "Aucune IP attribuée n’est disponible comme source.", Validate)));
        form.Fields.Children.Add(Ui.Field("VLAN utilisé(s)",
            MulticastFilterPicker("MulticastVlanSearch", "Rechercher un site, un VLAN, un nom ou un CIDR",
                vlanChoices, "Aucun VLAN disponible.", Validate)));

        port.TextChanged += (_, _) => Validate();
        content.TextChanged += (_, _) => Validate();
        description.TextChanged += (_, _) => Validate();
        form.Opened += (_, _) => Validate();

        form.Submit = () => Save(Mutation(), existing is null ? "Création" : "Modification", "Flux multicast",
            $"{group.Address}:{port.Text?.Trim()}");

        if (existing is not null)
            form.Fields.Children.Add(Ui.Button("Supprimer le flux", async () =>
            {
                try
                {
                    if (await DeleteMulticastFlow(group, existing)) form.Close(true);
                }
                catch (Exception ex) { form.Error.Text = ex.Message; }
            }, CanWrite));

        await form.ShowDialog<bool>(this);
    }

    private async Task<bool> DeleteMulticastFlow(MulticastGroup group, MulticastFlow flow)
    {
        if (!await Confirm("Supprimer le flux", $"Supprimer {group.Address}:{flow.Port} ?")) return false;
        await Save(db => ModelValidator.DeleteMulticastFlow(db, group.Address, flow.Port), "Suppression", "Flux multicast",
            $"{group.Address}:{flow.Port}");
        return true;
    }

}

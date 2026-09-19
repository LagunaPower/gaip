using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GAIP.Core;
using GAIP.Storage;
using GAIP.Sync;

namespace GAIP.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly string _localRoot;
    private readonly string _configRoot;
    private AppConfig _config = new();
    private DataSession? _session;
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly TextBlock _status = Ui.Text("Chargement…", 12);
    private readonly TextBlock _revision = Ui.Text("", 12);
    private readonly TextBox _search = Ui.Input("", "Rechercher : IP, hostname, VLAN, site…");
    private readonly ContentControl _body = new();
    private readonly ContentControl _pageHeading = new();
    private readonly Grid _globalSearch;
    private readonly WrapPanel _actions = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastSync;
    private bool _refreshing;
    private bool _closing;
    private Guid? _selectedSite;
    private Guid? _selectedVlan;
    private bool _showFreeAddresses;
    private string _subnetSearch = "";
    private Grid? _siteCards;
    private ScrollViewer? _siteCardsScroll;
    public DataSession? Session => _session;

    public MainWindow() : this(UserPaths.Root, UserPaths.ConfigRoot) { }
    public MainWindow(string localRoot, string configRoot)
    {
        _localRoot = localRoot; _configRoot = configRoot;
        Title = "G@IP — Gestion d’Adresses IP"; Width = 1320; Height = 850; MinWidth = 760; MinHeight = 540;
        Icon = Branding.Icon;
        var heading = new Grid { Name = "CompactHeader", ColumnDefinitions = new ColumnDefinitions("96,*"),
            RowDefinitions = new RowDefinitions("Auto,*"), ColumnSpacing = 12, RowSpacing = 6, MinHeight = 96 };
        var logo = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6), Width = 96, Height = 96,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Image { Name = "OfficialLogo", Source = Branding.Logo, Stretch = Stretch.Uniform } };
        Grid.SetRowSpan(logo, 2); heading.Children.Add(logo);
        _globalSearch = Ui.SearchField(_search, "Recherche globale : IP, hostname, VLAN ou site");
        _actions.Children.Add(_globalSearch);
        _search.MinWidth = 120; ResizeSearch();
        Grid.SetColumn(_actions, 1); heading.Children.Add(_actions);
        _pageHeading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_pageHeading, 1); Grid.SetRow(_pageHeading, 1); heading.Children.Add(_pageHeading);
        _status.MaxLines = 2; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.Margin = new Thickness(16, 10, 16, 10);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Margin = new Thickness(16, 6) };
        footer.Children.Add(_status); Grid.SetColumn(_revision, 1); footer.Children.Add(_revision);
        var root = new DockPanel(); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _body.Margin = new Thickness(16, 0, 16, 0); root.Children.Add(_body); Content = root;
        _search.TextChanged += (_, _) => Render();
        SizeChanged += (_, _) => { ResizeSearch(); ResizeCards(); };
        Opened += async (_, _) => await Initialize();
        _timer.Tick += async (_, _) =>
        {
            if (_session is null || _refreshing || _io.CurrentCount == 0 || _closing) return;
            if (_session.Config.Mode == StorageMode.Shared && (DateTime.UtcNow - _lastSync).TotalSeconds >= (_session.IsEditing ? 10 : _config.SyncSeconds))
            {
                _refreshing = true;
                try { await Refresh(); } catch (Exception ex) { _status.Text = ex.Message; }
                finally { _refreshing = false; }
            }
        };
        Closing += async (_, e) =>
        {
            if (_closing) return;
            e.Cancel = true; _closing = true; _timer.Stop();
            try { if (_session?.Lease is not null) await Io(() => _session.EndEdit()); }
            catch { /* A remaining lease is visible and can be explicitly forced by another client. */ }
            Close();
        };
    }

    public async Task Initialize()
    {
        try
        {
            _config = UserPaths.LoadConfig(_configRoot); ApplyTheme();
            _session = new(_config, _localRoot, UserPaths.User, UserPaths.Machine);
            await Io(_session.Open);
        }
        catch (Exception ex) { _status.Text = "Erreur de chargement"; _body.Content = Ui.Scroll(Ui.Text(ex.Message)); }
        Render(); _timer.Start();
    }
    private async Task Io(Action action)
    {
        await _io.WaitAsync();
        try { await Task.Run(action); } finally { _io.Release(); }
    }
    private async Task<T> Io<T>(Func<T> action)
    {
        await _io.WaitAsync();
        try { return await Task.Run(action); } finally { _io.Release(); }
    }
    private async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { await Message("Action impossible", ex.Message); }
        finally { Render(); }
    }
    private async Task Refresh()
    {
        if (_session is null) return;
        var previous = (_session.Hash, _session.Status, _session.Warning, _session.IsEditing, _session.HasData);
        await Io(_session.Refresh); _lastSync = DateTime.UtcNow;
        // Heartbeats must not steal focus or reset scroll while a user is consulting a page.
        if (previous != (_session.Hash, _session.Status, _session.Warning, _session.IsEditing, _session.HasData)) Render();
    }
    private async Task Save(Action<Database> mutation, string action, string type, string target)
    {
        if (_session is null) throw new InvalidOperationException("Base non chargée.");
        await Io(() => _session.Save(mutation, action, type, target)); Render();
        if (_session.Warning is { } warning) await Message("Publication effectuée avec avertissement", warning);
    }
    private bool CanEdit => _session?.IsEditing == true;
    private Database Db => _session?.Data ?? new();
    private void ApplyTheme()
    {
        if (Application.Current is { } app) app.RequestedThemeVariant = _config.Theme switch
        { AppTheme.Light => ThemeVariant.Light, AppTheme.Dark => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
    private void Render()
    {
        if (_session is null || !_session.HasData) return;
        _status.Text = _session.Status + (_session.Warning is { } w ? $" · {w}" : "");
        _status.Foreground = _session.IsOffline ? Brushes.IndianRed : _session.IsEditing && _config.Mode == StorageMode.Shared ? Brushes.DodgerBlue : Brushes.SeaGreen;
        _revision.Text = $"Révision {Db.Revision}  ·  {Db.Sites.Count} sites  ·  {UserPaths.User}";
        // Keep the search control attached so typing does not lose keyboard focus.
        for (var i = _actions.Children.Count - 1; i >= 0; i--)
            if (_actions.Children[i] != _globalSearch) _actions.Children.RemoveAt(i);
        _actions.Children.Insert(0, Ui.Button("Accueil", () => { _selectedSite = null; _selectedVlan = null; _search.Text = ""; Render(); }));
        _actions.Children.Insert(1, Ui.Button("Actualiser", () => Run(Refresh)));
        if (_config.Mode == StorageMode.Shared)
            _actions.Children.Add(Ui.Button(CanEdit ? "Terminer la modification" : "Passer en modification", () => Run(async () =>
            { if (CanEdit) await Io(_session.EndEdit); else await Io(_session.BeginEdit); }), !_session.IsOffline || CanEdit));
        Guid? context = string.IsNullOrWhiteSpace(_search.Text) && Db.Sites.Any(s => s.Vlans.Any(v => v.Id == _selectedVlan)) ? _selectedVlan : null;
        if (context is null)
        {
            _actions.Children.Add(Ui.Button("Ajouter un site", () => Run(() => EditSite(null)), CanEdit));
            _actions.Children.Add(Ui.Button("Ajouter un VLAN", () => Run(() => EditVlan(null, null)), CanEdit && Db.Sites.Count > 0));
        }
        else
        {
            var site = Db.Sites.Single(s => s.Vlans.Any(v => v.Id == context));
            var vlan = site.Vlans.Single(v => v.Id == context);
            _actions.Children.Add(Ui.Button("Modifier le VLAN", () => Run(() => EditVlan(site, vlan)), CanEdit));
        }
        _actions.Children.Add(Ui.Button("CSV / Excel", () => Run(() => CsvDialog(context))));
        _actions.Children.Add(Ui.Button("Historique", () => Run(() => History(context))));
        _actions.Children.Add(Ui.Button("Configuration", () => Run(Configure)));
        foreach (var action in _actions.Children)
        {
            action.Margin = new Thickness(0, 0, 6, 4);
            if (action is Button button) button.Padding = new Thickness(10, 6);
        }
        ToolTip.SetTip(_status, _status.Text);
        _siteCards = null;
        _siteCardsScroll = null;
        if (!string.IsNullOrWhiteSpace(_search.Text)) { RenderSearch(); return; }
        if (_selectedVlan is { } vlanId)
        {
            var site = Db.Sites.FirstOrDefault(s => s.Vlans.Any(v => v.Id == vlanId));
            var vlan = site?.Vlans.FirstOrDefault(v => v.Id == vlanId);
            if (site is not null && vlan is not null) { RenderVlan(site, vlan); return; }
            _selectedVlan = null;
        }
        RenderHome();
    }
    private void RenderHome()
    {
        _pageHeading.Content = Ui.Text(_selectedSite is null ? "Plans d’adressage" : "Site sélectionné", 22, true);
        var count = Db.Sites.Sum(s => s.Vlans.Count);
        var used = Db.Sites.Sum(s => s.Vlans.Sum(v => v.Subnet is null ? 0 : Queries.UsedCount(v.Subnet)));
        _siteCards = new Grid { Name = "SiteCards", ColumnSpacing = 8, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var site in SiteOrdering.Ordered(Db.Sites.Where(s => _selectedSite is null || s.Id == _selectedSite)))
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            header.Children.Add(Ui.Text(site.Name, 18, true));
            var stats = Ui.Text($"{site.Vlans.Count} VLAN / {site.Vlans.Sum(v => v.Subnet is null ? 0 : Queries.UsedCount(v.Subnet))} IP utilisées", 11);
            Grid.SetColumn(stats, 1); header.Children.Add(stats);

            var details = Ui.Column(header, Ui.Text(site.Code + (site.Description.Length > 0 ? " · " + site.Description : ""), 12));
            foreach (var vlan in site.Vlans.OrderBy(v => v.Vid))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,Auto"), ColumnSpacing = 8 };
                row.Children.Add(Ui.Text(vlan.Vid.ToString(), 13, true));
                var name = Ui.Text(vlan.Name, 13); Grid.SetColumn(name, 1); row.Children.Add(name);
                var cidr = Ui.Text(vlan.Subnet?.Cidr ?? "—", 12);
                Grid.SetColumn(cidr, 2); row.Children.Add(cidr);
                var button = Ui.Button("", () => OpenVlan(site.Id, vlan.Id)); button.Content = row;
                button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0, 0, 0, 1);
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                details.Children.Add(button);
            }
            if (site.Vlans.Count == 0) details.Children.Add(Ui.Text("Aucun VLAN. Créez le premier plan d’adressage.", 12));

            var siteActions = Ui.Row(
                Ui.Button("Modifier le site", () => Run(() => EditSite(site)), CanEdit),
                Ui.Button("Ajouter un VLAN", () => Run(() => EditVlan(site, null)), CanEdit));
            siteActions.HorizontalAlignment = HorizontalAlignment.Center;

            var content = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
            content.Children.Add(details);
            Grid.SetRow(siteActions, 2); content.Children.Add(siteActions);

            var card = Ui.Card(content);
            card.Margin = new Thickness(0);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.VerticalAlignment = VerticalAlignment.Stretch;
            _siteCards.Children.Add(card);
        }

        var summary = Ui.Text($"{Db.Sites.Count} sites  ·  {count} VLAN  ·  {used} IP utilisées", 13);
        if (Db.Sites.Count == 0)
        {
            _body.Content = Ui.Scroll(Ui.Column(summary, Ui.Card(Ui.Column(Ui.Text("Bienvenue dans G@IP", 22, true),
                Ui.Text("Créez un site, ajoutez ses VLAN et définissez vos sous-réseaux. Votre base est actuellement vide."),
                Ui.Button("Ajouter un site", () => Run(() => EditSite(null)), CanEdit)))));
            return;
        }

        _siteCardsScroll = new ScrollViewer
        {
            Name = "SiteCardsScroll",
            Content = _siteCards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        _siteCardsScroll.SizeChanged += (_, _) => ResizeCards();

        var home = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 8 };
        home.Children.Add(summary);
        Grid.SetRow(_siteCardsScroll, 1); home.Children.Add(_siteCardsScroll);
        _body.Content = home;
        ResizeCards();
    }
    private void ResizeSearch() => _globalSearch.Width = Math.Clamp(Width - 900, 120, 440);
    private void ResizeCards()
    {
        if (_siteCards is null) return;

        const double minCardWidth = 430;
        const double gap = 8;
        var viewportWidth = _siteCardsScroll?.Bounds.Width ?? 0;
        var available = Math.Max(300, viewportWidth > 0 ? viewportWidth : Bounds.Width - 32);
        var fittingColumns = Math.Max(1, (int)Math.Floor((available + gap) / (minCardWidth + gap)));
        var columns = Math.Min(_config.MaxHomeColumns, Math.Min(fittingColumns, Math.Max(1, _siteCards.Children.Count)));

        _siteCards.Width = available;
        if (_siteCardsScroll is { Bounds.Height: > 0 } scroll)
            _siteCards.MinHeight = scroll.Bounds.Height;

        _siteCards.ColumnDefinitions.Clear();
        _siteCards.RowDefinitions.Clear();
        for (var column = 0; column < columns; column++)
            _siteCards.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var rows = (_siteCards.Children.Count + columns - 1) / columns;
        for (var row = 0; row < rows; row++)
            _siteCards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        for (var index = 0; index < _siteCards.Children.Count; index++)
        {
            Grid.SetColumn(_siteCards.Children[index], index % columns);
            Grid.SetRow(_siteCards.Children[index], index / columns);
        }
    }
    private void RenderSearch()
    {
        var results = Queries.Search(Db, _search.Text!).ToArray();
        _pageHeading.Content = Ui.Text("Recherche globale", 22, true);
        var stack = Ui.Column(Ui.Text($"{results.Length} résultat(s) · IP, sites, VLAN et descriptions", 12));
        foreach (var result in results.Take(1000))
        {
            var button = Ui.Button(result.Label, () =>
            {
                _selectedSite = result.SiteId; _selectedVlan = result.VlanId; _showFreeAddresses = false;
                _subnetSearch = result.Address ?? ""; _search.Text = ""; Render();
            });
            button.HorizontalAlignment = HorizontalAlignment.Stretch; stack.Children.Add(button);
        }
        if (results.Length > 1000) stack.Children.Add(Ui.Text("Les 1 000 premiers résultats sont affichés. Précisez votre recherche."));
        _body.Content = Ui.Scroll(stack);
    }
    private void OpenVlan(Guid site, Guid vlan)
    { _selectedSite = site; _selectedVlan = vlan; _showFreeAddresses = false; _subnetSearch = ""; _search.Text = ""; Render(); }

    private void RenderVlan(Site site, Vlan vlan)
    {
        _pageHeading.Content = Ui.Text($"{site.Code} / VLAN {vlan.Vid} — {vlan.Name}", 22, true);
        var stack = new StackPanel { Spacing = 8 };
        if (vlan.Subnet is not { } subnet)
        {
            if (!string.IsNullOrWhiteSpace(vlan.Description)) stack.Children.Add(Ui.Text(vlan.Description));
            stack.Children.Add(Ui.Card(Ui.Text("Ce VLAN n’a pas de sous-réseau. Vous pouvez en définir un dans sa fiche.")));
            _body.Content = Ui.Scroll(stack); return;
        }
        var network = Ipv4Network.Parse(subnet.Cidr);
        var summary = new StackPanel { Spacing = 4 };
        var networkLine = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 16 };
        networkLine.Children.Add(Ui.Text(subnet.Cidr, 16, true));
        var mask = Ui.Text($"Masque : {network.DottedMask}", 13); Grid.SetColumn(mask, 1); networkLine.Children.Add(mask);
        var totals = Ui.Text($"{Queries.UsedCount(subnet):N0} IP utilisées / {Queries.FreeCount(subnet):N0} libres", 13);
        Grid.SetColumn(totals, 2); networkLine.Children.Add(totals); summary.Children.Add(networkLine);
        var details = new StackPanel { Name = "NetworkDetails", Spacing = 4, IsVisible = false };
        details.Children.Add(Ui.Text(NetworkSummary(network), 12));
        if (!string.IsNullOrWhiteSpace(vlan.Description)) details.Children.Add(Ui.Text(vlan.Description, 12));
        var gatewayLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        gatewayLine.Children.Add(Ui.Text(subnet.Gateway is { } g ? $"Passerelle : {g.Address} · {g.Comment}" : "Aucune passerelle", 13));
        var toggle = Ui.Button("Détails réseau ▾", () => { details.IsVisible = !details.IsVisible; });
        toggle.Click += (_, _) => toggle.Content = details.IsVisible ? "Détails réseau ▴" : "Détails réseau ▾";
        toggle.Padding = new Thickness(4, 2); toggle.Background = Brushes.Transparent; toggle.BorderThickness = new Thickness(0);
        Grid.SetColumn(toggle, 1); gatewayLine.Children.Add(toggle);
        summary.Children.Add(gatewayLine); summary.Children.Add(details);
        stack.Children.Add(new Border { Child = summary, Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(5),
            BorderBrush = new SolidColorBrush(Color.Parse("#65758B"), .35), BorderThickness = new Thickness(1) });
        var showFree = new CheckBox { Content = "Afficher les adresses libres", IsChecked = _showFreeAddresses };
        var search = Ui.Input(_subnetSearch, "Adresse IP, hostname ou description");
        var count = Ui.Text("", 12);
        var table = new ListBox
        {
            Name = "AddressList", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<AddressRow>((row, _) =>
            {
                // Avalonia clears a container's content when it leaves the viewport.
                if (row is null) return null;
                var button = Ui.Button("", () => Run(() => row.IsGateway ? EditVlan(site, vlan) : EditAddress(site.Id, vlan.Id, row)));
                button.Content = AddressGrid(row.Address, row.Hostname, row.Description, row.IsGateway);
                button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0, 0, 0, 1);
                button.Padding = new Thickness(8, 2); button.Height = 34;
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                if (!row.IsUsed) button.Opacity = .7;
                button.IsEnabled = CanEdit; ToolTip.SetTip(button, $"{row.Address} · {row.Hostname} · {row.Description}");
                return button;
            })
        };
        table.Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters =
        {
            new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
            new Setter(ListBoxItem.PaddingProperty, new Thickness(0)), new Setter(ListBoxItem.MinHeightProperty, 34d)
        } });
        void Rows()
        {
            try
            {
                var rows = new AddressRows(subnet, _showFreeAddresses, _subnetSearch);
                table.ItemsSource = rows;
                count.Text = rows.Count == 0 ? "Aucune adresse à afficher." : $"{rows.Count:N0} adresse(s) · {network.UsableCount:N0} utilisables dans le sous-réseau";
            }
            catch (InvalidOperationException ex) { table.ItemsSource = Array.Empty<AddressRow>(); count.Text = ex.Message; }
        }
        showFree.IsCheckedChanged += (_, _) => { _showFreeAddresses = showFree.IsChecked == true; Rows(); };
        // Debounce range search so a large network is not scanned on every keystroke.
        var searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); Rows(); };
        table.DetachedFromVisualTree += (_, _) => searchTimer.Stop();
        search.TextChanged += (_, _) => { _subnetSearch = search.Text ?? ""; searchTimer.Stop(); searchTimer.Start(); };
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        filters.Children.Add(Ui.SearchField(search, "Filtrer les adresses IP, hostnames ou descriptions"));
        Grid.SetColumn(showFree, 1); filters.Children.Add(showFree);
        var add = Ui.Button("Ajouter une IP", () => Run(() => EditAddress(site.Id, vlan.Id, null)), CanEdit);
        Grid.SetColumn(add, 2); filters.Children.Add(add); stack.Children.Add(filters);
        stack.Children.Add(AddressGrid("Adresse IP", "Nom / Hostname", "Description", true));
        // Finite viewport is essential: never put the virtualized list in an outer ScrollViewer.
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        layout.Children.Add(stack); Grid.SetRow(table, 1); layout.Children.Add(table); Rows();
        count.Margin = new Thickness(0, 4); Grid.SetRow(count, 2); layout.Children.Add(count);
        _body.Content = layout;
    }
    private static Grid AddressGrid(string ip, string name, string description, bool bold)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("155,220,*"), ColumnSpacing = 12, Margin = new Thickness(4) };
        grid.Children.Add(Ui.Text(ip, 13, bold)); var n = Ui.Text(name, 13, bold); Grid.SetColumn(n, 1); grid.Children.Add(n);
        var d = Ui.Text(description, 13); Grid.SetColumn(d, 2); grid.Children.Add(d); return grid;
    }
    private static string NetworkSummary(Ipv4Network n) => $"Masque : {n.DottedMask}\nRéseau : {Ipv4Network.Format(n.Network)}   ·   Broadcast : {Ipv4Network.Format(n.Broadcast)}\n" +
        (n.UsableCount == 0 ? "Aucune adresse utilisable (/31 et /32)." : $"Première : {Ipv4Network.Format(n.First)}   ·   Dernière : {Ipv4Network.Format(n.Last)}   ·   {n.UsableCount:N0} adresses utilisables");
    private async Task Message(string title, string text)
    {
        var form = new FormWindow(title, "Fermer"); form.Fields.Children.Add(Ui.Text(text)); await form.ShowDialog<bool>(this);
    }
    private async Task<bool> Confirm(string title, string text, bool strong = false)
    {
        var form = new FormWindow(title, "Confirmer"); form.Fields.Children.Add(Ui.Text(text));
        if (strong)
        {
            var input = Ui.Input(); form.Add("Confirmation : tapez LIBÉRER", input);
            form.Submit = () => { if (input.Text != "LIBÉRER") throw new InvalidOperationException("Tapez exactement LIBÉRER."); return Task.CompletedTask; };
        }
        return await form.ShowDialog<bool>(this);
    }
}

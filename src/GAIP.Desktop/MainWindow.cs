using Avalonia;
using Avalonia.Controls;
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
    private readonly WrapPanel _actions = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime _lastSync;
    private bool _refreshing;
    private bool _closing;
    private Guid? _selectedSite;
    private Guid? _selectedVlan;
    private bool _showFreeAddresses;
    private string _subnetSearch = "";
    private WrapPanel? _siteCards;
    public DataSession? Session => _session;

    public MainWindow() : this(UserPaths.Root, UserPaths.ConfigRoot) { }
    public MainWindow(string localRoot, string configRoot)
    {
        _localRoot = localRoot; _configRoot = configRoot;
        Title = "G@IP — Gestion d’Adresses IP"; Width = 1320; Height = 850; MinWidth = 760; MinHeight = 540;
        Icon = Branding.Icon;
        var brand = new Grid { ColumnDefinitions = new ColumnDefinitions("88,*"), ColumnSpacing = 8 };
        brand.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(8),
            Child = new Image { Source = Branding.Logo, Width = 80, Height = 80, Stretch = Stretch.Uniform } });
        var brandName = Ui.Column(Ui.Text("G@IP", 27, true), Ui.Text("Gestion d’Adresses IP", 12));
        Grid.SetColumn(brandName, 1); brand.Children.Add(brandName);
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*,Auto"), ColumnSpacing = 24 };
        heading.Children.Add(brand);
        _search.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(_search, 1); heading.Children.Add(_search);
        var account = Ui.Column(Ui.Text(UserPaths.User, 12), Ui.Button("Configuration", () => Run(Configure)));
        Grid.SetColumn(account, 2); heading.Children.Add(account);
        _actions.Children.Add(Ui.Button("Actualiser", () => Run(Refresh)));
        _status.MaxLines = 2; _status.TextTrimming = TextTrimming.CharacterEllipsis;
        var top = Ui.Column(heading, _actions, _status); top.Margin = new Thickness(24, 20, 24, 8);
        var footer = new DockPanel { Margin = new Thickness(24, 8) };
        DockPanel.SetDock(_revision, Dock.Right); footer.Children.Add(_revision);
        var root = new DockPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        _body.Margin = new Thickness(24, 8, 8, 0); root.Children.Add(_body); Content = root;
        _search.TextChanged += (_, _) => Render();
        SizeChanged += (_, _) => ResizeCards();
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
        _revision.Text = $"Révision {Db.Revision}  ·  {Db.Sites.Count} sites";
        _actions.Children.Clear();
        _actions.Children.Add(Ui.Button("Accueil", () => { _selectedSite = null; _selectedVlan = null; _search.Text = ""; Render(); }));
        _actions.Children.Add(Ui.Button("Actualiser", () => Run(Refresh)));
        if (_config.Mode == StorageMode.Shared)
            _actions.Children.Add(Ui.Button(CanEdit ? "Terminer la modification" : "Passer en modification", () => Run(async () =>
            { if (CanEdit) await Io(_session.EndEdit); else await Io(_session.BeginEdit); }), !_session.IsOffline || CanEdit));
        Guid? context = string.IsNullOrWhiteSpace(_search.Text) && Db.Sites.Any(s => s.Vlans.Any(v => v.Id == _selectedVlan)) ? _selectedVlan : null;
        if (context is null)
        {
            _actions.Children.Add(Ui.Button("Ajouter un site", () => Run(() => EditSite(null)), CanEdit));
            _actions.Children.Add(Ui.Button("Ajouter un VLAN", () => Run(() => EditVlan(null, null)), CanEdit && Db.Sites.Count > 0));
        }
        _actions.Children.Add(Ui.Button("CSV", () => Run(() => CsvDialog(context))));
        _actions.Children.Add(Ui.Button("Historique", () => Run(() => History(context))));
        foreach (var action in _actions.Children) action.Margin = new Thickness(0, 0, 8, 6);
        ToolTip.SetTip(_status, _status.Text);
        _siteCards = null;
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
        var title = Ui.Text(_selectedSite is null ? "Plans d’adressage" : "Site sélectionné", 24, true);
        var count = Db.Sites.Sum(s => s.Vlans.Count);
        var used = Db.Sites.Sum(s => s.Vlans.Sum(v => v.Subnet is null ? 0 : Queries.UsedCount(v.Subnet)));
        _siteCards = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var site in Db.Sites.Where(s => _selectedSite is null || s.Id == _selectedSite).OrderBy(s => s.Code))
        {
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
            header.Children.Add(Ui.Text(site.Name, 18, true));
            var stats = Ui.Text($"{site.Vlans.Count} VLAN / {site.Vlans.Sum(v => v.Subnet is null ? 0 : Queries.UsedCount(v.Subnet))} IP utilisées", 11);
            Grid.SetColumn(stats, 1); header.Children.Add(stats);
            var content = Ui.Column(header, Ui.Text(site.Code + (site.Description.Length > 0 ? " · " + site.Description : ""), 12));
            foreach (var vlan in site.Vlans.OrderBy(v => v.Vid))
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("48,*,Auto"), ColumnSpacing = 8 };
                row.Children.Add(Ui.Text(vlan.Vid.ToString(), 13, true));
                var name = Ui.Text(vlan.Name, 13); Grid.SetColumn(name, 1); row.Children.Add(name);
                var cidr = Ui.Text(vlan.Subnet?.Cidr ?? "—", 12); Grid.SetColumn(cidr, 2); row.Children.Add(cidr);
                var button = Ui.Button("", () => OpenVlan(site.Id, vlan.Id)); button.Content = row;
                button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0, 0, 0, 1);
                button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                content.Children.Add(button);
            }
            if (site.Vlans.Count == 0) content.Children.Add(Ui.Text("Aucun VLAN. Créez le premier plan d’adressage.", 12));
            content.Children.Add(Ui.Row(Ui.Button("Modifier le site", () => Run(() => EditSite(site)), CanEdit),
                Ui.Button("Ajouter un VLAN", () => Run(() => EditVlan(site, null)), CanEdit)));
            _siteCards.Children.Add(Ui.Card(content));
        }
        var stack = Ui.Column(title, Ui.Text($"{Db.Sites.Count} sites  ·  {count} VLAN  ·  {used} IP utilisées", 13), _siteCards);
        if (Db.Sites.Count == 0) stack.Children.Add(Ui.Card(Ui.Column(Ui.Text("Bienvenue dans G@IP", 22, true),
            Ui.Text("Créez un site, ajoutez ses VLAN et définissez vos sous-réseaux. Votre base est actuellement vide."),
            Ui.Button("Ajouter un site", () => Run(() => EditSite(null)), CanEdit))));
        _body.Content = Ui.Scroll(stack); ResizeCards();
    }
    private void ResizeCards()
    {
        if (_siteCards is null) return;
        var available = Math.Max(300, Bounds.Width - 64);
        int columns = available >= 1500 ? 3 : available >= 980 ? 2 : 1;
        foreach (var child in _siteCards.Children) child.Width = available / columns - 16;
    }
    private void RenderSearch()
    {
        var results = Queries.Search(Db, _search.Text!).ToArray();
        var stack = Ui.Column(Ui.Text("Recherche globale", 24, true), Ui.Text($"{results.Length} résultat(s) · IP, sites, VLAN et descriptions", 12));
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
        var stack = Ui.Column(Ui.Text($"{site.Code} / VLAN {vlan.Vid} — {vlan.Name}", 24, true), Ui.Text(vlan.Description),
            Ui.Row(Ui.Button("← Tous les sites", () => { _selectedVlan = null; _selectedSite = null; Render(); }),
                Ui.Button("Modifier le VLAN / réseau", () => Run(() => EditVlan(site, vlan)), CanEdit)));
        if (vlan.Subnet is not { } subnet)
        { stack.Children.Add(Ui.Card(Ui.Text("Ce VLAN n’a pas de sous-réseau. Vous pouvez en définir un dans sa fiche."))); _body.Content = Ui.Scroll(stack); return; }
        var network = Ipv4Network.Parse(subnet.Cidr);
        stack.Children.Add(Ui.Card(Ui.Column(Ui.Text(subnet.Cidr, 22, true), Ui.Text(NetworkSummary(network)),
            Ui.Text(subnet.Gateway is { } g ? $"Passerelle : {g.Address} · {g.Comment}" : "Aucune passerelle"),
            Ui.Text($"{Queries.UsedCount(subnet)} IP utilisées   /   {Queries.FreeCount(subnet)} IP libres", 15, true))));
        var showFree = new CheckBox { Content = "Afficher les adresses libres", IsChecked = _showFreeAddresses };
        var search = Ui.Input(_subnetSearch, "Adresse IP, hostname ou description"); search.Width = 300;
        var count = Ui.Text("", 12);
        var table = new ListBox
        {
            Name = "AddressList", HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsPanel = new FuncTemplate<Panel>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<AddressRow>((row, _) =>
            {
                var button = Ui.Button("", () => Run(() => row!.IsGateway ? EditVlan(site, vlan) : EditAddress(site.Id, vlan.Id, row)));
                button.Content = AddressGrid(row!.Address, row.Hostname, row.Description, row.IsGateway);
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
        stack.Children.Add(Ui.Row(showFree, search, Ui.Button("Ajouter une IP", () => Run(() => EditAddress(site.Id, vlan.Id, null)), CanEdit)));
        stack.Children.Add(count);
        stack.Children.Add(AddressGrid("Adresse IP", "Nom / Hostname", "Description", true));
        // Finite viewport is essential: never put the virtualized list in an outer ScrollViewer.
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(stack); Grid.SetRow(table, 1); layout.Children.Add(table); Rows();
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
            var input = Ui.Input("", "Tapez LIBÉRER"); form.Add("Confirmation explicite", input);
            form.Submit = () => { if (input.Text != "LIBÉRER") throw new InvalidOperationException("Tapez exactement LIBÉRER."); return Task.CompletedTask; };
        }
        return await form.ShowDialog<bool>(this);
    }
}

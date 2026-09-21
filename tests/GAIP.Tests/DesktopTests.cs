using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using GAIP.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GAIP.Tests.TestAppBuilder))]

namespace GAIP.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false });
}

public sealed partial class DesktopTests
{
    private static GAIP.Core.MulticastFlow groupFlow(ListBox list, int port) =>
        list.Items.Cast<GAIP.Core.MulticastFlow>().Single(flow => flow.Port == port);


    private static Button Button(Control control, string label) => control.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == label);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static Task UntilReady(MainWindow main) => Until(() => main.Session?.HasData == true &&
        main.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Ajouter un site"));
    private static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("UI operation timed out."); await Task.Delay(20); }
    }
    [AvaloniaFact]
    public async Task MainWindowCreatesSiteVlanGatewayAndAddressThroughForms()
    {
        using var temp = new TempDirectory(); var main = new MainWindow(temp.Sub("data"), temp.Sub("config")); main.Show();
        await UntilReady(main);
        Click(Button(main, "Ajouter un site")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any());
        var form = main.OwnedWindows.OfType<FormWindow>().Last(); var fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "LEVANT"; fields[1].Text = "Île du Levant"; fields[2].Text = "Site test";
        await Until(() => form.Save.IsEnabled); Click(form.Save);
        await Until(() => main.Session!.Data.Sites.Count == 1 && !form.IsVisible &&
            main.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Plans d’adressage"));
        Click(Button(main, "Ajouter un VLAN")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible); fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "120"; fields[1].Text = "SERVEURS";
        form.Fields.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        fields[3].Text = "10.20.120.0/24"; fields[4].Text = "10.20.120.1"; fields[5].Text = "Pare-feu";
        await Until(() => form.Save.IsEnabled); Click(form.Save);
        await Until(() => main.Session!.Data.Sites[0].Vlans.Count == 1 && !form.IsVisible &&
            main.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Plans d’adressage"));
        var vlanButton = main.GetLogicalDescendants().OfType<Button>().First(b => b.Content is Grid g && g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "10.20.120.0/24"));
        Click(vlanButton); Click(Button(main, "Ajouter une IP")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible); fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "10.20.120.25"; fields[1].Text = "SRV-App-01";
        await Until(() => form.Save.IsEnabled); Click(form.Save); await Until(() => main.Session!.Data.Sites[0].Vlans[0].Subnet!.Addresses.Count == 1 && !form.IsVisible);
        Assert.Equal(3, main.Session!.Data.Revision);
        await Until(() => main.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "PASSERELLE"));

        Click(Button(main, "Modifier le VLAN"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var cidr = form.Fields.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Text == "10.20.120.0/24");
        Assert.True(cidr.IsReadOnly);
        Assert.False(cidr.IsEnabled);
        form.Close(false); await Until(() => !form.IsVisible);

        var list = main.GetLogicalDescendants().OfType<ListBox>().Single(l => l.Name == "AddressList");
        await Until(() => main.GetVisualDescendants().OfType<Border>().Any(b =>
            b.Tag is GAIP.Core.AddressRow row && row.Address == "10.20.120.25"));
        var assigned = main.GetVisualDescendants().OfType<Border>().Single(b =>
            b.Tag is GAIP.Core.AddressRow row && row.Address == "10.20.120.25");
        var gateway = main.GetVisualDescendants().OfType<Border>().Single(b =>
            b.Tag is GAIP.Core.AddressRow row && row.IsGateway);
        Assert.Null(gateway.ContextMenu);
        Assert.NotNull(assigned.ContextMenu);
        var menuItems = assigned.ContextMenu!.ItemsSource!.Cast<MenuItem>().ToArray();
        Assert.Equal(new[] { "Modifier", "Libérer" }, menuItems.Select(i => i.Header as string).ToArray());
        list.SelectedItem = list.Items.Cast<GAIP.Core.AddressRow>().Single(row => row.Address == "10.20.120.25");
        Assert.Equal("10.20.120.25", ((GAIP.Core.AddressRow)list.SelectedItem!).Address);

        Click(menuItems.Single(i => i.Header as string == "Modifier"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Assert.Contains("Modifier l’adresse IP", form.Title);
        form.Close(false); await Until(() => !form.IsVisible);

        Click(menuItems.Single(i => i.Header as string == "Libérer"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var confirm = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Assert.Contains("Libérer l’adresse", confirm.Title);
        Click(confirm.Save);
        await Until(() => main.Session!.Data.Sites[0].Vlans[0].Subnet!.Addresses.Count == 0 && !confirm.IsVisible);

        Click(Button(main, "Modifier le VLAN"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Click(Button(form, "Supprimer le VLAN"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Count(w => w.IsVisible) == 2);
        confirm = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Click(confirm.Save);
        await Until(() => main.Session!.Data.Sites[0].Vlans.Count == 0 && !form.IsVisible &&
            main.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Plans d’adressage"));

        Click(Button(main, "Modifier le site"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Click(Button(form, "Supprimer le site"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Count(w => w.IsVisible) == 2);
        confirm = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Click(confirm.Save);
        await Until(() => main.Session!.Data.Sites.Count == 0 && !form.IsVisible &&
            main.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "Plans d’adressage"));

        main.Close(); await Until(() => !main.IsVisible);
    }
    [AvaloniaFact]
    public async Task RenderHomeAndNetworkScreenshots()
    {
        using var temp = new TempDirectory(); var data = temp.Sub("data"); var repo = new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc");
        var db = TestData.Example(); db.Sites[0].Vlans.Add(new() { Vid = 150, Name = "TRANSIT" });
        TestData.Subnet(db).Gateway = new() { Address = "10.20.120.1", Comment = "Firewall principal" };
        TestData.Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRV-APP-01", Description = "Serveur applicatif" });
        var second = TestData.Example("10.30.120.0/24").Sites[0]; second.Code = "COUDON"; second.Name = "Coudon"; db.Sites.Add(second);
        repo.Initialize(db);
        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await UntilReady(main);
        await Task.Delay(150);
        Assert.DoesNotContain(main.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "255.255.255.0");
        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots")); Directory.CreateDirectory(output);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "home.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        main.Width = 800; await Task.Delay(100);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "home-narrow.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        main.Width = 1320; Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; await Task.Delay(100);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "home-dark.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var globalSearch = main.GetLogicalDescendants().OfType<TextBox>().Single();
        Assert.True(globalSearch.Focus()); globalSearch.Text = "10";
        Assert.True(globalSearch.IsFocused); globalSearch.Text = "10.20";
        Assert.True(globalSearch.IsFocused);
        var clearSearch = main.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "ClearSearch");
        Click(clearSearch);
        Assert.Equal("", globalSearch.Text);
        var vlanButton = main.GetLogicalDescendants().OfType<Button>().First(b => b.Content is Grid g && g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "10.20.120.0/24"));
        Click(vlanButton); await Task.Delay(100);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "subnet.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        main.Close(); await Until(() => !main.IsVisible);
    }

    [AvaloniaFact]
    public async Task HomeColumnsRespectConfiguredMaximumAndKeepActionsAtCardBottom()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var local = temp.Sub("data/local");
        var configRoot = temp.Sub("config");
        var sites = Enumerable.Range(1, 6).Select(index => new GAIP.Core.Site
        {
            Code = $"S{index}",
            Name = $"Site {index}",
            Vlans = Enumerable.Range(1, index % 3 + 1)
                .Select(vid => new GAIP.Core.Vlan { Vid = vid, Name = $"VLAN {vid}" })
                .ToList()
        }).ToList();
        new GAIP.Storage.FileRepository(local, "test", "pc")
            .Initialize(new GAIP.Core.Database { Sites = sites });
        GAIP.Storage.UserPaths.SaveConfig(configRoot, new GAIP.Storage.AppConfig { MaxHomeColumns = 8 });

        var main = new MainWindow(data, configRoot) { Width = 2800 };
        main.Show();
        await UntilReady(main);
        var cards = main.GetLogicalDescendants().OfType<Grid>().Single(g => g.Name == "SiteCards");
        Assert.All(main.GetLogicalDescendants().OfType<StackPanel>().Where(panel => panel.Name == "HomeVlanRows"),
            panel => Assert.Equal(2, panel.Spacing));
        Assert.All(main.GetLogicalDescendants().OfType<Button>().Where(button => button.Name == "HomeVlanRow"),
            button => Assert.Equal(new Thickness(8, 4), button.Padding));
        await Until(() => cards.ColumnDefinitions.Count == 6);
        Assert.Equal(6, cards.ColumnDefinitions.Count);
        Assert.Equal(8, main.Session!.Config.MaxHomeColumns);

        main.Width = 1320;
        await Until(() => cards.ColumnDefinitions.Count == 2);
        await Task.Delay(50);
        Assert.Equal(2, cards.ColumnDefinitions.Count);

        var first = Assert.IsType<Border>(cards.Children[0]);
        var second = Assert.IsType<Border>(cards.Children[1]);
        Assert.InRange(Math.Abs(first.Bounds.Height - second.Bounds.Height), 0, 0.5);

        var scroll = main.GetLogicalDescendants().OfType<ScrollViewer>().Single(s => s.Name == "SiteCardsScroll");
        Assert.True(cards.Bounds.Height >= scroll.Bounds.Height - 1);
        Assert.All(cards.RowDefinitions, row => Assert.Equal(GridUnitType.Star, row.Height.GridUnitType));

        var firstContent = Assert.IsType<Grid>(first.Child);
        Assert.Equal(3, firstContent.RowDefinitions.Count);
        var actions = firstContent.Children.OfType<WrapPanel>().Single();
        Assert.Equal(2, Grid.GetRow(actions));
        Assert.Equal(HorizontalAlignment.Center, actions.HorizontalAlignment);
        Assert.Contains(actions.Children.OfType<Button>(), b => b.Content as string == "Modifier le site");
        Assert.Contains(actions.Children.OfType<Button>(), b => b.Content as string == "Ajouter un VLAN");

        main.Close();
        await Until(() => !main.IsVisible);
    }

    [AvaloniaFact]
    public async Task MultiSiteFormBlocksOverlapsAndCreatesIndependentVlans()
    {
        using var temp = new TempDirectory(); var data = temp.Sub("data");
        var repo = new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc");
        repo.Initialize(new() { Sites = [new() { Code = "A", Name = "Alpha" }, new() { Code = "B", Name = "Beta" }] });
        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await UntilReady(main);
        Click(Button(main, "Ajouter un VLAN")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "120"; fields[1].Text = "SERVEURS";
        foreach (var check in form.Fields.GetLogicalDescendants().OfType<CheckBox>()) check.IsChecked = true;
        fields[3].Text = "10.20.120.0/24"; fields[6].Text = "10.20.120.0/24";
        await Until(() => form.Error.Text?.Contains("chevauche") == true); Assert.False(form.Save.IsEnabled);
        fields[6].Text = "10.30.120.0/24"; await Until(() => form.Save.IsEnabled); Click(form.Save);
        await Until(() => main.Session!.Data.Sites.All(s => s.Vlans.Count == 1) && !form.IsVisible);
        var first = main.Session!.Data.Sites[0].Vlans[0]; var second = main.Session.Data.Sites[1].Vlans[0]; Assert.NotEqual(first.Id, second.Id);
        var vlanButton = main.GetLogicalDescendants().OfType<Button>().First(b => b.Content is Grid g && g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "10.20.120.0/24"));
        Click(vlanButton); Click(Button(main, "Modifier le VLAN")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible); fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[1].Text = "APPLICATIONS"; await Task.Delay(50); Click(form.Save);
        await Until(() => main.Session.Data.Sites[0].Vlans[0].Name == "APPLICATIONS" && !form.IsVisible);
        Assert.Equal("SERVEURS", main.Session.Data.Sites[1].Vlans[0].Name);
        main.Close(); await Until(() => !main.IsVisible);
    }

    [AvaloniaFact]
    public async Task HistorySearchFiltersHeadersAndChangeDetails()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var local = temp.Sub("data/local");
        var repo = new GAIP.Storage.FileRepository(local, "test", "pc");
        var snapshot = repo.Initialize(TestData.Example());
        var changed = GAIP.Storage.JsonData.Clone(snapshot.Data);
        changed.Sites[0].Description = "Premier changement";
        snapshot = repo.Commit(changed, snapshot.Hash, null, "Modification", "Site", "LEVANT").Snapshot;
        changed = GAIP.Storage.JsonData.Clone(snapshot.Data);
        TestData.Subnet(changed).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRV-SEARCH" });
        repo.Commit(changed, snapshot.Hash, null, "Attribution", "IP", "10.20.120.25");

        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await UntilReady(main);
        Click(Button(main, "Historique")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var search = form.Fields.GetLogicalDescendants().OfType<TextBox>().Single(t => t.Name == "HistorySearch");
        var entries = form.Fields.GetLogicalDescendants().OfType<Expander>().ToArray();
        Assert.Equal(2, entries.Length);

        search.Text = "SRV-SEARCH";
        await Until(() => entries.Count(e => e.IsVisible) == 1 &&
            (entries.Single(e => e.IsVisible).Header as string)?.Contains("Attribution", StringComparison.Ordinal) == true);
        var attribution = entries.Single(e => e.IsVisible);
        var details = Assert.IsType<TextBox>(attribution.Content).Text!;
        Assert.Contains("Existence", details);
        Assert.Contains("Hostname", details);
        Assert.Contains("SRV-SEARCH", details);
        Assert.DoesNotContain("IP 10.20.120.25", details);

        search.Text = "Modification LEVANT";
        await Until(() => entries.Count(e => e.IsVisible) == 1 &&
            (entries.Single(e => e.IsVisible).Header as string)?.Contains("Modification", StringComparison.Ordinal) == true);

        search.Text = "";
        await Until(() => entries.All(e => e.IsVisible));
        form.Close(false); main.Close(); await Until(() => !main.IsVisible);
    }


    [AvaloniaFact]
    public async Task MulticastCardOpensGroupAndShowsFlows()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var db = TestData.Example();
        TestData.Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRC-VIDEO" });
        db.MulticastGroups.Add(new()
        {
            Address = "239.10.20.15",
            Name = "VIDEO",
            Description = "Diffusion vidéo",
            Flows = [new() { Port = 5004, Content = "Vidéo principale", Sources = ["10.20.120.25"], VlanIds = [db.Sites[0].Vlans[0].Id] }]
        });
        new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc").Initialize(db);

        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await UntilReady(main);
        var card = main.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "MulticastCard");
        var row = card.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "MulticastGroupRow");
        Click(row);
        await Until(() => main.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Modifier le multicast"));
        Assert.Contains(main.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "239.10.20.15 — VIDEO");
        await Until(() => main.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "MulticastFlow_5004"));
        var flowList = main.GetLogicalDescendants().OfType<ListBox>().Single(list => list.Name == "MulticastFlowList");
        var flow = groupFlow(flowList, 5004);
        flowList.SelectedItem = flow;
        Assert.Same(flow, flowList.SelectedItem);
        var flowRow = main.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MulticastFlow_5004");
        Assert.NotNull(flowRow.ContextMenu);
        Assert.Equal(new[] { "Modifier", "Supprimer" },
            flowRow.ContextMenu!.ItemsSource!.Cast<MenuItem>().Select(item => item.Header as string).ToArray());
        main.Close(); await Until(() => !main.IsVisible);
    }


    [AvaloniaFact]
    public async Task MulticastFlowEditorKeepsSelectionsAboveVirtualizedFilteredAvailableItems()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var db = TestData.Example("10.20.112.0/20");
        TestData.Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRC-VIDEO" });
        TestData.Subnet(db).Addresses.Add(new() { Address = "10.20.120.26", Hostname = "SRC-AUDIO" });
        var network = GAIP.Core.Ipv4Network.Parse("10.20.112.0/20");
        for (var index = 0; index < 1000; index++)
        {
            var address = GAIP.Core.Ipv4Network.Format(network.First + (uint)index);
            TestData.Subnet(db).Addresses.Add(new() { Address = address, Hostname = $"BULK-{index:D4}" });
        }
        db.Sites[0].Vlans.Add(new() { Vid = 130, Name = "AUDIO" });
        db.MulticastGroups.Add(new()
        {
            Address = "239.10.20.15", Name = "VIDEO",
            Flows = [new()
            {
                Port = 5004, Content = "Vidéo",
                Sources = ["10.20.120.25"],
                VlanIds = [db.Sites[0].Vlans[0].Id]
            }]
        });
        new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc").Initialize(db);

        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await UntilReady(main);
        var card = main.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "MulticastCard");
        Click(card.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "MulticastGroupRow"));
        await Until(() => main.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "MulticastFlow_5004"));
        var flowRow = main.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "MulticastFlow_5004");
        Click(flowRow.ContextMenu!.ItemsSource!.Cast<MenuItem>().Single(item => item.Header as string == "Modifier"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);

        var sourceSearch = form.Fields.GetLogicalDescendants().OfType<TextBox>()
            .First(box => box.Name == "MulticastSourceSearch");
        var vlanSearch = form.Fields.GetLogicalDescendants().OfType<TextBox>()
            .First(box => box.Name == "MulticastVlanSearch");
        var sourceSelected = form.Fields.GetLogicalDescendants().OfType<ListBox>()
            .First(list => list.Name == "MulticastSourceSearchSelected");
        var sourceAvailable = form.Fields.GetLogicalDescendants().OfType<ListBox>()
            .First(list => list.Name == "MulticastSourceSearchAvailable");
        var vlanSelected = form.Fields.GetLogicalDescendants().OfType<ListBox>()
            .First(list => list.Name == "MulticastVlanSearchSelected");
        var vlanAvailable = form.Fields.GetLogicalDescendants().OfType<ListBox>()
            .First(list => list.Name == "MulticastVlanSearchAvailable");

        Assert.Single(sourceSelected.Items);
        Assert.True(sourceAvailable.Items.Count > 1000);
        sourceAvailable.BringIntoView(); form.UpdateLayout();
        await Until(() => sourceAvailable.GetVisualDescendants().OfType<CheckBox>().Any());
        Assert.InRange(sourceAvailable.GetVisualDescendants().OfType<CheckBox>().Count(), 1, 50);

        sourceSearch.Text = "SRC-AUDIO";
        await Until(() => sourceAvailable.Items.Count == 1);
        Assert.Single(sourceSelected.Items);
        sourceSelected.BringIntoView(); form.UpdateLayout();
        await Until(() => sourceSelected.GetVisualDescendants().OfType<CheckBox>().Any());
        var source25 = sourceSelected.GetVisualDescendants().OfType<CheckBox>().Single();
        Assert.StartsWith("10.20.120.25", source25.Content as string);
        source25.IsChecked = false;
        await Until(() => sourceSelected.Items.Count == 0 && sourceAvailable.Items.Count == 1);

        sourceAvailable.BringIntoView(); form.UpdateLayout();
        await Until(() => sourceAvailable.GetVisualDescendants().OfType<CheckBox>()
            .Any(check => (check.Content as string)?.StartsWith("10.20.120.26", StringComparison.Ordinal) == true));
        var source26 = sourceAvailable.GetVisualDescendants().OfType<CheckBox>()
            .Single(check => (check.Content as string)!.StartsWith("10.20.120.26", StringComparison.Ordinal));
        source26.IsChecked = true;
        await Until(() => sourceSelected.Items.Count == 1 && sourceAvailable.Items.Count == 0);

        Assert.Single(vlanSelected.Items);
        Assert.Single(vlanAvailable.Items);
        vlanSearch.Text = "130";
        await Until(() => vlanAvailable.Items.Count == 1);
        vlanSelected.BringIntoView(); form.UpdateLayout();
        await Until(() => vlanSelected.GetVisualDescendants().OfType<CheckBox>().Any());
        var vlan120 = vlanSelected.GetVisualDescendants().OfType<CheckBox>().Single();
        Assert.Contains("VLAN 120", vlan120.Content as string);
        vlanAvailable.BringIntoView(); form.UpdateLayout();
        await Until(() => vlanAvailable.GetVisualDescendants().OfType<CheckBox>().Any());
        var vlan130 = vlanAvailable.GetVisualDescendants().OfType<CheckBox>().Single();
        Assert.Contains("VLAN 130", vlan130.Content as string);

        form.Close(false); main.Close(); await Until(() => !main.IsVisible);
    }

    [AvaloniaFact]
    public async Task MulticastTilesAreBalancedCappedAndUseSiteDisplayOrder()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var configRoot = temp.Sub("config");
        var db = TestData.Example();
        db.Sites[0].DisplayOrder = 1;
        TestData.Subnet(db).Addresses.Add(new() { Address = "10.20.120.25", Hostname = "SRC-VIDEO" });
        var coudon = new GAIP.Core.Site
        {
            Code = "COUDON", Name = "Mont Coudon", DisplayOrder = 0,
            Vlans = [new() { Vid = 310, Name = "VIDEO" }]
        };
        db.Sites.Add(coudon);

        for (var index = 1; index <= 17; index++)
            db.MulticastGroups.Add(new()
            {
                Address = $"239.10.0.{index}",
                Name = $"MCAST-{index:D2}",
                Flows = index == 1
                    ? [new()
                    {
                        Port = 5004, Content = "Vidéo",
                        Sources = ["10.20.120.25"],
                        VlanIds = [db.Sites[0].Vlans[0].Id]
                    }]
                    : []
            });

        new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc").Initialize(db);
        GAIP.Storage.UserPaths.SaveConfig(configRoot, new GAIP.Storage.AppConfig { MulticastHomeTiles = 8 });
        var main = new MainWindow(data, configRoot); main.Show(); await UntilReady(main);

        var cards = main.GetLogicalDescendants().OfType<Border>().Where(b => b.Name == "MulticastCard").ToArray();
        Assert.Equal(3, cards.Length);
        Assert.Equal(new[] { 6, 6, 5 }, cards.Select(card =>
            card.GetLogicalDescendants().OfType<Button>().Count(button => button.Name == "MulticastGroupRow")).ToArray());
        Assert.All(cards, card =>
        {
            Assert.Contains(card.GetLogicalDescendants().OfType<Button>(),
                button => button.Content as string == "Ajouter un multicast");
            Assert.All(card.GetLogicalDescendants().OfType<StackPanel>()
                    .Where(panel => panel.Name == "MulticastGroupRows"),
                panel => Assert.Equal(2, panel.Spacing));
            Assert.All(card.GetLogicalDescendants().OfType<Button>()
                    .Where(button => button.Name == "MulticastGroupRow"),
                button => Assert.Equal(new Thickness(8, 4), button.Padding));
        });

        var firstGroup = cards[0].GetLogicalDescendants().OfType<Button>().First(button => button.Name == "MulticastGroupRow");
        Click(firstGroup);
        await Until(() => main.GetVisualDescendants().OfType<Border>().Any(border => border.Name == "MulticastFlow_5004"));

        var siteHeaders = main.GetLogicalDescendants().OfType<TextBlock>()
            .Where(text => text.Name?.StartsWith("MulticastSiteHeader_", StringComparison.Ordinal) == true)
            .Select(text => text.Text).ToArray();
        Assert.Equal(new[] { "COUDON", "LEVANT" }, siteHeaders);

        var coudonMark = main.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Name == $"MulticastSite_5004_{coudon.Id:N}");
        var levantMark = main.GetLogicalDescendants().OfType<TextBlock>()
            .Single(text => text.Name == $"MulticastSite_5004_{db.Sites[0].Id:N}");
        Assert.Equal("✕", coudonMark.Text);
        Assert.Equal("✓", levantMark.Text);
        Assert.Equal(Color.Parse("#EF4444"), Assert.IsType<SolidColorBrush>(coudonMark.Foreground).Color);
        Assert.Equal(Color.Parse("#22C55E"), Assert.IsType<SolidColorBrush>(levantMark.Foreground).Color);

        Click(Button(main, "Accueil"));
        Click(Button(main, "Configuration"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(window => window.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(window => window.IsVisible);
        var multicastTiles = form.Fields.GetLogicalDescendants().OfType<NumericUpDown>()
            .First(control => control.Name == "MulticastHomeTiles");
        Assert.Equal(3, multicastTiles.Maximum);
        Assert.Equal(3, multicastTiles.Value);
        form.Close(false);

        main.Close(); await Until(() => !main.IsVisible);
    }

    [AvaloniaFact]
    public async Task ConfigurationMovesLocalToSharedThenEmptyLocalWithBackup()
    {
        using var temp = new TempDirectory(); var data = temp.Sub("data"); var configRoot = temp.Sub("config"); var central = temp.Sub("central");
        new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc").Initialize(TestData.Example());
        var main = new MainWindow(data, configRoot); main.Show(); await UntilReady(main);
        Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var tabs = form.Fields.GetLogicalDescendants().OfType<TabControl>().Single(t => t.Name == "ConfigurationTabs");
        Assert.Equal(new[] { "Stockage & synchronisation", "Affichage", "Données & export" },
            tabs.Items.Cast<TabItem>().Select(t => t.Header as string).ToArray());
        form.Fields.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Name == "StorageMode").SelectedIndex = 1;
        form.Fields.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Name == "SharedMigration").SelectedIndex = 1;
        form.Fields.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Name == "Theme").SelectedIndex = 2;
        form.Fields.GetLogicalDescendants().OfType<TextBox>().First(t => t.Name == "SharedPath").Text = central;
        Click(form.Save); await Until(() => main.Session!.Config.Mode == GAIP.Storage.StorageMode.Shared && !form.IsVisible);
        Assert.Single(main.Session!.Data.Sites); Assert.False(main.Session.IsEditing);
        Assert.Equal(GAIP.Storage.AppTheme.Dark, GAIP.Storage.UserPaths.LoadConfig(configRoot).Theme);
        Assert.DoesNotContain(main.GetLogicalDescendants().OfType<Button>(), b => b.Content as string is "Passer en modification" or "Terminer la modification");
        Assert.False(File.Exists(main.Session.Repository.LockPath));
        Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        form.Fields.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Name == "StorageMode").SelectedIndex = 0;
        form.Fields.GetLogicalDescendants().OfType<ComboBox>().First(c => c.Name == "LocalMigration").SelectedIndex = 1;
        Click(form.Save);
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Count(w => w.IsVisible) == 2);
        Click(main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible).Save);
        await Until(() => main.Session!.Config.Mode == GAIP.Storage.StorageMode.Local && !form.IsVisible);
        Assert.Empty(main.Session!.Data.Sites); Assert.False(File.Exists(System.IO.Path.Combine(central, "edit.lock")));
        Assert.NotEmpty(Directory.GetFiles(System.IO.Path.Combine(data, "local", "backup")));
        main.Close(); await Until(() => !main.IsVisible);
    }
    [AvaloniaFact]
    public async Task CreationFormsOfferSaveAndAddAnother()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var db = TestData.Example();
        db.MulticastGroups.Add(new() { Address = "239.10.20.15", Name = "VIDEO" });
        new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc").Initialize(db);
        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await UntilReady(main);

        Click(Button(main, "Ajouter un VLAN"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Assert.NotNull(Button(form, "Enregistrer et en ajouter un autre"));
        form.Close(false); await Until(() => !form.IsVisible);

        var vlanButton = main.GetLogicalDescendants().OfType<Button>().First(b => b.Content is Grid g &&
            g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "10.20.120.0/24"));
        Click(vlanButton); Click(Button(main, "Ajouter une IP"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[1].Text = "SRV-01";
        await Until(() => form.Save.IsEnabled);
        var first = fields[0].Text;
        Click(Button(form, "Enregistrer et en ajouter un autre"));
        await Until(() => TestData.Subnet(main.Session!.Data).Addresses.Count == 1);
        await Until(() => fields[0].Text != first);
        Assert.True(form.IsVisible);
        Assert.NotEqual(first, fields[0].Text);
        Assert.Equal("", fields[1].Text);
        form.Close(false); await Until(() => !form.IsVisible);

        Click(Button(main, "Accueil")); Click(Button(main, "Ajouter un multicast"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Assert.NotNull(Button(form, "Enregistrer et en ajouter un autre"));
        form.Close(false); await Until(() => !form.IsVisible);

        var card = main.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "MulticastCard");
        Click(card.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "MulticastGroupRow"));
        await Until(() => main.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "Ajouter un flux"));
        Click(Button(main, "Ajouter un flux"));
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        Assert.NotNull(Button(form, "Enregistrer et en ajouter un autre"));
        form.Close(false);

        main.Close(); await Until(() => !main.IsVisible);
    }

}

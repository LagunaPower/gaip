using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
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
        GAIP.Storage.UserPaths.SaveConfig(configRoot, new GAIP.Storage.AppConfig { MaxHomeColumns = 6 });

        var main = new MainWindow(data, configRoot) { Width = 2800 };
        main.Show();
        await UntilReady(main);
        var cards = main.GetLogicalDescendants().OfType<Grid>().Single(g => g.Name == "SiteCards");
        await Until(() => cards.ColumnDefinitions.Count == 6);
        Assert.Equal(6, cards.ColumnDefinitions.Count);

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
    public async Task ConfigurationMovesLocalToSharedThenEmptyLocalWithBackup()
    {
        using var temp = new TempDirectory(); var data = temp.Sub("data"); var configRoot = temp.Sub("config"); var central = temp.Sub("central");
        new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc").Initialize(TestData.Example());
        var main = new MainWindow(data, configRoot); main.Show(); await UntilReady(main);
        Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var combos = form.Fields.GetLogicalDescendants().OfType<ComboBox>().ToArray();
        combos[0].SelectedIndex = 1; combos[1].SelectedIndex = 1; combos[3].SelectedIndex = 2;
        form.Fields.GetLogicalDescendants().OfType<TextBox>().First().Text = central;
        Click(form.Save); await Until(() => main.Session!.Config.Mode == GAIP.Storage.StorageMode.Shared && !form.IsVisible);
        Assert.Single(main.Session!.Data.Sites); Assert.False(main.Session.IsEditing);
        Assert.Equal(GAIP.Storage.AppTheme.Dark, GAIP.Storage.UserPaths.LoadConfig(configRoot).Theme);
        Assert.DoesNotContain(main.GetLogicalDescendants().OfType<Button>(), b => b.Content as string is "Passer en modification" or "Terminer la modification");
        Assert.False(File.Exists(main.Session.Repository.LockPath));
        Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible); combos = form.Fields.GetLogicalDescendants().OfType<ComboBox>().ToArray();
        combos[0].SelectedIndex = 0; combos[2].SelectedIndex = 1; Click(form.Save);
        await Until(() => main.OwnedWindows.OfType<FormWindow>().Count(w => w.IsVisible) == 2);
        Click(main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible).Save);
        await Until(() => main.Session!.Config.Mode == GAIP.Storage.StorageMode.Local && !form.IsVisible);
        Assert.Empty(main.Session!.Data.Sites); Assert.False(File.Exists(System.IO.Path.Combine(central, "edit.lock")));
        Assert.NotEmpty(Directory.GetFiles(System.IO.Path.Combine(data, "local", "backup")));
        main.Close(); await Until(() => !main.IsVisible);
    }
}

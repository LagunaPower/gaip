using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using GAIP.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(GAIP.Tests.TestAppBuilder))]

namespace GAIP.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false });
}

public sealed class DesktopTests
{
    private static Button Button(Control control, string label) => control.GetLogicalDescendants().OfType<Button>().First(b => b.Content as string == label);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    private static async Task Until(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("UI operation timed out."); await Task.Delay(20); }
    }
    [AvaloniaFact]
    public async Task MainWindowCreatesSiteVlanGatewayAndAddressThroughForms()
    {
        using var temp = new TempDirectory(); var main = new MainWindow(temp.Sub("data"), temp.Sub("config")); main.Show();
        await Until(() => main.Session?.HasData == true);
        Click(Button(main, "+ Site")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any());
        var form = main.OwnedWindows.OfType<FormWindow>().Last(); var fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "LEVANT"; fields[1].Text = "Île du Levant"; fields[2].Text = "Site test";
        await Until(() => form.Save.IsEnabled); Click(form.Save); await Until(() => main.Session!.Data.Sites.Count == 1 && !form.IsVisible);
        Click(Button(main, "+ VLAN")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible); fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "120"; fields[1].Text = "SERVEURS";
        form.Fields.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        fields[3].Text = "10.20.120.0/24"; fields[4].Text = "10.20.120.1"; fields[5].Text = "Pare-feu";
        await Until(() => form.Save.IsEnabled); Click(form.Save); await Until(() => main.Session!.Data.Sites[0].Vlans.Count == 1 && !form.IsVisible);
        var vlanButton = main.GetLogicalDescendants().OfType<Button>().First(b => b.Content is Grid g && g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "10.20.120.0/24"));
        Click(vlanButton); Click(Button(main, "+ Ajouter une IP")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible); fields = form.Fields.GetLogicalDescendants().OfType<TextBox>().ToArray();
        fields[0].Text = "10.20.120.25"; fields[1].Text = "SRV-App-01";
        await Until(() => form.Save.IsEnabled); Click(form.Save); await Until(() => main.Session!.Data.Sites[0].Vlans[0].Subnet!.Addresses.Count == 1 && !form.IsVisible);
        Assert.Equal(3, main.Session!.Data.Revision);
        Assert.Contains(main.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "PASSERELLE");
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
        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await Until(() => main.Session?.HasData == true);
        await Task.Delay(150);
        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots")); Directory.CreateDirectory(output);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "home.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        main.Width = 800; await Task.Delay(100);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "home-narrow.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        main.Width = 1320; Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; await Task.Delay(100);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "home-dark.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        Application.Current!.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        var vlanButton = main.GetLogicalDescendants().OfType<Button>().First(b => b.Content is Grid g && g.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "10.20.120.0/24"));
        Click(vlanButton); await Task.Delay(100);
        using (var frame = main.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "subnet.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        main.Close(); await Until(() => !main.IsVisible);
    }

    [AvaloniaFact]
    public async Task MultiSiteFormBlocksOverlapsAndCreatesIndependentVlans()
    {
        using var temp = new TempDirectory(); var data = temp.Sub("data");
        var repo = new GAIP.Storage.FileRepository(temp.Sub("data/local"), "test", "pc");
        repo.Initialize(new() { Sites = [new() { Code = "A", Name = "Alpha" }, new() { Code = "B", Name = "Beta" }] });
        var main = new MainWindow(data, temp.Sub("config")); main.Show(); await Until(() => main.Session?.HasData == true);
        Click(Button(main, "+ VLAN")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
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
        Click(vlanButton); Click(Button(main, "Modifier le VLAN / réseau")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
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
        var main = new MainWindow(data, configRoot); main.Show(); await Until(() => main.Session?.HasData == true);
        Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        var combos = form.Fields.GetLogicalDescendants().OfType<ComboBox>().ToArray();
        combos[0].SelectedIndex = 1; combos[1].SelectedIndex = 1; combos[3].SelectedIndex = 2;
        form.Fields.GetLogicalDescendants().OfType<TextBox>().First().Text = central;
        Click(form.Save); await Until(() => main.Session!.Config.Mode == GAIP.Storage.StorageMode.Shared && !form.IsVisible);
        Assert.Single(main.Session!.Data.Sites); Assert.False(main.Session.IsEditing);
        Assert.Equal(GAIP.Storage.AppTheme.Dark, GAIP.Storage.UserPaths.LoadConfig(configRoot).Theme);
        Click(Button(main, "Passer en modification")); await Until(() => main.Session.IsEditing); Assert.True(File.Exists(main.Session.Repository.LockPath));
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

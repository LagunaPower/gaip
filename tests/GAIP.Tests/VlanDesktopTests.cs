using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using GAIP.Core;
using GAIP.Desktop;
using GAIP.Storage;
using Xunit;

namespace GAIP.Tests;

public sealed partial class DesktopTests
{
    [AvaloniaFact]
    public async Task Slash22ViewScrollsFullRangeVirtuallyAndKeepsActionsContextual()
    {
        using var temp = new TempDirectory();
        var data = temp.Sub("data");
        var repo = new FileRepository(temp.Sub("data/local"), "test", "pc");

        var db = TestData.Example("10.20.120.0/22");
        var subnet = TestData.Subnet(db);

        subnet.Gateway = new()
        {
            Address = "10.20.120.1",
            Comment = "Firewall"
        };

        subnet.Addresses.Add(new()
        {
            Address = "10.20.123.254",
            Hostname = "LAST-IP"
        });

        var other = TestData.Example("10.30.120.0/24").Sites[0];
        other.Code = "OTHER";
        db.Sites.Add(other);

        var snap = repo.Initialize(db);

        var next = JsonData.Clone(snap.Data);
        next.Sites[1].Vlans[0].Name = "OTHER-EVENT";

        snap = repo.Commit(
            next,
            snap.Hash,
            null,
            "Unrelated",
            "VLAN",
            "120").Snapshot;

        next = JsonData.Clone(snap.Data);
        next.Sites[0].Vlans[0].Name = "SERVEURS-22";

        repo.Commit(
            next,
            snap.Hash,
            null,
            "ThisVlan",
            "VLAN",
            "120");

        var main = new MainWindow(data, temp.Sub("config"));
        main.Show();

        await UntilReady(main);

        Assert.NotNull(main.Icon);
        Assert.NotNull(Branding.Logo);

        var link = main
            .GetLogicalDescendants()
            .OfType<Button>()
            .First(b =>
                b.Content is Grid g &&
                g.GetLogicalDescendants()
                    .OfType<TextBlock>()
                    .Any(t => t.Text == "10.20.120.0/22"));

        Click(link);

        var list = main
            .GetLogicalDescendants()
            .OfType<ListBox>()
            .Single(l => l.Name == "AddressList");

        Assert.Equal(2, list.Items.Count);
        Assert.All(
            list.Items.Cast<AddressRow>(),
            r => Assert.True(r.IsUsed));

        Assert.Contains(
            main.GetLogicalDescendants().OfType<TextBlock>(),
            t => t.Text?.Contains("255.255.252.0") == true);

        var buttons = main
            .GetLogicalDescendants()
            .OfType<Button>()
            .Select(b => b.Content as string)
            .Where(s => s is not null)
            .ToArray();

        Assert.DoesNotContain("Ajouter un site", buttons);
        Assert.DoesNotContain("Ajouter un VLAN", buttons);

        Assert.Single(
            buttons,
            s => s == "Ajouter une IP");

        Assert.DoesNotContain(
            buttons,
            s => s!.StartsWith('+'));

        Assert.DoesNotContain(
            buttons,
            s => s!.Contains("Précédent") || s.Contains("Suivant"));

        Assert.DoesNotContain(
            buttons,
            s => s!.Contains("Tous les sites"));

        Assert.Single(
            buttons,
            s => s == "Modifier le VLAN");

        Assert.All(
            main.GetLogicalDescendants().OfType<TextBox>(),
            t => Assert.True(string.IsNullOrEmpty(t.PlaceholderText)));

        var show = main
            .GetLogicalDescendants()
            .OfType<CheckBox>()
            .Single(c =>
                c.Content as string == "Afficher les adresses libres");

        Assert.False(show.IsChecked);

        show.IsChecked = true;

        await Until(() => list.Items.Count == 1022);

        /*
         * Avalonia Headless ne matérialise pas toujours immédiatement
         * les ListBoxItem sous Linux.
         *
         * Attendre la création réelle d'au moins un conteneur visuel
         * évite un test dépendant du timing de rendu.
         */
        await Until(() =>
            list.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Any());

        // Keep the useful viewport large, including when the window is resized.
        Assert.InRange(
            list.TranslatePoint(default, main)!.Value.Y,
            150,
            300);

        Assert.True(
            list.Bounds.Height > 500,
            $"IP viewport is only {list.Bounds.Height}px high.");

        var logo = main
            .GetLogicalDescendants()
            .OfType<Image>()
            .Single(i => i.Name == "OfficialLogo");

        Assert.Equal(96, logo.Bounds.Height);

        var details = main
            .GetLogicalDescendants()
            .OfType<StackPanel>()
            .Single(p => p.Name == "NetworkDetails");

        Assert.False(details.IsVisible);

        Click(Button(main, "Détails réseau ▾"));
        await Task.Delay(50);

        Assert.True(details.IsVisible);

        Assert.Contains(
            details.GetLogicalDescendants().OfType<TextBlock>(),
            t => t.Text!.Contains("10.20.123.255"));

        Click(Button(main, "Détails réseau ▴"));
        await Task.Delay(50);

        Assert.False(details.IsVisible);

        Assert.IsType<VirtualizingStackPanel>(
            list.ItemsPanelRoot);

        Assert.InRange(
            list.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Count(),
            1,
            100);

        list.ScrollIntoView(1021);

        /*
         * Même principe après ScrollIntoView :
         * attendre que la dernière IP soit réellement matérialisée
         * au lieu d'utiliser un délai fixe.
         */
        await Until(() =>
            list.GetVisualDescendants()
                .OfType<TextBlock>()
                .Any(t => t.Text == "10.20.123.254"));

        Assert.Contains(
            list.GetVisualDescendants()
                .OfType<TextBlock>(),
            t => t.Text == "10.20.123.254");

        Assert.InRange(
            list.GetVisualDescendants()
                .OfType<ListBoxItem>()
                .Count(),
            1,
            100);

        var output = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(
                AppContext.BaseDirectory,
                "../../../../../artifacts/screenshots"));

        Directory.CreateDirectory(output);

        using (var frame = main.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);

            frame.Save(
                System.IO.Path.Combine(
                    output,
                    "slash22-scroll-end.png"),
                Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        main.Width = 760;
        main.Height = 540;

        await Task.Delay(100);

        Assert.True(
            list.Bounds.Height > 180,
            $"Small-window viewport is only {list.Bounds.Height}px high.");

        Assert.True(
            Button(main, "Ajouter une IP")
                .TranslatePoint(default, main)!
                .Value.X
            + Button(main, "Ajouter une IP").Bounds.Width
            <= main.Bounds.Width);

        using (var frame = main.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);

            frame.Save(
                System.IO.Path.Combine(
                    output,
                    "slash22-compact-small.png"),
                Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        main.Width = 1320;
        main.Height = 850;

        Avalonia.Application.Current!
            .RequestedThemeVariant =
            Avalonia.Styling.ThemeVariant.Dark;

        await Task.Delay(100);

        using (var frame = main.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);

            frame.Save(
                System.IO.Path.Combine(
                    output,
                    "slash22-compact-dark.png"),
                Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        Avalonia.Application.Current!
            .RequestedThemeVariant =
            Avalonia.Styling.ThemeVariant.Light;

        show.IsChecked = false;

        Assert.Equal(2, list.Items.Count);

        Click(Button(main, "Ajouter une IP"));

        await Until(() =>
            main.OwnedWindows
                .OfType<FormWindow>()
                .Any(w => w.IsVisible));

        var form = main
            .OwnedWindows
            .OfType<FormWindow>()
            .Last(w => w.IsVisible);

        Assert.All(
            form.Fields
                .GetLogicalDescendants()
                .OfType<TextBox>(),
            t => Assert.True(
                string.IsNullOrEmpty(t.PlaceholderText)));

        Click(Button(form, "Prochaine libre"));

        Assert.Equal(
            "10.20.120.2",
            form.Fields
                .GetLogicalDescendants()
                .OfType<TextBox>()
                .First()
                .Text);

        form.Close(false);

        await Until(() => !form.IsVisible);

        Click(Button(main, "CSV"));

        await Until(() =>
            main.OwnedWindows
                .OfType<FormWindow>()
                .Any(w => w.IsVisible));

        form = main
            .OwnedWindows
            .OfType<FormWindow>()
            .Last(w => w.IsVisible);

        Assert.Contains(
            "LEVANT / VLAN 120",
            form.Title);

        Assert.DoesNotContain(
            form.GetLogicalDescendants().OfType<Button>(),
            b => (b.Content as string)?.StartsWith("Importer") == true);

        Assert.NotNull(
            Button(form, "Exporter ce VLAN / réseau…"));

        form.Close(false);

        await Until(() => !form.IsVisible);

        Click(Button(main, "Historique"));

        await Until(() =>
            main.OwnedWindows
                .OfType<FormWindow>()
                .Any(w => w.IsVisible));

        form = main
            .OwnedWindows
            .OfType<FormWindow>()
            .Last(w => w.IsVisible);

        var history = form
            .GetLogicalDescendants()
            .OfType<Expander>()
            .ToArray();

        Assert.Single(history);

        Assert.Contains(
            "ThisVlan",
            history[0].Header as string);

        Assert.DoesNotContain(
            "OTHER-EVENT",
            ((TextBox)history[0].Content!).Text);

        form.Close(false);

        await Until(() => !form.IsVisible);

        main.Close();

        await Until(() => !main.IsVisible);
    }
}
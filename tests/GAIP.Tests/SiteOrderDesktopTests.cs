using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    public async Task SiteOrderTabIsWritableWithoutManualSharedEditLock()
    {
        using var temp = new TempDirectory(); var config = temp.Sub("config"); var central = temp.Sub("central");
        new FileRepository(central, "seed", "pc").Initialize(new() { Sites = [new() { Code = "A", Name = "Alpha" }, new() { Code = "B", Name = "Bravo" }] });
        UserPaths.SaveConfig(config, new() { Mode = StorageMode.Shared, SharedPath = central });
        var main = new MainWindow(temp.Sub("data"), config); main.Show(); await UntilReady(main);
        Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
        var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
        form.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 1; await Task.Delay(100);
        Assert.True(form.Save.IsEnabled); Assert.True(form.GetVisualDescendants().OfType<SiteOrderEditor>().Single().IsEnabled);
        Assert.False(main.Session!.IsEditing); Assert.False(File.Exists(main.Session.Repository.LockPath));
        form.Close(false); await Until(() => !form.IsVisible); main.Close(); await Until(() => !main.IsVisible);
    }
    [AvaloniaFact]
    public async Task DragDropSitesCanBeCancelledThenSavedAndRestoredOnHome()
    {
        using var temp = new TempDirectory(); var data = temp.Sub("data"); var config = temp.Sub("config");
        var repo = new FileRepository(temp.Sub("data/local"), "test", "pc");
        repo.Initialize(new() { Sites = [new() { Code = "A", Name = "Alpha" }, new() { Code = "B", Name = "Bravo" }, new() { Code = "C", Name = "Charlie" }] });
        var main = new MainWindow(data, config); main.Show(); await UntilReady(main);
        async Task<FormWindow> OpenOrder()
        {
            Click(Button(main, "Configuration")); await Until(() => main.OwnedWindows.OfType<FormWindow>().Any(w => w.IsVisible));
            var form = main.OwnedWindows.OfType<FormWindow>().Last(w => w.IsVisible);
            form.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = 1;
            await Task.Delay(100); return form;
        }
        static async Task Drag(FormWindow form, int from, int to, bool below)
        {
            var list = form.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "SiteOrderList");
            var source = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content == list.Items[from]);
            var target = list.GetVisualDescendants().OfType<ListBoxItem>().Single(i => i.Content == list.Items[to]);
            var start = source.TranslatePoint(new Point(20, source.Bounds.Height / 2), form)!.Value;
            var end = target.TranslatePoint(new Point(20, below ? target.Bounds.Height - 4 : 4), form)!.Value;
            form.MouseMove(start); form.MouseDown(start, MouseButton.Left);
            form.MouseMove(end, RawInputModifiers.LeftMouseButton); form.MouseUp(end, MouseButton.Left);
            await Task.Delay(100);
        }
        var form = await OpenOrder(); await Drag(form, 0, 2, true);
        var editor = form.GetVisualDescendants().OfType<SiteOrderEditor>().Single();
        var ids = main.Session!.Data.Sites.Select(s => s.Id).ToArray();
        Assert.Equal(new[] { ids[1], ids[2], ids[0] }, editor.OrderedIds);
        Click(Button(form, "Annuler")); await Until(() => !form.IsVisible);
        Assert.Equal(0, repo.Read().Data.Revision); Assert.All(main.Session.Data.Sites, s => Assert.Null(s.DisplayOrder));
        form = await OpenOrder(); await Drag(form, 2, 0, false);
        editor = form.GetVisualDescendants().OfType<SiteOrderEditor>().Single(); Assert.Equal(new[] { ids[2], ids[0], ids[1] }, editor.OrderedIds);
        var output = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/screenshots")); Directory.CreateDirectory(output);
        using (var frame = form.CaptureRenderedFrame()) { Assert.NotNull(frame); frame.Save(System.IO.Path.Combine(output, "site-display-order.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default); }
        Click(form.Save); await Until(() => !form.IsVisible);
        Assert.Equal(1, repo.Read().Data.Revision);
        Assert.Equal(new[] { "C", "A", "B" }, SiteOrdering.Ordered(repo.Read().Data.Sites).Select(s => s.Code));
        var labels = main.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).Where(t => t is "Alpha" or "Bravo" or "Charlie").ToArray();
        Assert.Equal(new[] { "Charlie", "Alpha", "Bravo" }, labels);
        form = await OpenOrder(); editor = form.GetVisualDescendants().OfType<SiteOrderEditor>().Single();
        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, editor.OrderedIds);
        form.Close(false); await Until(() => !form.IsVisible); main.Close(); await Until(() => !main.IsVisible);
    }
}

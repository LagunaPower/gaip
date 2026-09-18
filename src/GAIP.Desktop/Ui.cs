using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace GAIP.Desktop;

public static class Ui
{
    public static TextBlock Text(string text, double size = 14, bool bold = false) => new()
    {
        Text = text, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };
    public static TextBox Input(string value = "", string? hint = null, int max = 500) => new()
    { Text = value, PlaceholderText = hint, MaxLength = max, MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
    public static StackPanel Field(string label, Control input) => new() { Spacing = 5, Children = { Text(label, 12, true), input } };
    public static StackPanel Column(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (var child in children) stack.Children.Add(child);
        return stack;
    }
    public static WrapPanel Row(params Control[] children)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) { child.Margin = new Thickness(0, 0, 8, 8); row.Children.Add(child); }
        return row;
    }
    public static Button Button(string label, Action click, bool enabled = true)
    {
        var b = new Button { Content = label, IsEnabled = enabled, Padding = new Thickness(12, 8) };
        b.Click += (_, _) => click();
        return b;
    }
    public static Border Card(Control child) => new()
    {
        Child = child, Padding = new Thickness(18), Margin = new Thickness(0, 0, 16, 16),
        BorderBrush = new SolidColorBrush(Color.Parse("#65758B"), .35), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10)
    };
    public static ScrollViewer Scroll(Control child) => new()
    { Content = child, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}

public sealed class FormWindow : Window
{
    public StackPanel Fields { get; } = new() { Spacing = 14 };
    public TextBlock Error { get; } = Ui.Text("");
    public Button Save { get; }
    public Func<Task>? Submit { get; set; }
    public FormWindow(string title, string submit = "Enregistrer", double width = 610)
    {
        Title = $"G@IP — {title}"; Width = width; Height = 640; MinWidth = 400; MinHeight = 320;
        Icon = Branding.Icon;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Error.Foreground = Brushes.IndianRed;
        Save = Ui.Button(submit, async () =>
        {
            Save!.IsEnabled = false; Error.Text = "";
            try { if (Submit is not null) await Submit(); Close(true); }
            catch (Exception ex) { Error.Text = ex.Message; }
            finally { Save.IsEnabled = true; }
        });
        var footer = Ui.Column(Error, Ui.Row(Save, Ui.Button("Annuler", () => Close(false))));
        var dock = new DockPanel { Margin = new Thickness(24), LastChildFill = true };
        var heading = Ui.Text(title, 22, true); heading.Margin = new Thickness(0, 0, 0, 20);
        DockPanel.SetDock(heading, Dock.Top); dock.Children.Add(heading);
        DockPanel.SetDock(footer, Dock.Bottom); footer.Margin = new Thickness(0, 16, 0, 0); dock.Children.Add(footer);
        dock.Children.Add(Ui.Scroll(Fields)); Content = dock;
    }
    public void Add(string label, Control input) => Fields.Children.Add(Ui.Field(label, input));
}

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
    public static TextBox Input(string value = "", string? hint = null, int max = 500)
    {
        var input = new TextBox { Text = value, MaxLength = max, MinWidth = 180, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (hint is not null) ToolTip.SetTip(input, hint);
        return input;
    }
    public static Grid SearchField(TextBox input, string label)
    {
        Avalonia.Automation.AutomationProperties.SetName(input, label);
        input.Padding = new Thickness(30, 6, 32, 6);
        var grid = new Grid(); grid.Children.Add(input);
        grid.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M 12,7 A 5,5 0 1 1 2,7 A 5,5 0 1 1 12,7 M 11,11 L 16,16"),
            Stroke = Brushes.Gray, StrokeThickness = 1.5, Width = 18, Height = 18,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0), IsHitTestVisible = false
        });
        var clear = new Button
        {
            Name = "ClearSearch",
            Content = "×",
            FontSize = 18,
            Padding = new Thickness(0),
            Width = 26,
            Height = 26,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            IsEnabled = !string.IsNullOrEmpty(input.Text)
        };
        Avalonia.Automation.AutomationProperties.SetName(clear, $"Vider {label}");
        ToolTip.SetTip(clear, "Vider la recherche");
        clear.Click += (_, _) => { input.Text = ""; input.Focus(); };
        input.TextChanged += (_, _) => clear.IsEnabled = !string.IsNullOrEmpty(input.Text);
        grid.Children.Add(clear);
        return grid;
    }
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

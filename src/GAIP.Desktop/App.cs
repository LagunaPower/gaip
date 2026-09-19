using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;

namespace GAIP.Desktop;

public sealed class App : Application
{
    internal static string? StartupMessage { get; set; }

    public App() => Name = "GAIP";
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = StartupMessage is { } message ? StartupMessageWindow(message) : new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }

    private static Window StartupMessageWindow(string message)
    {
        var window = new Window
        {
            Title = "G@IP — Gestion d’Adresses IP",
            Icon = Branding.Icon,
            Width = 470,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        var close = Ui.Button("Fermer", window.Close);
        close.HorizontalAlignment = HorizontalAlignment.Center;
        window.Content = new Border
        {
            Padding = new Thickness(24),
            Child = Ui.Column(Ui.Text(message, 15, true), close)
        };
        return window;
    }
}

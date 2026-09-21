using Avalonia;

namespace GAIP.Desktop;

public static class Program
{
    private const string SingleInstanceName = "GAIP.SingleInstance";

    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = TryAcquireSingleInstance(SingleInstanceName);
        if (singleInstance is null)
        {
            App.StartupMessage = "G@IP est déjà en cours d’exécution.";
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return;
        }

        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        finally { singleInstance.ReleaseMutex(); }
    }

    public static Mutex? TryAcquireSingleInstance(string name)
    {
        var options = new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = false
        };
        var mutex = new Mutex(true, name, options, out var createdNew);
        if (createdNew) return mutex;
        mutex.Dispose();
        return null;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>();
#if GAIP_WINDOWS
        // Same Windows services as UsePlatformDetect, without its cross-platform package.
        builder.UseHarfBuzz().UseWin32().UseSkia();
#elif GAIP_LINUX
        // The Linux branch of UsePlatformDetect defaults to X11 (including WSLg).
        builder.UseHarfBuzz().UseX11().UseSkia();
#else
        builder.UsePlatformDetect();
#endif
#if !GAIP_WINDOWS
        if (ShouldUseNativeWayland(OperatingSystem.IsLinux(), Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            Environment.GetEnvironmentVariable("GAIP_USE_WAYLAND"))) builder = builder.UseWayland();
#endif
        return builder.WithInterFont().LogToTrace();
    }

    // X11/XWayland is the stable Linux default. Native Wayland remains opt-in.
    public static bool ShouldUseNativeWayland(bool isLinux, string? sessionType, string? forceWayland) =>
        isLinux &&
        string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) &&
        forceWayland == "1";
}

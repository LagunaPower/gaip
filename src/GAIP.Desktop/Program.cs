using Avalonia;

namespace GAIP.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
            Environment.GetEnvironmentVariable("GAIP_USE_X11"))) builder = builder.UseWayland();
#endif
        return builder.WithInterFont().LogToTrace();
    }

    // WSLg exposes WAYLAND_DISPLAY without declaring a native Wayland session.
    // Keep the default X11 backend in that case to permit X11/XWayland.
    public static bool ShouldUseNativeWayland(bool isLinux, string? sessionType, string? forceX11) =>
        isLinux && string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) && forceX11 != "1";
}

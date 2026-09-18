using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace GAIP.Desktop;

public static class Branding
{
    private static readonly Lazy<Bitmap> LogoBitmap = new(() => new Bitmap(AssetLoader.Open(new("avares://GAIP/Assets/gaip-logo.png"))));
    private static readonly Lazy<WindowIcon> AppIcon = new(() => new WindowIcon(AssetLoader.Open(new("avares://GAIP/Assets/GAIP.ico"))));
    public static Bitmap Logo => LogoBitmap.Value;
    public static WindowIcon Icon => AppIcon.Value;
}

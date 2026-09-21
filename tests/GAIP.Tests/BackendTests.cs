using GAIP.Desktop;
using Xunit;

namespace GAIP.Tests;

public sealed class BackendTests
{
    [Fact]
    public void SingleInstanceIsExclusivePerUserAndCanBeReacquiredAfterRelease()
    {
        var name = $"GAIP.Tests.{Guid.NewGuid():N}";
        using var first = Program.TryAcquireSingleInstance(name);
        Assert.NotNull(first);
        using var second = Program.TryAcquireSingleInstance(name);
        Assert.Null(second);
        first!.ReleaseMutex();
        first.Dispose();
        using var third = Program.TryAcquireSingleInstance(name);
        Assert.NotNull(third);
        third!.ReleaseMutex();
    }

    [Theory]
    [InlineData(false, "wayland", "1", false)]
    [InlineData(false, null, "1", false)]
    [InlineData(true, "wayland", null, false)]
    [InlineData(true, "wayland", "0", false)]
    [InlineData(true, "wayland", "1", true)]
    [InlineData(true, "x11", "1", false)]
    [InlineData(true, "", "1", false)]
    [InlineData(true, null, "1", false)]
    public void BackendDefaultsToX11AndRequiresExplicitWaylandOptIn(bool linux, string? session, string? forceWayland, bool native)
        => Assert.Equal(native, Program.ShouldUseNativeWayland(linux, session, forceWayland));
}

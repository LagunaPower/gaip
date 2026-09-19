using GAIP.Desktop;
using Xunit;

namespace GAIP.Tests;

public sealed class BackendTests
{
    [Theory]
    [InlineData(false, "wayland", null, false)]
    [InlineData(false, null, null, false)]
    [InlineData(true, "wayland", null, true)]
    [InlineData(true, "wayland", "0", true)]
    [InlineData(true, "wayland", "1", false)]
    [InlineData(true, "x11", null, false)]
    [InlineData(true, "", null, false)]
    [InlineData(true, null, null, false)]
    public void BackendDependsOnSessionTypeAndExplicitOverride(bool linux, string? session, string? forceX11, bool native)
        => Assert.Equal(native, Program.ShouldUseNativeWayland(linux, session, forceX11));
}

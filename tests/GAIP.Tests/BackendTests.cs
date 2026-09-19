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

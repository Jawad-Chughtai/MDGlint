using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class RenderPlanTests
{
    private const string File = @"C:\docs\readme.md";

    [Fact]
    public void ADifferentFileStartsAtTheTopAndClearsTheBar()
    {
        var plan = RenderPlan.For(File, @"C:\docs\other.md", new RemoteContentPolicy(false));

        Assert.Equal(new RenderPlan(true, false, true), plan);
    }

    [Fact]
    public void TheSameFileSavedAgainKeepsItsPlaceAndTheBar()
    {
        // Paths compare without case, as Windows does.
        var plan = RenderPlan.For(File, @"C:\DOCS\README.md", new RemoteContentPolicy(false));

        Assert.Equal(new RenderPlan(false, false, false), plan);
    }

    [Fact]
    public void AllowingRemoteContentClearsTheBar()
    {
        var policy = new RemoteContentPolicy(false);
        policy.AllowForFile(File);

        Assert.Equal(new RenderPlan(false, true, true), RenderPlan.For(File, File, policy));
    }

    [Fact]
    public void RequiresAPolicy()
    {
        Assert.Throws<ArgumentNullException>(() => RenderPlan.For(File, null, null!));
    }
}

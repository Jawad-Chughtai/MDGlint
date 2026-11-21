using MDGlint.Core.Models;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class RemoteContentPolicyTests
{
    private static readonly string FileA = Path.Combine(Path.GetTempPath(), "docs", "a.md");
    private static readonly string FileB = Path.Combine(Path.GetTempPath(), "docs", "b.md");

    [Fact]
    public void Settings_DefaultToBlocked()
    {
        Assert.False(new AppSettings().AllowRemoteContent);
    }

    [Fact]
    public void Blocked_ByDefault()
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);

        Assert.False(policy.AlwaysAllow);
        Assert.False(policy.IsAllowed(FileA));
    }

    [Fact]
    public void AlwaysAllow_AllowsEveryFile()
    {
        var policy = new RemoteContentPolicy(alwaysAllow: true);

        Assert.True(policy.AlwaysAllow);
        Assert.True(policy.IsAllowed(FileA));
        Assert.True(policy.IsAllowed(null));
    }

    [Fact]
    public void AllowForFile_AllowsOnlyThatFile()
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);

        policy.AllowForFile(FileA);

        Assert.True(policy.IsAllowed(FileA));
        Assert.False(policy.IsAllowed(FileB));
        Assert.False(policy.AlwaysAllow);
    }

    [Fact]
    public void AllowForFile_MatchesTheSameFileSpelledDifferently()
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);
        var folder = Path.GetDirectoryName(FileA)!;

        policy.AllowForFile(FileA);

        Assert.True(policy.IsAllowed(FileA.ToUpperInvariant()));
        Assert.True(policy.IsAllowed(Path.Combine(folder, "sub", "..", "a.md")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path.md")]
    public void IsAllowed_NoOrInvalidPath_IsBlocked(string? path)
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);

        Assert.False(policy.IsAllowed(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0path.md")]
    public void AllowForFile_InvalidPath_Throws(string path)
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);

        Assert.Throws<ArgumentException>(() => policy.AllowForFile(path));
    }

    [Fact]
    public void TurningOff_ForgetsPerFileChoices()
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);
        policy.AllowForFile(FileA);
        policy.SetAlwaysAllow(true);

        policy.SetAlwaysAllow(false);

        Assert.False(policy.AlwaysAllow);
        Assert.False(policy.IsAllowed(FileA));
    }

    [Fact]
    public void TurningOn_KeepsPerFileChoicesUntilTurnedOff()
    {
        var policy = new RemoteContentPolicy(alwaysAllow: false);
        policy.AllowForFile(FileA);

        policy.SetAlwaysAllow(true);

        Assert.True(policy.IsAllowed(FileB));
    }

    [Theory]
    [InlineData("https://example.com/a.png", true)]
    [InlineData("http://example.com/a.png", true)]
    [InlineData("HTTPS://EXAMPLE.COM/a.png", true)]
    [InlineData("http://127.0.0.1:8080/a.png", true)]
    [InlineData("https://app.mdglint.example/app.css", false)]
    [InlineData("https://APP.mdglint.example/app.css", false)]
    [InlineData("https://doc.mdglint.example/C/a.png", false)]
    [InlineData("data:image/png;base64,AAAA", false)]
    [InlineData("file:///C:/a.png", false)]
    [InlineData("ftp://example.com/a.png", false)]
    public void IsRemote_OnlyWebAddressesOutsideTheAppHosts(string address, bool expected)
    {
        Assert.Equal(expected, RemoteContentPolicy.IsRemote(new Uri(address), "app.mdglint.example", "doc.mdglint.example"));
    }

    [Fact]
    public void IsRemote_RelativeUri_IsFalse()
    {
        Assert.False(RemoteContentPolicy.IsRemote(new Uri("a.png", UriKind.Relative)));
    }

    [Fact]
    public void IsRemote_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => RemoteContentPolicy.IsRemote(null!));
        Assert.Throws<ArgumentNullException>(() => RemoteContentPolicy.IsRemote(new Uri("https://example.com/"), null!));
    }
}

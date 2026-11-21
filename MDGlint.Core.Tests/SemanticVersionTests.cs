using MDGlint.Core.Models;

namespace MDGlint.Core.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData(" 1.2.3 ", "1.2.3")]
    [InlineData("1.0.0-beta.1", "1.0.0-beta.1")]
    [InlineData("1.0.0+3f2a1b9", "1.0.0")]
    [InlineData("10.20.30-rc.1+build.5", "10.20.30-rc.1")]
    public void TryParse_ReadsVersions(string text, string expected)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0-beta..1")]
    [InlineData("V1.2.3")]
    [InlineData("1.2.3 beta")]
    [InlineData("99999999999.0.0")]
    public void TryParse_RejectsInvalidVersions(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
    }

    [Theory]
    // In order, oldest first, as the Semantic Versioning 2.0 specification lists them.
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.beta")]
    [InlineData("1.0.0-alpha.beta", "1.0.0-beta")]
    [InlineData("1.0.0-beta", "1.0.0-beta.2")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.3")]
    [InlineData("1.0.0-beta.11", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.9", "1.1.0")]
    [InlineData("1.9.0", "1.10.0")]
    [InlineData("1.99.99", "2.0.0")]
    public void CompareTo_OrdersVersions(string older, string newer)
    {
        SemanticVersion.TryParse(older, out var a);
        SemanticVersion.TryParse(newer, out var b);

        Assert.True(b > a);
        Assert.True(a < b);
        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Equals_IgnoresBuildMetadataAndTagPrefix()
    {
        SemanticVersion.TryParse("v1.2.3+abc", out var a);
        SemanticVersion.TryParse("1.2.3", out var b);

        Assert.Equal(a, b);
        Assert.True(a.Equals((object)b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.Equal(0, a.CompareTo(b));
    }

    [Fact]
    public void Compare_TreatsNullAndOtherTypesAsDifferent()
    {
        SemanticVersion.TryParse("1.0.0", out var version);

        Assert.Equal(1, version.CompareTo(null));
        Assert.False(version.Equals(null));
        Assert.False(version.Equals((object?)null));
        Assert.False(version.Equals("1.0.0"));
    }
}

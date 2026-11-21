using MDGlint.Core.Models;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class ThemeServiceTests
{
    [Theory]
    [InlineData("dark", ThemeKind.Dark)]
    [InlineData("DARK", ThemeKind.Dark)]
    [InlineData("light", ThemeKind.Light)]
    [InlineData("purple", ThemeKind.Light)]
    [InlineData(null, ThemeKind.Light)]
    public void StartsWithTheSavedTheme(string? saved, ThemeKind expected)
    {
        var service = new ThemeService(saved);

        Assert.Equal(expected, service.Theme);
        Assert.Equal(expected == ThemeKind.Dark, service.IsDark);
    }

    [Fact]
    public void Toggle_SwitchesBackAndForth()
    {
        var service = new ThemeService("light");

        service.Toggle();
        Assert.Equal(ThemeKind.Dark, service.Theme);

        service.Toggle();
        Assert.Equal(ThemeKind.Light, service.Theme);
    }

    [Theory]
    [InlineData(ThemeKind.Light, "light")]
    [InlineData(ThemeKind.Dark, "dark")]
    public void ToStorageValue_RoundTrips(ThemeKind theme, string stored)
    {
        Assert.Equal(stored, theme.ToStorageValue());
        Assert.Equal(theme, ThemeKindExtensions.FromStorageValue(stored));
    }
}

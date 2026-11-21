using System.Windows;
using MDGlint.Core.Models;
using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Tests;

public sealed class WindowPlacementTests
{
    // Two 1920x1080 monitors side by side, the second to the left of the main one.
    private static readonly WindowBounds Screen = new(-1920, 0, 3840, 1080);

    private static AppSettings Saved(double? left, double? top, double? width, double? height) =>
        new() { WindowLeft = left, WindowTop = top, WindowWidth = width, WindowHeight = height };

    private static WindowBounds? Restore(AppSettings settings) => WindowPlacement.Restore(settings, Screen, 640, 420);

    [Fact]
    public void OpensMaximizedAtTheDefaultSizeUntilTheWindowHasClosed()
    {
        var settings = new AppSettings();

        Assert.True(settings.WindowMaximized);
        Assert.Null(Restore(settings));
    }

    [Fact]
    public void RestoresTheSavedBounds()
    {
        Assert.Equal(new WindowBounds(100, 50, 900, 600), Restore(Saved(100, 50, 900, 600)));
        Assert.Equal(new WindowBounds(-1800, 10, 900, 600), Restore(Saved(-1800, 10, 900, 600))); // The other monitor.
    }

    [Theory]
    [InlineData(null, 50.0, 900.0, 600.0)]
    [InlineData(100.0, null, 900.0, 600.0)]
    [InlineData(100.0, 50.0, null, 600.0)]
    [InlineData(100.0, 50.0, 900.0, null)]
    [InlineData(double.NaN, 50.0, 900.0, 600.0)]
    [InlineData(100.0, double.PositiveInfinity, 900.0, 600.0)]
    [InlineData(100.0, 50.0, 0.0, 600.0)]
    [InlineData(100.0, 50.0, 900.0, -1.0)]
    public void IgnoresBoundsThatArentComplete(double? left, double? top, double? width, double? height)
    {
        Assert.Null(Restore(Saved(left, top, width, height)));
    }

    [Theory]
    [InlineData(5000.0, 50.0)] // Right of every monitor, as when one was unplugged.
    [InlineData(-4000.0, 50.0)]
    [InlineData(100.0, 2000.0)]
    [InlineData(100.0, -700.0)]
    [InlineData(1870.0, 50.0)] // Only 50 pixels showing.
    public void IgnoresBoundsThatAreOffScreen(double left, double top)
    {
        Assert.Null(Restore(Saved(left, top, 900, 600)));
    }

    [Fact]
    public void MovesAPartlyHiddenWindowBackOnScreen()
    {
        Assert.Equal(new WindowBounds(1020, 480, 900, 600), Restore(Saved(1500, 700, 900, 600)));
        Assert.Equal(new WindowBounds(-1920, 0, 900, 600), Restore(Saved(-2500, -200, 900, 600)));
    }

    [Fact]
    public void KeepsTheSizeWithinTheScreenAndTheMinimum()
    {
        Assert.Equal(new WindowBounds(-1920, 0, 3840, 1080), Restore(Saved(0, 0, 5000, 3000)));
        Assert.Equal(new WindowBounds(100, 50, 640, 420), Restore(Saved(100, 50, 200, 100)));
    }

    [Theory]
    [InlineData(WindowState.Maximized, true)]
    [InlineData(WindowState.Normal, false)]
    public void RemembersWhetherTheWindowWasMaximized(WindowState state, bool maximized)
    {
        var settings = new AppSettings { WindowMaximized = !maximized };

        WindowPlacement.Remember(settings, new Rect(10, 20, 800, 500), state);

        Assert.Equal(maximized, settings.WindowMaximized);
        Assert.Equal((10.0, 20.0, 800.0, 500.0), (settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void KeepsThePreviousChoiceWhenClosedMinimized(bool maximized)
    {
        var settings = new AppSettings { WindowMaximized = maximized };

        WindowPlacement.Remember(settings, new Rect(10, 20, 800, 500), WindowState.Minimized);

        Assert.Equal(maximized, settings.WindowMaximized);
        Assert.Equal(800, settings.WindowWidth);
    }

    [Fact]
    public void KeepsTheSavedBoundsWhenTheNewOnesArentUsable()
    {
        var settings = Saved(10, 20, 800, 500);

        WindowPlacement.Remember(settings, Rect.Empty, WindowState.Normal);
        WindowPlacement.Remember(settings, new Rect(double.NaN, 0, 800, 500), WindowState.Normal);
        WindowPlacement.Remember(settings, new Rect(0, 0, 0, 500), WindowState.Normal);

        Assert.Equal((10.0, 20.0, 800.0, 500.0), (settings.WindowLeft, settings.WindowTop, settings.WindowWidth, settings.WindowHeight));
    }

    [Fact]
    public void RejectsMissingSettings()
    {
        Assert.Throws<ArgumentNullException>(() => WindowPlacement.Restore(null!, Screen, 0, 0));
        Assert.Throws<ArgumentNullException>(() => WindowPlacement.Remember(null!, Rect.Empty, WindowState.Normal));
    }
}

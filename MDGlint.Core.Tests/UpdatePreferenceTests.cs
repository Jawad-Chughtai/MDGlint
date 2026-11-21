using MDGlint.Core.Models;

namespace MDGlint.Core.Tests;

public sealed class UpdatePreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, null, false)] // Opt-in: no choice anywhere means no checks.
    [InlineData(null, false, false)]
    [InlineData(null, true, true)] // Ticked in setup.
    [InlineData(false, true, false)] // Turned off in the app after ticking it in setup.
    [InlineData(true, false, true)] // Turned on in the app after leaving it unticked in setup.
    [InlineData(true, null, true)]
    public void IsEnabled_PrefersTheInAppChoice(bool? inApp, bool? setup, bool expected)
    {
        Assert.Equal(expected, UpdatePreference.IsEnabled(new AppSettings { CheckForUpdates = inApp }, setup));
    }

    [Fact]
    public void IsCheckDue_NeverWhenChecksAreOff()
    {
        Assert.False(UpdatePreference.IsCheckDue(new AppSettings(), setupChoice: null, Now));
        Assert.False(UpdatePreference.IsCheckDue(new AppSettings { CheckForUpdates = false }, setupChoice: true, Now));
    }

    [Fact]
    public void IsCheckDue_AtMostOnceADay()
    {
        var settings = new AppSettings { CheckForUpdates = true };
        Assert.True(UpdatePreference.IsCheckDue(settings, null, Now));

        settings.LastUpdateCheck = Now.AddHours(-23);
        Assert.False(UpdatePreference.IsCheckDue(settings, null, Now));

        settings.LastUpdateCheck = Now.AddHours(-24);
        Assert.True(UpdatePreference.IsCheckDue(settings, null, Now));
    }

    [Fact]
    public void IsCheckDue_WhenTheLastCheckIsInTheFuture()
    {
        var settings = new AppSettings { CheckForUpdates = true, LastUpdateCheck = Now.AddDays(3) };

        Assert.True(UpdatePreference.IsCheckDue(settings, null, Now));
    }
}

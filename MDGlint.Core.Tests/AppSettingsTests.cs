using MDGlint.Core.Models;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Deserialize_ReadsTheFileVersionOneZeroZeroWrites()
    {
        // settings.json exactly as MDGlint 1.0.0's first builds wrote it, before update settings
        // existed. Every later version must keep reading it (docs/decisions.md, "Updates keep working").
        var settings = AppSettingsSerializer.Deserialize("""
            {
              "Theme": "dark",
              "AllowRemoteContent": true
            }
            """);

        Assert.Equal("dark", settings.Theme);
        Assert.True(settings.AllowRemoteContent);
        Assert.Null(settings.CheckForUpdates);
        Assert.Null(settings.LastUpdateCheck);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("""{"Theme": 5}""")]
    [InlineData("null")]
    public void Deserialize_FallsBackToDefaults(string? json)
    {
        var settings = AppSettingsSerializer.Deserialize(json);

        Assert.Equal("light", settings.Theme);
        Assert.False(settings.AllowRemoteContent);
        Assert.Null(settings.CheckForUpdates);
    }

    [Fact]
    public void Deserialize_IgnoresSettingsItDoesNotKnow()
    {
        // A newer version's file, opened by an older one after a downgrade.
        var settings = AppSettingsSerializer.Deserialize("""{"Theme":"dark","SomeLaterSetting":[1,2,3]}""");

        Assert.Equal("dark", settings.Theme);
    }

    [Fact]
    public void Deserialize_NormalizesAnUnknownTheme()
    {
        Assert.Equal("light", AppSettingsSerializer.Deserialize("""{"Theme":"purple"}""").Theme);
    }

    [Fact]
    public void Serialize_RoundTripsEverySetting()
    {
        var original = new AppSettings
        {
            Theme = "dark",
            AllowRemoteContent = true,
            CheckForUpdates = false,
            LastUpdateCheck = new DateTimeOffset(2026, 11, 19, 8, 30, 0, TimeSpan.FromHours(-3)),
        };

        var copy = AppSettingsSerializer.Deserialize(AppSettingsSerializer.Serialize(original));

        Assert.Equal(original.Theme, copy.Theme);
        Assert.Equal(original.AllowRemoteContent, copy.AllowRemoteContent);
        Assert.Equal(original.CheckForUpdates, copy.CheckForUpdates);
        Assert.Equal(original.LastUpdateCheck, copy.LastUpdateCheck);
    }

    [Fact]
    public void Serialize_KeepsTheSettingNamesOlderVersionsRead()
    {
        var json = AppSettingsSerializer.Serialize(new AppSettings());

        Assert.Contains("\"Theme\"", json);
        Assert.Contains("\"AllowRemoteContent\"", json);
    }
}

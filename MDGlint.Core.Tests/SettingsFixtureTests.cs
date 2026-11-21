using System.Reflection;
using System.Text.Json;
using MDGlint.Core.Models;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

/// <summary>
/// Every released version's settings.json must still load after an update. Fixtures/settings holds
/// one file per release, as that version writes it with every setting changed from its default;
/// releasing adds the next one (docs/releasing.md). A fixture is never edited once released.
/// </summary>
public sealed class SettingsFixtureTests
{
    private static readonly string FixtureFolder = Path.Combine(AppContext.BaseDirectory, "Fixtures", "settings");

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(FixtureFolder, "*.json").Order())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EveryReleasedSettingsFileKeepsItsValues(string fixture)
    {
        var json = File.ReadAllText(Path.Combine(FixtureFolder, fixture));

        var rewritten = JsonDocument.Parse(AppSettingsSerializer.Serialize(AppSettingsSerializer.Deserialize(json))).RootElement;

        foreach (var setting in JsonDocument.Parse(json).RootElement.EnumerateObject())
        {
            Assert.True(rewritten.TryGetProperty(setting.Name, out var value), $"{fixture}: {setting.Name} was dropped.");
            Assert.Equal(Normalized(setting.Value), Normalized(value));
        }
    }

    [Fact]
    public void ThisVersionHasAFixture()
    {
        var version = AppVersion.ToDisplay(typeof(AppSettings).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

        Assert.True(File.Exists(Path.Combine(FixtureFolder, version + ".json")),
            $"Add Fixtures/settings/{version}.json: what this version writes, with every setting changed (docs/releasing.md).");
    }

    [Fact]
    public void TheLatestFixtureChangesEverySetting()
    {
        var latest = Directory.GetFiles(FixtureFolder, "*.json")
            .Select(f => (Version: SemanticVersion.TryParse(Path.GetFileNameWithoutExtension(f), out var v) ? v : null, File: f))
            .Where(f => f.Version is not null)
            .MaxBy(f => f.Version)!;
        var settings = AppSettingsSerializer.Deserialize(File.ReadAllText(latest.File));
        var defaults = new AppSettings();

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            Assert.NotEqual(property.GetValue(defaults), property.GetValue(settings));
        }
    }

    // Dates may be written with any offset; the moment is what matters.
    private static string Normalized(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var date)
            ? date.UtcDateTime.ToString("O")
            : value.GetRawText();
}

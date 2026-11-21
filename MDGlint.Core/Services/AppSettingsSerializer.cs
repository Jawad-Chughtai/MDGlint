using System.Text.Json;
using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

/// <summary>
/// Reads and writes <c>settings.json</c>. Reading never fails: a missing, damaged or unreadable
/// file gives the defaults, and settings a file doesn't mention keep theirs, so a file from any
/// older version still loads.
/// </summary>
public static class AppSettingsSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static AppSettings Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppSettings();
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings());
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public static string Serialize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return JsonSerializer.Serialize(Normalize(settings), WriteOptions);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings.Theme = ThemeKindExtensions.FromStorageValue(settings.Theme).ToStorageValue();
        return settings;
    }
}

using System.IO;

namespace MDGlint.Desktop.Helpers;

/// <summary>Where MDGlint keeps its settings and data (README, "Where MDGlint stores things").</summary>
public static class AppPaths
{
    /// <summary>
    /// Debug builds only: a folder that takes the place of <c>%APPDATA%</c> and
    /// <c>%LOCALAPPDATA%</c>, so the end-to-end tests never touch the real settings.
    /// </summary>
    public const string DebugDataFolderVariable = "MDGLINT_DEBUG_DATA_FOLDER";

    private static readonly (string Roaming, string Local) Folders = Resolve(
#if DEBUG
        Environment.GetEnvironmentVariable(DebugDataFolderVariable),
#else
        null,
#endif
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary><c>%APPDATA%\MDGlint</c>: settings.</summary>
    public static string RoamingFolder => Folders.Roaming;

    /// <summary><c>%LOCALAPPDATA%\MDGlint</c>: WebView2 data, downloaded updates, the error log.</summary>
    public static string LocalFolder => Folders.Local;

    public static string SettingsFile => Path.Combine(RoamingFolder, "settings.json");

    public static string WebView2Folder => Path.Combine(LocalFolder, "WebView2");

    public static string UpdatesFolder => Path.Combine(LocalFolder, "Updates");

    public static string ErrorLogFile => Path.Combine(LocalFolder, "startup-error.txt");

    internal static (string Roaming, string Local) Resolve(string? overrideFolder, string appData, string localAppData) =>
        string.IsNullOrWhiteSpace(overrideFolder)
            ? (Path.Combine(appData, "MDGlint"), Path.Combine(localAppData, "MDGlint"))
            : (Path.Combine(overrideFolder, "Roaming", "MDGlint"), Path.Combine(overrideFolder, "Local", "MDGlint"));
}

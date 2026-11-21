using System.IO;
using MDGlint.Core.Contracts;
using MDGlint.Core.Models;
using MDGlint.Core.Services;
using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Services;

/// <param name="settingsPath">The settings file.</param>
/// <param name="waitBeforeRetry">
/// Runs between save attempts while the file is in use, given the attempt that failed. Tests pass
/// their own, so they don't depend on timing.
/// </param>
public sealed class AppSettingsStore(string settingsPath, Action<int> waitBeforeRetry) : ISettingsStore
{
    public AppSettingsStore()
        : this(AppPaths.SettingsFile)
    {
    }

    public AppSettingsStore(string settingsPath)
        : this(settingsPath, WaitBriefly)
    {
    }

    /// <summary>The app's wait between attempts: about half a second in all.</summary>
    internal static void WaitBriefly(int attempt) => Thread.Sleep(TimeSpan.FromMilliseconds(50));

    public string SettingsPath { get; } = settingsPath;

    /// <summary>The saved settings, or the defaults when the file is missing, damaged or unreadable.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? AppSettingsSerializer.Deserialize(File.ReadAllText(SettingsPath))
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    /// <summary>How many times a save is tried while another program has the file open.</summary>
    internal const int Attempts = 10;

    private static readonly object Gate = new();

    private string TempPath => SettingsPath + ".tmp";

    /// <summary>
    /// Saves the settings, never throwing. The file is written in full to a temporary file and
    /// then swapped in, so a reader never sees half of it. While another program has the file open
    /// (an antivirus scan, a sync tool), it tries again briefly; if it still can't, the change
    /// stays in memory for this session only, rather than failing the click that made it.
    /// </summary>
    public void Save(AppSettings settings)
    {
        var json = AppSettingsSerializer.Serialize(settings);
        lock (Gate)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                    File.WriteAllText(TempPath, json);
                    File.Move(TempPath, SettingsPath, overwrite: true);
                    return;
                }
                catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < Attempts)
                {
                    waitBeforeRetry(attempt);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    TryDeleteTemp();
                    return;
                }
            }
        }
    }

    private void TryDeleteTemp()
    {
        try
        {
            File.Delete(TempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Held open too; the next save replaces it.
        }
    }
}

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace MDGlint.Desktop.Helpers;

/// <summary>
/// Every program MDGlint starts goes through here: the default browser for links, Explorer for
/// local files, and setup for an update.
/// </summary>
public static class ProcessLauncher
{
    /// <summary>
    /// Debug builds only: a file that records each launch (one <c>file|arguments</c> line) instead
    /// of starting it, so the end-to-end tests can check links and updates without opening anything.
    /// </summary>
    public const string DebugLaunchLogVariable = "MDGLINT_DEBUG_LAUNCH_LOG";

    private static readonly string? LaunchLog =
#if DEBUG
        Environment.GetEnvironmentVariable(DebugLaunchLogVariable);
#else
        null;
#endif

    /// <summary>Opens a URL or file with its default app.</summary>
    public static void OpenWithDefaultApp(string target) => OpenWithDefaultApp(target, LaunchLog);

    /// <summary>Shows a file or folder selected in Explorer.</summary>
    [ExcludeFromCodeCoverage(Justification = UnitTestsCannotLaunch)]
    public static void ShowInExplorer(string path) => ShowInExplorer(path, LaunchLog);

    [ExcludeFromCodeCoverage(Justification = UnitTestsCannotLaunch)]
    public static void Start(ProcessStartInfo startInfo) => Start(startInfo, LaunchLog);

    /// <summary>Why the public launchers are left to the end-to-end tests.</summary>
    internal const string UnitTestsCannotLaunch = "Forwards to the unit-tested overload with the real launch setting, so "
        + "calling it from a unit test would start a program; the end-to-end tests call it through the launch log.";

    internal static void OpenWithDefaultApp(string target, string? launchLog)
    {
        if (!string.IsNullOrWhiteSpace(target))
        {
            Start(new ProcessStartInfo(target) { UseShellExecute = true }, launchLog);
        }
    }

    internal static void ShowInExplorer(string path, string? launchLog) =>
        Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false }, launchLog);

    internal static void Start(ProcessStartInfo startInfo, string? launchLog) => Start(startInfo, launchLog, Process.Start);

    internal static void Start(ProcessStartInfo startInfo, string? launchLog, Func<ProcessStartInfo, Process?> start)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        if (launchLog is null)
        {
            // Null when the shell handed the target to a program that was already running.
            start(startInfo)?.Dispose();
            return;
        }

        File.AppendAllText(launchLog, $"{startInfo.FileName}|{startInfo.Arguments}{Environment.NewLine}");
    }
}

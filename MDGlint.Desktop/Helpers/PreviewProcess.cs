using System.IO;

namespace MDGlint.Desktop.Helpers;

/// <summary>
/// Recognises the WebView2 browser process that serves MDGlint's previews: the
/// <c>msedgewebview2.exe</c> started with MDGlint's data folder and no <c>--type=</c> (its
/// renderer, GPU and other helpers all have one). Every MDGlint window shares that one process.
/// </summary>
public static class PreviewProcess
{
    private const string DataDirArgument = "--user-data-dir=";

    /// <summary>
    /// Whether <paramref name="commandLine"/> is the browser process for
    /// <paramref name="webView2Folder"/> (WebView2 keeps its data in its <c>EBWebView</c> subfolder).
    /// </summary>
    public static bool IsBrowserFor(string? commandLine, string webView2Folder)
    {
        if (string.IsNullOrEmpty(commandLine) || commandLine.Contains("--type=", StringComparison.Ordinal))
        {
            return false;
        }

        var start = commandLine.IndexOf(DataDirArgument, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var value = commandLine[(start + DataDirArgument.Length)..];
        value = value.StartsWith('"')
            ? value[1..].Split('"')[0]
            : value.Split(' ')[0];
        var expected = Path.Combine(webView2Folder, "EBWebView");
        return string.Equals(Normalize(value), Normalize(expected), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the window that started the browser process is gone: no process with that ID, a
    /// different program now, or one started after the browser process (the ID was reused).
    /// </summary>
    public static bool IsOwnerGone(string? parentName, DateTime? parentStart, string mdglintName, DateTime browserStart) =>
        !string.Equals(parentName, mdglintName, StringComparison.OrdinalIgnoreCase)
        || parentStart is not { } started
        || started > browserStart;

    private static string Normalize(string path) => path.Trim().TrimEnd('\\', '/').Replace('/', '\\');
}

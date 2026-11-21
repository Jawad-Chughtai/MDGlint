namespace MDGlint.Core.Models;

/// <summary>
/// What the window shows instead of the preview when the Microsoft Edge WebView2 Runtime, which
/// draws it, is missing or fails to start. Both cases link to Microsoft's download page.
/// </summary>
public sealed record PreviewUnavailableNotice(string Title, string Message)
{
    /// <summary>Microsoft's WebView2 page, with the Evergreen runtime download.</summary>
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static PreviewUnavailableNotice RuntimeMissing { get; } = new(
        "MDGlint needs the Microsoft Edge WebView2 Runtime",
        "It's a free component from Microsoft that MDGlint uses to show your files, and it isn't "
        + "installed on this PC. Download the Evergreen runtime from Microsoft, install it, then "
        + "open MDGlint again.");

    /// <summary>The runtime is there but didn't start; <paramref name="reason"/> is the error text.</summary>
    public static PreviewUnavailableNotice StartFailed(string? reason)
    {
        var message = "The Microsoft Edge WebView2 Runtime is installed but didn't start. Restarting "
            + "the PC, or reinstalling the Evergreen runtime from Microsoft, usually fixes this.";
        return new PreviewUnavailableNotice(
            "The preview couldn't start",
            string.IsNullOrWhiteSpace(reason) ? message : $"{message}\n\nError: {reason.Trim()}");
    }
}

namespace MDGlint.Core.Models;

/// <summary>What the notice's button does.</summary>
public enum PreviewNoticeAction
{
    /// <summary>Opens Microsoft's WebView2 download page.</summary>
    DownloadRuntime,

    /// <summary>Ends the preview process a closed MDGlint window left behind, then starts the preview again.</summary>
    EndStuckPreview,

    /// <summary>No button.</summary>
    None,
}

/// <summary>
/// What the window shows instead of the preview when the Microsoft Edge WebView2 Runtime, which
/// draws it, is missing, fails to start, or is held by a preview process that a closed MDGlint
/// window left behind.
/// </summary>
public sealed record PreviewUnavailableNotice(
    string Title, string Message, PreviewNoticeAction Action = PreviewNoticeAction.DownloadRuntime)
{
    /// <summary>Microsoft's WebView2 page, with the Evergreen runtime download.</summary>
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    /// <summary>The HRESULT WebView2 reports when its process didn't answer in time (ERROR_TIMEOUT).</summary>
    public const int TimeoutHResult = unchecked((int)0x800705B4);

    public static PreviewUnavailableNotice RuntimeMissing { get; } = new(
        "MDGlint needs the Microsoft Edge WebView2 Runtime",
        "It's a free component from Microsoft that MDGlint uses to show your files, and it isn't "
        + "installed on this PC. Download the Evergreen runtime from Microsoft, install it, then "
        + "open MDGlint again.");

    /// <summary>
    /// A preview process holds MDGlint's data folder with no other MDGlint window running (one
    /// closed without it, for example while a dialog was open), so every new window would wait
    /// for it.
    /// </summary>
    public static PreviewUnavailableNotice Stuck { get; } = new(
        "Another MDGlint preview is stuck",
        "An MDGlint window that has closed left its preview process running, so this window can't "
        + "start its own. End it to continue. Only that leftover process is ended; nothing else is "
        + "closed.",
        PreviewNoticeAction.EndStuckPreview);

    /// <summary>The preview timed out while other MDGlint windows, which share it, are open.</summary>
    public static PreviewUnavailableNotice SharedPreviewNotAnswering { get; } = new(
        "The preview didn't start in time",
        "MDGlint's windows share one preview process, and it isn't answering. Close every MDGlint "
        + "window, then open this file again. If it still happens, restart the PC.",
        PreviewNoticeAction.None);

    /// <summary>The runtime is there but didn't start; <paramref name="reason"/> is the error text.</summary>
    public static PreviewUnavailableNotice StartFailed(string? reason)
    {
        var message = "The Microsoft Edge WebView2 Runtime is installed but didn't start. Restarting "
            + "the PC, or reinstalling the Evergreen runtime from Microsoft, usually fixes this.";
        return new PreviewUnavailableNotice(
            "The preview couldn't start",
            string.IsNullOrWhiteSpace(reason) ? message : $"{message}\n\nError: {reason.Trim()}");
    }

    /// <summary>
    /// Checked before the preview starts. <paramref name="leftover"/> says a preview process holds
    /// MDGlint's data folder although the window that started it is gone. With no other MDGlint
    /// window running, nothing else can be using it, so <see cref="Stuck"/> is shown at once
    /// instead of waiting for the start to time out. Otherwise null, and the preview starts.
    /// </summary>
    public static PreviewUnavailableNotice? BeforeStart(bool leftover, int otherWindows) =>
        leftover && otherWindows == 0 ? Stuck : null;

    /// <summary>
    /// The notice for a start that timed out: <see cref="Stuck"/> if a leftover preview process
    /// is in the way, even with other windows open (it isn't answering them either); else, with
    /// other windows open, <see cref="SharedPreviewNotAnswering"/>; else <see cref="StartFailed"/>.
    /// </summary>
    public static PreviewUnavailableNotice ForTimeout(bool leftover, int otherWindows, string? reason) =>
        leftover ? Stuck
        : otherWindows > 0 ? SharedPreviewNotAnswering
        : StartFailed(reason);
}

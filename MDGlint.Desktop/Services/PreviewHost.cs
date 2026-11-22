using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using MDGlint.Core.Models;
using MDGlint.Desktop.Helpers;
using Microsoft.Web.WebView2.Core;

namespace MDGlint.Desktop.Services;

/// <summary>
/// Owns the preview page inside WebView2: loads the fixed shell page from a virtual host, answers
/// the page's requests for images and media from local disk, sends rendered content to the page
/// as messages, and applies <see cref="PreviewPolicy"/> when the page tries to navigate.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "WebView2 event wiring; its rules are in PreviewPolicy (unit tests) and the "
    + "wiring is covered by the end-to-end tests, which need a real WebView2.")]
public sealed class PreviewHost
{
    private readonly CoreWebView2 _core;
    private readonly Action<string> _openMarkdownFile;
    private bool _isReady;
    private bool _blockedReported;
    private bool _offersOpen;

    private PreviewHost(CoreWebView2 core, Action<string> openMarkdownFile)
    {
        _core = core;
        _openMarkdownFile = openMarkdownFile;
    }

    /// <summary>Raised when the shell page has loaded and is ready for content.</summary>
    public event EventHandler? Ready;

    /// <summary>
    /// Raised once per <see cref="Show"/> when the page tried to load remote content and it was
    /// blocked because <see cref="AllowRemoteContent"/> is false.
    /// </summary>
    public event EventHandler? RemoteContentBlocked;

    /// <summary>
    /// Raised when the user clicks <b>Open…</b> on the no-file page. Ignored while any other page
    /// is shown, so a document can never ask for the dialog.
    /// </summary>
    public event EventHandler? OpenRequested;

    /// <summary>
    /// Whether the document being shown may load images, video and audio from the internet. Set
    /// before <see cref="Show"/>; applies to the requests that render makes.
    /// </summary>
    public bool AllowRemoteContent { get; set; }

    public static PreviewHost Attach(CoreWebView2 core, string assetsFolder, Action<string> openMarkdownFile)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(openMarkdownFile);

        var host = new PreviewHost(core, openMarkdownFile);

        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        // SmartScreen sends the addresses of pages it checks to Microsoft. The preview only ever
        // shows MDGlint's own page, and links open outside it, so it has nothing to check. Set
        // before the first navigation, which would otherwise apply the default (on).
        core.Settings.IsReputationCheckingRequired = false;
#if !DEBUG
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(PreviewPolicy.AppHostName, assetsFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.AddWebResourceRequestedFilter($"https://{DocumentUrls.HostName}/*", CoreWebView2WebResourceContext.All);
        // Every image and media request, so remote ones (including inline-style URLs) pass through
        // the remote-content choice. The CSP blocks every other kind of remote request.
        foreach (var scheme in new[] { "http", "https" })
        {
            core.AddWebResourceRequestedFilter($"{scheme}://*", CoreWebView2WebResourceContext.Image);
            core.AddWebResourceRequestedFilter($"{scheme}://*", CoreWebView2WebResourceContext.Media);
        }
        core.WebResourceRequested += host.OnWebResourceRequested;
        core.NavigationStarting += host.OnNavigationStarting;
        core.NewWindowRequested += host.OnNewWindowRequested;
        core.WebMessageReceived += host.OnWebMessageReceived;
        core.Navigate(PreviewPolicy.ShellUrl);

        return host;
    }

    /// <summary>
    /// Shows a rendered document. <paramref name="isNewDocument"/> scrolls to the top; otherwise the
    /// reader keeps their place (the same file was saved again). Ignored until the page is ready;
    /// <see cref="Ready"/> asks for it again.
    /// </summary>
    public void Show(RenderedDocument document, ThemeKind theme, bool isNewDocument)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!_isReady)
        {
            return;
        }

        _blockedReported = false;
        _offersOpen = document.OffersOpen;
        Post(new
        {
            type = "render",
            html = document.Html,
            @base = document.BaseDirectory is null ? null : DocumentUrls.ToFolderUrl(document.BaseDirectory),
            theme = theme.ToStorageValue(),
            resetScroll = isNewDocument,
            offersOpen = document.OffersOpen,
            printLight = PrintInLightTheme,
            title = document.Title
        });
    }

    /// <summary>
    /// Loads the shell page again; <see cref="Ready"/> follows. Used when remote content is turned
    /// off, because the page keeps images it already loaded in memory and would show them again
    /// without a request.
    /// </summary>
    public void Reload()
    {
        _isReady = false;
        _core.Navigate(PreviewPolicy.ShellUrl);
    }

    /// <summary>
    /// Whether printing and PDF export use the light theme whatever the window shows. Takes effect
    /// on the next <see cref="Show"/> or <see cref="SetTheme"/>.
    /// </summary>
    public bool PrintInLightTheme { get; set; } = true;

    public void SetTheme(ThemeKind theme)
    {
        if (_isReady)
        {
            Post(new { type = "theme", theme = theme.ToStorageValue(), printLight = PrintInLightTheme });
        }
    }

    /// <summary>Prints the shown file through WebView2's print dialog, which previews the printout; it
    /// also offers the system dialog. It runs in the page, so it closes with the window.</summary>
    public void ShowPrintDialog() => _core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);

    /// <summary>
    /// Saves the shown file as a PDF, as it would print (backgrounds included, no browser header
    /// or footer). Returns false if WebView2 couldn't write it.
    /// </summary>
    public Task<bool> PrintToPdfAsync(string path)
    {
        var settings = _core.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        return _core.PrintToPdfAsync(path, settings);
    }

    /// <summary>
    /// The shown content's HTML for Export ▸ HTML, from the shell's <c>mdglintExport()</c>
    /// (app.js), or null if the preview isn't showing the shell page.
    /// </summary>
    public async Task<string?> ExportContentAsync()
    {
        if (!PreviewPolicy.IsShell(_core.Source))
        {
            return null;
        }

        var result = await _core.ExecuteScriptAsync("typeof mdglintExport === 'function' ? mdglintExport() : null");
        return JsonSerializer.Deserialize<string?>(result);
    }

    private void Post(object message) => _core.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    /// <summary>
    /// Answers the page's requests to the document host with images and media read from disk.
    /// Responses carry no CORS headers, so the page can display them but script can't read them.
    /// Remote images and media go ahead only when <see cref="AllowRemoteContent"/> is set.
    /// </summary>
    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (PreviewPolicy.IsDocumentHost(uri))
        {
            e.Response = CreateFileResponse(e.Request.Method, uri) ?? Respond(404, "Not Found", "Content-Type: text/plain");
            return;
        }

        if (!PreviewPolicy.IsBlockedRemote(uri, AllowRemoteContent))
        {
            return; // Not set: the request goes ahead as normal.
        }

        e.Response = Respond(403, "Blocked", "Content-Type: text/plain\r\nCache-Control: no-store");
        if (!_blockedReported)
        {
            _blockedReported = true;
            RemoteContentBlocked?.Invoke(this, EventArgs.Empty);
        }
    }

    private CoreWebView2WebResourceResponse? CreateFileResponse(string method, Uri uri)
    {
        if (!PreviewPolicy.TryGetLocalMedia(method, uri, out var path, out var contentType))
        {
            return null;
        }

        try
        {
            // WebView2 reads and releases the stream after the response is sent.
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return _core.Environment.CreateWebResourceResponse(
                stream, 200, "OK", $"Content-Type: {contentType}\r\nCache-Control: no-cache\r\nX-Content-Type-Options: nosniff");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private CoreWebView2WebResourceResponse Respond(int status, string reason, string headers) =>
        _core.Environment.CreateWebResourceResponse(null, status, reason, headers);

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!PreviewPolicy.IsShell(e.Source))
        {
            return;
        }

        if (PreviewPolicy.IsReadyMessage(e.WebMessageAsJson))
        {
            _isReady = true;
            Ready?.Invoke(this, EventArgs.Empty);
        }
        else if (PreviewPolicy.IsDropMessage(e.WebMessageAsJson))
        {
            // Only files the user really dropped carry a path: a page can't make one up.
            var dropped = PreviewPolicy.DroppedMarkdownFile(
                e.AdditionalObjects?.OfType<CoreWebView2File>().Select(f => f.Path));
            if (dropped is not null)
            {
                _openMarkdownFile(dropped);
            }
        }
        else if (PreviewPolicy.IsOpenMessage(e.WebMessageAsJson) && _offersOpen)
        {
            OpenRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // The shell page is the only document the preview ever shows. Everything else is a link the
        // user clicked (handled outside the preview) or something the content tried on its own
        // (ignored).
        if (PreviewPolicy.IsShell(e.Uri))
        {
            _isReady = false;
            return;
        }

        e.Cancel = true;
        if (e.IsUserInitiated)
        {
            OpenLink(e.Uri);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.IsUserInitiated)
        {
            OpenLink(e.Uri);
        }
    }

    private void OpenLink(string link)
    {
        var action = PreviewPolicy.ForLink(link);
        switch (action.Kind)
        {
            case LinkActionKind.OpenInMDGlint:
                _openMarkdownFile(action.Target);
                break;
            case LinkActionKind.ShowInExplorer:
                ProcessLauncher.ShowInExplorer(action.Target);
                break;
            case LinkActionKind.OpenInBrowser:
                ProcessLauncher.OpenWithDefaultApp(action.Target);
                break;
        }
    }
}

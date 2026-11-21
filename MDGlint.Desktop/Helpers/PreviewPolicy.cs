using System.IO;
using System.Text.Json;
using MDGlint.Core.Services;

namespace MDGlint.Desktop.Helpers;

public enum LinkActionKind
{
    /// <summary>Nothing happens.</summary>
    Ignore,

    /// <summary>An <c>http</c>, <c>https</c> or <c>mailto</c> link, opened in the default app.</summary>
    OpenInBrowser,

    /// <summary>A local Markdown file, opened in MDGlint.</summary>
    OpenInMDGlint,

    /// <summary>Any other local file or folder, shown in Explorer rather than run.</summary>
    ShowInExplorer,
}

public sealed record LinkAction(LinkActionKind Kind, string Target = "")
{
    public static LinkAction Ignore { get; } = new(LinkActionKind.Ignore);
}

/// <summary>
/// The preview's rules, apart from WebView2: which page it may show, which local files it may
/// load and as what, which remote requests are blocked, which messages it accepts, and what a
/// clicked link does. <see cref="Services.PreviewHost"/> applies them to WebView2's events.
/// </summary>
public static class PreviewPolicy
{
    public const string AppHostName = "app.mdglint.example";
    public const string ShellPath = "/index.html";
    public static readonly string ShellUrl = $"https://{AppHostName}{ShellPath}";

    // Only these file types are served from disk to the preview: what <img>, <video> and <audio>
    // can show. Everything else gets a 404, so a document can't pull other files into the page.
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".apng"] = "image/apng",
        [".avif"] = "image/avif",
        [".bmp"] = "image/bmp",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".mp3"] = "audio/mpeg",
        [".m4a"] = "audio/mp4",
        [".ogg"] = "audio/ogg",
        [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm"
    };

    /// <summary>The shell page, the only document the preview ever shows.</summary>
    public static bool IsShell(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, AppHostName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.AbsolutePath, ShellPath, StringComparison.Ordinal);

    public static bool IsDocumentHost(Uri uri) =>
        string.Equals(uri.Host, DocumentUrls.HostName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The local file a document-host request may load, and its content type: only a GET for an
    /// image, audio or video file.
    /// </summary>
    public static bool TryGetLocalMedia(string method, Uri uri, out string path, out string contentType)
    {
        contentType = string.Empty;
        path = string.Empty;
        return string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase)
            && DocumentUrls.TryGetPath(uri, out path)
            && MediaTypes.TryGetValue(Path.GetExtension(path), out contentType!);
    }

    /// <summary>A remote image or media request that must be answered with 403.</summary>
    public static bool IsBlockedRemote(Uri uri, bool allowRemoteContent) =>
        !allowRemoteContent && RemoteContentPolicy.IsRemote(uri, AppHostName, DocumentUrls.HostName);

    /// <summary>The shell's <c>{ "type": "ready" }</c> message; anything else is ignored.</summary>
    public static bool IsReadyMessage(string json) => MessageType(json) == "ready";

    /// <summary>
    /// The shell's <c>{ "type": "drop" }</c> message, sent with the files the user dropped on the
    /// preview (WebView2 is a window of its own, so drops there never reach the WPF window).
    /// </summary>
    public static bool IsDropMessage(string json) => MessageType(json) == "drop";

    /// <summary>The first dropped file that is Markdown, or null; others are ignored.</summary>
    public static string? DroppedMarkdownFile(IEnumerable<string?>? paths) =>
        paths?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && MarkdownFiles.IsMarkdownFile(p));

    private static string? MessageType(string json)
    {
        try
        {
            using var message = JsonDocument.Parse(json);
            return message.RootElement.ValueKind == JsonValueKind.Object
                && message.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                    ? type.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a link the user clicked does. Local Markdown files open in MDGlint; any other local
    /// file is shown in Explorer, never run, so a link can't launch a program.
    /// </summary>
    public static LinkAction ForLink(string? link, Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null)
    {
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;

        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return LinkAction.Ignore;
        }

        if (IsDocumentHost(uri))
        {
            if (!DocumentUrls.TryGetPath(uri, out var path))
            {
                return LinkAction.Ignore;
            }

            if (fileExists(path))
            {
                return MarkdownFiles.IsMarkdownFile(path)
                    ? new LinkAction(LinkActionKind.OpenInMDGlint, path)
                    : new LinkAction(LinkActionKind.ShowInExplorer, path);
            }

            return directoryExists(path) ? new LinkAction(LinkActionKind.ShowInExplorer, path) : LinkAction.Ignore;
        }

        if (string.Equals(uri.Host, AppHostName, StringComparison.OrdinalIgnoreCase))
        {
            return LinkAction.Ignore;
        }

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeMailto
            ? new LinkAction(LinkActionKind.OpenInBrowser, uri.AbsoluteUri)
            : LinkAction.Ignore;
    }
}

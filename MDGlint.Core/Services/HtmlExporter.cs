using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

/// <summary>
/// Builds the file Export ▸ HTML saves: one page that needs nothing else. The rendered content
/// comes from the preview as it is shown (highlighted, already sanitized), the styles are
/// inlined, and images are embedded as <c>data:</c> URLs. An image that can't be embedded is
/// left out rather than linked, and video and audio are left out, so opening the file never
/// loads anything; its Content Security Policy says the same.
/// </summary>
public static class HtmlExporter
{
    /// <summary>Only inline styles and embedded images: no script, and nothing from anywhere else.</summary>
    public const string ContentSecurityPolicy = "default-src 'none'; img-src data:; style-src 'unsafe-inline'";

    /// <summary>
    /// The exported page. <paramref name="embedImage"/> turns an image's absolute http(s) address
    /// into a <c>data:</c> URL, or null to leave the image out; each address is asked for once.
    /// </summary>
    public static async Task<string> BuildAsync(
        string contentHtml, string title, ThemeKind theme, string styles, Func<Uri, Task<string?>> embedImage)
    {
        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><head></head><body></body></html>");
        document.DocumentElement.SetAttribute("data-theme", theme.ToStorageValue());

        var head = document.Head!;
        head.Append(Meta(document, "charset", "utf-8"));
        var policy = Meta(document, "http-equiv", "Content-Security-Policy");
        policy.SetAttribute("content", ContentSecurityPolicy);
        head.Append(policy);
        var viewport = Meta(document, "name", "viewport");
        viewport.SetAttribute("content", "width=device-width, initial-scale=1");
        head.Append(viewport);
        var titleElement = document.CreateElement("title");
        titleElement.TextContent = title;
        head.Append(titleElement);
        var style = document.CreateElement("style");
        style.TextContent = styles;
        head.Append(style);

        var main = document.CreateElement("main");
        main.ClassName = "markdown-body";
        main.InnerHtml = contentHtml;
        document.Body!.Append(main);

        // Their sources would be links to files or the internet; <picture>'s <img> stays.
        foreach (var media in main.QuerySelectorAll("video, audio, source").ToList())
        {
            media.Remove();
        }

        var embedded = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var image in main.QuerySelectorAll("img"))
        {
            image.RemoveAttribute("srcset");
            image.RemoveAttribute("sizes");
            var source = await EmbedAsync(image.GetAttribute("src"), embedded, embedImage);
            if (source is null)
            {
                image.RemoveAttribute("src");
            }
            else
            {
                image.SetAttribute("src", source);
            }
        }

        return "<!DOCTYPE html>\n" + document.DocumentElement.OuterHtml + "\n";
    }

    private static async Task<string?> EmbedAsync(
        string? source, Dictionary<string, string?> embedded, Func<Uri, Task<string?>> embedImage)
    {
        if (source is null)
        {
            return null;
        }

        if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        if (!embedded.TryGetValue(source, out var result))
        {
            result = await embedImage(uri);
            embedded[source] = result;
        }

        return result is not null && result.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) ? result : null;
    }

    private static IElement Meta(IDocument document, string attribute, string value)
    {
        var meta = document.CreateElement("meta");
        meta.SetAttribute(attribute, value);
        return meta;
    }
}

using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using DocumentFormat.OpenXml.Packaging;
using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

/// <summary>
/// Builds the file Export ▸ Word saves: a <c>.docx</c> made from the content the preview shows
/// (already sanitized), in Word's own styles and GitHub's light colours. Images are stored in the
/// document, never linked, so opening it never loads anything; an image that can't be stored is
/// replaced by its alt text. Only <c>http</c>, <c>https</c> and <c>mailto</c> links stay links.
/// A <c>.docx</c> can't hold macros.
/// </summary>
public static partial class DocxExporter
{
    /// <summary>
    /// The exported document. <paramref name="loadImage"/> reads or downloads an image's absolute
    /// http(s) address, or returns null to leave it out; each address is asked for once.
    /// </summary>
    public static async Task<byte[]> BuildAsync(string contentHtml, string title, Func<Uri, Task<ExportImage?>> loadImage)
    {
        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body></body></html>");
        var body = document.Body!;
        body.InnerHtml = contentHtml;

        var images = new Dictionary<string, WordImage?>(StringComparer.Ordinal);
        foreach (var image in body.QuerySelectorAll("img"))
        {
            var source = image.GetAttribute("src");
            if (source is not null && !images.ContainsKey(source))
            {
                images[source] = await LoadAsync(source, loadImage);
            }
        }

        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            package.PackageProperties.Title = title;
            new DocxWriter(package.AddMainDocumentPart(), images).Write(body);
        }

        return stream.ToArray();
    }

    private static async Task<WordImage?> LoadAsync(string source, Func<Uri, Task<ExportImage?>> loadImage)
    {
        byte[] bytes;
        var data = DataImage().Match(source);
        if (data.Success)
        {
            try
            {
                bytes = Convert.FromBase64String(data.Groups[1].Value);
            }
            catch (FormatException)
            {
                return null;
            }
        }
        else if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var image = await loadImage(uri);
            if (image is null || !image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            bytes = image.Bytes;
        }
        else
        {
            return null;
        }

        // The header decides the type, whatever the file was served or named as.
        return ImageInfo.Read(bytes) is { } info ? new WordImage(info.ContentType, bytes, info.Width, info.Height) : null;
    }

    [GeneratedRegex(@"^\s*data:image/[a-z0-9.+-]+;base64,([A-Za-z0-9+/=\s]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex DataImage();
}

/// <summary>An image Word can show, with its size in pixels.</summary>
internal sealed record WordImage(string ContentType, byte[] Bytes, int Width, int Height);

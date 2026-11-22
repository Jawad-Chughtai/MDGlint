using System.IO;
using System.Net.Http;
using MDGlint.Core.Models;
using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Services;

/// <summary>
/// Reads the images of a file being exported, under the preview's rules:
/// local images the preview may load (<see cref="PreviewPolicy.TryGetLocalMedia"/>) are read
/// from disk, and remote ones are downloaded only when remote content is allowed for the file.
/// Anything else, or anything that fails or isn't an image, is left out (null).
/// </summary>
public sealed class ExportImages(HttpClient http, bool allowRemoteContent)
{
    /// <summary>Larger images are left out, so one huge file can't fill the export.</summary>
    public const long MaxBytes = 25 * 1024 * 1024;

    /// <summary>The image as a <c>data:</c> URL, for Export ▸ HTML.</summary>
    public async Task<string?> EmbedAsync(Uri uri) =>
        await LoadAsync(uri) is { } image ? $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}" : null;

    /// <summary>The image's bytes, for Export ▸ Word.</summary>
    public async Task<ExportImage?> LoadAsync(Uri uri)
    {
        if (PreviewPolicy.IsDocumentHost(uri))
        {
            return PreviewPolicy.TryGetLocalMedia("GET", uri, out var path, out var contentType) && IsImage(contentType)
                && ReadLocal(path) is { } bytes
                    ? new ExportImage(contentType, bytes)
                    : null;
        }

        return allowRemoteContent && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? await DownloadAsync(uri)
            : null;
    }

    private static byte[]? ReadLocal(string path)
    {
        try
        {
            return new FileInfo(path).Length <= MaxBytes ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task<ExportImage?> DownloadAsync(Uri uri)
    {
        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || !IsImage(contentType) || response.Content.Headers.ContentLength > MaxBytes)
            {
                return null;
            }

            using var body = await response.Content.ReadAsStreamAsync();
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(buffer)) > 0)
            {
                if (bytes.Length + read > MaxBytes)
                {
                    return null;
                }

                bytes.Write(buffer, 0, read);
            }

            return new ExportImage(contentType!, bytes.ToArray());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static bool IsImage(string? contentType) =>
        contentType is not null && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

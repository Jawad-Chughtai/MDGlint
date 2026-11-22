using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

/// <summary>
/// Asks GitHub for the latest MDGlint release and, when the user chooses to install it,
/// downloads its installer and checks it against the release's SHA-256 file. Nothing here runs
/// on its own: the caller decides when to check (only when the user turned checks on, or asked)
/// and when to download (only when the user pressed Install).
/// </summary>
public sealed partial class UpdateService
{
    /// <summary>Larger than any MDGlint installer will be; a bigger download is refused.</summary>
    public const long MaxInstallerBytes = 300L * 1024 * 1024;

    private const int MaxReleaseJsonBytes = 1024 * 1024;
    private const int MaxHashFileBytes = 4 * 1024;

    private readonly HttpClient _http;
    private readonly string _userAgentVersion;

    /// <param name="http">Used for every request; it must follow redirects (GitHub's downloads do).</param>
    /// <param name="currentVersion">The running version, also sent as the user agent.</param>
    public UpdateService(HttpClient http, string currentVersion)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        CurrentVersion = currentVersion ?? string.Empty;
        _userAgentVersion = SemanticVersion.TryParse(CurrentVersion, out var parsed) ? parsed.ToString() : "0.0.0";
    }

    public string CurrentVersion { get; }

    public Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default) =>
        CheckAsync(includeBetas: false, cancellationToken);

    /// <param name="includeBetas">Also offer pre-releases: reads the newest releases instead of
    /// the latest stable one, and offers whichever is newest, beta or stable.</param>
    public async Task<UpdateCheckResult> CheckAsync(bool includeBetas, CancellationToken cancellationToken = default)
    {
        if (!SemanticVersion.TryParse(CurrentVersion, out var current))
        {
            return UpdateCheckResult.Failed($"The running version '{CurrentVersion}' isn't a valid version.");
        }

        string json;
        try
        {
            using var request = CreateRequest(includeBetas ? UpdateSource.ReleasesApi : UpdateSource.LatestReleaseApi);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return UpdateCheckResult.UpToDate; // No release has been published yet.
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed($"GitHub answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            json = await ReadLimitedStringAsync(response, MaxReleaseJsonBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return UpdateCheckResult.Failed("Couldn't reach GitHub to check for updates.");
        }

        AvailableUpdate? latest;
        try
        {
            latest = includeBetas ? ParseNewestRelease(json) : ParseRelease(json);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return UpdateCheckResult.Failed("GitHub's answer about the latest release couldn't be read.");
        }

        return latest is not null && latest.Version > current
            ? UpdateCheckResult.Available(latest)
            : UpdateCheckResult.UpToDate;
    }

    /// <summary>
    /// Reads GitHub's "latest release" JSON. Returns null for a release MDGlint should ignore:
    /// a draft, a pre-release, a tag that isn't a version, or a page outside the repository.
    /// The installer is only offered when both the installer and its <c>.sha256</c> file are
    /// attached, under the repository's download address.
    /// </summary>
    public static AvailableUpdate? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ParseRelease(document.RootElement, allowPreRelease: false);
    }

    /// <summary>
    /// Reads GitHub's list of releases and returns the newest version in it, pre-releases
    /// included, under the same rules as <see cref="ParseRelease(string)"/>; null when none qualifies.
    /// </summary>
    public static AvailableUpdate? ParseNewestRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        AvailableUpdate? newest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (ParseRelease(release, allowPreRelease: true) is { } update && (newest is null || update.Version > newest.Version))
            {
                newest = update;
            }
        }

        return newest;
    }

    private static AvailableUpdate? ParseRelease(JsonElement root, bool allowPreRelease)
    {
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()
            || !allowPreRelease && root.TryGetProperty("prerelease", out var preRelease) && preRelease.GetBoolean())
        {
            return null;
        }

        if (!SemanticVersion.TryParse(root.GetProperty("tag_name").GetString(), out var version)
            || version.IsPreRelease && !allowPreRelease)
        {
            return null;
        }

        var page = root.GetProperty("html_url").GetString();
        if (page is null || !page.StartsWith(UpdateSource.ReleasePagePrefix, StringComparison.Ordinal)
            || !Uri.TryCreate(page, UriKind.Absolute, out var pageUri))
        {
            return null;
        }

        Uri? installer = null, hash = null;
        long installerSize = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var url = asset.GetProperty("browser_download_url").GetString();
                if (url is null || !url.StartsWith(UpdateSource.DownloadPrefix, StringComparison.Ordinal)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    continue;
                }

                if (name == UpdateSource.InstallerName)
                {
                    installer = uri;
                    installerSize = asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0;
                }
                else if (name == UpdateSource.HashName)
                {
                    hash = uri;
                }
            }
        }

        return installer is not null && hash is not null && installerSize is > 0 and <= MaxInstallerBytes
            ? new AvailableUpdate(version, pageUri, installer, installerSize, hash)
            : new AvailableUpdate(version, pageUri, null, 0, null);
    }

    /// <summary>
    /// Reads the SHA-256 from a <c>.sha256</c> file (<c>hash  MDGlintSetup.exe</c>, or just the
    /// hash). Returns null when there is no 64-digit hex hash at the start.
    /// </summary>
    public static byte[]? ParseHashFile(string text)
    {
        var match = HashPattern().Match(text ?? string.Empty);
        return match.Success ? Convert.FromHexString(match.Groups["hash"].Value) : null;
    }

    /// <summary>
    /// Downloads the update's installer into <paramref name="folder"/> and checks it against the
    /// release's SHA-256 file. Returns it only when the hash matches, held open so it can't be
    /// changed before it runs; otherwise deletes what was downloaded and throws
    /// <see cref="UpdateDownloadException"/>.
    /// </summary>
    public async Task<VerifiedInstaller> DownloadInstallerAsync(
        AvailableUpdate update,
        string folder,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.CanInstall)
        {
            throw new UpdateDownloadException("This release can only be installed from its release page.");
        }

        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, UpdateSource.InstallerName);
        var partial = target + ".download";

        try
        {
            var expected = await DownloadHashAsync(update.HashUrl!, cancellationToken).ConfigureAwait(false);
            await DownloadFileAsync(update.InstallerUrl!, update.InstallerSize, partial, progress, cancellationToken)
                .ConfigureAwait(false);
            File.Move(partial, target, overwrite: true);

            // Hash the file as it will run: opened so nothing else can write, move or delete it
            // until the caller has started it.
            var file = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var actual = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(expected, actual))
                {
                    throw new UpdateDownloadException(
                        "The downloaded installer doesn't match the release's SHA-256, so it wasn't run.");
                }
            }
            catch
            {
                await file.DisposeAsync().ConfigureAwait(false);
                TryDelete(target);
                throw;
            }

            return new VerifiedInstaller(target, update.Version, file);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException
            || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            throw new UpdateDownloadException("The update couldn't be downloaded.", ex);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private async Task<byte[]> DownloadHashAsync(Uri url, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response);
        var text = await ReadLimitedStringAsync(response, MaxHashFileBytes, cancellationToken).ConfigureAwait(false);
        return ParseHashFile(text)
            ?? throw new UpdateDownloadException("The release's SHA-256 file couldn't be read.");
    }

    private async Task DownloadFileAsync(
        Uri url, long expectedSize, string path, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccess(response);

        if (response.Content.Headers.ContentLength is { } length && length != expectedSize)
        {
            throw new UpdateDownloadException("The installer's size doesn't match the release.");
        }

        var buffer = new byte[81920];
        long total = 0;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > expectedSize)
                {
                    throw new UpdateDownloadException("The installer is larger than the release says.");
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                progress?.Report((double)total / expectedSize);
            }
        }

        if (total != expectedSize)
        {
            throw new UpdateDownloadException("The installer download was incomplete.");
        }
    }

    private HttpRequestMessage CreateRequest(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("MDGlint", _userAgentVersion));
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new UpdateDownloadException(
                $"The download failed: {(int)response.StatusCode} {response.ReasonPhrase}.");
        }
    }

    private static async Task<string> ReadLimitedStringAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (memory.Length + read > maxBytes)
            {
                throw new IOException("The response is larger than expected.");
            }

            memory.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [GeneratedRegex(@"^\s*(?<hash>[0-9A-Fa-f]{64})(?![0-9A-Fa-f])")]
    private static partial Regex HashPattern();
}

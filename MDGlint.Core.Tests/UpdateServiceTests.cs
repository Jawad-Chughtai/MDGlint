using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MDGlint.Core.Models;
using MDGlint.Core.Services;

namespace MDGlint.Core.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    internal const string Download = "https://github.com/Jawad-Chughtai/MDGlint/releases/download/v1.2.0/";
    internal static readonly byte[] Installer = Encoding.ASCII.GetBytes("pretend this is MDGlintSetup.exe");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MDGlintTests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task CheckAsync_FindsANewerRelease()
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, ReleaseJson("v1.2.0"));

        var result = await Service("1.0.0").CheckAsync();

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        Assert.Equal("1.2.0", result.Update!.Version.ToString());
        Assert.True(result.Update.CanInstall);
        Assert.Equal(Installer.Length, result.Update.InstallerSize);
    }

    [Fact]
    public async Task CheckAsync_OffersTheLatestReleaseWhateverWasSkipped()
    {
        // A user several versions behind is offered the latest one directly.
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, ReleaseJson("v4.0.0"));

        var result = await Service("1.0.0").CheckAsync();

        Assert.Equal("4.0.0", result.Update!.Version.ToString());
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.0")]
    public async Task CheckAsync_IsUpToDateWhenTheLatestIsNotNewer(string tag)
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, ReleaseJson(tag));

        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service("1.0.0").CheckAsync()).Status);
    }

    [Fact]
    public async Task CheckAsync_OffersTheStableReleaseToAPreRelease()
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, ReleaseJson("v1.0.0"));

        Assert.Equal(UpdateCheckStatus.Available, (await Service("1.0.0-rc.1").CheckAsync()).Status);
    }

    [Fact]
    public async Task CheckAsync_IsUpToDateWhenNothingIsReleased()
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, "{\"message\":\"Not Found\"}", HttpStatusCode.NotFound);

        Assert.Equal(UpdateCheckStatus.UpToDate, (await Service("1.0.0").CheckAsync()).Status);
    }

    [Fact]
    public async Task CheckAsync_SendsGitHubsHeaders()
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, ReleaseJson("v1.0.0"));

        await Service("1.0.0").CheckAsync();

        var request = Assert.Single(_handler.Requests);
        Assert.Equal("MDGlint/1.0.0", request.Headers.UserAgent.ToString());
        Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
        Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)] // GitHub's rate limit.
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task CheckAsync_FailsOnAnErrorAnswer(HttpStatusCode status)
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, "{}", status);

        var result = await Service("1.0.0").CheckAsync();

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
        Assert.Contains(((int)status).ToString(), result.Error);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\": 5, \"html_url\": \"x\"}")]
    public async Task CheckAsync_FailsOnAnUnreadableAnswer(string json)
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, json);

        Assert.Equal(UpdateCheckStatus.Failed, (await Service("1.0.0").CheckAsync()).Status);
    }

    [Fact]
    public async Task CheckAsync_FailsWhenGitHubCannotBeReached()
    {
        _handler.Fail(UpdateSource.LatestReleaseApi.AbsoluteUri, new HttpRequestException("offline"));

        Assert.Equal(UpdateCheckStatus.Failed, (await Service("1.0.0").CheckAsync()).Status);
    }

    [Fact]
    public async Task CheckAsync_RejectsAnOversizedAnswer()
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, new string(' ', 2 * 1024 * 1024) + "{}");

        Assert.Equal(UpdateCheckStatus.Failed, (await Service("1.0.0").CheckAsync()).Status);
    }

    [Fact]
    public async Task CheckAsync_PassesOnCancellation()
    {
        _handler.Respond(UpdateSource.LatestReleaseApi.AbsoluteUri, ReleaseJson("v1.2.0"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service("1.0.0").CheckAsync(cancelled.Token));
    }

    [Fact]
    public async Task CheckAsync_FailsForAnInvalidRunningVersion()
    {
        Assert.Equal(UpdateCheckStatus.Failed, (await Service("").CheckAsync()).Status);
        Assert.Empty(_handler.Requests);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ParseRelease_IgnoresDraftsAndPreReleases(bool draft, bool preRelease)
    {
        Assert.Null(UpdateService.ParseRelease(ReleaseJson("v1.2.0", draft: draft, preRelease: preRelease)));
    }

    [Theory]
    [InlineData("v1.2.0-beta.1")]
    [InlineData("latest")]
    [InlineData("1.2")]
    public void ParseRelease_IgnoresTagsThatAreNotStableVersions(string tag)
    {
        Assert.Null(UpdateService.ParseRelease(ReleaseJson(tag)));
    }

    [Theory]
    [InlineData("https://github.com/someone-else/MDGlint/releases/tag/v1.2.0")]
    [InlineData("http://github.com/Jawad-Chughtai/MDGlint/releases/tag/v1.2.0")]
    [InlineData("https://example.com/Jawad-Chughtai/MDGlint/releases/tag/v1.2.0")]
    public void ParseRelease_IgnoresAPageOutsideTheRepository(string page)
    {
        Assert.Null(UpdateService.ParseRelease(ReleaseJson("v1.2.0", page: page)));
    }

    [Fact]
    public void ParseRelease_OffersOnlyThePageWithoutTheHashFile()
    {
        // Rule 5: a release published without the .sha256 file (or the installer) isn't installed
        // by MDGlint itself; the user is sent to its page.
        var update = UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets: [Asset(UpdateSource.InstallerName)]));

        Assert.NotNull(update);
        Assert.False(update.CanInstall);
        Assert.Null(update.InstallerUrl);
    }

    [Fact]
    public void ParseRelease_OffersOnlyThePageWithoutTheInstaller()
    {
        var update = UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets: [Asset(UpdateSource.HashName)]));

        Assert.False(update!.CanInstall);
    }

    [Fact]
    public void ParseRelease_IgnoresAssetsOutsideTheRepository()
    {
        var update = UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets:
        [
            Asset(UpdateSource.InstallerName, url: "https://example.com/MDGlintSetup.exe"),
            Asset(UpdateSource.HashName),
        ]));

        Assert.False(update!.CanInstall);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(UpdateService.MaxInstallerBytes + 1)]
    public void ParseRelease_OffersOnlyThePageForAnImplausibleSize(long size)
    {
        var update = UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets:
            [Asset(UpdateSource.InstallerName, size: size), Asset(UpdateSource.HashName)]));

        Assert.False(update!.CanInstall);
    }

    [Theory]
    [InlineData("{0}  MDGlintSetup.exe\n")]
    [InlineData("{0}\r\n")]
    [InlineData("  {0}")]
    public void ParseHashFile_ReadsTheHash(string format)
    {
        var hash = SHA256.HashData(Installer);
        var text = string.Format(format, Convert.ToHexString(hash).ToLowerInvariant());

        Assert.Equal(hash, UpdateService.ParseHashFile(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a hash")]
    [InlineData("abc123")]
    [InlineData("MDGlintSetup.exe 0000000000000000000000000000000000000000000000000000000000000000")]
    public void ParseHashFile_RejectsAnythingElse(string text)
    {
        Assert.Null(UpdateService.ParseHashFile(text));
    }

    [Fact]
    public void ParseHashFile_RejectsALongerHexRun()
    {
        Assert.Null(UpdateService.ParseHashFile(new string('a', 65)));
    }

    [Fact]
    public async Task DownloadInstallerAsync_KeepsAMatchingInstallerLocked()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer));

        using (var installer = await Service("1.0.0").DownloadInstallerAsync(update, _folder))
        {
            Assert.Equal(Path.Combine(_folder, UpdateSource.InstallerName), installer.Path);
            Assert.Equal(Installer, await File.ReadAllBytesAsync(installer.Path));
            Assert.Throws<IOException>(() => File.OpenWrite(installer.Path).Dispose());
            Assert.Single(Directory.GetFiles(_folder));
        }

        File.Delete(Path.Combine(_folder, UpdateSource.InstallerName)); // Unlocked once disposed.
    }

    [Fact]
    public async Task DownloadInstallerAsync_ReportsProgress()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer));
        var reports = new List<double>();

        using var installer = await Service("1.0.0").DownloadInstallerAsync(update, _folder, new SyncProgress(reports.Add));

        Assert.Equal(1.0, reports[^1]);
    }

    [Fact]
    public async Task DownloadInstallerAsync_DeletesAnInstallerThatDoesNotMatch()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Encoding.ASCII.GetBytes("something else")));

        var error = await Assert.ThrowsAsync<UpdateDownloadException>(
            () => Service("1.0.0").DownloadInstallerAsync(update, _folder));

        Assert.Contains("SHA-256", error.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsADownloadLargerThanTheRelease()
    {
        var update = ServeRelease([.. Installer, .. Installer], SHA256.HashData(Installer), size: Installer.Length, sendLength: false);

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsAnIncompleteDownload()
    {
        var update = ServeRelease(Installer[..10], SHA256.HashData(Installer), size: Installer.Length, sendLength: false);

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsAMismatchedContentLength()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer), size: Installer.Length + 1);

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RejectsAnUnreadableHashFile()
    {
        var update = ServeRelease(Installer, hash: null);

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_WrapsNetworkFailures()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer));
        _handler.Fail(Download + UpdateSource.InstallerName, new HttpRequestException("offline"));

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_RefusesAPageOnlyRelease()
    {
        var update = UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets: []))!;

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task DownloadInstallerAsync_PassesOnCancellation()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service("1.0.0").DownloadInstallerAsync(update, _folder, cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Constructor_RequiresAClientAndToleratesNoVersion()
    {
        Assert.Throws<ArgumentNullException>(() => new UpdateService(null!, "1.0.0"));
        Assert.Equal(string.Empty, new UpdateService(new HttpClient(_handler), null!).CurrentVersion);
    }

    [Fact]
    public void ParseRelease_HandlesMissingOrOddAssets()
    {
        Assert.False(UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets: null, noAssets: true))!.CanInstall);
        var notAnArray = JsonSerializer.Serialize(new
        {
            tag_name = "v1.2.0",
            html_url = "https://github.com/Jawad-Chughtai/MDGlint/releases/tag/v1.2.0",
            assets = "none",
        });
        Assert.False(UpdateService.ParseRelease(notAnArray)!.CanInstall);
    }

    [Fact]
    public void ParseRelease_OffersOnlyThePageWhenTheSizeIsMissing()
    {
        var withoutSize = new Dictionary<string, object>
        {
            ["name"] = UpdateSource.InstallerName,
            ["browser_download_url"] = Download + UpdateSource.InstallerName,
        };

        var update = UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets: [withoutSize, Asset(UpdateSource.HashName)]));

        Assert.False(update!.CanInstall);
    }

    [Fact]
    public void ParseHashFile_ToleratesNull()
    {
        Assert.Null(UpdateService.ParseHashFile(null!));
    }

    [Fact]
    public async Task CheckAsync_FailsWhenTheRequestTimesOut()
    {
        _handler.Fail(UpdateSource.LatestReleaseApi.AbsoluteUri, new TaskCanceledException("timed out"));

        Assert.Equal(UpdateCheckStatus.Failed, (await Service("1.0.0").CheckAsync()).Status);
    }

    [Fact]
    public async Task DownloadInstallerAsync_FailsWhenTheHashFileIsMissing()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer));
        _handler.Respond(Download + UpdateSource.HashName, "Not Found", HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<UpdateDownloadException>(
            () => Service("1.0.0").DownloadInstallerAsync(update, _folder));

        Assert.Contains("404", error.Message);
    }

    [Fact]
    public async Task DownloadInstallerAsync_FailsWhenTheFolderCannotBeWritten()
    {
        // A folder where the partial download should go: it can be neither written nor deleted.
        var update = ServeRelease(Installer, SHA256.HashData(Installer));
        Directory.CreateDirectory(Path.Combine(_folder, UpdateSource.InstallerName + ".download"));

        await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
    }

    [Fact]
    public async Task DownloadInstallerAsync_FailsWhenThePartialFileIsInUse()
    {
        var update = ServeRelease(Installer, SHA256.HashData(Installer));
        Directory.CreateDirectory(_folder);
        var partial = Path.Combine(_folder, UpdateSource.InstallerName + ".download");

        using (new FileStream(partial, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            await Assert.ThrowsAsync<UpdateDownloadException>(() => Service("1.0.0").DownloadInstallerAsync(update, _folder));
        }
    }

    private UpdateService Service(string version) => new(new HttpClient(_handler), version);

    private AvailableUpdate ServeRelease(byte[] body, byte[]? hash, long? size = null, bool sendLength = true) =>
        ServeRelease(_handler, body, hash, size, sendLength);

    internal static AvailableUpdate ServeRelease(
        FakeHandler handler, byte[] body, byte[]? hash, long? size = null, bool sendLength = true)
    {
        handler.Respond(Download + UpdateSource.HashName,
            hash is null ? "no hash here" : Convert.ToHexString(hash).ToLowerInvariant() + "  MDGlintSetup.exe\n");
        handler.Respond(Download + UpdateSource.InstallerName, body, sendLength);
        return UpdateService.ParseRelease(ReleaseJson("v1.2.0", assets:
            [Asset(UpdateSource.InstallerName, size: size ?? body.Length), Asset(UpdateSource.HashName)]))!;
    }

    internal static string ReleaseJson(
        string tag,
        string? page = null,
        bool draft = false,
        bool preRelease = false,
        IEnumerable<object>? assets = null,
        bool noAssets = false)
    {
        var release = new Dictionary<string, object?>
        {
            ["tag_name"] = tag,
            ["html_url"] = page ?? $"https://github.com/Jawad-Chughtai/MDGlint/releases/tag/{tag}",
            ["draft"] = draft,
            ["prerelease"] = preRelease,
            ["assets"] = assets ?? [Asset(UpdateSource.InstallerName), Asset(UpdateSource.HashName)],
        };
        if (noAssets)
        {
            release.Remove("assets");
        }

        return JsonSerializer.Serialize(release);
    }

    internal static object Asset(string name, string? url = null, long? size = null) => new Dictionary<string, object>
    {
        ["name"] = name,
        ["browser_download_url"] = url ?? Download + name,
        ["size"] = size ?? (name == UpdateSource.InstallerName ? Installer.Length : 100),
    };

    internal sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    internal sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _responses = [];
        private readonly Dictionary<string, Task> _gates = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        public void Respond(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            _responses[url] = () => new HttpResponseMessage(status) { Content = new StringContent(body) };

        public void Respond(string url, byte[] body, bool sendLength) =>
            _responses[url] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = sendLength ? new ByteArrayContent(body) : new StreamContent(new UnknownLengthStream(body)),
            };

        public void Fail(string url, Exception error) => _responses[url] = () => throw error;

        /// <summary>Requests to <paramref name="url"/> wait for <paramref name="gate"/> (or cancellation) first.</summary>
        public void Hold(string url, Task gate) => _gates[url] = gate;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var url = request.RequestUri!.AbsoluteUri;
            if (_gates.TryGetValue(url, out var gate))
            {
                await gate.WaitAsync(cancellationToken);
            }

            return _responses.TryGetValue(url, out var respond) ? respond() : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>A body without a Content-Length, as a server that streams it would send.</summary>
    internal sealed class UnknownLengthStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}

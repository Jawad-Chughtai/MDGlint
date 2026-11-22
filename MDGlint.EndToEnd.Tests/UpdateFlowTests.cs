using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using MDGlint.Core.Models;

namespace MDGlint.EndToEnd.Tests;

/// <summary>
/// The update bar against a stand-in for GitHub on 127.0.0.1 (the Debug build's update-server
/// hook sends every update request there). Setup itself is never run: the launch log records it.
/// </summary>
public sealed class UpdateFlowTests
{
    private const string NewVersion = "9.9.9";
    private static readonly byte[] Setup = RandomNumberGenerator.GetBytes(256 * 1024);

    [EndToEndFact]
    public async Task NeverChecksUnlessTheUserOptedIn()
    {
        using var github = FakeGitHub(NewVersion, Hash(Setup));

        await using (var app = await AppSession.StartAsync(null, new AppOptions { UpdateServer = github.Origin }))
        {
            await Task.Delay(3000);
            Assert.Empty(github.Requests);
            Assert.Null(app.Find("UpdateBarText"));
        }

        // Nor when the user turned checks on but has already checked today.
        var today = JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddHours(-1));
        await using (var app = await AppSession.StartAsync(null, new AppOptions
        {
            UpdateServer = github.Origin,
            Settings = $$"""{"CheckForUpdates":true,"LastUpdateCheck":{{today}}}""",
        }))
        {
            await Task.Delay(3000);
            Assert.Empty(github.Requests);
        }
    }

    [EndToEndFact]
    public async Task InstallsAVerifiedUpdateOnlyWhenAsked()
    {
        using var github = FakeGitHub(NewVersion, Hash(Setup));

        await using var app = await AppSession.StartAsync(null, new AppOptions
        {
            UpdateServer = github.Origin,
            Settings = """{"CheckForUpdates":true}""",
        });

        var text = await app.WaitForTextAsync("UpdateBarText", t => t.Contains("is available"));
        Assert.Equal($"MDGlint {NewVersion} is available (you have {CurrentVersion()}).", text);
        Assert.Equal([LatestReleaseApi], github.Requests); // Nothing downloaded yet.
        Assert.True(WindowTests.Settings(app).TryGetProperty("LastUpdateCheck", out _));

        await app.InvokeAsync("UpdateNotesButton");
        await AppSession.WaitUntilAsync(() => app.Launches().Length == 1, "Release notes didn't open.");
        Assert.Equal($"{UpdateSource.ReleasePagePrefix}tag/v{NewVersion}|", app.Launches()[0]);

        await app.InvokeAsync("UpdateInstallButton");

        // Setup runs quietly, and MDGlint closes so setup can replace it.
        Assert.True(app.Process.WaitForExit(30_000), "MDGlint didn't close for setup.");
        var setup = app.Launches()[1].Split('|');
        Assert.Equal(Path.GetFullPath(app.UpdatesFolder), Path.GetDirectoryName(setup[0]));
        Assert.Equal("/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /UPDATE=1", setup[1]);
        Assert.Equal(Setup, File.ReadAllBytes(setup[0]));
    }

    [EndToEndFact]
    public async Task ReopensTheFileAfterAnUpdate()
    {
        using var github = FakeGitHub(NewVersion, Hash(Setup));
        var docs = AppSession.NewFolder();
        try
        {
            var file = Path.Combine(docs, "notes.md");
            File.WriteAllText(file, "# Notes");

            // Installing notes the open file just before setup starts.
            string note;
            await using (var app = await AppSession.StartAsync(file, new AppOptions
            {
                UpdateServer = github.Origin,
                Settings = """{"CheckForUpdates":true}""",
            }))
            {
                await app.WaitForTextAsync("UpdateBarText", t => t.Contains("is available"));
                await app.InvokeAsync("UpdateInstallButton");
                Assert.True(app.Process.WaitForExit(30_000), "MDGlint didn't close for setup.");
                note = File.ReadAllText(Path.Combine(app.Folder, "data", "Local", "MDGlint", "reopen.txt"));
            }

            Assert.Equal(file, note);

            // Setup opens MDGlint with no file: it opens the noted one, and takes the note.
            string? local = null;
            await using var reopened = await AppSession.StartAsync(null, new AppOptions
            {
                BeforeStart = folder =>
                {
                    local = Path.Combine(folder, "data", "Local", "MDGlint");
                    Directory.CreateDirectory(local);
                    File.WriteAllText(Path.Combine(local, "reopen.txt"), note);
                },
            });
            await reopened.Page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'", "The file wasn't reopened.");
            Assert.False(File.Exists(Path.Combine(local!, "reopen.txt")));
        }
        finally
        {
            Directory.Delete(docs, recursive: true);
        }
    }

    [EndToEndFact]
    public async Task RefusesAnInstallerThatDoesNotMatchItsHash()
    {
        using var github = FakeGitHub(NewVersion, Hash(RandomNumberGenerator.GetBytes(16)));

        await using var app = await AppSession.StartAsync(null, new AppOptions
        {
            UpdateServer = github.Origin,
            Settings = """{"CheckForUpdates":true}""",
        });
        await app.WaitForTextAsync("UpdateBarText", t => t.Contains("is available"));

        await app.InvokeAsync("UpdateInstallButton");

        await app.WaitForTextAsync("UpdateBarText", t => t.Contains("doesn't match the release's SHA-256"), 30);
        Assert.Equal("Try again", app.Text("UpdateInstallLabel"));
        Assert.Empty(app.Launches());
        Assert.False(app.Process.HasExited);
        Assert.Empty(Directory.Exists(app.UpdatesFolder) ? Directory.GetFiles(app.UpdatesFolder) : []);

        await app.InvokeAsync("UpdateDismissButton");
        await AppSession.WaitUntilAsync(() => app.Find("UpdateBarText") is null, "The bar didn't close.");
    }

    [EndToEndFact]
    public async Task OffersAReleaseWithoutAHashOnlyAsALink()
    {
        // How a release older versions shouldn't install themselves is published (releasing.md).
        using var github = FakeGitHub(NewVersion, Hash(Setup), withHashFile: false);

        await using var app = await AppSession.StartAsync(null, new AppOptions
        {
            UpdateServer = github.Origin,
            Settings = """{"CheckForUpdates":true}""",
        });

        Assert.EndsWith("Install it from its release page.", await app.WaitForTextAsync("UpdateBarText", t => t.Contains("is available")));
        Assert.Null(app.Find("UpdateInstallButton"));
        await app.InvokeAsync("UpdateNotesButton");
        await AppSession.WaitUntilAsync(() => app.Launches().Length == 1, "The release page didn't open.");
        Assert.StartsWith(UpdateSource.ReleasePagePrefix, app.Launches()[0]);
    }

    [EndToEndFact]
    public async Task ChecksFromTheMenuAndRemembersTheChoice()
    {
        using var github = FakeGitHub(CurrentVersion(), Hash(Setup));

        await using var app = await AppSession.StartAsync(null, new AppOptions { UpdateServer = github.Origin });

        ((InvokePattern)(await app.MenuItemAsync("Check for updates now")).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Assert.Equal($"MDGlint {CurrentVersion()} is the latest version.",
            await app.WaitForTextAsync("UpdateBarText", t => t.Contains("latest")));
        // Asking once doesn't turn on the daily check.
        Assert.False(WindowTests.Settings(app).GetProperty("CheckForUpdates").GetBoolean());

        var automatic = await app.MenuItemAsync("Check for updates automatically");
        Assert.Equal(ToggleState.Off, ((TogglePattern)automatic.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        ((InvokePattern)automatic.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        await AppSession.WaitUntilAsync(() => WindowTests.Settings(app).GetProperty("CheckForUpdates").GetBoolean(), "The choice wasn't saved.");
    }

    [EndToEndFact]
    public async Task OffersBetasOnlyWhenTurnedOn()
    {
        const string beta = NewVersion + "-beta.1";
        using var github = FakeGitHub(CurrentVersion(), Hash(Setup), beta: beta);
        var today = JsonSerializer.Serialize(DateTimeOffset.UtcNow.AddHours(-1));

        await using var app = await AppSession.StartAsync(null, new AppOptions
        {
            UpdateServer = github.Origin,
            Settings = $$"""{"CheckForUpdates":true,"LastUpdateCheck":{{today}}}""",
        });

        // Off by default: the stable check finds nothing new.
        var betas = await app.MenuItemAsync("Get beta versions");
        Assert.Equal(ToggleState.Off, ((TogglePattern)betas.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        ((InvokePattern)(await app.MenuItemAsync("Check for updates now")).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        await app.WaitForTextAsync("UpdateBarText", t => t.Contains("latest version"));

        // Turned on, it checks at once and offers the beta, installable like any update.
        ((InvokePattern)(await app.MenuItemAsync("Get beta versions")).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Assert.Equal($"MDGlint {beta} is available (you have {CurrentVersion()}).",
            await app.WaitForTextAsync("UpdateBarText", t => t.Contains("is available")));
        Assert.NotNull(app.Find("UpdateInstallButton"));
        Assert.Equal([LatestReleaseApi, ReleasesApi], github.Requests);
        await AppSession.WaitUntilAsync(() => WindowTests.Settings(app).GetProperty("GetBetaVersions").GetBoolean(),
            "The choice wasn't saved.");

        // Turned off again, the beta is no longer offered.
        ((InvokePattern)(await app.MenuItemAsync("Get beta versions")).GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        await AppSession.WaitUntilAsync(() => app.Find("UpdateBarText") is null, "The beta was still offered.");
        Assert.False(WindowTests.Settings(app).GetProperty("GetBetaVersions").GetBoolean());
    }

    private static string LatestReleaseApi =>"/" + UpdateSource.LatestReleaseApi.Host + UpdateSource.LatestReleaseApi.AbsolutePath;

    /// <summary>The version the app reports: the build's version without its commit.</summary>
    private static string CurrentVersion() =>
        AppVersion.ToDisplay(FileVersionInfo.GetVersionInfo(AppSession.AppPath()).ProductVersion);

    private static string Hash(byte[] file) => Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();

    private static string ReleasesApi => "/" + UpdateSource.ReleasesApi.Host + UpdateSource.ReleasesApi.PathAndQuery;

    /// <summary>
    /// GitHub's latest-release API and the release's two files, at the paths the app asks for;
    /// with <paramref name="beta"/>, the releases list also has that pre-release, newest first.
    /// </summary>
    private static LocalServer FakeGitHub(string version, string hash, bool withHashFile = true, string? beta = null)
    {
        var download = $"{UpdateSource.DownloadPrefix}v{version}/";
        var release = Release(version, prerelease: false, withHashFile);
        var releases = beta is null ? new[] { release } : [Release(beta, prerelease: true, withHashFile), release];
        var downloadPath = "/" + new Uri(download).Host + new Uri(download).AbsolutePath;

        return new LocalServer(path =>
            path == LatestReleaseApi ? new LocalServer.Response(200, "application/json", JsonSerializer.SerializeToUtf8Bytes(release))
            : path == ReleasesApi ? new LocalServer.Response(200, "application/json", JsonSerializer.SerializeToUtf8Bytes(releases))
            : path == downloadPath + UpdateSource.InstallerName ? new LocalServer.Response(200, "application/octet-stream", Setup)
            : path == downloadPath + UpdateSource.HashName ? new LocalServer.Response(200, "text/plain", DevTools.Utf8($"{hash}  {UpdateSource.InstallerName}"))
            : LocalServer.NotFound);
    }

    private static object Release(string version, bool prerelease, bool withHashFile)
    {
        var download = $"{UpdateSource.DownloadPrefix}v{version}/";
        var assets = new List<object>
        {
            new { name = UpdateSource.InstallerName, browser_download_url = download + UpdateSource.InstallerName, size = Setup.Length },
        };
        if (withHashFile)
        {
            assets.Add(new { name = UpdateSource.HashName, browser_download_url = download + UpdateSource.HashName, size = 100 });
        }

        return new
        {
            tag_name = $"v{version}",
            html_url = $"{UpdateSource.ReleasePagePrefix}tag/v{version}",
            draft = false,
            prerelease,
            assets,
        };
    }
}

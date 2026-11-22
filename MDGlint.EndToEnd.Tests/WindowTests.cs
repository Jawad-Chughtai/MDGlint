using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
using MDGlint.Core.Models;

namespace MDGlint.EndToEnd.Tests;

/// <summary>The window around the preview: theme, remote content, the WebView2-missing notice.</summary>
public sealed class WindowTests : IDisposable
{
    private readonly string _docs = AppSession.NewFolder();

    public void Dispose() => Directory.Delete(_docs, recursive: true);

    [EndToEndFact]
    public async Task SwitchesAndRemembersTheTheme()
    {
        var file = Path.Combine(_docs, "notes.md");
        File.WriteAllText(file, "# Theme\n\n```js\nconst x = 1;\n```");

        await using var app = await AppSession.StartAsync(file, new AppOptions { Settings = """{"Theme":"light","CheckForUpdates":false}""" });
        var page = app.Page;
        Assert.Equal("light", await page.EvaluateAsync<string>("document.documentElement.dataset.theme"));
        Assert.True(await page.EvaluateAsync<bool>("document.querySelector('pre code').classList.contains('hljs')"));

        await app.InvokeAsync("ThemeButton");

        await page.WaitForAsync("document.documentElement.dataset.theme === 'dark'");
        Assert.EndsWith("github-markdown-dark.css", await page.EvaluateAsync<string>("document.getElementById('mdglint-markdown-theme').href"));
        Assert.EndsWith("github-dark.min.css", await page.EvaluateAsync<string>("document.getElementById('mdglint-code-theme').href"));
        Assert.Equal("dark", Settings(app).GetProperty("Theme").GetString());
    }

    [EndToEndFact]
    public async Task BlocksRemoteContentUntilAllowed()
    {
        using var server = new LocalServer(path => path.StartsWith("/remote.png", StringComparison.Ordinal)
            ? new LocalServer.Response(200, "image/png", PreviewTests.Png)
            : LocalServer.NotFound);
        var file = Path.Combine(_docs, "notes.md");
        File.WriteAllText(file, $"# Remote\n\n![remote]({server.Origin}/remote.png)");
        const string Loaded = "document.querySelector('img').complete && document.querySelector('img').naturalWidth === 1";

        await using var app = await AppSession.StartAsync(file);
        var page = app.Page;

        // Blocked by default, without the server hearing about it.
        await app.WaitForAsync("LoadRemoteButton");
        Assert.Empty(server.Requests);
        Assert.Equal(0, await page.EvaluateAsync<int>("document.querySelector('img').naturalWidth"));

        // For this file only: loads now and after a save, but isn't remembered.
        await app.InvokeAsync("LoadRemoteButton");
        await page.WaitForAsync(Loaded);
        Assert.Single(server.Requests);
        Assert.Null(app.Find("LoadRemoteButton"));
        File.WriteAllText(file, $"# Remote, saved\n\n![remote]({server.Origin}/remote.png?2)");
        await page.WaitForAsync("document.querySelector('h1').textContent === 'Remote, saved' && " + Loaded);
        Assert.Null(app.Find("LoadRemoteButton"));
        Assert.False(RemoteAllowed(app));

        // Always allow, then off again from the toolbar: blocked again.
        await app.InvokeAsync("RemoteButton");
        await AppSession.WaitUntilAsync(() => RemoteAllowed(app), "Always allow wasn't saved.");
        await page.WaitForAsync(Loaded);
        await app.InvokeAsync("RemoteButton");
        await AppSession.WaitUntilAsync(() => !RemoteAllowed(app), "Turning it off wasn't saved.");
        var requests = server.Requests.Count;
        await app.WaitForAsync("LoadRemoteButton");
        Assert.Equal(requests, server.Requests.Count);

        await app.InvokeAsync("DismissRemoteButton");
        await AppSession.WaitUntilAsync(() => app.Find("LoadRemoteButton") is null, "The bar didn't close.");

        // "Always allow" in the bar is remembered.
        await app.InvokeAsync("ReloadButton");
        await app.InvokeAsync("AlwaysAllowButton");
        await page.WaitForAsync(Loaded);
        Assert.True(RemoteAllowed(app));
    }

    [EndToEndFact]
    public async Task OpensMaximizedAndRemembersWhenItIsnt()
    {
        await using var app = await AppSession.StartAsync(null, new AppOptions { ConnectToPreview = false });
        var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);
        Assert.Equal(WindowVisualState.Maximized, window.Current.WindowVisualState);

        window.SetWindowVisualState(WindowVisualState.Normal);
        await AppSession.WaitUntilAsync(() => window.Current.WindowVisualState == WindowVisualState.Normal, "The window didn't restore.");
        app.Process.CloseMainWindow();
        Assert.True(app.Process.WaitForExit(10_000), "The window didn't close.");

        var settings = Settings(app);
        Assert.False(settings.GetProperty("WindowMaximized").GetBoolean());
        Assert.True(settings.GetProperty("WindowWidth").GetDouble() > 0);
        Assert.True(settings.GetProperty("WindowHeight").GetDouble() > 0);
    }

    [EndToEndFact]
    public async Task OpensWhereItWasLeft()
    {
        await using var app = await AppSession.StartAsync(null, new AppOptions
        {
            ConnectToPreview = false,
            Settings = """{"CheckForUpdates":false,"WindowMaximized":false,"WindowLeft":60,"WindowTop":40,"WindowWidth":900,"WindowHeight":600}""",
        });
        var window = (WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern);
        Assert.Equal(WindowVisualState.Normal, window.Current.WindowVisualState);

        // UI Automation reports physical pixels; the settings are device-independent ones.
        var bounds = app.Window.Current.BoundingRectangle;
        var scale = bounds.Width / 900;
        Assert.InRange(scale, 1, 4);
        Assert.Equal(600 * scale, bounds.Height, 2.0);
        Assert.Equal(60 * scale, bounds.Left, 2.0);
        Assert.Equal(40 * scale, bounds.Top, 2.0);
    }

    [EndToEndFact]
    public async Task ExplainsWhenWebView2IsMissing()
    {
        // An empty folder in place of the WebView2 Runtime.
        var noRuntime = Path.Combine(_docs, "no-runtime");
        Directory.CreateDirectory(noRuntime);

        await using var app = await AppSession.StartAsync(null, new AppOptions
        {
            ConnectToPreview = false,
            Environment = { ["WEBVIEW2_BROWSER_EXECUTABLE_FOLDER"] = noRuntime },
        });

        var title = await app.WaitForTextAsync("PreviewUnavailableTitle", t => t.Length > 0, 30);
        Assert.Equal(PreviewUnavailableNotice.RuntimeMissing.Title, title);
        Assert.Equal(PreviewUnavailableNotice.RuntimeDownloadUrl, app.Text("PreviewUnavailableLink"));

        await app.InvokeAsync("DownloadWebView2Button");
        await AppSession.WaitUntilAsync(() => app.Launches().Length == 1, "The download link didn't open.");
        Assert.Equal(PreviewUnavailableNotice.RuntimeDownloadUrl + "|", app.Launches()[0]);
    }

    [EndToEndFact]
    public async Task EndsAPreviewProcessAClosedWindowLeftBehind()
    {
        // Stands in for a leftover WebView2 browser process: a program named msedgewebview2.exe
        // whose command line names the session's data folder, started by something that isn't
        // MDGlint, so no MDGlint window owns it. (cmd waits on ping; "rem" ignores the rest.)
        var file = Path.Combine(_docs, "notes.md");
        File.WriteAllText(file, "# Notes");
        Process? leftover = null;
        try
        {
            await using var app = await AppSession.StartAsync(file, new AppOptions
            {
                ConnectToPreview = false,
                BeforeStart = folder =>
                {
                    var fake = Path.Combine(folder, "fake", "msedgewebview2.exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(fake)!);
                    File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), fake);
                    var dataDir = Path.Combine(folder, "data", "Local", "MDGlint", "WebView2", "EBWebView");
                    leftover = Process.Start(new ProcessStartInfo(fake)
                    {
                        Arguments = $"/d /c ping -n 120 127.0.0.1 >nul & rem --user-data-dir=\"{dataDir}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                },
            });

            // Said at once, not after WebView2's long timeout, and with the way out.
            var title = await app.WaitForTextAsync("PreviewUnavailableTitle", t => t.Length > 0, 10);
            Assert.Equal(PreviewUnavailableNotice.Stuck.Title, title);
            Assert.Null(app.Find("DownloadWebView2Button"));

            await app.InvokeAsync("EndStuckPreviewButton");
            await AppSession.WaitUntilAsync(() => leftover!.HasExited, "The leftover process wasn't ended.");
            await app.ConnectToPreviewAsync();
            await app.Page.WaitForAsync("document.querySelector('h1')?.textContent === 'Notes'", "The file wasn't shown.", 30);
            Assert.Equal("notes.md", await app.Page.EvaluateAsync<string>("document.title"));
        }
        finally
        {
            if (leftover is { HasExited: false })
            {
                leftover.Kill(entireProcessTree: true);
            }

            leftover?.Dispose();
        }
    }

    /// <summary>The saved "Always allow" choice; off until the user changes it.</summary>
    private static bool RemoteAllowed(AppSession app) =>
        Settings(app).TryGetProperty("AllowRemoteContent", out var allowed) && allowed.GetBoolean();

    internal static JsonElement Settings(AppSession app)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var settings = JsonDocument.Parse(File.ReadAllText(app.SettingsFile));
                return settings.RootElement.Clone();
            }
            catch (IOException) when (attempt < 20)
            {
                Thread.Sleep(50); // Being written.
            }
        }
    }
}

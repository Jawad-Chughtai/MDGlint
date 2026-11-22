using System.Diagnostics;
using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Tests;

public sealed class HelpersTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MDGlintTests", Guid.NewGuid().ToString("N"));

    public HelpersTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(@"C:\docs\notes.md", "notes.pdf")]
    [InlineData(@"C:\docs\release.notes.markdown", "release.notes.pdf")]
    [InlineData("readme.md", "readme.pdf")]
    public void ExportFiles_SuggestsTheFilesName(string path, string expected)
    {
        Assert.Equal(expected, ExportFiles.SuggestedName(path, ".pdf"));
    }

    [Fact]
    public void ExportFiles_SuggestsTheFilesFolder()
    {
        Assert.Equal(@"C:\docs", ExportFiles.SuggestedFolder(@"C:\docs\notes.md"));
        Assert.Equal(Path.GetFullPath("."), ExportFiles.SuggestedFolder("readme.md"));
    }

    [Theory]
    [InlineData("a.md", true)]
    [InlineData("A.MD", true)]
    [InlineData(@"C:\x\notes.markdown", true)]
    [InlineData("notes.mdx", false)]
    [InlineData("notes.txt", false)]
    [InlineData("md", false)]
    public void MarkdownFiles_RecognisesBothExtensions(string path, bool expected)
    {
        Assert.Equal(expected, MarkdownFiles.IsMarkdownFile(path));
    }

    [Fact]
    public void AppPaths_UseTheUsersFoldersByDefault()
    {
        var (roaming, local) = AppPaths.Resolve(null, @"C:\U\Roaming", @"C:\U\Local");

        Assert.Equal(@"C:\U\Roaming\MDGlint", roaming);
        Assert.Equal(@"C:\U\Local\MDGlint", local);
        Assert.Equal(AppPaths.Resolve(" ", @"C:\U\Roaming", @"C:\U\Local"), (roaming, local));
    }

    [Fact]
    public void AppPaths_UseTheTestFolderWhenGiven()
    {
        var (roaming, local) = AppPaths.Resolve(@"D:\e2e", @"C:\U\Roaming", @"C:\U\Local");

        Assert.Equal(@"D:\e2e\Roaming\MDGlint", roaming);
        Assert.Equal(@"D:\e2e\Local\MDGlint", local);
    }

    [Fact]
    public void AppPaths_NameEachFile()
    {
        Assert.Equal(Path.Combine(AppPaths.RoamingFolder, "settings.json"), AppPaths.SettingsFile);
        Assert.Equal(Path.Combine(AppPaths.LocalFolder, "WebView2"), AppPaths.WebView2Folder);
        Assert.Equal(Path.Combine(AppPaths.LocalFolder, "Updates"), AppPaths.UpdatesFolder);
        Assert.Equal(Path.Combine(AppPaths.LocalFolder, "startup-error.txt"), AppPaths.ErrorLogFile);
        Assert.Equal(Path.Combine(AppPaths.LocalFolder, "reopen.txt"), AppPaths.ReopenNoteFile);
    }

    [Fact]
    public void BrowserArguments_AreNoneUnlessGiven()
    {
        Assert.Null(BrowserArguments.Resolve(null));
        Assert.Null(BrowserArguments.Resolve(" "));
        Assert.Equal("--remote-debugging-port=9222", BrowserArguments.Resolve(" --remote-debugging-port=9222 "));
        // The tests don't set the variable for their own process.
        Assert.Null(BrowserArguments.Get());
    }

    [Fact]
    public void ProcessLauncher_RecordsLaunchesWhenLogging()
    {
        var log = Path.Combine(_folder, "launches.txt");

        ProcessLauncher.Start(new ProcessStartInfo("explorer.exe", "/select,\"C:\\x\""), log);
        ProcessLauncher.Start(new ProcessStartInfo("https://example.com/"), log);

        Assert.Equal(["explorer.exe|/select,\"C:\\x\"", "https://example.com/|"], File.ReadAllLines(log));
    }

    [Fact]
    public void ProcessLauncher_StartsTheProgramOtherwise()
    {
        var marker = Path.Combine(_folder, "ran.txt");

        ProcessLauncher.Start(
            new ProcessStartInfo("cmd.exe", $"/c echo ran> \"{marker}\"") { UseShellExecute = false, CreateNoWindow = true }, null);

        Assert.True(SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void ProcessLauncher_AcceptsATargetHandedToARunningProgram()
    {
        // The shell returns no process when an already-running program takes the target.
        ProcessStartInfo? started = null;

        ProcessLauncher.Start(new ProcessStartInfo("https://example.com/"), null, info =>
        {
            started = info;
            return null;
        });

        Assert.Equal("https://example.com/", started!.FileName);
    }

    [Fact]
    public void DocumentUrls_IsUnderComparesTheRoot()
    {
        Assert.True(DocumentUrls.TryAcceptUnder(@"c:\docs\a.md", @"C:\", out var inside));
        Assert.Equal(@"c:\docs\a.md", inside);
        Assert.False(DocumentUrls.TryAcceptUnder(@"D:\docs\a.md", @"C:\", out var outside));
        Assert.Empty(outside);
    }

    [Fact]
    public async Task FileMarkdownReader_ReadsTheFile()
    {
        var path = Path.Combine(_folder, "notes.md");
        File.WriteAllText(path, "# Notes");

        Assert.Equal("# Notes", await new Services.FileMarkdownReader().ReadAsync(path));
    }

    [Fact]
    public void ProcessLauncher_RequiresStartInfo()
    {
        Assert.Throws<ArgumentNullException>(() => ProcessLauncher.Start(null!, null));
    }

    [Fact]
    public void ProcessLauncher_BuildsBrowserAndExplorerLaunches()
    {
        // Through the logging hook the e2e tests use, so nothing opens.
        var log = Path.Combine(_folder, "launches.txt");

        ProcessLauncher.OpenWithDefaultApp(" ", log); // Nothing to open: ignored.
        ProcessLauncher.OpenWithDefaultApp("https://example.com/", log);
        ProcessLauncher.ShowInExplorer(@"C:\tools\setup.exe", log);

        Assert.Equal(["https://example.com/|", "explorer.exe|/select,\"C:\\tools\\setup.exe\""], File.ReadAllLines(log));
    }

    [Fact]
    public void ProcessLauncher_PublicCallsIgnoreAnEmptyTarget()
    {
        // The public entry points, without a target, so nothing real is started.
        ProcessLauncher.OpenWithDefaultApp(string.Empty);
    }

    [Fact]
    public void ErrorLog_SavesTheDetails()
    {
        var path = Path.Combine(_folder, "sub", "startup-error.txt");
        var error = new InvalidOperationException("boom");

        Assert.True(ErrorLog.TryWrite(path, error));
        Assert.Contains("boom", File.ReadAllText(path));
        Assert.Contains(path, ErrorLog.DialogText(path, saved: true, error));
    }

    [Fact]
    public void ErrorLog_SaysSoWhenItCannotSave()
    {
        var error = new InvalidOperationException("boom");
        var blocked = Path.Combine(_folder, "file-in-the-way");
        File.WriteAllText(blocked, string.Empty);

        Assert.False(ErrorLog.TryWrite(Path.Combine(blocked, "startup-error.txt"), error));
        Assert.DoesNotContain("saved to", ErrorLog.DialogText("x", saved: false, error));
        Assert.Throws<ArgumentNullException>(() => ErrorLog.TryWrite(blocked, null!));
    }
}

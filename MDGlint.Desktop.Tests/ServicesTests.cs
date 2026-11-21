using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using MDGlint.Core.Models;
using MDGlint.Desktop.Services;
using Microsoft.Win32;

namespace MDGlint.Desktop.Tests;

public sealed class ServicesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MDGlintTests", Guid.NewGuid().ToString("N"));
    private readonly string _registryKey = $@"Software\MDGlint.Tests\{Guid.NewGuid():N}";

    public ServicesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\MDGlint.Tests", throwOnMissingSubKey: false);
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AppSettingsStore_SavesAndLoads()
    {
        var store = new AppSettingsStore(Path.Combine(_folder, "new folder", "settings.json"));
        Assert.Equal("light", store.Load().Theme); // No file yet: defaults.

        store.Save(new AppSettings { Theme = "dark", CheckForUpdates = true });

        var loaded = store.Load();
        Assert.Equal("dark", loaded.Theme);
        Assert.True(loaded.CheckForUpdates);
    }

    [Fact]
    public void AppSettingsStore_FallsBackToDefaultsWhenUnreadable()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{"Theme":"dark"}""");
        var store = new AppSettingsStore(path);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal("light", store.Load().Theme);
        }

        Assert.Equal("dark", store.Load().Theme);
    }

    [Fact]
    public void AppSettingsStore_SavesWhileAnotherProgramIsReadingTheFile()
    {
        // As the end-to-end tests, an antivirus scan or a sync tool do: open it to read, briefly.
        // The reader lets go while the store waits to retry, so the test doesn't depend on timing.
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{"Theme":"light"}""");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var failedAttempts = new List<int>();
        var store = new AppSettingsStore(path, attempt =>
        {
            failedAttempts.Add(attempt);
            reader.Dispose();
        });

        store.Save(new AppSettings { Theme = "dark" });

        Assert.Equal([1], failedAttempts); // The reader did get in the way, once.
        Assert.Equal("dark", store.Load().Theme);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AppSettingsStore_WaitsBrieflyBetweenAttempts()
    {
        var waited = Stopwatch.StartNew();
        AppSettingsStore.WaitBriefly(1);

        Assert.InRange(waited.ElapsedMilliseconds, 40, 5000);
        Assert.Equal(10, AppSettingsStore.Attempts);
    }

    [Fact]
    public void AppSettingsStore_KeepsGoingWhenTheFileStaysLocked()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{"Theme":"light"}""");
        var waits = 0;
        var store = new AppSettingsStore(path, _ => waits++);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            store.Save(new AppSettings { Theme = "dark" }); // Doesn't throw: the change lasts this session.
        }

        Assert.Equal(AppSettingsStore.Attempts - 1, waits); // Tried every time before giving up.
        Assert.Equal("light", store.Load().Theme);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void AppSettingsStore_KeepsGoingWhenItCantWriteAtAll()
    {
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, """{"Theme":"light"}""");
        var store = new AppSettingsStore(path, _ => { });

        // Even its temporary file is held open, so it can neither write nor clean up.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        using (new FileStream(path + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
        {
            store.Save(new AppSettings { Theme = "dark" });
        }

        Assert.Equal("light", store.Load().Theme);
    }

    [Fact]
    public void AppSettingsStore_DefaultsToTheUsersSettingsFile()
    {
        Assert.Equal(Helpers.AppPaths.SettingsFile, new AppSettingsStore().SettingsPath);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ReadSetupChoice_ReadsWhatSetupWrote(int value, bool expected)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_registryKey))
        {
            key.SetValue("CheckForUpdates", value, RegistryValueKind.DWord);
        }

        Assert.Equal(expected, UpdateLauncher.ReadSetupChoice(_registryKey, Registry.CurrentUser));
    }

    [Fact]
    public void ReadSetupChoice_TriesEachRootInTurn()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_registryKey))
        {
            key.SetValue("CheckForUpdates", "1", RegistryValueKind.String); // Not a DWORD: ignored.
        }

        Assert.Null(UpdateLauncher.ReadSetupChoice(_registryKey, Registry.CurrentUser, Registry.CurrentUser));
        Assert.Null(UpdateLauncher.ReadSetupChoice(_registryKey + @"\missing", Registry.CurrentUser));
    }

    [Fact]
    public void ReadSetupChoice_SkipsARootThatCannotBeRead()
    {
        // Two roots holding the same key: the first one denies this user access to it.
        using var denied = Registry.CurrentUser.CreateSubKey(_registryKey + @"\denied");
        using var readable = Registry.CurrentUser.CreateSubKey(_registryKey + @"\readable");
        using (var key = readable.CreateSubKey("probe"))
        {
            key.SetValue("CheckForUpdates", 1, RegistryValueKind.DWord);
        }

        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.RegistryAccessRule(
            user, System.Security.AccessControl.RegistryRights.ReadKey, System.Security.AccessControl.AccessControlType.Deny);
        using (var key = denied.CreateSubKey("probe", RegistryKeyPermissionCheck.ReadWriteSubTree))
        {
            var security = key.GetAccessControl();
            security.AddAccessRule(deny);
            key.SetAccessControl(security);
        }

        try
        {
            Assert.Throws<System.Security.SecurityException>(() => denied.OpenSubKey("probe"));
            Assert.True(UpdateLauncher.ReadSetupChoice("probe", denied, readable));
        }
        finally
        {
            // The owner can always change the permissions back, so the key can be deleted.
            using var key = denied.OpenSubKey(
                "probe", RegistryKeyPermissionCheck.ReadWriteSubTree, System.Security.AccessControl.RegistryRights.ChangePermissions)!;
            var security = key.GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
            security.RemoveAccessRule(deny);
            key.SetAccessControl(security);
        }
    }

    [Fact]
    public void ReadSetupChoice_ReadsTheRealKeysWithoutFailing()
    {
        // Whatever this machine has installed, reading must not throw.
        _ = UpdateLauncher.ReadSetupChoice();
    }

    [Fact]
    public void DeleteOldDownloads_RemovesTheFolder()
    {
        var updates = Path.Combine(_folder, "Updates");
        Directory.CreateDirectory(updates);
        File.WriteAllText(Path.Combine(updates, "MDGlintSetup.exe"), "old");

        UpdateLauncher.DeleteOldDownloads(updates);
        UpdateLauncher.DeleteOldDownloads(updates); // Already gone: nothing to do.

        Assert.False(Directory.Exists(updates));
    }

    [Fact]
    public void DeleteOldDownloads_LeavesAFileStillInUse()
    {
        var updates = Path.Combine(_folder, "Updates");
        Directory.CreateDirectory(updates);
        var installer = Path.Combine(updates, "MDGlintSetup.exe");

        using (new FileStream(installer, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            UpdateLauncher.DeleteOldDownloads(updates);
            Assert.True(File.Exists(installer));
        }
    }

    [Fact]
    public void StartInstaller_RunsSetupQuietlyAndReleasesTheFile()
    {
        var (installer, file) = Installer();
        ProcessStartInfo? started = null;

        Assert.Null(UpdateLauncher.StartInstaller(installer, info => started = info));

        Assert.Equal(file, started!.FileName);
        Assert.Equal(UpdateLauncher.InstallerArguments, started.Arguments);
        Assert.Contains("/UPDATE=1", started.Arguments);
        Assert.True(started.UseShellExecute);
        File.Delete(file); // Released.
    }

    [Theory]
    [InlineData(typeof(Win32Exception))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(IOException))]
    public void StartInstaller_ReportsWhySetupDidNotStart(Type error)
    {
        var (installer, file) = Installer();
        var exception = (Exception)Activator.CreateInstance(error, "declined")!;

        Assert.Equal("declined", UpdateLauncher.StartInstaller(installer, _ => throw exception));
        File.Delete(file);
    }

    [Fact]
    public async Task CreateHttpClient_SendsRequestsStraightThrough()
    {
        var inner = new RecordingHandler();

        using var client = UpdateLauncher.CreateHttpClient(null, inner);
        await client.GetAsync("https://api.github.com/repos/x/y/releases/latest");

        Assert.Equal("https://api.github.com/repos/x/y/releases/latest", inner.Last!.AbsoluteUri);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public async Task CreateHttpClient_RedirectsToALocalTestServer()
    {
        var inner = new RecordingHandler();

        using var client = UpdateLauncher.CreateHttpClient("http://127.0.0.1:8765", inner);
        await client.GetAsync("https://github.com/owner/repo/releases/download/v1.2.0/MDGlintSetup.exe?x=1");

        Assert.Equal(
            "http://127.0.0.1:8765/github.com/owner/repo/releases/download/v1.2.0/MDGlintSetup.exe?x=1",
            inner.Last!.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://example.com:8765")] // Only a server on this machine.
    [InlineData("not a url")]
    public async Task CreateHttpClient_NeverRedirectsElsewhere(string server)
    {
        var inner = new RecordingHandler();

        using var client = UpdateLauncher.CreateHttpClient(server, inner);
        await client.GetAsync("https://api.github.com/x");

        Assert.Equal("api.github.com", inner.Last!.Host);
    }

    [Fact]
    public void CreateHttpClient_BuildsTheRealClient()
    {
        using var client = UpdateLauncher.CreateHttpClient();

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    private (VerifiedInstaller Installer, string File) Installer()
    {
        var file = Path.Combine(_folder, $"{Guid.NewGuid():N}.exe");
        File.WriteAllText(file, "setup");
        SemanticVersion.TryParse("1.2.0", out var version);
        return (new VerifiedInstaller(file, version, new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)), file);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}

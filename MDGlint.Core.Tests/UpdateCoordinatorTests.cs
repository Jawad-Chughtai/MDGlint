using System.Net;
using System.Security.Cryptography;
using MDGlint.Core.Contracts;
using MDGlint.Core.Models;
using MDGlint.Core.Services;
using static MDGlint.Core.Tests.UpdateServiceTests;

namespace MDGlint.Core.Tests;

public sealed class UpdateCoordinatorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
    private static readonly string Api = UpdateSource.LatestReleaseApi.AbsoluteUri;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MDGlintTests", Guid.NewGuid().ToString("N"));
    private readonly FakeHandler _handler = new();
    private readonly FakeStore _store = new();
    private readonly List<VerifiedInstaller> _started = [];
    private readonly List<UpdateBarState> _states = [];
    private string? _startError;
    private int _shutdowns;

    public void Dispose()
    {
        _started.ForEach(installer => installer.Dispose());
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Constructor_RequiresItsParts()
    {
        var service = new UpdateService(new HttpClient(_handler), "1.0.0");
        Assert.Throws<ArgumentNullException>(() => new UpdateCoordinator(null!, _store, new(), null, _folder, Start));
        Assert.Throws<ArgumentNullException>(() => new UpdateCoordinator(service, null!, new(), null, _folder, Start));
        Assert.Throws<ArgumentNullException>(() => new UpdateCoordinator(service, _store, null!, null, _folder, Start));
        Assert.Throws<ArgumentNullException>(() => new UpdateCoordinator(service, _store, new(), null, null!, Start));
        Assert.Throws<ArgumentNullException>(() => new UpdateCoordinator(service, _store, new(), null, _folder, null!));
    }

    [Fact]
    public void UsesTheRealClockWhenNoneIsGiven()
    {
        var coordinator = new UpdateCoordinator(
            new UpdateService(new HttpClient(_handler), "1.0.0"), _store, new AppSettings(), null, _folder, Start);

        Assert.False(coordinator.AutomaticChecks);
        Assert.False(coordinator.IsBusy);
        Assert.Equal(UpdateBarState.Hidden, coordinator.State);
    }

    [Fact]
    public async Task CheckIfDueAsync_DoesNothingWhileChecksAreOff()
    {
        var coordinator = Coordinator(new AppSettings());

        await coordinator.CheckIfDueAsync();

        Assert.Empty(_handler.Requests);
        Assert.Empty(_states);
    }

    [Fact]
    public async Task CheckIfDueAsync_ChecksWhenSetupTurnedThemOn()
    {
        _handler.Respond(Api, ReleaseJson("v1.0.0"));
        var coordinator = Coordinator(new AppSettings(), setupChoice: true);

        await coordinator.CheckIfDueAsync();

        Assert.Single(_handler.Requests);
        Assert.Empty(_states); // Up to date: a check of MDGlint's own stays quiet.
        Assert.Equal(Now, _store.Saved!.LastUpdateCheck);
    }

    [Fact]
    public async Task CheckIfDueAsync_ShowsAnInstallableUpdate()
    {
        _handler.Respond(Api, ReleaseJson("v1.2.0"));
        var coordinator = Coordinator(new AppSettings { CheckForUpdates = true });

        await coordinator.CheckIfDueAsync();

        Assert.Equal(new UpdateBarState(true, "MDGlint 1.2.0 is available (you have 1.0.0).", "Install", true), coordinator.State);
        Assert.Equal("1.2.0", coordinator.Update!.Version.ToString());
    }

    [Fact]
    public async Task CheckIfDueAsync_StaysQuietWhenTheCheckFails()
    {
        _handler.Respond(Api, "{}", HttpStatusCode.InternalServerError);
        var settings = new AppSettings { CheckForUpdates = true };
        var coordinator = Coordinator(settings);

        await coordinator.CheckIfDueAsync();

        Assert.Empty(_states);
        Assert.Null(settings.LastUpdateCheck); // Tried again at the next start.
    }

    [Fact]
    public async Task CheckAsync_OffersOnlyThePageForAPageOnlyRelease()
    {
        _handler.Respond(Api, ReleaseJson("v1.2.0", assets: [Asset(UpdateSource.InstallerName)]));
        var coordinator = Coordinator(new AppSettings());

        await coordinator.CheckAsync(userAsked: true);

        Assert.Equal(
            new UpdateBarState(true, "MDGlint 1.2.0 is available (you have 1.0.0). Install it from its release page.", null, true),
            coordinator.State);
    }

    [Fact]
    public async Task CheckAsync_SaysWhenUpToDateOrFailedIfTheUserAsked()
    {
        _handler.Respond(Api, ReleaseJson("v1.0.0"));
        var coordinator = Coordinator(new AppSettings());

        await coordinator.CheckAsync(userAsked: true);
        Assert.Equal("Checking for updates…", _states[0].Text);
        Assert.Equal(new UpdateBarState(true, "MDGlint 1.0.0 is the latest version.", null, false), coordinator.State);

        _handler.Respond(Api, "{}", HttpStatusCode.Forbidden);
        await coordinator.CheckAsync(userAsked: true);
        Assert.StartsWith("Couldn't check for updates. GitHub answered 403", coordinator.State.Text);
    }

    [Fact]
    public async Task CheckAsync_ReportsATimeout()
    {
        _handler.Respond(Api, ReleaseJson("v1.2.0"));
        _handler.Hold(Api, new TaskCompletionSource().Task); // GitHub never answers.
        var coordinator = new UpdateCoordinator(
            new UpdateService(new HttpClient(_handler), "1.0.0"), _store, new AppSettings(), null, _folder, Start, () => Now)
        {
            CheckTimeout = TimeSpan.FromMilliseconds(50),
        };

        await coordinator.CheckAsync(userAsked: true);

        Assert.Equal("Couldn't check for updates. GitHub took too long to answer.", coordinator.State.Text);
    }

    [Fact]
    public async Task WorksWithoutListenersOrADownload()
    {
        _handler.Respond(Api, ReleaseJson("v1.0.0"));
        var coordinator = new UpdateCoordinator(
            new UpdateService(new HttpClient(_handler), "1.0.0"), _store, new AppSettings(), null, _folder, Start, () => Now);

        await coordinator.CheckAsync(userAsked: true);
        coordinator.Dismiss();

        Assert.Equal(UpdateBarState.Hidden, coordinator.State);
    }

    [Fact]
    public async Task ReportsProgressDirectlyWithoutAWindowContext()
    {
        // Created on a thread-pool thread, where there is no synchronization context.
        var coordinator = await Task.Run(() => CoordinatorWithUpdate(SHA256.HashData(Installer)));

        await coordinator.InstallOrCancelAsync();

        Assert.Contains(_states, s => s.Text == "Downloading MDGlint 1.2.0… 100%");
    }

    [Fact]
    public async Task CheckAsync_IgnoresASecondCheckWhileOneRuns()
    {
        var gate = new TaskCompletionSource();
        _handler.Respond(Api, ReleaseJson("v1.0.0"));
        _handler.Hold(Api, gate.Task);
        var coordinator = Coordinator(new AppSettings());

        var first = coordinator.CheckAsync(userAsked: true);
        Assert.True(coordinator.IsBusy);
        await coordinator.CheckAsync(userAsked: true);
        gate.SetResult();
        await first;

        Assert.Single(_handler.Requests);
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task SetAutomaticChecksAsync_SavesTheChoiceAndChecksWhenDue()
    {
        _handler.Respond(Api, ReleaseJson("v1.0.0"));
        var settings = new AppSettings();
        var coordinator = Coordinator(settings, setupChoice: false);

        await coordinator.SetAutomaticChecksAsync(true);
        Assert.True(coordinator.AutomaticChecks);
        Assert.True(_store.Saved!.CheckForUpdates);
        Assert.Single(_handler.Requests);

        await coordinator.SetAutomaticChecksAsync(false);
        Assert.False(coordinator.AutomaticChecks);
        Assert.Single(_handler.Requests);
    }

    [Fact]
    public async Task InstallOrCancelAsync_DownloadsVerifiesStartsSetupAndShutsDown()
    {
        var coordinator = await CoordinatorWithUpdate(SHA256.HashData(Installer));

        await coordinator.InstallOrCancelAsync();

        var installer = Assert.Single(_started);
        Assert.Equal("1.2.0", installer.Version.ToString());
        Assert.Equal(1, _shutdowns);
        Assert.Equal(new UpdateBarState(true, "Installing MDGlint 1.2.0…", null, false), coordinator.State);
        Assert.Contains(_states, s => s.Text == "Downloading MDGlint 1.2.0…" && s.InstallLabel == "Cancel");
        Assert.Contains(_states, s => s.Text == "Downloading MDGlint 1.2.0… 100%");
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task InstallOrCancelAsync_OffersToTryAgainAfterAHashMismatch()
    {
        var coordinator = await CoordinatorWithUpdate(SHA256.HashData([1, 2, 3]));

        await coordinator.InstallOrCancelAsync();

        Assert.Empty(_started);
        Assert.Equal("Try again", coordinator.State.InstallLabel);
        Assert.Contains("SHA-256", coordinator.State.Text);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task InstallOrCancelAsync_StaysOpenWhenSetupCannotStart()
    {
        var coordinator = await CoordinatorWithUpdate(SHA256.HashData(Installer));
        _startError = "The operation was canceled by the user.";

        await coordinator.InstallOrCancelAsync();

        Assert.Equal(new UpdateBarState(true, "Setup couldn't be started. The operation was canceled by the user.", "Try again", true),
            coordinator.State);
        Assert.Equal(0, _shutdowns);
    }

    [Fact]
    public async Task InstallOrCancelAsync_CancelsADownloadInProgress()
    {
        var coordinator = await CoordinatorWithUpdate(SHA256.HashData(Installer), holdDownload: true);

        var install = coordinator.InstallOrCancelAsync();
        Assert.True(coordinator.IsBusy);
        await coordinator.CheckAsync(userAsked: true); // Ignored while downloading.
        await coordinator.InstallOrCancelAsync(); // The button reads "Cancel".
        await install;

        Assert.Empty(_started);
        Assert.Equal("Install", coordinator.State.InstallLabel); // Back to the offer.
        Assert.False(coordinator.IsBusy);
    }

    [Fact]
    public async Task Dismiss_HidesTheBarAndStopsTheDownload()
    {
        var coordinator = await CoordinatorWithUpdate(SHA256.HashData(Installer), holdDownload: true);

        var install = coordinator.InstallOrCancelAsync();
        coordinator.Dismiss();
        await install;

        Assert.Equal(UpdateBarState.Hidden, coordinator.State);
        Assert.Empty(_started);
    }

    [Fact]
    public async Task InstallOrCancelAsync_DoesNothingWithoutAnInstallableUpdate()
    {
        var coordinator = Coordinator(new AppSettings());
        await coordinator.InstallOrCancelAsync();

        _handler.Respond(Api, ReleaseJson("v1.2.0", assets: []));
        await coordinator.CheckAsync(userAsked: true);
        await coordinator.InstallOrCancelAsync();

        Assert.Empty(_started);
        Assert.Null(coordinator.State.InstallLabel);
    }

    [Fact]
    public async Task InstallOrCancelAsync_WaitsForARunningCheck()
    {
        var coordinator = await CoordinatorWithUpdate(SHA256.HashData(Installer));
        var gate = new TaskCompletionSource();
        _handler.Hold(Api, gate.Task);

        var check = coordinator.CheckAsync(userAsked: true);
        await coordinator.InstallOrCancelAsync();
        gate.SetResult();
        await check;

        Assert.Empty(_started);
    }

    [Fact]
    public async Task ReportsProgressThroughTheWindowsContext()
    {
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        UpdateCoordinator coordinator;
        try
        {
            coordinator = await CoordinatorWithUpdate(SHA256.HashData(Installer));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        await coordinator.InstallOrCancelAsync();
        context.RunPosted();

        Assert.True(context.Posted > 0);
        // Progress that arrives after setup started doesn't replace the "Installing" text.
        Assert.Equal("Installing MDGlint 1.2.0…", coordinator.State.Text);
    }

    private async Task<UpdateCoordinator> CoordinatorWithUpdate(byte[] hash, bool holdDownload = false)
    {
        ServeRelease(_handler, Installer, hash);
        _handler.Respond(Api, ReleaseJson("v1.2.0", assets:
            [Asset(UpdateSource.InstallerName), Asset(UpdateSource.HashName)]));
        if (holdDownload)
        {
            _handler.Hold(Download + UpdateSource.InstallerName, new TaskCompletionSource().Task);
        }

        var coordinator = Coordinator(new AppSettings());
        await coordinator.CheckAsync(userAsked: false);
        return coordinator;
    }

    private UpdateCoordinator Coordinator(AppSettings settings, bool? setupChoice = null)
    {
        var coordinator = new UpdateCoordinator(
            new UpdateService(new HttpClient(_handler), "1.0.0"), _store, settings, setupChoice, _folder, Start, () => Now);
        coordinator.StateChanged += (_, _) => _states.Add(coordinator.State);
        coordinator.ShutdownRequested += (_, _) => _shutdowns++;
        return coordinator;
    }

    private string? Start(VerifiedInstaller installer)
    {
        if (_startError is not null)
        {
            installer.Dispose();
            return _startError;
        }

        _started.Add(installer);
        return null;
    }

    private sealed class FakeStore : ISettingsStore
    {
        public AppSettings? Saved { get; private set; }

        public AppSettings Load() => Saved ?? new AppSettings();

        public void Save(AppSettings settings) => Saved = settings;
    }

    /// <summary>Stands in for the window's thread: holds posted work until <see cref="RunPosted"/>.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly List<(SendOrPostCallback Callback, object? State)> _queue = [];

        public int Posted { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_queue)
            {
                Posted++;
                _queue.Add((d, state));
            }
        }

        public void RunPosted()
        {
            (SendOrPostCallback, object?)[] work;
            lock (_queue)
            {
                work = [.. _queue];
                _queue.Clear();
            }

            foreach (var (callback, state) in work)
            {
                callback(state);
            }
        }
    }
}

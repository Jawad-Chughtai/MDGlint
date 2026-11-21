using MDGlint.Core.Contracts;
using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

/// <summary>What the update bar shows. <see cref="InstallLabel"/> null hides that button.</summary>
public sealed record UpdateBarState(bool Visible, string Text, string? InstallLabel, bool ShowReleaseNotes)
{
    public static UpdateBarState Hidden { get; } = new(false, string.Empty, null, false);
}

/// <summary>
/// Runs update checks and installs for the window: decides when MDGlint checks on its own, what
/// the update bar says, and when the app hands over to setup. The window only shows
/// <see cref="State"/> and forwards button clicks.
/// </summary>
public sealed class UpdateCoordinator
{
    /// <summary>How long a check waits for GitHub.</summary>
    public TimeSpan CheckTimeout { get; init; } = TimeSpan.FromSeconds(30);

    private readonly UpdateService _service;
    private readonly ISettingsStore _store;
    private readonly AppSettings _settings;
    private readonly bool? _setupChoice;
    private readonly string _downloadFolder;
    private readonly Func<VerifiedInstaller, string?> _startInstaller;
    private readonly Func<DateTimeOffset> _now;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private CancellationTokenSource? _download;
    private bool _checking;

    /// <param name="startInstaller">Starts setup for the installer; returns null when it started,
    /// or why it couldn't. After it started, <see cref="ShutdownRequested"/> is raised.</param>
    public UpdateCoordinator(
        UpdateService service,
        ISettingsStore store,
        AppSettings settings,
        bool? setupChoice,
        string downloadFolder,
        Func<VerifiedInstaller, string?> startInstaller,
        Func<DateTimeOffset>? now = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _setupChoice = setupChoice;
        _downloadFolder = downloadFolder ?? throw new ArgumentNullException(nameof(downloadFolder));
        _startInstaller = startInstaller ?? throw new ArgumentNullException(nameof(startInstaller));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public event EventHandler? StateChanged;

    /// <summary>Setup has started; the app must close so it can replace MDGlint's files.</summary>
    public event EventHandler? ShutdownRequested;

    public UpdateBarState State { get; private set; } = UpdateBarState.Hidden;

    /// <summary>The release the bar is about, for its Release notes button.</summary>
    public AvailableUpdate? Update { get; private set; }

    public bool IsBusy => _checking || _download is not null;

    /// <summary>The effective "Check for updates automatically" choice.</summary>
    public bool AutomaticChecks => UpdatePreference.IsEnabled(_settings, _setupChoice);

    private string CurrentVersion => _service.CurrentVersion;

    /// <summary>Saves the user's choice; turning checks on checks right away if one is due.</summary>
    public Task SetAutomaticChecksAsync(bool enabled)
    {
        _settings.CheckForUpdates = enabled;
        _store.Save(_settings);
        return CheckIfDueAsync();
    }

    /// <summary>The check MDGlint makes on its own: only when checks are on, at most once a day.</summary>
    public Task CheckIfDueAsync() =>
        UpdatePreference.IsCheckDue(_settings, _setupChoice, _now())
            ? CheckAsync(userAsked: false)
            : Task.CompletedTask;

    /// <summary>
    /// Asks for the latest release. A check of MDGlint's own only speaks up when there is an
    /// update; one the user asked for also says it is up to date, or why it failed.
    /// </summary>
    public async Task CheckAsync(bool userAsked)
    {
        if (IsBusy)
        {
            return;
        }

        _checking = true;
        if (userAsked)
        {
            SetState(new UpdateBarState(true, "Checking for updates…", null, false));
        }

        UpdateCheckResult result;
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            result = await _service.CheckAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            result = UpdateCheckResult.Failed("GitHub took too long to answer.");
        }
        finally
        {
            _checking = false;
        }

        if (result.Status != UpdateCheckStatus.Failed)
        {
            _settings.LastUpdateCheck = _now();
            _store.Save(_settings);
        }

        if (result.Status == UpdateCheckStatus.Available)
        {
            ShowAvailable(result.Update!);
        }
        else if (userAsked)
        {
            SetState(new UpdateBarState(
                true,
                result.Status == UpdateCheckStatus.UpToDate
                    ? $"MDGlint {CurrentVersion} is the latest version."
                    : $"Couldn't check for updates. {result.Error}",
                null,
                false));
        }
    }

    /// <summary>The Install button: downloads, verifies and starts setup; while downloading it cancels.</summary>
    public async Task InstallOrCancelAsync()
    {
        if (_download is not null)
        {
            await _download.CancelAsync();
            return;
        }

        if (Update is not { CanInstall: true } update || _checking)
        {
            return;
        }

        using var download = new CancellationTokenSource();
        _download = download;
        SetState(new UpdateBarState(true, $"Downloading MDGlint {update.Version}…", "Cancel", true));
        var progress = new ProgressReporter(_context, fraction =>
        {
            if (ReferenceEquals(_download, download))
            {
                SetState(State with { Text = $"Downloading MDGlint {update.Version}… {fraction:P0}" });
            }
        });

        VerifiedInstaller installer;
        try
        {
            installer = await _service.DownloadInstallerAsync(update, _downloadFolder, progress, download.Token);
        }
        catch (OperationCanceledException)
        {
            _download = null;
            if (State.Visible)
            {
                ShowAvailable(update); // Cancelled; a dismissed bar stays hidden.
            }

            return;
        }
        catch (UpdateDownloadException ex)
        {
            _download = null;
            SetState(new UpdateBarState(true, ex.Message, "Try again", true));
            return;
        }

        _download = null;
        SetState(new UpdateBarState(true, $"Installing MDGlint {update.Version}…", null, false));
        var error = _startInstaller(installer);
        if (error is not null)
        {
            SetState(new UpdateBarState(true, $"Setup couldn't be started. {error}", "Try again", true));
            return;
        }

        ShutdownRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hides the bar, and stops a download in progress.</summary>
    public void Dismiss()
    {
        _download?.Cancel();
        SetState(UpdateBarState.Hidden);
    }

    private void ShowAvailable(AvailableUpdate update)
    {
        Update = update;
        var text = $"MDGlint {update.Version} is available (you have {CurrentVersion}).";
        SetState(update.CanInstall
            ? new UpdateBarState(true, text, "Install", true)
            // Rule 5 in docs/releasing.md: a release MDGlint shouldn't install itself.
            : new UpdateBarState(true, text + " Install it from its release page.", null, true));
    }

    private void SetState(UpdateBarState state)
    {
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reports progress on the window's thread when there is one; <see cref="Progress{T}"/> would
    /// post to the thread pool instead when there isn't, out of order with the other states.
    /// </summary>
    private sealed class ProgressReporter(SynchronizationContext? context, Action<double> report) : IProgress<double>
    {
        public void Report(double value)
        {
            if (context is null)
            {
                report(value);
            }
            else
            {
                context.Post(_ => report(value), null);
            }
        }
    }
}

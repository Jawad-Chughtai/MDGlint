using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MDGlint.Core.Models;
using MDGlint.Core.Services;
using MDGlint.Desktop.Helpers;
using MDGlint.Desktop.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MDGlint.Desktop;

[ExcludeFromCodeCoverage(Justification = "WPF window glue: it forwards clicks and shows state. The decisions are in "
    + "Core (RenderPlan, UpdateCoordinator, RemoteContentPolicy, ThemeService) and PreviewPolicy, with unit tests; "
    + "the window itself is covered by the end-to-end tests.")]
public partial class MainWindow : Window
{
    private const int DwmaUseImmersiveDarkMode = 20;
    private readonly FileMarkdownReader _fileReader = new();
    private readonly FileWatcherService _fileWatcher;
    private readonly MarkdownRenderer _renderer = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly AppSettings _settings;
    private readonly ThemeService _themeService;
    private readonly RemoteContentPolicy _remotePolicy;
    private readonly UpdateCoordinator _updates;
    private PreviewHost? _preview;
    private string? _currentFilePath;
    private string? _shownFilePath;

    public MainWindow(string? filePath = null)
    {
        InitializeComponent();

        _settings = _settingsStore.Load();
        _themeService = new ThemeService(_settings.Theme);
        _remotePolicy = new RemoteContentPolicy(_settings.AllowRemoteContent);
        _fileWatcher = new FileWatcherService(RenderCurrentStateAsync);
        _currentFilePath = filePath;
        RestorePlacement();

        var version = AppVersion.ToDisplay(
            typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        VersionRun.Text = version.Length > 0 ? "  " + version : string.Empty;

        UpdateLauncher.DeleteOldDownloads(AppPaths.UpdatesFolder);
        _updates = new UpdateCoordinator(
            new UpdateService(UpdateLauncher.CreateHttpClient(), version),
            _settingsStore,
            _settings,
            UpdateLauncher.ReadSetupChoice(),
            AppPaths.UpdatesFolder,
            UpdateLauncher.StartInstaller);
        _updates.StateChanged += (_, _) => ShowUpdateState();
        // Setup replaces MDGlint's files, so MDGlint closes; setup opens it again when it's done.
        _updates.ShutdownRequested += (_, _) => Application.Current.Shutdown();

        Loaded += MainWindow_Loaded;
        Loaded += (_, _) => _ = _updates.CheckIfDueAsync();
        SourceInitialized += (_, _) => ApplyThemeChrome();
        Closing += (_, _) =>
        {
            WindowPlacement.Remember(_settings, RestoreBounds, WindowState);
            _settingsStore.Save(_settings);
        };
        Closed += (_, _) =>
        {
            _fileWatcher.Dispose();
            _updates.Dismiss();
        };
        ApplyThemeChrome();
        ApplyRemoteButton();
    }

    /// <summary>Before the window is shown: the last normal position and size, then maximized if it was.</summary>
    private void RestorePlacement()
    {
        var screen = new WindowBounds(
            SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (WindowPlacement.Restore(_settings, screen, MinWidth, MinHeight) is { } bounds)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        // Set after the bounds, so it maximizes on the monitor it was last on.
        WindowState = _settings.WindowMaximized ? WindowState.Maximized : WindowState.Normal;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) => await StartPreviewAsync();

    private async Task StartPreviewAsync()
    {
        PreviewUnavailablePanel.Visibility = Visibility.Collapsed;
        PreviewStartingText.Visibility = Visibility.Visible;

        // A preview process left by a closed window would make this one wait and time out, so say
        // so at once instead.
        if (PreviewUnavailableNotice.BeforeStart(LeftoverPreview.Exists(AppPaths.WebView2Folder), LeftoverPreview.OtherWindows()) is { } stuck)
        {
            ShowPreviewUnavailable(stuck);
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.WebView2Folder);
            var arguments = BrowserArguments.Get();
            var options = new CoreWebView2EnvironmentOptions(additionalBrowserArguments: arguments)
            {
                // WebView2 crash reports stay in its own folder instead of going to Microsoft.
                IsCustomCrashReportingEnabled = true,
            };
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: AppPaths.WebView2Folder, options: options);
            await PreviewBrowser.EnsureCoreWebView2Async(environment);
            _preview = PreviewHost.Attach(
                PreviewBrowser.CoreWebView2,
                Path.Combine(AppContext.BaseDirectory, "Assets"),
                OpenMarkdownFile);
            _preview.PrintInLightTheme = _settings.PrintInLightTheme;
            _preview.Ready += async (_, _) =>
            {
                _shownFilePath = null; // A freshly loaded page shows nothing yet.
                await RenderCurrentStateAsync();
            };
            _preview.RemoteContentBlocked += (_, _) => RemoteBar.Visibility = Visibility.Visible;
            _preview.OpenRequested += (_, _) => ShowOpenDialog();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowPreviewUnavailable(PreviewUnavailableNotice.RuntimeMissing);
            return;
        }
        catch (Exception ex) when (ex.HResult == PreviewUnavailableNotice.TimeoutHResult)
        {
            ShowPreviewUnavailable(PreviewUnavailableNotice.ForTimeout(LeftoverPreview.Exists(AppPaths.WebView2Folder), LeftoverPreview.OtherWindows(), ex.Message));
            return;
        }
        catch (Exception ex)
        {
            ShowPreviewUnavailable(PreviewUnavailableNotice.StartFailed(ex.Message));
            return;
        }

        PreviewStartingText.Visibility = Visibility.Collapsed;

        // The first render happens when the preview page reports it is ready.
        if (!string.IsNullOrWhiteSpace(_currentFilePath))
        {
            OpenMarkdownFile(_currentFilePath);
        }
    }

    private void ShowPreviewUnavailable(PreviewUnavailableNotice notice)
    {
        PreviewStartingText.Visibility = Visibility.Collapsed;
        PreviewBrowser.Visibility = Visibility.Collapsed;
        PreviewUnavailableTitle.Text = notice.Title;
        PreviewUnavailableMessage.Text = notice.Message;
        var download = notice.Action == PreviewNoticeAction.DownloadRuntime;
        DownloadWebView2Button.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
        PreviewUnavailableLink.Visibility = download ? Visibility.Visible : Visibility.Collapsed;
        PreviewUnavailableLink.Text = PreviewUnavailableNotice.RuntimeDownloadUrl;
        EndStuckPreviewButton.Visibility = notice.Action == PreviewNoticeAction.EndStuckPreview
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviewUnavailablePanel.Visibility = Visibility.Visible;
    }

    private async void EndStuckPreview_Click(object sender, RoutedEventArgs e)
    {
        EndStuckPreviewButton.IsEnabled = false;
        LeftoverPreview.End(AppPaths.WebView2Folder);

        // A WebView2 control whose start failed can't start again, so a new one takes its place.
        var grid = (System.Windows.Controls.Grid)PreviewBrowser.Parent;
        var index = grid.Children.IndexOf(PreviewBrowser);
        var fresh = new Microsoft.Web.WebView2.Wpf.WebView2();
        System.Windows.Controls.Grid.SetRow(fresh, System.Windows.Controls.Grid.GetRow(PreviewBrowser));
        grid.Children.RemoveAt(index);
        PreviewBrowser.Dispose();
        grid.Children.Insert(index, fresh);
        PreviewBrowser = fresh;
        ApplyThemeChrome();

        EndStuckPreviewButton.IsEnabled = true;
        await StartPreviewAsync();
    }

    private void DownloadWebView2_Click(object sender, RoutedEventArgs e)
    {
        ProcessLauncher.OpenWithDefaultApp(PreviewUnavailableNotice.RuntimeDownloadUrl);
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => ShowOpenDialog();

    private void ShowOpenDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*",
            Title = "Open Markdown File"
        };

        if (dialog.ShowDialog(this) == true)
        {
            OpenMarkdownFile(dialog.FileName);
        }
    }

    private async void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        await RenderCurrentStateAsync();
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _themeService.Toggle();
        _settings.Theme = _themeService.Theme.ToStorageValue();
        _settingsStore.Save(_settings);
        ApplyThemeChrome();
        _preview?.SetTheme(_themeService.Theme);
    }

    /// <summary>Toolbar toggle for the saved "Always allow" choice.</summary>
    private async void RemoteButton_Click(object sender, RoutedEventArgs e)
    {
        await SetAlwaysAllowRemoteAsync(!_remotePolicy.AlwaysAllow);
    }

    private async void LoadRemoteForFile_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        try
        {
            _remotePolicy.AllowForFile(_currentFilePath);
        }
        catch (ArgumentException)
        {
            return;
        }

        await RenderCurrentStateAsync();
    }

    private async void AlwaysAllowRemote_Click(object sender, RoutedEventArgs e)
    {
        await SetAlwaysAllowRemoteAsync(true);
    }

    private void DismissRemoteBar_Click(object sender, RoutedEventArgs e)
    {
        RemoteBar.Visibility = Visibility.Collapsed;
    }

    private async Task SetAlwaysAllowRemoteAsync(bool alwaysAllow)
    {
        _remotePolicy.SetAlwaysAllow(alwaysAllow);
        _settings.AllowRemoteContent = alwaysAllow;
        _settingsStore.Save(_settings);
        ApplyRemoteButton();

        if (alwaysAllow)
        {
            await RenderCurrentStateAsync();
        }
        else
        {
            // A fresh page, so images that already loaded aren't shown again from memory. The page
            // reports Ready, which renders the file.
            _preview?.Reload();
        }
    }

    private void ApplyRemoteButton()
    {
        RemoteButtonLabel.Text = _remotePolicy.AlwaysAllow ? "Remote on" : "Remote off";
        RemoteButton.ToolTip = _remotePolicy.AlwaysAllow
            ? "Images, video and audio from the internet load in every file. Click to block them."
            : "Images, video and audio from the internet are blocked unless you allow them for a file. Click to always allow them.";
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        PrintMenuItem.IsEnabled = FileShown;
        ExportMenuItem.IsEnabled = FileShown;
        CopyMarkdownMenuItem.IsEnabled = FileShown;
        PrintLightMenuItem.IsChecked = _settings.PrintInLightTheme;
        AutoUpdateMenuItem.IsChecked = _updates.AutomaticChecks;
        CheckNowMenuItem.IsEnabled = !_updates.IsBusy;
        var menu = MoreButton.ContextMenu;
        menu.PlacementTarget = MoreButton;
        // Below the button, right edges lined up, so the menu stays over the window.
        menu.Placement = PlacementMode.Custom;
        menu.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        [
            new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height), PopupPrimaryAxis.Horizontal),
        ];
        menu.IsOpen = true;
    }

    /// <summary>A file is shown (not the no-file page or a "File not found" message).</summary>
    private bool FileShown => _preview is not null && _shownFilePath is not null && File.Exists(_shownFilePath);

    private void PrintMenuItem_Click(object sender, RoutedEventArgs e) => Print();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Also over the preview, where WebView2 would otherwise show its own print preview (and, in
        // Debug builds, open DevTools for Ctrl+Shift+C).
        if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Print();
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            _ = CopyMarkdownAsync();
        }
    }

    private async void CopyMarkdownMenuItem_Click(object sender, RoutedEventArgs e) => await CopyMarkdownAsync();

    /// <summary>Copies the shown file's Markdown as it is on disk now, as plain text.</summary>
    private async Task CopyMarkdownAsync()
    {
        if (!FileShown)
        {
            return;
        }

        var shown = _shownFilePath!;
        try
        {
            Clipboard.SetText(await _fileReader.ReadAsync(shown));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            MessageBox.Show(this, $"MDGlint couldn't copy {Path.GetFileName(shown)}.", "Copy Markdown",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Print()
    {
        if (FileShown)
        {
            _preview!.ShowPrintDialog();
        }
    }

    private async void ExportPdfMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!FileShown)
        {
            return;
        }

        var shown = _shownFilePath!;
        var dialog = new SaveFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Export to PDF",
            FileName = ExportFiles.SuggestedName(shown, ".pdf"),
            InitialDirectory = ExportFiles.SuggestedFolder(shown),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        bool saved;
        try
        {
            saved = await _preview!.PrintToPdfAsync(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            saved = false;
        }

        if (!saved)
        {
            MessageBox.Show(this, $"MDGlint couldn't save {dialog.FileName}.", "Export to PDF",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PrintLightMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _settings.PrintInLightTheme = PrintLightMenuItem.IsChecked;
        _settingsStore.Save(_settings);
        if (_preview is not null)
        {
            _preview.PrintInLightTheme = _settings.PrintInLightTheme;
            _preview.SetTheme(_themeService.Theme);
        }
    }

    private void AutoUpdateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _ = _updates.SetAutomaticChecksAsync(AutoUpdateMenuItem.IsChecked);
    }

    private void CheckNowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _ = _updates.CheckAsync(userAsked: true);
    }

    private void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        _ = _updates.InstallOrCancelAsync();
    }

    private void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_updates.Update is { } update)
        {
            ProcessLauncher.OpenWithDefaultApp(update.ReleasePage.AbsoluteUri);
        }
    }

    private void UpdateDismiss_Click(object sender, RoutedEventArgs e)
    {
        _updates.Dismiss();
    }

    private void ShowUpdateState()
    {
        var state = _updates.State;
        UpdateBarText.Text = state.Text;
        UpdateInstallButton.Visibility = state.InstallLabel is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateInstallLabel.Text = state.InstallLabel ?? string.Empty;
        UpdateNotesButton.Visibility = state.ShowReleaseNotes ? Visibility.Visible : Visibility.Collapsed;
        UpdateBar.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = GetDraggedMarkdownFile(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var filePath = GetDraggedMarkdownFile(e);
        if (filePath is not null)
        {
            OpenMarkdownFile(filePath);
        }
    }

    private void OpenMarkdownFile(string filePath)
    {
        _currentFilePath = filePath;
        if (File.Exists(filePath))
        {
            _fileWatcher.Watch(filePath);
        }
        else
        {
            _fileWatcher.Stop();
        }
        _ = RenderCurrentStateAsync();
    }

    private async Task RenderCurrentStateAsync()
    {
        if (_preview is null)
        {
            return;
        }

        var filePath = _currentFilePath;
        var document = await _renderer.RenderFileAsync(filePath, _fileReader);
        if (!string.Equals(filePath, _currentFilePath, StringComparison.Ordinal))
        {
            return; // Another file was opened while this one was being read; its render follows.
        }

        var plan = RenderPlan.For(filePath, _shownFilePath, _remotePolicy);
        if (plan.HideRemoteBar)
        {
            RemoteBar.Visibility = Visibility.Collapsed;
        }

        _preview.AllowRemoteContent = plan.AllowRemoteContent;
        _preview.Show(document, _themeService.Theme, plan.IsNewDocument);
        _shownFilePath = filePath;
    }

    private void ApplyThemeChrome()
    {
        SetTitleBarTheme(_themeService.IsDark);
        // Matches the page background so the preview never flashes the wrong colour while loading.
        PreviewBrowser.DefaultBackgroundColor = _themeService.IsDark
            ? System.Drawing.Color.FromArgb(13, 17, 23)
            : System.Drawing.Color.White;

        if (_themeService.IsDark)
        {
            Background = Brushes.Black;
            Header.Background = Brush(22, 27, 34);
            Header.BorderBrush = Brush(48, 54, 61);
            Resources["ToolbarForegroundBrush"] = Brush(230, 237, 243);
            Resources["ToolbarMutedBrush"] = Brush(145, 152, 161);
            Resources["ToolbarHoverBrush"] = Brush(33, 38, 45);
            Resources["ToolbarPressedBrush"] = Brush(48, 54, 61);
            Resources["NoticeBackgroundBrush"] = Brush(39, 33, 21);
            Resources["NoticeBorderBrush"] = Brush(102, 78, 22);
            ThemeButtonLabel.Text = "Light";
            return;
        }

        Background = Brushes.White;
        Header.Background = Brush(246, 248, 250);
        Header.BorderBrush = Brush(208, 215, 222);
        Resources["ToolbarForegroundBrush"] = Brush(31, 35, 40);
        Resources["ToolbarMutedBrush"] = Brush(89, 99, 110);
        Resources["ToolbarHoverBrush"] = Brush(234, 238, 242);
        Resources["ToolbarPressedBrush"] = Brush(208, 215, 222);
        Resources["NoticeBackgroundBrush"] = Brush(255, 248, 197);
        Resources["NoticeBorderBrush"] = Brush(227, 194, 107);
        ThemeButtonLabel.Text = "Dark";
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private void SetTitleBarTheme(bool useDarkMode)
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        var value = useDarkMode ? 1 : 0;
        DwmSetWindowAttribute(windowHandle, DwmaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int valueSize);

    private static string? GetDraggedMarkdownFile(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        return PreviewPolicy.DroppedMarkdownFile(e.Data.GetData(DataFormats.FileDrop) as string[]);
    }
}

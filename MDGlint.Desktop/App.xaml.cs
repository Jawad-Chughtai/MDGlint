using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Threading;
using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop;

[ExcludeFromCodeCoverage(Justification = "WPF startup; the end-to-end tests launch the app with a file. The error "
    + "report's logic is in ErrorLog (unit tests).")]
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        // Every start takes the note an update leaves; only a start with no file opens it.
        var reopen = ReopenNote.Take(AppPaths.ReopenNoteFile, DateTime.UtcNow);
        var filePath = e.Args.Length > 0 ? e.Args[0] : reopen;
        var window = new MainWindow(filePath);
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var path = AppPaths.ErrorLogFile;
        var saved = ErrorLog.TryWrite(path, e.Exception);
        MessageBox.Show(
            ErrorLog.DialogText(path, saved, e.Exception),
            "MDGlint startup error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}

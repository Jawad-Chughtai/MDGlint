using System.IO;

namespace MDGlint.Desktop.Services;

/// <summary>
/// Watches the open file. Changes, renames and deletes restart a short timer; when it fires,
/// <c>changed</c> runs on the thread that created the watcher (the window's), so a burst of
/// writes from one save renders once.
/// </summary>
public sealed class FileWatcherService : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(250);

    private readonly Func<Task> _changed;
    private readonly TimeSpan _delay;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public FileWatcherService(Func<Task> changed)
        : this(changed, DefaultDelay)
    {
    }

    internal FileWatcherService(Func<Task> changed, TimeSpan delay)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _delay = delay;
        _timer = new Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The file being watched, or null.</summary>
    public string? WatchedPath { get; private set; }

    public void Watch(string filePath)
    {
        Stop();

        var directory = Path.GetDirectoryName(filePath);
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.EnableRaisingEvents = true;
        WatchedPath = filePath;
    }

    public void Stop()
    {
        lock (_timer)
        {
            if (!_disposed)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        WatchedPath = null;
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose()
    {
        Stop();
        lock (_timer)
        {
            _disposed = true;
            _timer.Dispose();
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Restart();

    /// <summary>(Re)starts the timer. A file event can still arrive while the window closes.</summary>
    internal void Restart()
    {
        lock (_timer)
        {
            if (!_disposed)
            {
                _timer.Change(_delay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void OnTimer()
    {
        if (_context is null)
        {
            _ = _changed();
        }
        else
        {
            _context.Post(_ => _ = _changed(), null);
        }
    }
}

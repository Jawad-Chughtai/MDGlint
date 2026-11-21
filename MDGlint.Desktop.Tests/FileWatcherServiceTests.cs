using MDGlint.Desktop.Services;

namespace MDGlint.Desktop.Tests;

public sealed class FileWatcherServiceTests : IDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MDGlintTests", Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private int _changes;

    public FileWatcherServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _file = Path.Combine(_folder, "notes.md");
        File.WriteAllText(_file, "# One");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Requires_ACallback()
    {
        Assert.Throws<ArgumentNullException>(() => new FileWatcherService(null!));
    }

    [Fact]
    public void Watch_ReportsASaveOnce()
    {
        using var watcher = Watcher();
        watcher.Watch(_file);
        Assert.Equal(_file, watcher.WatchedPath);

        // Several writes in a burst, as editors save.
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(_file, $"# Save {i}");
        }

        Assert.True(SpinWait.SpinUntil(() => _changes > 0, Patience));
        Thread.Sleep(Delay * 6);
        Assert.Equal(1, _changes);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("delete")]
    public void Watch_ReportsRenamesAndDeletes(string change)
    {
        using var watcher = Watcher();
        watcher.Watch(_file);

        if (change == "rename")
        {
            File.Move(_file, _file + ".bak");
        }
        else
        {
            File.Delete(_file);
        }

        Assert.True(SpinWait.SpinUntil(() => _changes > 0, Patience));
    }

    [Fact]
    public void Watch_ReportsTheFileComingBack()
    {
        File.Delete(_file);
        using var watcher = Watcher();
        watcher.Watch(_file);

        File.WriteAllText(_file, "# Back");

        Assert.True(SpinWait.SpinUntil(() => _changes > 0, Patience));
    }

    [Fact]
    public void Stop_EndsReports()
    {
        using var watcher = Watcher();
        watcher.Watch(_file);
        watcher.Stop();
        watcher.Stop(); // Twice is fine.

        File.WriteAllText(_file, "# Ignored");
        Thread.Sleep(Delay * 6);

        Assert.Equal(0, _changes);
        Assert.Null(watcher.WatchedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(@"C:\")] // A folder, not a file.
    [InlineData(@"Z:\does\not\exist\notes.md")]
    public void Watch_IgnoresPathsItCannotWatch(string path)
    {
        using var watcher = Watcher();

        watcher.Watch(path);

        Assert.Null(watcher.WatchedPath);
    }

    [Fact]
    public void Restart_AfterDisposeIsIgnored()
    {
        // A file event can still arrive while the window closes.
        var watcher = Watcher();
        watcher.Dispose();
        watcher.Dispose();

        watcher.Restart();
        watcher.Stop();

        Assert.Equal(0, _changes);
    }

    [Fact]
    public void RunsTheCallbackOnTheCreatingThreadsContext()
    {
        var context = new CountingContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        FileWatcherService watcher;
        try
        {
            watcher = Watcher();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (watcher)
        {
            watcher.Restart();
            Assert.True(SpinWait.SpinUntil(() => _changes > 0, Patience));
        }

        Assert.Equal(1, context.Posts);
    }

    [Fact]
    public async Task RunsTheCallbackDirectlyWithoutAContext()
    {
        // Task.Run has no SynchronizationContext, unlike xUnit's test thread.
        using var watcher = await Task.Run(Watcher);

        watcher.Restart();

        Assert.True(SpinWait.SpinUntil(() => _changes > 0, Patience));
    }

    [Fact]
    public void UsesAQuarterSecondByDefault()
    {
        using var watcher = new FileWatcherService(() => Task.CompletedTask);

        Assert.Equal(TimeSpan.FromMilliseconds(250), FileWatcherService.DefaultDelay);
    }

    private FileWatcherService Watcher() => new(() =>
    {
        Interlocked.Increment(ref _changes);
        return Task.CompletedTask;
    }, Delay);

    private sealed class CountingContext : SynchronizationContext
    {
        public int Posts { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            Posts++;
            d(state);
        }
    }
}

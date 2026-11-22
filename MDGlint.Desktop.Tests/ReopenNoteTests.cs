using MDGlint.Core.Models;
using MDGlint.Desktop.Helpers;

namespace MDGlint.Desktop.Tests;

public sealed class ReopenNoteTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "MDGlintTests", Guid.NewGuid().ToString("N"));
    private readonly string _note;
    private readonly string _file;

    public ReopenNoteTests()
    {
        Directory.CreateDirectory(_folder);
        _note = Path.Combine(_folder, "Local", "reopen.txt");
        _file = Path.Combine(_folder, "notes.md");
        File.WriteAllText(_file, "# Notes");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Take_OpensTheNotedFileOnceWhileTheNoteIsFresh()
    {
        ReopenNote.Write(_note, _file);

        Assert.Equal(_file, ReopenNote.Take(_note, DateTime.UtcNow));
        Assert.False(File.Exists(_note));
        Assert.Null(ReopenNote.Take(_note, DateTime.UtcNow));
    }

    [Fact]
    public void Take_IgnoresAnOldNoteAndRemovesIt()
    {
        ReopenNote.Write(_note, _file);

        Assert.Null(ReopenNote.Take(_note, DateTime.UtcNow + ReopenNote.MaxAge + TimeSpan.FromMinutes(1)));
        Assert.False(File.Exists(_note));

        // Nor one dated well after now: the clock changed.
        ReopenNote.Write(_note, _file);
        Assert.Null(ReopenNote.Take(_note, DateTime.UtcNow - ReopenNote.MaxAge - TimeSpan.FromMinutes(1)));
        Assert.False(File.Exists(_note));
    }

    [Theory]
    [InlineData("gone.md")]
    [InlineData("")]
    [InlineData("relative.md")]
    public void Take_IgnoresANoteThatNamesNoFileThatIsThere(string name)
    {
        File.WriteAllText(Path.Combine(_folder, "relative.md"), "x");
        Directory.CreateDirectory(Path.GetDirectoryName(_note)!);
        File.WriteAllText(_note, name == "gone.md" ? Path.Combine(_folder, name) : name);

        Assert.Null(ReopenNote.Take(_note, DateTime.UtcNow));
        Assert.False(File.Exists(_note));
    }

    [Fact]
    public void Take_IgnoresANoteItCannotRead()
    {
        ReopenNote.Write(_note, _file);
        using (new FileStream(_note, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(ReopenNote.Take(_note, DateTime.UtcNow));
        }

        // Left behind while it was locked; the next start doesn't open it either once it's old.
        Assert.True(File.Exists(_note));
    }

    [Fact]
    public void Write_NotesNothingWithNoFileOpen()
    {
        ReopenNote.Write(_note, _file);
        ReopenNote.Write(_note, null);

        Assert.False(File.Exists(_note));
    }

    [Fact]
    public void Write_GoesAheadWhenTheNoteCannotBeWritten()
    {
        // A file where the note's folder should be.
        var blocked = Path.Combine(_folder, "blocked");
        File.WriteAllText(blocked, "x");

        ReopenNote.Write(Path.Combine(blocked, "reopen.txt"), _file);

        Assert.True(File.Exists(blocked));
    }

    [Fact]
    public void Around_NotesTheOpenFileBeforeSetupStarts()
    {
        bool? notedFirst = null;
        var start = ReopenNote.Around(_ => { notedFirst = File.Exists(_note); return null; }, _note, () => _file);

        using var installer = Installer();
        Assert.Null(start(installer));

        Assert.True(notedFirst);
        Assert.Equal(_file, File.ReadAllText(_note));
    }

    [Fact]
    public void Around_RemovesTheNoteWhenSetupDidNotStart()
    {
        var start = ReopenNote.Around(_ => "declined", _note, () => _file);

        using var installer = Installer();
        Assert.Equal("declined", start(installer));

        Assert.False(File.Exists(_note));
    }

    [Fact]
    public void Around_KeepsAnUnremovableNoteFromStoppingTheUpdate()
    {
        FileStream? held = null;
        var start = ReopenNote.Around(_ =>
        {
            held = new FileStream(_note, FileMode.Open, FileAccess.Read, FileShare.None);
            return "declined";
        }, _note, () => _file);

        using var installer = Installer();
        try
        {
            Assert.Equal("declined", start(installer));
        }
        finally
        {
            held?.Dispose();
        }
    }

    [Fact]
    public void Around_NeedsBothFunctions()
    {
        Assert.Throws<ArgumentNullException>(() => ReopenNote.Around(null!, _note, () => _file));
        Assert.Throws<ArgumentNullException>(() => ReopenNote.Around(_ => null, _note, null!));
    }

    private VerifiedInstaller Installer()
    {
        SemanticVersion.TryParse("1.2.0", out var version);
        return new VerifiedInstaller(Path.Combine(_folder, "setup.exe"), version, new MemoryStream());
    }
}

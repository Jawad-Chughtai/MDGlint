using System.IO;
using MDGlint.Core.Models;

namespace MDGlint.Desktop.Helpers;

/// <summary>
/// The file to open again after an update. MDGlint notes the open file just before it starts
/// setup; setup opens MDGlint with no file, and that start takes the note.
/// </summary>
public static class ReopenNote
{
    /// <summary>Older than this, the note is left from an update that didn't finish, and is ignored.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Wraps <paramref name="startInstaller"/>: notes the open file first, and removes the note
    /// again if setup didn't start.
    /// </summary>
    public static Func<VerifiedInstaller, string?> Around(
        Func<VerifiedInstaller, string?> startInstaller, string notePath, Func<string?> openFile)
    {
        ArgumentNullException.ThrowIfNull(startInstaller);
        ArgumentNullException.ThrowIfNull(openFile);
        return installer =>
        {
            Write(notePath, openFile());
            var error = startInstaller(installer);
            if (error is not null)
            {
                Delete(notePath);
            }

            return error;
        };
    }

    /// <summary>Notes <paramref name="file"/>; with no file open there is nothing to note.</summary>
    public static void Write(string notePath, string? file)
    {
        Delete(notePath);
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
            File.WriteAllText(notePath, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The update still goes ahead; MDGlint just opens without the file.
        }
    }

    /// <summary>
    /// Removes the note, and returns the file it names when it was written in the last few
    /// minutes and the file is still there; otherwise null.
    /// </summary>
    public static string? Take(string notePath, DateTime utcNow)
    {
        string text;
        DateTime written;
        try
        {
            if (!File.Exists(notePath))
            {
                return null;
            }

            written = File.GetLastWriteTimeUtc(notePath);
            text = File.ReadAllText(notePath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Delete(notePath);
            return null;
        }

        Delete(notePath);
        var age = utcNow - written;
        return age >= -MaxAge && age <= MaxAge && Path.IsPathFullyQualified(text) && File.Exists(text)
            ? text
            : null;
    }

    private static void Delete(string notePath)
    {
        try
        {
            File.Delete(notePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind: Take ignores it once it's a few minutes old.
        }
    }
}

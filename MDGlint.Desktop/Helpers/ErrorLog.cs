using System.IO;

namespace MDGlint.Desktop.Helpers;

/// <summary>Saves the details of an unhandled error, so they survive the dialog that shows them.</summary>
public static class ErrorLog
{
    /// <summary>Writes the exception to <paramref name="path"/>; returns false if that wasn't possible.</summary>
    public static bool TryWrite(string path, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, exception.ToString());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public static string DialogText(string path, bool saved, Exception exception) =>
        saved
            ? $"MDGlint could not start.\n\nDetails were saved to:\n{path}\n\n{exception}"
            : $"MDGlint could not start.\n\n{exception}";
}

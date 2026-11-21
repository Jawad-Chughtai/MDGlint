using System.IO;
using MDGlint.Core.Contracts;

namespace MDGlint.Desktop.Services;

public sealed class FileMarkdownReader : IMarkdownFileReader
{
    public Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return File.ReadAllTextAsync(filePath, cancellationToken);
    }
}

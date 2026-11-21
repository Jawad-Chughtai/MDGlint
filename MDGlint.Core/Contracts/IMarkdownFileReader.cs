namespace MDGlint.Core.Contracts;

public interface IMarkdownFileReader
{
    Task<string> ReadAsync(string filePath, CancellationToken cancellationToken = default);
}

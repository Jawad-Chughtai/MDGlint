using System.Net;
using Markdig;
using MDGlint.Core.Contracts;
using MDGlint.Core.Models;

namespace MDGlint.Core.Services;

/// <summary>
/// Converts Markdown to an HTML fragment for the preview page. The page itself (styles, scripts,
/// Content Security Policy) is a fixed shell owned by the desktop app; this class only produces
/// the content that goes inside it, with raw HTML from the file sanitized (see
/// <see cref="HtmlContentSanitizer"/>).
/// </summary>
public sealed class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseFootnotes()
        .Build();

    private readonly HtmlContentSanitizer _sanitizer = new();

    public async Task<RenderedDocument> RenderFileAsync(
        string? filePath,
        IMarkdownFileReader fileReader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileReader);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Message("Open a Markdown file to preview it.");
        }

        try
        {
            var markdown = await fileReader.ReadAsync(filePath, cancellationToken);
            return new RenderedDocument(Render(markdown), Path.GetDirectoryName(Path.GetFullPath(filePath)));
        }
        catch (FileNotFoundException)
        {
            return Message("File not found.");
        }
        catch (DirectoryNotFoundException)
        {
            return Message("File not found.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Message("Invalid path.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Message("Unable to render this Markdown file.");
        }
    }

    public string Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        return _sanitizer.Sanitize(Markdown.ToHtml(markdown, _pipeline));
    }

    private static RenderedDocument Message(string message)
    {
        return new RenderedDocument($"<p class=\"message\">{WebUtility.HtmlEncode(message)}</p>", null);
    }
}

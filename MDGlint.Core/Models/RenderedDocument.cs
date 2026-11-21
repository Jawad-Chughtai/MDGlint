namespace MDGlint.Core.Models;

/// <summary>
/// The result of rendering a file: an HTML fragment for the preview page, and the folder that
/// relative links and images resolve against (null for message pages).
/// </summary>
public sealed record RenderedDocument(string Html, string? BaseDirectory);

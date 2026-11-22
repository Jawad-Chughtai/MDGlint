namespace MDGlint.Core.Models;

/// <summary>An image read or downloaded for an export: its content type and its bytes.</summary>
public sealed record ExportImage(string ContentType, byte[] Bytes);

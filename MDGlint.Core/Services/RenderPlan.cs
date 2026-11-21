namespace MDGlint.Core.Services;

/// <summary>How the window shows a render of <c>filePath</c> after <c>shownFilePath</c>.</summary>
/// <param name="IsNewDocument">A different file: start at the top. The same file saved again keeps
/// the reader's place.</param>
/// <param name="AllowRemoteContent">Whether this render may load remote images, video and audio.</param>
/// <param name="HideRemoteBar">The remote-content bar stays up across saves of the same file while it
/// is still blocked; a different file, or allowing remote content, clears it.</param>
public sealed record RenderPlan(bool IsNewDocument, bool AllowRemoteContent, bool HideRemoteBar)
{
    public static RenderPlan For(string? filePath, string? shownFilePath, RemoteContentPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var isNewDocument = !string.Equals(filePath, shownFilePath, StringComparison.OrdinalIgnoreCase);
        var allowRemote = policy.IsAllowed(filePath);
        return new RenderPlan(isNewDocument, allowRemote, isNewDocument || allowRemote);
    }
}

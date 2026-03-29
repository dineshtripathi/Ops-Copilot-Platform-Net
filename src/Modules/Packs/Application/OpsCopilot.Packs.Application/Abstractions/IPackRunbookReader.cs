namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Reads runbook markdown files from across all loaded packs.
/// Searches every pack for a <c>runbooks/{runbookName}</c> file and returns
/// the first match.  Returns <c>null</c> when no pack contains that runbook.
/// </summary>
public interface IPackRunbookReader
{
    /// <param name="runbookName">
    /// Filename only, e.g. <c>exception-diagnosis.md</c>.
    /// Must match <c>^[a-z][a-z0-9-]*\.md$</c> — no path separators, no traversal.
    /// </param>
    Task<string?> ReadAsync(string runbookName, CancellationToken ct = default);
}

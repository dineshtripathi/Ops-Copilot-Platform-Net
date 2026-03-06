namespace OpsCopilot.Packs.Application.Abstractions;

/// <summary>
/// Reads individual files from a pack directory with path-traversal protection.
/// </summary>
public interface IPackFileReader
{
    /// <summary>
    /// Reads the text content of a file relative to the given pack path.
    /// Returns <c>null</c> if the file does not exist or the path is invalid.
    /// </summary>
    /// <param name="packPath">Absolute directory path of the pack.</param>
    /// <param name="relativeFilePath">Relative path within the pack directory.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string?> ReadFileAsync(string packPath, string relativeFilePath, CancellationToken ct = default);
}

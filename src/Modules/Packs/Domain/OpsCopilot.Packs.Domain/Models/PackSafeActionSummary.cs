namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Read-only summary of a single safe action within a pack.
/// </summary>
public sealed record PackSafeActionSummary(string Id, string RequiresMode, string? DefinitionFile);

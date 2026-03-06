namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Read-only summary of a single evidence collector within a pack.
/// </summary>
public sealed record PackEvidenceCollectorSummary(string Id, string RequiredMode, string? QueryFile);

namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Read-only summary of a single runbook within a pack.
/// </summary>
public sealed record PackRunbookSummary(string Id, string File);

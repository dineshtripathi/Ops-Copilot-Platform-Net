namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// A runbook declared by a pack, referencing a file relative to the pack directory.
/// </summary>
public sealed record PackRunbook(
    string Id,
    string File);

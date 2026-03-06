namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// A safe action declared by a pack. Safe actions always require mode "C".
/// The optional definition file is relative to the pack directory.
/// </summary>
public sealed record PackSafeAction(
    string Id,
    string RequiresMode,
    string? DefinitionFile);

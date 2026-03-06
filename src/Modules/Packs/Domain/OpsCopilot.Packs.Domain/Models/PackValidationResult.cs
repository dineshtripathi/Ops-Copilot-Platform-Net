namespace OpsCopilot.Packs.Domain.Models;

/// <summary>
/// Result of validating a pack manifest against the PACKS.md rules.
/// </summary>
public sealed record PackValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors);

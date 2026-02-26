namespace OpsCopilot.SafeActions.Application.Abstractions;

/// <summary>
/// Config-driven catalog of recognised action types.
/// <para>
/// <strong>Backward-compatible:</strong> when the <c>SafeActions:ActionTypes</c>
/// configuration section is missing or empty the catalog operates in
/// <em>allow-all</em> mode — every action type is considered allowlisted
/// and <see cref="Get"/> returns <c>null</c>.
/// </para>
/// </summary>
public interface IActionTypeCatalog
{
    /// <summary>
    /// Returns <c>true</c> when the action type may be proposed.
    /// In allow-all mode (no config) this always returns <c>true</c>.
    /// When the catalog is populated, the type must be present <em>and</em> <see cref="ActionTypeDefinition.Enabled"/>.
    /// </summary>
    bool IsAllowlisted(string actionType);

    /// <summary>
    /// Returns the full definition for the given action type, or <c>null</c>
    /// when the type is unknown (or the catalog is in allow-all mode).
    /// </summary>
    ActionTypeDefinition? Get(string actionType);

    /// <summary>
    /// Returns every definition currently loaded from configuration.
    /// Empty when the catalog is in allow-all mode.
    /// </summary>
    IReadOnlyList<ActionTypeDefinition> List();
}

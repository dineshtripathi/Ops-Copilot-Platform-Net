using Microsoft.Extensions.Configuration;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Policies;

/// <summary>
/// Config-driven action-type catalog.
/// Reads <c>SafeActions:ActionTypes</c> — an array of objects with
/// <c>ActionType</c>, <c>RiskTier</c>, and <c>Enabled</c> properties.
/// <para>
/// <strong>Backward-compatible / allow-all default:</strong>
/// When the configuration section is missing or empty, every action type
/// is considered allowlisted and <see cref="Get"/> returns <c>null</c>.
/// When the section is populated, only entries that are present
/// <em>and</em> have <c>Enabled = true</c> pass the allowlist check.
/// </para>
/// </summary>
public sealed class ConfigActionTypeCatalog : IActionTypeCatalog
{
    private const string ReasonCode = "action_type_not_allowed";

    private readonly Dictionary<string, ActionTypeDefinition> _definitions;

    public ConfigActionTypeCatalog(IConfiguration configuration)
    {
        _definitions = new Dictionary<string, ActionTypeDefinition>(StringComparer.OrdinalIgnoreCase);

        var section = configuration.GetSection("SafeActions:ActionTypes");
        if (!section.Exists())
            return;

        foreach (var child in section.GetChildren())
        {
            var actionType = child["ActionType"];
            if (string.IsNullOrWhiteSpace(actionType))
                continue;

            var riskTierText = child["RiskTier"] ?? "Low";
            if (!Enum.TryParse<ActionRiskTier>(riskTierText, ignoreCase: true, out var riskTier))
                riskTier = ActionRiskTier.Low;

            var enabledText = child["Enabled"];
            var enabled = enabledText is null || bool.TryParse(enabledText, out var e) && e;

            _definitions[actionType] = new ActionTypeDefinition(actionType, riskTier, enabled);
        }
    }

    /// <inheritdoc />
    public bool IsAllowlisted(string actionType)
    {
        // Allow-all mode when catalog is not populated.
        if (_definitions.Count == 0)
            return true;

        return _definitions.TryGetValue(actionType, out var def) && def.Enabled;
    }

    /// <inheritdoc />
    public ActionTypeDefinition? Get(string actionType)
    {
        _definitions.TryGetValue(actionType, out var def);
        return def;
    }

    /// <inheritdoc />
    public IReadOnlyList<ActionTypeDefinition> List() =>
        _definitions.Values.ToList().AsReadOnly();

    // ── Diagnostic helpers (used for startup logging) ──

    /// <summary>
    /// Returns the number of action type definitions loaded from configuration.
    /// </summary>
    internal int DefinitionCount => _definitions.Count;

    /// <summary>
    /// Returns the number of definitions that are enabled.
    /// </summary>
    internal int EnabledCount => _definitions.Values.Count(d => d.Enabled);
}

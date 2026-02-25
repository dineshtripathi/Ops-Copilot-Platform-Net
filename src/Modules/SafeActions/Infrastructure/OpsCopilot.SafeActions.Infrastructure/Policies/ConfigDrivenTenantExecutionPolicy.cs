using Microsoft.Extensions.Configuration;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Policies;

/// <summary>
/// Config-driven tenant execution policy.
/// Reads <c>SafeActions:AllowedExecutionTenants</c> — a dictionary mapping
/// action types to arrays of authorized tenant IDs.
/// <para>
/// <strong>Strict / secure-by-default:</strong>
/// <list type="bullet">
///   <item>Action type key missing → <see cref="PolicyDecision.Deny"/></item>
///   <item>Empty tenant array      → <see cref="PolicyDecision.Deny"/></item>
///   <item>Tenant not in list      → <see cref="PolicyDecision.Deny"/></item>
///   <item>Tenant found in list    → <see cref="PolicyDecision.Allow"/></item>
/// </list>
/// </para>
/// </summary>
public sealed class ConfigDrivenTenantExecutionPolicy : ITenantExecutionPolicy
{
    private const string ReasonCode = "tenant_not_authorized_for_action";

    private readonly Dictionary<string, HashSet<string>> _allowedTenants;

    public ConfigDrivenTenantExecutionPolicy(IConfiguration configuration)
    {
        _allowedTenants = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var section = configuration.GetSection("SafeActions:AllowedExecutionTenants");
        if (!section.Exists())
            return;

        foreach (var actionSection in section.GetChildren())
        {
            var actionType = actionSection.Key;
            var tenantIds = actionSection.GetChildren()
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _allowedTenants[actionType] = tenantIds;
        }
    }

    /// <inheritdoc />
    public PolicyDecision EvaluateExecution(string tenantId, string actionType)
    {
        // Key missing → DENY (no action types configured at all, or this one isn't listed)
        if (!_allowedTenants.TryGetValue(actionType, out var authorizedTenants))
            return PolicyDecision.Deny(ReasonCode,
                $"Action type '{actionType}' has no tenant execution allowlist configured.");

        // Empty list → DENY
        if (authorizedTenants.Count == 0)
            return PolicyDecision.Deny(ReasonCode,
                $"Action type '{actionType}' has an empty tenant execution allowlist.");

        // Tenant not in list → DENY
        if (!authorizedTenants.Contains(tenantId))
            return PolicyDecision.Deny(ReasonCode,
                $"Tenant '{tenantId}' is not authorized to execute action type '{actionType}'.");

        return PolicyDecision.Allow();
    }

    /// <summary>
    /// Returns the number of action types configured (used for startup diagnostics).
    /// </summary>
    internal int ConfiguredActionTypeCount => _allowedTenants.Count;

    /// <summary>
    /// Returns total number of tenant entries across all action types (used for startup diagnostics).
    /// </summary>
    internal int TotalTenantEntryCount => _allowedTenants.Values.Sum(s => s.Count);
}

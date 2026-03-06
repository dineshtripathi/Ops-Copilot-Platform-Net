namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Request to execute pack evidence collectors at the given deployment mode.
/// </summary>
/// <param name="DeploymentMode">Current deployment mode (A/B/C).</param>
/// <param name="TenantId">
/// Tenant identifier used by <c>ITenantWorkspaceResolver</c> to look up the
/// Log Analytics workspace.  Required for Mode-B+ execution; when absent or
/// empty the resolver will return <c>missing_workspace</c>.
/// </param>
/// <param name="CorrelationId">
/// Optional caller-supplied correlation identifier propagated through structured
/// logs and telemetry counters to enable end-to-end request tracing.
/// </param>
public sealed record PackEvidenceExecutionRequest(string DeploymentMode, string? TenantId = null, string? CorrelationId = null);

/// <summary>
/// A single evidence item returned from executing a pack evidence collector.
/// </summary>
public sealed record PackEvidenceItem(
    string PackName,
    string CollectorId,
    string ConnectorName,
    string? QueryFile,
    string? QueryContent,
    string? ResultJson,
    int RowCount,
    string? ErrorMessage);

/// <summary>
/// Aggregated result of executing eligible pack evidence collectors.
/// </summary>
public sealed record PackEvidenceExecutionResult(
    IReadOnlyList<PackEvidenceItem> EvidenceItems,
    IReadOnlyList<string> Errors);

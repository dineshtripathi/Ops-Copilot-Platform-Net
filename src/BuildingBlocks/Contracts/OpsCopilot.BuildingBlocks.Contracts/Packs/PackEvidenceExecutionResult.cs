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
/// <param name="FromUtc">
/// Optional start of the query time window (UTC). When provided, the executor substitutes
/// the <c>{FROM_UTC}</c> token in KQL files with a <c>datetime()</c> literal so queries
/// honour the operator-selected date range. Falls back to <c>ago(30d)</c> when absent.
/// </param>
/// <param name="ToUtc">
/// Optional end of the query time window (UTC). Substituted as <c>{TO_UTC}</c> in KQL.
/// Falls back to <c>now()</c> when absent.
/// </param>
public sealed record PackEvidenceExecutionRequest(
    string DeploymentMode,
    string? TenantId = null,
    string? CorrelationId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null);

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

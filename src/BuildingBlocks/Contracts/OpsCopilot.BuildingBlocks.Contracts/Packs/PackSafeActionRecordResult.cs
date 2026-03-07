namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Input for <see cref="IPackSafeActionRecorder.RecordAsync"/>.
/// </summary>
/// <param name="DeploymentMode">Current deployment mode (A/B/C).</param>
/// <param name="TenantId">Tenant identifier for scoping and SafeAction ownership.</param>
/// <param name="TriageRunId">The triage run identifier that sourced these proposals.</param>
/// <param name="Proposals">Pack safe-action proposals to record (from the proposer stage).</param>
public sealed record PackSafeActionRecordRequest(
    string DeploymentMode,
    string TenantId,
    Guid TriageRunId,
    IReadOnlyList<PackSafeActionProposalItem> Proposals);

/// <summary>
/// Per-proposal outcome of <see cref="IPackSafeActionRecorder.RecordAsync"/>.
/// </summary>
/// <param name="PackName">The pack that owns this action.</param>
/// <param name="ActionId">Unique action identifier within the pack.</param>
/// <param name="ActionType">The type of action (e.g. run-command).</param>
/// <param name="ActionRecordId">The persisted SafeAction record identifier, or null on failure/skip.</param>
/// <param name="Status">Outcome status: Created, Skipped, PolicyDenied, or Failed.</param>
/// <param name="ErrorMessage">Non-null when the record could not be created.</param>
/// <param name="PolicyDenialReasonCode">Non-null when the proposal was denied by SafeAction policy.</param>
public sealed record PackSafeActionRecordItem(
    string PackName,
    string ActionId,
    string ActionType,
    Guid? ActionRecordId,
    string Status,
    string? ErrorMessage,
    string? PolicyDenialReasonCode);

/// <summary>
/// Result of <see cref="IPackSafeActionRecorder.RecordAsync"/>.
/// </summary>
/// <param name="Records">Per-proposal outcomes.</param>
/// <param name="CreatedCount">Number of proposals that were successfully recorded.</param>
/// <param name="SkippedCount">Number of proposals that were skipped (not executable / governance denied).</param>
/// <param name="FailedCount">Number of proposals that failed (policy denied or unexpected error).</param>
/// <param name="Errors">Aggregate error messages encountered during recording.</param>
public sealed record PackSafeActionRecordResult(
    IReadOnlyList<PackSafeActionRecordItem> Records,
    int CreatedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

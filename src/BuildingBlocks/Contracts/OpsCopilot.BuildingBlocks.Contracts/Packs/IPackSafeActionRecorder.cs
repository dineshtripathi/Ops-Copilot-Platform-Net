namespace OpsCopilot.BuildingBlocks.Contracts.Packs;

/// <summary>
/// Creates real SafeAction proposal records from pack safe-action proposals (Mode C only).
/// Each eligible proposal is forwarded to <c>SafeActionOrchestrator.ProposeAsync</c> to
/// create a persisted <c>ActionRecord</c> with <c>Status = Proposed</c>.
/// </summary>
/// <remarks>
/// Double-gated: recording requires
/// <list type="number">
///   <item><c>deploymentMode == C</c></item>
///   <item><c>Packs:SafeActionsEnabled == true</c> in configuration</item>
/// </list>
/// All proposals are recommend-only; no auto-approve or auto-execute occurs.
/// </remarks>
public interface IPackSafeActionRecorder
{
    /// <summary>
    /// Records eligible pack safe-action proposals as real SafeAction proposal records.
    /// Double-gated: returns empty when mode is not C <em>or</em> safe-actions are disabled.
    /// </summary>
    Task<PackSafeActionRecordResult> RecordAsync(
        PackSafeActionRecordRequest request,
        CancellationToken ct = default);
}

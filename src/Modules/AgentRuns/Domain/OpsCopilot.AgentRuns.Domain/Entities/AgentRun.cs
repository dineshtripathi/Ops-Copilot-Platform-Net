using OpsCopilot.AgentRuns.Domain.Enums;
using OpsCopilot.AgentRuns.Domain.Models;

namespace OpsCopilot.AgentRuns.Domain.Entities;

/// <summary>
/// Aggregate root for a single agent triage run.
/// Append-only rule: Status, CompletedAtUtc, SummaryJson, CitationsJson are the
/// only mutable fields. All other fields are set at creation and never changed.
/// </summary>
public sealed class AgentRun
{
    // EF Core constructor
    private AgentRun() { }

    public static AgentRun Create(
        string tenantId,
        string alertFingerprint,
        Guid? sessionId = null,
        RunContext? context = null)
        => new()
        {
            RunId            = Guid.NewGuid(),
            TenantId         = tenantId,
            AlertFingerprint = alertFingerprint,
            Status           = AgentRunStatus.Pending,
            CreatedAtUtc     = DateTimeOffset.UtcNow,
            SessionId        = sessionId,
            AlertProvider    = context?.AlertProvider,
            AlertSourceType  = context?.AlertSourceType,
            IsExceptionSignal = context?.IsExceptionSignal ?? false,
            AzureSubscriptionId = context?.AzureSubscriptionId,
            AzureResourceGroup  = context?.AzureResourceGroup,
            AzureResourceId     = context?.AzureResourceId,
            AzureApplication    = context?.AzureApplication,
            AzureWorkspaceId    = context?.AzureWorkspaceId,
        };

    public Guid            RunId            { get; private set; }
    public string          TenantId         { get; private set; } = string.Empty;
    public AgentRunStatus  Status           { get; private set; }
    public DateTimeOffset  CreatedAtUtc     { get; private set; }
    public DateTimeOffset? CompletedAtUtc   { get; private set; }
    public string?         AlertFingerprint { get; private set; }
    public string?         SummaryJson      { get; private set; }
    public string?         CitationsJson    { get; private set; }
    public Guid?            SessionId        { get; private set; }
    public string?          AlertProvider    { get; private set; }
    public string?          AlertSourceType  { get; private set; }
    public bool             IsExceptionSignal { get; private set; }
    public string?          AzureSubscriptionId { get; private set; }
    public string?          AzureResourceGroup  { get; private set; }
    public string?          AzureResourceId     { get; private set; }
    public string?          AzureApplication    { get; private set; }
    public string?          AzureWorkspaceId    { get; private set; }

    // Populated by UpdateTokenUsageAsync (set post-completion)
    public string?          RunType      { get; private set; }
    public int?             InputTokens  { get; private set; }
    public int?             OutputTokens { get; private set; }
    public int?             TotalTokens  { get; private set; }

    // Populated by SetLedgerMetadata (Slice 56 — model routing + prompt ledger)
    public string?          ModelId         { get; private set; }
    public string?          PromptVersionId { get; private set; }
    public decimal?         EstimatedCost   { get; private set; }

    /// <summary>Atomically sets LLM ledger metadata. Overwrites InputTokens/OutputTokens/TotalTokens via this path.</summary>
    public void SetLedgerMetadata(
        string  modelId,
        string? promptVersionId,
        int     inputTokens,
        int     outputTokens,
        int     totalTokens,
        decimal estimatedCost)
    {
        ModelId         = modelId;
        PromptVersionId = promptVersionId;
        InputTokens     = inputTokens;
        OutputTokens    = outputTokens;
        TotalTokens     = totalTokens;
        EstimatedCost   = estimatedCost;
    }

    /// <summary>
    /// Transitions the run to a terminal state. May only be called once.
    /// </summary>
    public void Complete(AgentRunStatus finalStatus, string summaryJson, string citationsJson)
    {
        if (finalStatus is not (AgentRunStatus.Completed or AgentRunStatus.Degraded or AgentRunStatus.Failed))
            throw new ArgumentException(
                "finalStatus must be Completed, Degraded, or Failed.", nameof(finalStatus));

        Status         = finalStatus;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        SummaryJson    = summaryJson;
        CitationsJson  = citationsJson;
    }
}

namespace OpsCopilot.SafeActions.Presentation.Contracts;

/// <summary>
/// Request body for proposing a new safe action.
/// </summary>
public sealed class ProposeActionRequest
{
    public Guid   RunId                  { get; set; }
    public string ActionType             { get; set; } = string.Empty;
    public string ProposedPayloadJson    { get; set; } = string.Empty;
    public string? RollbackPayloadJson   { get; set; }
    public string? ManualRollbackGuidance { get; set; }
}

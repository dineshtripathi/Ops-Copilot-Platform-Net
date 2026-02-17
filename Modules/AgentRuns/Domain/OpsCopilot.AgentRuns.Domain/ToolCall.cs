namespace OpsCopilot.AgentRuns.Domain;

public sealed class ToolCall
{
    public ToolCall(Guid id, string toolName, string input, string output, string evidenceId, DateTimeOffset completedAt)
    {
        Id = id;
        ToolName = toolName;
        Input = input;
        Output = output;
        EvidenceId = evidenceId;
        CompletedAt = completedAt;
    }

    public Guid Id { get; }
    public string ToolName { get; }
    public string Input { get; }
    public string Output { get; }
    public string EvidenceId { get; }
    public DateTimeOffset CompletedAt { get; }

    public string Description => $"{ToolName}: {Output}";

    public static ToolCall FromExecution(string toolName, string input, string output, string evidenceId)
        => new(Guid.NewGuid(), toolName, input, output, evidenceId, DateTimeOffset.UtcNow);
}
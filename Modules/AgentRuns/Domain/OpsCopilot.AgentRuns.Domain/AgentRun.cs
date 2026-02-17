using OpsCopilot.BuildingBlocks.Contracts;

namespace OpsCopilot.AgentRuns.Domain;

public sealed class AgentRun
{
    private readonly List<ToolCall> _toolCalls = new();

    private AgentRun(Guid id, AlertPayload alert, DateTimeOffset createdAt)
    {
        Id = id;
        Alert = alert;
        CreatedAt = createdAt;
        Status = AgentRunStatus.InProgress;
    }

    public Guid Id { get; }
    public AlertPayload Alert { get; }
    public DateTimeOffset CreatedAt { get; }
    public string Status { get; private set; }
    public IReadOnlyCollection<ToolCall> ToolCalls => _toolCalls.AsReadOnly();

    public static AgentRun Start(AlertPayload alert)
        => new(Guid.NewGuid(), alert, DateTimeOffset.UtcNow);

    public void AddToolCall(ToolCall call)
    {
        _toolCalls.Add(call);
        Status = AgentRunStatus.Completed;
    }
}

public static class AgentRunStatus
{
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
}
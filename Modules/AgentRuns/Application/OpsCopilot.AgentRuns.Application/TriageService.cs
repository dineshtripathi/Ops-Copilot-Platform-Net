using System.Linq;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Domain;
using OpsCopilot.BuildingBlocks.Contracts;

namespace OpsCopilot.AgentRuns.Application;

public sealed class TriageService
{
    private readonly IAgentRunRepository _repository;
    private readonly IToolExecutor _toolExecutor;
    private readonly ILogger<TriageService> _logger;

    public TriageService(IAgentRunRepository repository, IToolExecutor toolExecutor, ILogger<TriageService> logger)
    {
        _repository = repository;
        _toolExecutor = toolExecutor;
        _logger = logger;
    }

    public async Task<TriageResponse> RunAsync(TriageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Alert);

        var run = AgentRun.Start(request.Alert);
        _logger.LogInformation("Starting triage run {RunId} for alert {AlertId}", run.Id, request.Alert.AlertId);

        var executionResult = await _toolExecutor.ExecuteKqlQueryAsync(request.Alert, cancellationToken).ConfigureAwait(false);
        var toolCall = ToolCall.FromExecution(executionResult.ToolName, executionResult.Input, executionResult.Output, executionResult.EvidenceId);
        run.AddToolCall(toolCall);

        await _repository.SaveAsync(run, cancellationToken).ConfigureAwait(false);

        var evidence = run.ToolCalls
            .Select(tc => new EvidenceRef
            {
                EvidenceId = tc.EvidenceId,
                ToolCallId = tc.Id.ToString("N"),
                Description = tc.Description
            })
            .ToArray();

        return new TriageResponse
        {
            RunId = run.Id,
            Status = run.Status,
            Evidence = evidence
        };
    }
}
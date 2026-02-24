using System.Diagnostics;
using System.Text.Json;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Deterministic dry-run executor that validates inputs and returns structured
/// responses without performing any real work against external systems.
/// <para>
/// Rules:
/// <list type="bullet">
///   <item>Empty / whitespace <c>actionType</c> → failure (<c>invalid_action_type</c>)</item>
///   <item>Empty / whitespace payload → failure (<c>empty_payload</c>)</item>
///   <item>Malformed JSON payload → failure (<c>invalid_json</c>)</item>
///   <item>Payload contains <c>"simulateFailure": true</c> → failure (<c>simulated_failure</c>)</item>
///   <item>Otherwise → success</item>
/// </list>
/// </para>
/// This class NEVER throws exceptions — every path returns an
/// <see cref="ActionExecutionResult"/>.
/// </summary>
internal sealed class DryRunActionExecutor : IActionExecutor
{
    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public Task<ActionExecutionResult> ExecuteAsync(
        string actionType, string payloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(Run(actionType, payloadJson, "dry-run"));
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string actionType, string rollbackPayloadJson, CancellationToken ct = default)
    {
        return Task.FromResult(Run(actionType, rollbackPayloadJson, "dry-run-rollback"));
    }

    // ── Core deterministic pipeline ──────────────────────────────────

    private static ActionExecutionResult Run(
        string actionType, string payloadJson, string mode)
    {
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(actionType))
            return Fail(mode, actionType, "invalid_action_type",
                "actionType must not be null or whitespace", sw);

        if (string.IsNullOrWhiteSpace(payloadJson))
            return Fail(mode, actionType, "empty_payload",
                "payload must not be null or whitespace", sw);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(payloadJson, JsonOptions);
        }
        catch (JsonException)
        {
            return Fail(mode, actionType, "invalid_json",
                "payload is not valid JSON", sw);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("simulateFailure", out var sf) &&
                sf.ValueKind == JsonValueKind.True)
            {
                return Fail(mode, actionType, "simulated_failure",
                    "caller requested simulated failure via simulateFailure flag", sw);
            }
        }

        sw.Stop();
        return new ActionExecutionResult(
            Success: true,
            ResponseJson: BuildJson(mode, actionType, "success", "dry-run completed", sw.ElapsedMilliseconds),
            DurationMs: sw.ElapsedMilliseconds);
    }

    // ── Response builders ────────────────────────────────────────────

    private static ActionExecutionResult Fail(
        string mode, string? actionType, string reason, string detail, Stopwatch sw)
    {
        sw.Stop();
        return new ActionExecutionResult(
            Success: false,
            ResponseJson: BuildJson(mode, actionType ?? "", reason, detail, sw.ElapsedMilliseconds),
            DurationMs: sw.ElapsedMilliseconds);
    }

    private static string BuildJson(
        string mode, string actionType, string simulatedOutcome, string reason, long durationMs)
    {
        return JsonSerializer.Serialize(new
        {
            mode,
            actionType,
            simulatedOutcome,
            reason,
            durationMs
        });
    }
}

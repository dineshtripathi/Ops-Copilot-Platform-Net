using System.Text.Json;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Direct unit tests for <see cref="DryRunActionExecutor"/>.
/// Validates the five deterministic rules documented on the class
/// for both Execute and Rollback paths.
/// </summary>
public class DryRunActionExecutorTests
{
    private readonly DryRunActionExecutor _sut = new();

    // ── Execute: success path ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Success_For_Valid_Input()
    {
        var result = await _sut.ExecuteAsync("restart_pod", "{\"target\":\"pod-1\"}", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DurationMs >= 0);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("restart_pod", json.RootElement.GetProperty("actionType").GetString());
        Assert.Equal("success", json.RootElement.GetProperty("simulatedOutcome").GetString());
        Assert.Equal("dry-run completed", json.RootElement.GetProperty("reason").GetString());
    }

    // ── Execute: invalid_action_type ────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Returns_Failure_For_Invalid_ActionType(string? actionType)
    {
        var result = await _sut.ExecuteAsync(actionType!, "{}", CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("invalid_action_type", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Execute: empty_payload ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_Returns_Failure_For_Empty_Payload(string? payload)
    {
        var result = await _sut.ExecuteAsync("restart_pod", payload!, CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("empty_payload", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Execute: invalid_json ───────────────────────────────────────

    [Theory]
    [InlineData("not-json")]
    [InlineData("{broken")]
    [InlineData("<xml/>")]
    public async Task ExecuteAsync_Returns_Failure_For_Malformed_Json(string payload)
    {
        var result = await _sut.ExecuteAsync("restart_pod", payload, CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("invalid_json", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Execute: simulated_failure ──────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_SimulateFailure_Is_True()
    {
        var payload = """{"target":"pod-1","simulateFailure":true}""";
        var result = await _sut.ExecuteAsync("restart_pod", payload, CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("simulated_failure", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Success_When_SimulateFailure_Is_False()
    {
        var payload = """{"target":"pod-1","simulateFailure":false}""";
        var result = await _sut.ExecuteAsync("restart_pod", payload, CancellationToken.None);

        Assert.True(result.Success);
    }

    // ── Rollback: success path ──────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_Success_With_Rollback_Mode()
    {
        var result = await _sut.RollbackAsync("restart_pod", "{\"undo\":\"stop_pod\"}", CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DurationMs >= 0);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("restart_pod", json.RootElement.GetProperty("actionType").GetString());
        Assert.Equal("success", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Rollback: invalid_action_type ───────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RollbackAsync_Returns_Failure_For_Invalid_ActionType(string? actionType)
    {
        var result = await _sut.RollbackAsync(actionType!, "{}", CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("invalid_action_type", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Rollback: empty_payload ─────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RollbackAsync_Returns_Failure_For_Empty_Payload(string? payload)
    {
        var result = await _sut.RollbackAsync("restart_pod", payload!, CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("empty_payload", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Rollback: invalid_json ──────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_Failure_For_Malformed_Json()
    {
        var result = await _sut.RollbackAsync("restart_pod", "{{bad}}", CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("invalid_json", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Rollback: simulated_failure ─────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_Failure_When_SimulateFailure_Is_True()
    {
        var payload = """{"undo":"stop_pod","simulateFailure":true}""";
        var result = await _sut.RollbackAsync("restart_pod", payload, CancellationToken.None);

        Assert.False(result.Success);

        var json = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("dry-run-rollback", json.RootElement.GetProperty("mode").GetString());
        Assert.Equal("simulated_failure", json.RootElement.GetProperty("simulatedOutcome").GetString());
    }

    // ── Response shape ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Response_Contains_All_Required_Fields()
    {
        var result = await _sut.ExecuteAsync("scale_up", """{"replicas":3}""", CancellationToken.None);

        var json = JsonDocument.Parse(result.ResponseJson);
        var root = json.RootElement;

        // All five fields must be present
        Assert.True(root.TryGetProperty("mode", out _));
        Assert.True(root.TryGetProperty("actionType", out _));
        Assert.True(root.TryGetProperty("simulatedOutcome", out _));
        Assert.True(root.TryGetProperty("reason", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
    }

    // ── Edge cases ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Accepts_Trailing_Commas_In_Payload()
    {
        var payload = """{"target":"pod-1",}""";
        var result = await _sut.ExecuteAsync("restart_pod", payload, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_Accepts_Comments_In_Payload()
    {
        var payload = """
        {
            // this is a comment
            "target": "pod-1"
        }
        """;
        var result = await _sut.ExecuteAsync("restart_pod", payload, CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_Handles_Array_Payload_Without_SimulateFailure()
    {
        // JSON array is valid JSON but simulateFailure check only applies to objects
        var result = await _sut.ExecuteAsync("restart_pod", "[1,2,3]", CancellationToken.None);

        Assert.True(result.Success);
    }
}

using System.Text.Json;
using Azure;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="AzureMonitorQueryActionExecutor"/>.
/// Exercises all error codes, payload validation, blocked-pattern guardrails,
/// timespan clamping, timeout, rollback, and the success path using a mock
/// <see cref="IAzureMonitorLogsReader"/>.
/// </summary>
public class AzureMonitorQueryActionExecutorTests
{
    private const string ValidWorkspaceId = "00000000-0000-0000-0000-000000000000";
    private const string ValidQuery = "Heartbeat | take 10";

    // ── Helpers ──────────────────────────────────────────────────────

    private static AzureMonitorQueryActionExecutor CreateSut(
        IAzureMonitorLogsReader reader, int timeoutMs = 5000,
        string[]? allowedWorkspaces = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["SafeActions:AzureMonitorQueryTimeoutMs"] = timeoutMs.ToString(),
        };

        if (allowedWorkspaces is not null)
        {
            for (var i = 0; i < allowedWorkspaces.Length; i++)
                dict[$"SafeActions:AllowedLogAnalyticsWorkspaceIds:{i}"] = allowedWorkspaces[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new AzureMonitorQueryActionExecutor(
            reader, config, NullLogger<AzureMonitorQueryActionExecutor>.Instance);
    }

    private static Mock<IAzureMonitorLogsReader> CreateReaderMock(
        MonitorQueryResult? result = null)
    {
        var mock = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        if (result is not null)
        {
            mock.Setup(r => r.QueryLogsAsync(
                    It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result);
        }
        return mock;
    }

    private static string MakePayload(
        string workspaceId, string query, int? timespanMinutes = null)
    {
        if (timespanMinutes.HasValue)
            return JsonSerializer.Serialize(new { workspaceId, query, timespanMinutes = timespanMinutes.Value });

        return JsonSerializer.Serialize(new { workspaceId, query });
    }

    // ── Success ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Success_With_QueryResult()
    {
        var queryResult = new MonitorQueryResult(
            RowCount: 2, ColumnCount: 3,
            ResultJson: """[{"col1":"a","col2":1},{"col1":"b","col2":2}]""");

        var reader = CreateReaderMock(queryResult);
        var sut = CreateSut(reader.Object);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.DurationMs >= 0);

        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("azure_monitor_query", root.GetProperty("mode").GetString());
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(ValidWorkspaceId, root.GetProperty("workspaceId").GetString());
        Assert.Equal(2, root.GetProperty("rowCount").GetInt32());
        Assert.Equal(3, root.GetProperty("columnCount").GetInt32());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("rows").ValueKind);

        reader.Verify(r => r.QueryLogsAsync(
            ValidWorkspaceId, ValidQuery,
            TimeSpan.FromMinutes(60), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Success_Uses_Custom_TimespanMinutes()
    {
        var queryResult = new MonitorQueryResult(1, 1, """[{"x":1}]""");
        var reader = CreateReaderMock(queryResult);
        var sut = CreateSut(reader.Object);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery, timespanMinutes: 120),
            CancellationToken.None);

        Assert.True(result.Success);

        reader.Verify(r => r.QueryLogsAsync(
            ValidWorkspaceId, ValidQuery,
            TimeSpan.FromMinutes(120), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Payload validation ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidJson_For_NonJsonPayload()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("<<<not json>>>", CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_json");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_MissingWorkspaceId()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync(
            """{ "query": "Heartbeat" }""", CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_EmptyWorkspaceId()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync(
            MakePayload("", ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_MissingQuery()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync(
            JsonSerializer.Serialize(new { workspaceId = ValidWorkspaceId }),
            CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_EmptyQuery()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ""), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    // ── WorkspaceId validation ───────────────────────────────────────

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("workspace-abc-xyz")]
    public async Task ExecuteAsync_Returns_InvalidWorkspaceId_For_BadFormat(string badId)
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync(
            MakePayload(badId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_workspace_id");
    }

    // ── Blocked query patterns ───────────────────────────────────────

    [Theory]
    [InlineData(".create")]
    [InlineData(".alter")]
    [InlineData(".drop")]
    [InlineData(".ingest")]
    [InlineData(".set")]
    [InlineData(".append")]
    [InlineData(".delete")]
    [InlineData(".execute")]
    public async Task ExecuteAsync_Returns_BlockedQueryPattern_For_MutatingCommand(
        string blockedPattern)
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var query = $"Heartbeat | {blockedPattern} something";
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, query), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "blocked_query_pattern");
    }

    [Fact]
    public async Task ExecuteAsync_Blocks_Patterns_CaseInsensitively()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var query = "Heartbeat | .CREATE table";
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, query), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "blocked_query_pattern");
    }

    // ── Timespan clamping ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Clamps_TimespanMinutes_To_Min_1()
    {
        var queryResult = new MonitorQueryResult(0, 0, "[]");
        var reader = CreateReaderMock(queryResult);
        var sut = CreateSut(reader.Object);

        await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery, timespanMinutes: -10),
            CancellationToken.None);

        reader.Verify(r => r.QueryLogsAsync(
            ValidWorkspaceId, ValidQuery,
            TimeSpan.FromMinutes(1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Clamps_TimespanMinutes_To_Max_1440()
    {
        var queryResult = new MonitorQueryResult(0, 0, "[]");
        var reader = CreateReaderMock(queryResult);
        var sut = CreateSut(reader.Object);

        await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery, timespanMinutes: 9999),
            CancellationToken.None);

        reader.Verify(r => r.QueryLogsAsync(
            ValidWorkspaceId, ValidQuery,
            TimeSpan.FromMinutes(1440), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Azure SDK error mapping ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_AuthFailed_On_AuthenticationFailedException()
    {
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationFailedException("bad creds"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_auth_failed");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Forbidden_On_403_RequestFailedException()
    {
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "forbidden"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_forbidden");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_NotFound_On_404_RequestFailedException()
    {
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_RequestFailed_On_Other_RequestFailedException()
    {
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "server error"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_request_failed");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_UnexpectedError_On_GenericException()
    {
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "unexpected_error");
    }

    // ── Timeout ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Timeout_On_SlowReader()
    {
        var reader = new Mock<IAzureMonitorLogsReader>(MockBehavior.Strict);
        reader.Setup(r => r.QueryLogsAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, TimeSpan, CancellationToken>(async (_, _, _, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct); // Will be cancelled
                return null!;
            });

        // Very short timeout to trigger cancel quickly
        var sut = CreateSut(reader.Object, timeoutMs: 50);
        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_monitor_timeout");
    }

    // ── Rollback ─────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_NotSupported()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.RollbackAsync("{}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, result.DurationMs);

        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("azure_monitor_query", root.GetProperty("mode").GetString());
        Assert.Equal("not_supported", root.GetProperty("reason").GetString());
    }

    // ── Response shape: every failure has mode + reason + detail ─────

    [Fact]
    public async Task ExecuteAsync_Failure_Response_Always_Has_Mode_And_Reason()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("<<<bad>>>", CancellationToken.None);

        Assert.False(result.Success);
        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("azure_monitor_query", root.GetProperty("mode").GetString());
        Assert.True(root.TryGetProperty("reason", out _));
        Assert.True(root.TryGetProperty("detail", out _));
    }

    // ── Workspace allowlist ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyWorkspaceAllowlist_AllowsAll()
    {
        var reader = CreateReaderMock(new MonitorQueryResult(1, 1, "[{\"x\":1}]"));
        var sut = CreateSut(reader.Object, allowedWorkspaces: []);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NoWorkspaceAllowlistConfig_AllowsAll()
    {
        var reader = CreateReaderMock(new MonitorQueryResult(1, 1, "[{\"x\":1}]"));
        var sut = CreateSut(reader.Object);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_AllowlistedWorkspace_Succeeds()
    {
        var reader = CreateReaderMock(new MonitorQueryResult(1, 1, "[{\"x\":1}]"));
        var sut = CreateSut(reader.Object,
            allowedWorkspaces: [ValidWorkspaceId]);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NonAllowlistedWorkspace_Returns_TargetNotAllowlisted()
    {
        var sut = CreateSut(CreateReaderMock().Object,
            allowedWorkspaces: ["11111111-1111-1111-1111-111111111111"]);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        AssertErrorCode(result, "target_not_allowlisted");
    }

    [Fact]
    public async Task ExecuteAsync_WorkspaceAllowlist_CaseInsensitive()
    {
        var upper = ValidWorkspaceId.ToUpperInvariant();
        var reader = CreateReaderMock(new MonitorQueryResult(1, 1, "[{\"x\":1}]"));
        var sut = CreateSut(reader.Object,
            allowedWorkspaces: [upper]);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleAllowedWorkspaces_AcceptsAny()
    {
        var reader = CreateReaderMock(new MonitorQueryResult(1, 1, "[{\"x\":1}]"));
        var sut = CreateSut(reader.Object,
            allowedWorkspaces: [
                "11111111-1111-1111-1111-111111111111",
                ValidWorkspaceId,
                "22222222-2222-2222-2222-222222222222"
            ]);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NonAllowlistedWorkspace_DetailContainsWorkspaceId()
    {
        var sut = CreateSut(CreateReaderMock().Object,
            allowedWorkspaces: ["11111111-1111-1111-1111-111111111111"]);

        var result = await sut.ExecuteAsync(
            MakePayload(ValidWorkspaceId, ValidQuery), CancellationToken.None);

        Assert.False(result.Success);
        using var doc = JsonDocument.Parse(result.ResponseJson);
        var detail = doc.RootElement.GetProperty("detail").GetString();
        Assert.Contains(ValidWorkspaceId, detail);
    }

    // ── Assert helper ────────────────────────────────────────────────

    private static void AssertErrorCode(
        ActionExecutionResult result, string expectedReason)
    {
        using var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal(expectedReason,
            doc.RootElement.GetProperty("reason").GetString());
    }
}

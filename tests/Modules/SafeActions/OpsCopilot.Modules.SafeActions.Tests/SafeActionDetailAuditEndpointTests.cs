using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Enums;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Presentation.Contracts;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level tests for Slice 16: SafeActions Action Detail Audit Enrichment.
/// Covers approval history collection, execution log collection, payload redaction,
/// and audit summary enrichment on the GET /safe-actions/{id} endpoint.
/// </summary>
public class SafeActionDetailAuditEndpointTests
{
    // ── Helpers ──────────────────────────────────────────────────

    private static ActionRecord CreateRecord(
        string tenantId = "t-detail",
        string actionType = "restart_pod")
    {
        return ActionRecord.Create(
            tenantId,
            Guid.NewGuid(),
            actionType,
            "{\"target\":\"pod-1\"}",
            "{\"undo\":\"stop_pod\"}");
    }

    private static async Task<(WebApplication App, HttpClient Client, Mock<IActionRecordRepository> Repo)>
        CreateTestHost()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton(repo.Object);
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>());
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>());
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient(), repo);
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── AC-1: GET-by-id returns approval history collection ─────

    [Fact]
    public async Task GetById_ReturnsApprovalHistory()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;

            var approval = ApprovalRecord.Create(
                id, "user@contoso.com", ApprovalDecision.Approved, "Looks good", "Action");

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>
                {
                    [id] = new AuditSummary(0, null, null, 1, "Approved", approval.CreatedAtUtc)
                });
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord> { approval });
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog>());

            var response = await client.GetAsync($"/safe-actions/{id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            var approvals = root.GetProperty("approvals");
            Assert.Equal(JsonValueKind.Array, approvals.ValueKind);
            Assert.Equal(1, approvals.GetArrayLength());

            var first = approvals[0];
            Assert.Equal("user@contoso.com", first.GetProperty("approverIdentity").GetString());
            Assert.Equal("Approved", first.GetProperty("decision").GetString());
            Assert.Equal("Looks good", first.GetProperty("reason").GetString());
            Assert.Equal("Action", first.GetProperty("target").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-2: GET-by-id returns execution log history ───────────

    [Fact]
    public async Task GetById_ReturnsExecutionLogHistory()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;

            var log = ExecutionLog.Create(
                id, "Execute", "{\"cmd\":\"restart\"}", null, "Success", 120);

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>
                {
                    [id] = new AuditSummary(1, log.ExecutedAtUtc, true, 0, null, null)
                });
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord>());
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog> { log });

            var response = await client.GetAsync($"/safe-actions/{id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            var logs = root.GetProperty("executionLogs");
            Assert.Equal(JsonValueKind.Array, logs.ValueKind);
            Assert.Equal(1, logs.GetArrayLength());

            var first = logs[0];
            Assert.Equal("Execute", first.GetProperty("executionType").GetString());
            Assert.True(first.GetProperty("success").GetBoolean());
            Assert.Equal(120, first.GetProperty("durationMs").GetInt64());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-3: Empty collections when no audit data ──────────────

    [Fact]
    public async Task GetById_ReturnsEmptyCollections_WhenNoAuditData()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord>());
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog>());

            var response = await client.GetAsync($"/safe-actions/{id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            Assert.Equal(0, root.GetProperty("approvals").GetArrayLength());
            Assert.Equal(0, root.GetProperty("executionLogs").GetArrayLength());
            Assert.Equal(0, root.GetProperty("approvalCount").GetInt32());
            Assert.Equal(0, root.GetProperty("executionLogCount").GetInt32());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-4: 404 still returned when record not found ──────────

    [Fact]
    public async Task GetById_Returns404_WhenRecordNotFound()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var id = Guid.NewGuid();

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((ActionRecord?)null);

            var response = await client.GetAsync($"/safe-actions/{id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-5: Payload redaction on execution log detail ─────────

    [Fact]
    public async Task GetById_RedactsSensitiveKeys_InExecutionLogPayloads()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;

            var log = ExecutionLog.Create(
                id,
                "Execute",
                "{\"token\":\"abc123\",\"password\":\"s3cret\",\"host\":\"vm-01\"}",
                "{\"key\":\"vault-key\",\"connectionString\":\"Server=x\",\"result\":\"ok\"}",
                "Success",
                200);

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord>());
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog> { log });

            var response = await client.GetAsync($"/safe-actions/{id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var logEntry = json.RootElement.GetProperty("executionLogs")[0];

            // Request payload redaction
            var reqPayload = logEntry.GetProperty("requestPayloadJson").GetString()!;
            using var reqDoc = JsonDocument.Parse(reqPayload);
            Assert.Equal("[REDACTED]", reqDoc.RootElement.GetProperty("token").GetString());
            Assert.Equal("[REDACTED]", reqDoc.RootElement.GetProperty("password").GetString());
            Assert.Equal("vm-01", reqDoc.RootElement.GetProperty("host").GetString()); // not sensitive

            // Response payload redaction
            var resPayload = logEntry.GetProperty("responsePayloadJson").GetString()!;
            using var resDoc = JsonDocument.Parse(resPayload);
            Assert.Equal("[REDACTED]", resDoc.RootElement.GetProperty("key").GetString());
            Assert.Equal("[REDACTED]", resDoc.RootElement.GetProperty("connectionString").GetString());
            Assert.Equal("ok", resDoc.RootElement.GetProperty("result").GetString()); // not sensitive
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-6: Audit summary fields populated on detail view ─────

    [Fact]
    public async Task GetById_IncludesAuditSummaryFields()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;
            var lastExecAt = DateTimeOffset.UtcNow.AddMinutes(-5);
            var lastApprovalAt = DateTimeOffset.UtcNow.AddMinutes(-10);

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>
                {
                    [id] = new AuditSummary(3, lastExecAt, true, 2, "Approved", lastApprovalAt)
                });
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord>());
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog>());

            var response = await client.GetAsync($"/safe-actions/{id}");

            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            Assert.Equal(3, root.GetProperty("executionLogCount").GetInt32());
            Assert.True(root.GetProperty("lastExecutionSuccess").GetBoolean());
            Assert.Equal(2, root.GetProperty("approvalCount").GetInt32());
            Assert.Equal("Approved", root.GetProperty("lastApprovalDecision").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-7: Multiple approvals returned in order ──────────────

    [Fact]
    public async Task GetById_ReturnsMultipleApprovals_InChronologicalOrder()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;

            var approval1 = ApprovalRecord.Create(
                id, "admin@contoso.com", ApprovalDecision.Rejected, "Needs review", "Action");
            var approval2 = ApprovalRecord.Create(
                id, "lead@contoso.com", ApprovalDecision.Approved, "Approved after fix", "Action");

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord> { approval1, approval2 });
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog>());

            var response = await client.GetAsync($"/safe-actions/{id}");
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);

            var approvals = json.RootElement.GetProperty("approvals");
            Assert.Equal(2, approvals.GetArrayLength());
            Assert.Equal("Rejected", approvals[0].GetProperty("decision").GetString());
            Assert.Equal("Approved", approvals[1].GetProperty("decision").GetString());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-8: Null response payload handled gracefully ──────────

    [Fact]
    public async Task GetById_HandlesNullResponsePayload_InExecutionLog()
    {
        var (app, client, repo) = await CreateTestHost();
        try
        {
            var record = CreateRecord();
            var id = record.ActionRecordId;

            var log = ExecutionLog.Create(
                id, "Execute", "{\"cmd\":\"check\"}", null, "Success", 50);

            repo.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(record);
            repo.Setup(r => r.GetAuditSummariesAsync(
                    It.Is<IReadOnlyList<Guid>>(l => l.Contains(id)),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, AuditSummary>());
            repo.Setup(r => r.GetApprovalsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ApprovalRecord>());
            repo.Setup(r => r.GetExecutionLogsForActionAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ExecutionLog> { log });

            var response = await client.GetAsync($"/safe-actions/{id}");
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);

            var logEntry = json.RootElement.GetProperty("executionLogs")[0];
            Assert.Equal(JsonValueKind.Null, logEntry.GetProperty("responsePayloadJson").ValueKind);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── AC-9: PayloadRedactor unit tests ────────────────────────

    [Fact]
    public void PayloadRedactor_RedactsAllSensitiveKeys()
    {
        var json = "{\"token\":\"abc\",\"Password\":\"x\",\"SECRET\":\"y\",\"Key\":\"z\",\"connectionString\":\"cs\",\"host\":\"vm\"}";
        var result = PayloadRedactor.Redact(json)!;

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("[REDACTED]", root.GetProperty("token").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("Password").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("SECRET").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("Key").GetString());
        Assert.Equal("[REDACTED]", root.GetProperty("connectionString").GetString());
        Assert.Equal("vm", root.GetProperty("host").GetString());
    }

    [Fact]
    public void PayloadRedactor_ReturnsNull_ForNullInput()
    {
        Assert.Null(PayloadRedactor.Redact(null));
    }

    [Fact]
    public void PayloadRedactor_ReturnsOriginal_ForNonJson()
    {
        var input = "not-json-at-all";
        Assert.Equal(input, PayloadRedactor.Redact(input));
    }

    [Fact]
    public void PayloadRedactor_HandlesNestedObjects()
    {
        var json = "{\"outer\":{\"token\":\"secret-val\",\"safe\":\"visible\"}}";
        var result = PayloadRedactor.Redact(json)!;

        using var doc = JsonDocument.Parse(result);
        var outer = doc.RootElement.GetProperty("outer");
        Assert.Equal("[REDACTED]", outer.GetProperty("token").GetString());
        Assert.Equal("visible", outer.GetProperty("safe").GetString());
    }
}

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Application.Orchestration;
using OpsCopilot.SafeActions.Domain.Entities;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Presentation.Endpoints;
using OpsCopilot.BuildingBlocks.Contracts.Governance;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// HTTP-level integration tests for execution throttling (429).
/// Verifies that <see cref="IExecutionThrottlePolicy"/> is evaluated at the
/// endpoint layer and that a deny decision produces a deterministic 429 JSON body.
/// Slice 19 — SafeActions Execution Throttling (STRICT, In-Process).
/// Slice 20 — Retry-After header + structured Warning logging.
/// </summary>
public class SafeActionExecutionThrottleEndpointTests
{
    // ── Lightweight test logger ─────────────────────────────────────

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = new();
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private sealed class CapturingLoggerProvider<T> : ILoggerProvider
    {
        private readonly CapturingLogger<T> _logger;
        public CapturingLoggerProvider(CapturingLogger<T> logger) => _logger = logger;
        public ILogger CreateLogger(string categoryName) => _logger;
        public void Dispose() { }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static ActionRecord CreateApprovedRecord()
    {
        var record = ActionRecord.Create(
            "t-throttle", Guid.NewGuid(), "restart_pod",
            "{\"target\":\"pod-1\"}", "{\"undo\":\"stop_pod\"}");
        record.Approve();
        return record;
    }

    private static ActionRecord CreateRollbackApprovedRecord()
    {
        var record = CreateApprovedRecord();
        record.MarkExecuting();
        record.CompleteExecution("{\"target\":\"pod-1\"}", "{\"ok\":true}");
        record.RequestRollback();
        record.ApproveRollback();
        return record;
    }

    private static Mock<IActionRecordRepository> CreateRepoMock(ActionRecord record)
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(record.ActionRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);
        repo.Setup(r => r.SaveAsync(record, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AppendExecutionLogAsync(
                It.IsAny<ExecutionLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repo;
    }

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        IActionRecordRepository repository,
        IExecutionThrottlePolicy throttlePolicy,
        ISafeActionsTelemetry? telemetry = null,
        ILoggerProvider? loggerProvider = null)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        if (loggerProvider is not null)
            builder.Logging.AddProvider(loggerProvider);

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "True"
            });

        builder.Services.AddSingleton(repository);
        var executorResult = new ActionExecutionResult(true, "{\"ok\":true}", 1);
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>(e =>
            e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(executorResult)
            && e.RollbackAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(executorResult)));
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton(telemetry ?? Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton(throttlePolicy);
        builder.Services.AddSingleton(Mock.Of<IActionTypeCatalog>(c => c.IsAllowlisted(It.IsAny<string>()) == true));
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    private static async Task DisposeHost(WebApplication app)
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }

    // ── Execute endpoint — throttled → 429 ──────────────────────────

    [Fact]
    public async Task Execute_Returns429_WhenThrottlePolicyDenies()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(45));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("throttled", root.GetProperty("reasonCode").GetString());
            Assert.NotNull(root.GetProperty("message").GetString());
            Assert.Equal(45, root.GetProperty("retryAfterSeconds").GetInt32());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Rollback/execute endpoint — throttled → 429 ─────────────────

    [Fact]
    public async Task RollbackExecute_Returns429_WhenThrottlePolicyDenies()
    {
        var record = CreateRollbackApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(30));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal("throttled", root.GetProperty("reasonCode").GetString());
            Assert.Equal(30, root.GetProperty("retryAfterSeconds").GetInt32());
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── 429 body shape ──────────────────────────────────────────────

    [Fact]
    public async Task Execute_429Body_HasExactThreeProperties()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(60));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var propertyNames = new List<string>();
            foreach (var prop in root.EnumerateObject())
                propertyNames.Add(prop.Name);

            Assert.Equal(3, propertyNames.Count);
            Assert.Contains("reasonCode", propertyNames);
            Assert.Contains("message", propertyNames);
            Assert.Contains("retryAfterSeconds", propertyNames);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Telemetry counter incremented on 429 ────────────────────────

    [Fact]
    public async Task Execute_Throttled_CallsTelemetryRecordExecutionThrottled()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(10));
        var telemetryMock = new Mock<ISafeActionsTelemetry>();

        var (app, client) = await CreateTestHost(repo.Object, throttle, telemetryMock.Object);
        try
        {
            await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            telemetryMock.Verify(t => t.RecordExecutionThrottled(
                "restart_pod", "t-throttle", "execute"), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Throttled_CallsTelemetryRecordExecutionThrottled()
    {
        var record = CreateRollbackApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(10));
        var telemetryMock = new Mock<ISafeActionsTelemetry>();

        var (app, client) = await CreateTestHost(repo.Object, throttle, telemetryMock.Object);
        try
        {
            await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            telemetryMock.Verify(t => t.RecordExecutionThrottled(
                "restart_pod", "t-throttle", "rollback_execute"), Times.Once);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Throttle allows → proceeds normally ─────────────────────────

    [Fact]
    public async Task Execute_ThrottleAllows_ReturnsNon429()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Allow());

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Execute: throttle evaluated after 501 guard ─────────────────

    [Fact]
    public async Task Execute_404WhenRecordMissing_ThrottleAllowed()
    {
        var repo = new Mock<IActionRecordRepository>(MockBehavior.Strict);
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActionRecord?)null);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Allow());

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{Guid.NewGuid()}/execute", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    // ── Slice 20 — Retry-After header tests ─────────────────────────

    [Fact]
    public async Task Execute_Throttled_HasRetryAfterHeader()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(45));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(response.Headers.Contains("Retry-After"),
                "429 response must include Retry-After header");
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Throttled_HasRetryAfterHeader()
    {
        var record = CreateRollbackApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(30));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.True(response.Headers.Contains("Retry-After"),
                "429 response must include Retry-After header");
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Throttled_RetryAfterHeaderMatchesBody()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(45));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            var headerValue = response.Headers.GetValues("Retry-After").Single();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var bodyValue = doc.RootElement.GetProperty("retryAfterSeconds").GetInt32();

            Assert.Equal(bodyValue.ToString(), headerValue);
            Assert.Equal("45", headerValue);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Throttled_RetryAfterHeaderMatchesBody()
    {
        var record = CreateRollbackApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(30));

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            var headerValue = response.Headers.GetValues("Retry-After").Single();
            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var bodyValue = doc.RootElement.GetProperty("retryAfterSeconds").GetInt32();

            Assert.Equal(bodyValue.ToString(), headerValue);
            Assert.Equal("30", headerValue);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_ThrottleAllows_NoRetryAfterHeader()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Allow());

        var (app, client) = await CreateTestHost(repo.Object, throttle);
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.False(response.Headers.Contains("Retry-After"),
                "Non-throttled response must NOT include Retry-After header");
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Guarded501_NoRetryAfterHeader()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Allow());

        // Override EnableExecution to false to trigger 501 guard
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SafeActions:EnableExecution"] = "False"
            });
        builder.Services.AddSingleton(repo.Object);
        var executorResult = new ActionExecutionResult(true, "{\"ok\":true}", 1);
        builder.Services.AddSingleton(Mock.Of<IActionExecutor>(e =>
            e.ExecuteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())
                == Task.FromResult(executorResult)));
        builder.Services.AddSingleton(Mock.Of<ISafeActionPolicy>());
        builder.Services.AddSingleton(Mock.Of<ITenantExecutionPolicy>(p =>
            p.EvaluateExecution(It.IsAny<string>(), It.IsAny<string>()) == PolicyDecision.Allow()));
        builder.Services.AddSingleton(Mock.Of<ISafeActionsTelemetry>());
        builder.Services.AddSingleton(throttle);
        builder.Services.AddSingleton(Mock.Of<IActionTypeCatalog>(c => c.IsAllowlisted(It.IsAny<string>()) == true));
        builder.Services.AddSingleton<SafeActionOrchestrator>();

        var app = builder.Build();
        app.MapSafeActionEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();
        try
        {
            var response = await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            Assert.Equal((HttpStatusCode)501, response.StatusCode);
            Assert.False(response.Headers.Contains("Retry-After"),
                "501 guarded response must NOT include Retry-After header");
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task Execute_Throttled_LogsWarning()
    {
        var record = CreateApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(45));
        var capturingLogger = new CapturingLogger<SafeActionOrchestrator>();
        var logProvider = new CapturingLoggerProvider<SafeActionOrchestrator>(capturingLogger);

        var (app, client) = await CreateTestHost(repo.Object, throttle, loggerProvider: logProvider);
        try
        {
            await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/execute", null);

            var warnings = capturingLogger.Entries
                .Where(e => e.Level == LogLevel.Warning)
                .ToList();
            Assert.Single(warnings);
            Assert.Contains("Execution throttled", warnings[0].Message);
            Assert.Contains("restart_pod", warnings[0].Message);
            Assert.Contains("t-throttle", warnings[0].Message);
            Assert.Contains("execute", warnings[0].Message);
            Assert.Contains("45", warnings[0].Message);
        }
        finally
        {
            await DisposeHost(app);
        }
    }

    [Fact]
    public async Task RollbackExecute_Throttled_LogsWarning()
    {
        var record = CreateRollbackApprovedRecord();
        var repo = CreateRepoMock(record);
        var throttle = Mock.Of<IExecutionThrottlePolicy>(p =>
            p.Evaluate(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())
                == ThrottleDecision.Deny(30));
        var capturingLogger = new CapturingLogger<SafeActionOrchestrator>();
        var logProvider = new CapturingLoggerProvider<SafeActionOrchestrator>(capturingLogger);

        var (app, client) = await CreateTestHost(repo.Object, throttle, loggerProvider: logProvider);
        try
        {
            await client.PostAsync(
                $"/safe-actions/{record.ActionRecordId}/rollback/execute", null);

            var warnings = capturingLogger.Entries
                .Where(e => e.Level == LogLevel.Warning)
                .ToList();
            Assert.Single(warnings);
            Assert.Contains("Execution throttled", warnings[0].Message);
            Assert.Contains("restart_pod", warnings[0].Message);
            Assert.Contains("t-throttle", warnings[0].Message);
            Assert.Contains("rollback_execute", warnings[0].Message);
            Assert.Contains("30", warnings[0].Message);
        }
        finally
        {
            await DisposeHost(app);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class SessionEndpointTests
{
    private const string TenantId = "tenant-session-test";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        ISessionStore       sessionStore,
        IAgentRunRepository runRepository)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<ISessionStore>(sessionStore);
        builder.Services.AddSingleton<IAgentRunRepository>(runRepository);

        var app = builder.Build();
        app.MapSessionEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task MissingTenantHeader_Returns400()
    {
        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        var runRepo      = new Mock<IAgentRunRepository>(MockBehavior.Strict);

        var (app, client) = await CreateTestHost(sessionStore.Object, runRepo.Object);
        try
        {
            var response = await client.GetAsync($"/session/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task SessionNotFound_Returns404()
    {
        var sessionId = Guid.NewGuid();

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionInfo?)null);

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);

        var (app, client) = await CreateTestHost(sessionStore.Object, runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/session/{sessionId}");
            request.Headers.Add("x-tenant-id", TenantId);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task TenantMismatch_Returns403()
    {
        var sessionId  = Guid.NewGuid();
        var sessionInfo = new SessionInfo(
            sessionId,   "other-tenant",
            DateTimeOffset.UtcNow.AddMinutes(-10),
            DateTimeOffset.UtcNow.AddMinutes(20),
            false);

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionInfo);

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);

        var (app, client) = await CreateTestHost(sessionStore.Object, runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/session/{sessionId}");
            request.Headers.Add("x-tenant-id", TenantId);   // TenantId != "other-tenant"
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ActiveSession_ReturnsOk_IsExpiredFalse_WithRecentRuns()
    {
        var sessionId   = Guid.NewGuid();
        var createdAt   = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expiresAt   = DateTimeOffset.UtcNow.AddMinutes(25);
        var sessionInfo = new SessionInfo(sessionId, TenantId, createdAt, expiresAt, false);

        var run1 = AgentRun.Create(TenantId, "fp-001", sessionId);

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionInfo);

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        runRepo
            .Setup(r => r.GetRecentRunsBySessionAsync(sessionId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AgentRun>)new[] { run1 });

        var (app, client) = await CreateTestHost(sessionStore.Object, runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/session/{sessionId}");
            request.Headers.Add("x-tenant-id", TenantId);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<SessionResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal(sessionId, body.SessionId);
            Assert.Equal(TenantId, body.TenantId);
            Assert.False(body.IsExpired);
            Assert.Single(body.RecentRuns);
            Assert.Equal("fp-001", body.RecentRuns[0].AlertFingerprint);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task ExpiredSession_ReturnsOk_IsExpiredTrue_EmptyRuns()
    {
        var sessionId   = Guid.NewGuid();
        var createdAt   = DateTimeOffset.UtcNow.AddHours(-2);
        var expiresAt   = DateTimeOffset.UtcNow.AddHours(-1);   // expired 1 hour ago
        var sessionInfo = new SessionInfo(sessionId, TenantId, createdAt, expiresAt, false);

        var sessionStore = new Mock<ISessionStore>(MockBehavior.Strict);
        sessionStore
            .Setup(s => s.GetIncludingExpiredAsync(sessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sessionInfo);

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        runRepo
            .Setup(r => r.GetRecentRunsBySessionAsync(sessionId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<AgentRun>)Array.Empty<AgentRun>());

        var (app, client) = await CreateTestHost(sessionStore.Object, runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"/session/{sessionId}");
            request.Headers.Add("x-tenant-id", TenantId);
            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<SessionResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.True(body.IsExpired);
            Assert.Empty(body.RecentRuns);
        }
        finally { await app.StopAsync(); }
    }
}

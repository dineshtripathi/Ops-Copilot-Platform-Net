using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using OpsCopilot.AgentRuns.Domain.Entities;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Presentation.Contracts;
using OpsCopilot.AgentRuns.Presentation.Endpoints;
using Xunit;

namespace OpsCopilot.Modules.AgentRuns.Tests;

public sealed class FeedbackEndpointTests
{
    private const string TenantId = "tenant-feedback-test";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static async Task<(WebApplication App, HttpClient Client)> CreateTestHost(
        IAgentRunRepository runRepository)
    {
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        builder.Services.AddSingleton<IAgentRunRepository>(runRepository);

        var app = builder.Build();
        app.MapFeedbackEndpoints();
        await app.StartAsync();

        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task SubmitFeedback_ValidRequest_Returns201WithResponse()
    {
        var runId     = Guid.NewGuid();
        var feedback  = AgentRunFeedback.Create(runId, TenantId, 4, "Looks good");

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        runRepo.Setup(r => r.FeedbackExistsAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        runRepo.Setup(r => r.SaveFeedbackAsync(runId, TenantId, 4, "Looks good", It.IsAny<CancellationToken>()))
               .ReturnsAsync(feedback);

        var (app, client) = await CreateTestHost(runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/agent/runs/{runId}/feedback");
            request.Headers.Add("x-tenant-id", TenantId);
            request.Content = JsonContent.Create(new SubmitFeedbackRequest(4, "Looks good"));

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<RunFeedbackResponse>(JsonOpts);
            Assert.NotNull(body);
            Assert.Equal(runId, body!.RunId);
            Assert.Equal(4,     body.Rating);
            Assert.Equal("Looks good", body.Comment);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task SubmitFeedback_RunNotFound_Returns404()
    {
        var runId = Guid.NewGuid();

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        runRepo.Setup(r => r.FeedbackExistsAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        runRepo.Setup(r => r.SaveFeedbackAsync(runId, TenantId, 3, null, It.IsAny<CancellationToken>()))
               .ThrowsAsync(new InvalidOperationException($"AgentRun {runId} not found."));

        var (app, client) = await CreateTestHost(runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/agent/runs/{runId}/feedback");
            request.Headers.Add("x-tenant-id", TenantId);
            request.Content = JsonContent.Create(new SubmitFeedbackRequest(3, null));

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task SubmitFeedback_DuplicateFeedback_Returns409Conflict()
    {
        var runId = Guid.NewGuid();

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);
        runRepo.Setup(r => r.FeedbackExistsAsync(runId, It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var (app, client) = await CreateTestHost(runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/agent/runs/{runId}/feedback");
            request.Headers.Add("x-tenant-id", TenantId);
            request.Content = JsonContent.Create(new SubmitFeedbackRequest(5, null));

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task SubmitFeedback_InvalidRating_Returns400(int rating)
    {
        var runId = Guid.NewGuid();

        var runRepo = new Mock<IAgentRunRepository>(MockBehavior.Strict);

        var (app, client) = await CreateTestHost(runRepo.Object);
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/agent/runs/{runId}/feedback");
            request.Headers.Add("x-tenant-id", TenantId);
            request.Content = JsonContent.Create(new SubmitFeedbackRequest(rating, null));

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { await app.StopAsync(); }
    }
}

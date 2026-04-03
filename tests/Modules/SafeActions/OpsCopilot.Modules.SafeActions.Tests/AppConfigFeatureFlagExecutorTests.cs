using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="AppConfigFeatureFlagExecutor"/>.
/// Exercises feature gate, payload validation, endpoint format checks,
/// allowlist, success path, HTTP error handling, and rollback
/// — all via a mocked <see cref="IAppConfigFeatureFlagWriter"/> so no live credentials are needed.
/// </summary>
public class AppConfigFeatureFlagExecutorTests
{
    private const string ValidEndpoint = "https://ops-test.azconfig.io";
    private const string ValidFlagId   = "my-feature-flag";

    // ── Helpers ──────────────────────────────────────────────────────

    private static AppConfigFeatureFlagExecutor CreateSut(
        IAppConfigFeatureFlagWriter? writer = null,
        bool enableAppConfigWrite = true,
        string[]? allowedEndpoints = null,
        int timeoutMs = 5_000)
    {
        var dict = new Dictionary<string, string?>
        {
            ["SafeActions:EnableAppConfigWrite"]    = enableAppConfigWrite.ToString(),
            ["SafeActions:AppConfigWriteTimeoutMs"] = timeoutMs.ToString(),
        };

        if (allowedEndpoints is not null)
        {
            for (var i = 0; i < allowedEndpoints.Length; i++)
                dict[$"SafeActions:AllowedAppConfigEndpoints:{i}"] = allowedEndpoints[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new AppConfigFeatureFlagExecutor(
            writer ?? Mock.Of<IAppConfigFeatureFlagWriter>(),
            config,
            NullLogger<AppConfigFeatureFlagExecutor>.Instance);
    }

    private static string Reason(string responseJson)
    {
        var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("reason").GetString() ?? "";
    }

    private static string Payload(string endpoint, string flagId, string enabledLiteral)
        => $"{{\"endpoint\":\"{endpoint}\",\"featureFlagId\":\"{flagId}\",\"enabled\":{enabledLiteral}}}";

    // ── Feature gate ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_AppConfigWrite_Disabled()
    {
        var sut = CreateSut(enableAppConfigWrite: false);
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("APP_CONFIG_WRITE_DISABLED", Reason(result.ResponseJson));
    }

    // ── Payload validation ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Payload_Is_Invalid_Json()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("not-json");

        Assert.False(result.Success);
        Assert.Equal("invalid_json", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Endpoint_Is_Missing()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync($"{{\"featureFlagId\":\"{ValidFlagId}\",\"enabled\":true}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Endpoint_Is_Empty()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(Payload("", ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    // ── Endpoint format validation ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Endpoint_Not_Https()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(Payload("http://ops-test.azconfig.io", ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("invalid_endpoint", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Endpoint_Not_AzconfigIo()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(Payload("https://ops-test.example.com", ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("invalid_endpoint", Reason(result.ResponseJson));
    }

    // ── Feature flag ID validation ────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_FeatureFlagId_Is_Missing()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync($"{{\"endpoint\":\"{ValidEndpoint}\",\"enabled\":true}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    // ── Enabled field validation ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Enabled_Field_Is_Missing()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(
            $"{{\"endpoint\":\"{ValidEndpoint}\",\"featureFlagId\":\"{ValidFlagId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Enabled_Field_Is_String()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "\"true\""));

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    // ── Allowlist ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Endpoint_Not_Allowlisted()
    {
        var sut = CreateSut(allowedEndpoints: ["https://other.azconfig.io"]);
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("endpoint_not_allowlisted", Reason(result.ResponseJson));
    }

    // ── Success path ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Calls_Writer_And_Returns_Success()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetEnabledAsync(ValidEndpoint, ValidFlagId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(false);
        writer.Setup(w => w.SetEnabledAsync(ValidEndpoint, ValidFlagId, true, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "true"));

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("app_config_feature_flag",  doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(ValidEndpoint,               doc.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal(ValidFlagId,                 doc.RootElement.GetProperty("featureFlagId").GetString());
        Assert.True(doc.RootElement.GetProperty("enabled").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("previousEnabled").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("durationMs").GetInt64() >= 0);
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Allows_Allowlisted_Endpoint()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetEnabledAsync(ValidEndpoint, ValidFlagId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(true);
        writer.Setup(w => w.SetEnabledAsync(ValidEndpoint, ValidFlagId, false, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(writer: writer.Object, allowedEndpoints: [ValidEndpoint]);
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "false"));

        Assert.True(result.Success);
        writer.VerifyAll();
    }

    // ── Writer error handling ─────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Writer_Throws_HttpRequestException()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetEnabledAsync(ValidEndpoint, ValidFlagId, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("Simulated App Config error"));

        var sut = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("app_config_api_error", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_On_Timeout()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetEnabledAsync(ValidEndpoint, ValidFlagId, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new TaskCanceledException("Simulated timeout"));

        var sut = CreateSut(writer: writer.Object, timeoutMs: 5_000);
        var result = await sut.ExecuteAsync(Payload(ValidEndpoint, ValidFlagId, "true"));

        Assert.False(result.Success);
        Assert.Equal("timeout", Reason(result.ResponseJson));
    }

    // ── Rollback ──────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Calls_Writer_And_Returns_Success()
    {
        var writer = new Mock<IAppConfigFeatureFlagWriter>(MockBehavior.Strict);
        writer.Setup(w => w.SetEnabledAsync(ValidEndpoint, ValidFlagId, false, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(writer: writer.Object);
        var result = await sut.RollbackAsync(Payload(ValidEndpoint, ValidFlagId, "false"));

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("app_config_feature_flag_rollback", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(ValidEndpoint, doc.RootElement.GetProperty("endpoint").GetString());
        Assert.Equal(ValidFlagId,   doc.RootElement.GetProperty("featureFlagId").GetString());
        Assert.False(doc.RootElement.GetProperty("enabled").GetBoolean());
        writer.VerifyAll();
    }

    [Fact]
    public async Task RollbackAsync_Returns_Failure_When_Disabled()
    {
        var sut = CreateSut(enableAppConfigWrite: false);
        var result = await sut.RollbackAsync(Payload(ValidEndpoint, ValidFlagId, "false"));

        Assert.False(result.Success);
        Assert.Equal("APP_CONFIG_WRITE_DISABLED", Reason(result.ResponseJson));
    }
}

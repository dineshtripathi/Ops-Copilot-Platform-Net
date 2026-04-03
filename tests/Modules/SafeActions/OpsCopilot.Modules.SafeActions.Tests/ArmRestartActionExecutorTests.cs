using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="ArmRestartActionExecutor"/>.
/// Exercises feature gate, payload validation, ARM ID checks, subscription
/// allowlist, success path, HTTP error handling, and rollback — all via a
/// mocked <see cref="IAzureVmWriter"/> so no live Azure credentials are needed.
/// </summary>
public class ArmRestartActionExecutorTests
{
    private const string ValidVmId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test" +
        "/providers/Microsoft.Compute/virtualMachines/vm-1";

    private const string ValidSubscriptionId = "00000000-0000-0000-0000-000000000000";

    // ── Helpers ──────────────────────────────────────────────────────

    private static ArmRestartActionExecutor CreateSut(
        IAzureVmWriter? writer = null,
        bool enableArmWrite = true,
        string[]? allowedSubscriptions = null,
        int timeoutMs = 5_000)
    {
        var dict = new Dictionary<string, string?>
        {
            ["SafeActions:EnableArmWrite"]    = enableArmWrite.ToString(),
            ["SafeActions:ArmWriteTimeoutMs"] = timeoutMs.ToString(),
        };

        if (allowedSubscriptions is not null)
        {
            for (var i = 0; i < allowedSubscriptions.Length; i++)
                dict[$"SafeActions:AllowedAzureSubscriptionIds:{i}"] = allowedSubscriptions[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new ArmRestartActionExecutor(
            writer ?? Mock.Of<IAzureVmWriter>(),
            config,
            NullLogger<ArmRestartActionExecutor>.Instance);
    }

    private static string Reason(string responseJson)
    {
        var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("reason").GetString() ?? "";
    }

    // ── Feature gate ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ArmWrite_Disabled()
    {
        var sut = CreateSut(enableArmWrite: false);
        var result = await sut.ExecuteAsync(
            $"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("ARM_WRITE_DISABLED", Reason(result.ResponseJson));
    }

    // ── Payload validation ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Payload_Is_Invalid_Json()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("not-json-at-all");

        Assert.False(result.Success);
        Assert.Equal("invalid_json", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Is_Missing()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("{}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Is_Empty()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("{\"resourceId\":\"\"}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    // ── ARM ID validation ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Lacks_Subscriptions_Prefix()
    {
        var sut = CreateSut();
        var payload = "{\"resourceId\":\"/resourceGroups/rg-test/providers/Microsoft.Compute/virtualMachines/vm-1\"}";
        var result = await sut.ExecuteAsync(payload);

        Assert.False(result.Success);
        Assert.Equal("invalid_resource_id", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Is_Not_Vm_Type()
    {
        var sut = CreateSut();
        var storageId =
            "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test" +
            "/providers/Microsoft.Storage/storageAccounts/sa1";
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{storageId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_resource_type", Reason(result.ResponseJson));
    }

    // ── Subscription allowlist ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Subscription_Not_Allowlisted()
    {
        var sut = CreateSut(allowedSubscriptions: ["aaaaaaaa-1111-1111-1111-111111111111"]);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("target_not_allowlisted", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Allows_When_Subscription_Is_Allowlisted()
    {
        var writer = new Mock<IAzureVmWriter>(MockBehavior.Strict);
        writer.Setup(w => w.RestartAsync(ValidVmId, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut = CreateSut(writer: writer.Object,
                            allowedSubscriptions: [ValidSubscriptionId]);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.True(result.Success);
        writer.VerifyAll();
    }

    // ── Success path ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Calls_Writer_And_Returns_Success()
    {
        var writer = new Mock<IAzureVmWriter>(MockBehavior.Strict);
        writer.Setup(w => w.RestartAsync(ValidVmId, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut    = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.True(result.Success);

        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("arm_restart", doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(ValidVmId,     doc.RootElement.GetProperty("resourceId").GetString());
        writer.VerifyAll();
    }

    // ── Error handling ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Writer_Throws_HttpRequestException()
    {
        var writer = new Mock<IAzureVmWriter>(MockBehavior.Strict);
        writer.Setup(w => w.RestartAsync(ValidVmId, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("ARM 503 Service Unavailable"));

        var sut    = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("arm_api_error", Reason(result.ResponseJson));
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_On_Timeout()
    {
        var writer = new Mock<IAzureVmWriter>(MockBehavior.Strict);
        writer.Setup(w => w.RestartAsync(ValidVmId, It.IsAny<CancellationToken>()))
              .Returns<string, CancellationToken>(async (_, ct) =>
              {
                  await Task.Delay(Timeout.Infinite, ct); // blocks until token is cancelled
              });

        // 10 ms timeout forces cancellation before the delay completes
        var sut    = CreateSut(writer: writer.Object, timeoutMs: 10);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("timeout", Reason(result.ResponseJson));
    }

    // ── Rollback ──────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_Not_Supported()
    {
        var sut    = CreateSut();
        var result = await sut.RollbackAsync($"{{\"resourceId\":\"{ValidVmId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("ROLLBACK_NOT_SUPPORTED", Reason(result.ResponseJson));
    }
}

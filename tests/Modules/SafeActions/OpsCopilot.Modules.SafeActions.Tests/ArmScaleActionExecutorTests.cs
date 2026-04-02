using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="ArmScaleActionExecutor"/>.
/// Exercises feature gate, payload validation, ARM ID checks, subscription
/// allowlist, capacity limits, success path, HTTP error handling, and rollback
/// — all via a mocked <see cref="IAzureScaleWriter"/> so no live Azure credentials are needed.
/// </summary>
public class ArmScaleActionExecutorTests
{
    private const string ValidVmssId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test" +
        "/providers/Microsoft.Compute/virtualMachineScaleSets/vmss-1";

    private const string ValidSubscriptionId = "00000000-0000-0000-0000-000000000000";

    // ── Helpers ──────────────────────────────────────────────────────

    private static ArmScaleActionExecutor CreateSut(
        IAzureScaleWriter? writer = null,
        bool enableArmWrite = true,
        string[]? allowedSubscriptions = null,
        int timeoutMs = 5_000,
        int maxCapacity = 100)
    {
        var dict = new Dictionary<string, string?>
        {
            ["SafeActions:EnableArmWrite"]      = enableArmWrite.ToString(),
            ["SafeActions:ArmWriteTimeoutMs"]   = timeoutMs.ToString(),
            ["SafeActions:MaxArmScaleCapacity"] = maxCapacity.ToString(),
        };

        if (allowedSubscriptions is not null)
        {
            for (var i = 0; i < allowedSubscriptions.Length; i++)
                dict[$"SafeActions:AllowedAzureSubscriptionIds:{i}"] = allowedSubscriptions[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new ArmScaleActionExecutor(
            writer ?? Mock.Of<IAzureScaleWriter>(),
            config,
            NullLogger<ArmScaleActionExecutor>.Instance);
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
            $"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":3}}");

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
        var result = await sut.ExecuteAsync("{\"targetCapacity\":3}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Is_Empty()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync("{\"resourceId\":\"\",\"targetCapacity\":3}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    // ── ARM ID validation ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Lacks_Subscriptions_Prefix()
    {
        var sut = CreateSut();
        var payload = "{\"resourceId\":\"/resourceGroups/rg-test/providers/Microsoft.Compute/virtualMachineScaleSets/vmss-1\",\"targetCapacity\":3}";
        var result = await sut.ExecuteAsync(payload);

        Assert.False(result.Success);
        Assert.Equal("invalid_resource_id", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_ResourceId_Is_Not_Vmss_Type()
    {
        var sut = CreateSut();
        var storageId =
            "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test" +
            "/providers/Microsoft.Storage/storageAccounts/sa1";
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{storageId}\",\"targetCapacity\":3}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_resource_type", Reason(result.ResponseJson));
    }

    // ── Subscription allowlist ────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Subscription_Not_Allowlisted()
    {
        var sut = CreateSut(allowedSubscriptions: ["aaaaaaaa-1111-1111-1111-111111111111"]);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":3}}");

        Assert.False(result.Success);
        Assert.Equal("target_not_allowlisted", Reason(result.ResponseJson));
    }

    // ── Capacity guards ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_TargetCapacity_Is_Missing()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_TargetCapacity_Is_Negative()
    {
        var sut = CreateSut();
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":-1}}");

        Assert.False(result.Success);
        Assert.Equal("invalid_payload", Reason(result.ResponseJson));
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_TargetCapacity_Exceeds_Max()
    {
        var sut = CreateSut(maxCapacity: 10);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":11}}");

        Assert.False(result.Success);
        Assert.Equal("capacity_exceeded", Reason(result.ResponseJson));
    }

    // ── Success path ──────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Calls_Writer_And_Returns_Success()
    {
        var writer = new Mock<IAzureScaleWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetCapacityAsync(ValidVmssId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(2);
        writer.Setup(w => w.SetCapacityAsync(ValidVmssId, 5, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut    = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":5}}");

        Assert.True(result.Success);

        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal("arm_scale",  doc.RootElement.GetProperty("mode").GetString());
        Assert.Equal(ValidVmssId,  doc.RootElement.GetProperty("resourceId").GetString());
        Assert.Equal(2,            doc.RootElement.GetProperty("previousCapacity").GetInt32());
        Assert.Equal(5,            doc.RootElement.GetProperty("targetCapacity").GetInt32());
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Allows_Zero_Capacity()
    {
        var writer = new Mock<IAzureScaleWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetCapacityAsync(ValidVmssId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(3);
        writer.Setup(w => w.SetCapacityAsync(ValidVmssId, 0, It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);

        var sut    = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":0}}");

        Assert.True(result.Success);
        var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal(0, doc.RootElement.GetProperty("targetCapacity").GetInt32());
        writer.VerifyAll();
    }

    // ── Error handling ────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_When_Writer_Throws_HttpRequestException()
    {
        var writer = new Mock<IAzureScaleWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetCapacityAsync(ValidVmssId, It.IsAny<CancellationToken>()))
              .ThrowsAsync(new HttpRequestException("ARM 503 Service Unavailable"));

        var sut    = CreateSut(writer: writer.Object);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":5}}");

        Assert.False(result.Success);
        Assert.Equal("arm_api_error", Reason(result.ResponseJson));
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Failure_On_Timeout()
    {
        var writer = new Mock<IAzureScaleWriter>(MockBehavior.Strict);
        writer.Setup(w => w.GetCapacityAsync(ValidVmssId, It.IsAny<CancellationToken>()))
              .Returns<string, CancellationToken>(async (_, ct) =>
              {
                  await Task.Delay(Timeout.Infinite, ct);
                  return 0;
              });

        // 10 ms timeout forces cancellation before the delay completes
        var sut    = CreateSut(writer: writer.Object, timeoutMs: 10);
        var result = await sut.ExecuteAsync($"{{\"resourceId\":\"{ValidVmssId}\",\"targetCapacity\":5}}");

        Assert.False(result.Success);
        Assert.Equal("timeout", Reason(result.ResponseJson));
    }

    // ── Rollback ──────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_Not_Supported()
    {
        var sut    = CreateSut();
        var result = await sut.RollbackAsync($"{{\"resourceId\":\"{ValidVmssId}\"}}");

        Assert.False(result.Success);
        Assert.Equal("ROLLBACK_NOT_SUPPORTED", Reason(result.ResponseJson));
    }
}

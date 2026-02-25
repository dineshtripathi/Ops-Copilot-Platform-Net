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
/// Unit tests for <see cref="AzureResourceGetActionExecutor"/>.
/// Exercises all error codes, payload validation, timeout, and the success path
/// using a mock <see cref="IAzureResourceReader"/>.
/// </summary>
public class AzureResourceGetActionExecutorTests
{
    private const string ValidResourceId =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-test/providers/Microsoft.Compute/virtualMachines/vm-1";

    // ── Helpers ──────────────────────────────────────────────────────

    private static AzureResourceGetActionExecutor CreateSut(
        IAzureResourceReader reader, int timeoutMs = 5000,
        string[]? allowedSubscriptions = null)
    {
        var dict = new Dictionary<string, string?>
        {
            ["SafeActions:AzureReadTimeoutMs"] = timeoutMs.ToString(),
        };

        if (allowedSubscriptions is not null)
        {
            for (var i = 0; i < allowedSubscriptions.Length; i++)
                dict[$"SafeActions:AllowedAzureSubscriptionIds:{i}"] = allowedSubscriptions[i];
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();

        return new AzureResourceGetActionExecutor(
            reader, config, NullLogger<AzureResourceGetActionExecutor>.Instance);
    }

    private static Mock<IAzureResourceReader> CreateReaderMock(
        AzureResourceMetadata? metadata = null)
    {
        var mock = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        if (metadata is not null)
        {
            mock.Setup(r => r.GetResourceMetadataAsync(
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(metadata);
        }
        return mock;
    }

    private static string MakePayload(string resourceId) =>
        JsonSerializer.Serialize(new { resourceId });

    // ── Success ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Success_With_Metadata()
    {
        var metadata = new AzureResourceMetadata(
            "vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", "etag-abc", 3);

        var reader = CreateReaderMock(metadata);
        var sut = CreateSut(reader.Object);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.True(result.Success);
        Assert.True(result.DurationMs >= 0);

        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("azure_resource_get", root.GetProperty("mode").GetString());
        Assert.Equal("vm-1", root.GetProperty("name").GetString());
        Assert.Equal("Microsoft.Compute/virtualMachines", root.GetProperty("resourceType").GetString());
        Assert.Equal("eastus", root.GetProperty("location").GetString());
        Assert.Equal("Succeeded", root.GetProperty("provisioningState").GetString());
        Assert.Equal("etag-abc", root.GetProperty("etag").GetString());
        Assert.Equal(3, root.GetProperty("tagsCount").GetInt32());
        Assert.Equal(ValidResourceId, root.GetProperty("resourceId").GetString());

        reader.Verify(r => r.GetResourceMetadataAsync(
            ValidResourceId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Payload validation ───────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidJson_For_NonJsonPayload()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("<<<not json>>>");

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_json");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_MissingResourceId()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("""{ "target": "abc" }""");

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_EmptyResourceId()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("""{ "resourceId": "" }""");

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_InvalidPayload_For_WhitespaceResourceId()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("""{ "resourceId": "   " }""");

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_payload");
    }

    [Theory]
    [InlineData("vm-1")]
    [InlineData("resourceGroups/rg-test")]
    [InlineData("https://management.azure.com/subscriptions/123")]
    public async Task ExecuteAsync_Returns_InvalidResourceId_For_BadFormat(string badId)
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync(MakePayload(badId));

        Assert.False(result.Success);
        AssertErrorCode(result, "invalid_resource_id");
    }

    // ── Azure SDK error mapping ──────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_AuthFailed_On_AuthenticationFailedException()
    {
        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AuthenticationFailedException("bad creds"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_auth_failed");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_Forbidden_On_403_RequestFailedException()
    {
        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "forbidden"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_forbidden");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_NotFound_On_404_RequestFailedException()
    {
        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "not found"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_RequestFailed_On_Other_RequestFailedException()
    {
        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(500, "server error"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_request_failed");
    }

    [Fact]
    public async Task ExecuteAsync_Returns_UnexpectedError_On_GenericException()
    {
        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var sut = CreateSut(reader.Object);
        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "unexpected_error");
    }

    // ── Timeout ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_Returns_Timeout_On_SlowReader()
    {
        var reader = new Mock<IAzureResourceReader>(MockBehavior.Strict);
        reader.Setup(r => r.GetResourceMetadataAsync(
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct); // Will be cancelled
                return null!;
            });

        // Very short timeout to trigger cancel quickly
        var sut = CreateSut(reader.Object, timeoutMs: 50);
        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "azure_timeout");
    }

    // ── Rollback ─────────────────────────────────────────────────────

    [Fact]
    public async Task RollbackAsync_Returns_NotSupported()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.RollbackAsync("{}");

        Assert.False(result.Success);
        Assert.Equal(0, result.DurationMs);

        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("azure_resource_get", root.GetProperty("mode").GetString());
        Assert.Contains("not supported", root.GetProperty("reason").GetString());
    }

    // ── Response shape: every failure has mode + reason + resourceId ─

    [Fact]
    public async Task ExecuteAsync_Failure_Response_Always_Has_Mode_And_Reason()
    {
        var sut = CreateSut(CreateReaderMock().Object);

        var result = await sut.ExecuteAsync("<<<bad>>>");

        Assert.False(result.Success);
        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("azure_resource_get", root.GetProperty("mode").GetString());
        Assert.True(root.TryGetProperty("reason", out _));
        Assert.True(root.TryGetProperty("detail", out _));
        Assert.True(root.TryGetProperty("durationMs", out _));
    }

    // ── Assert helper ────────────────────────────────────────────────

    private static void AssertErrorCode(
        ActionExecutionResult result, string expectedReason)
    {
        using var doc = JsonDocument.Parse(result.ResponseJson);
        Assert.Equal(expectedReason,
            doc.RootElement.GetProperty("reason").GetString());
    }

    // ── Subscription allowlist ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EmptyAllowlist_AllowsAll()
    {
        var metadata = new AzureResourceMetadata("vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", null, 0);
        var sut = CreateSut(CreateReaderMock(metadata).Object,
            allowedSubscriptions: []);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NoAllowlistConfig_AllowsAll()
    {
        var metadata = new AzureResourceMetadata("vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", null, 0);
        var sut = CreateSut(CreateReaderMock(metadata).Object);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_AllowlistedSubscription_Succeeds()
    {
        var metadata = new AzureResourceMetadata("vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", null, 0);
        var sut = CreateSut(CreateReaderMock(metadata).Object,
            allowedSubscriptions: ["00000000-0000-0000-0000-000000000000"]);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NonAllowlistedSubscription_Returns_TargetNotAllowlisted()
    {
        var sut = CreateSut(CreateReaderMock().Object,
            allowedSubscriptions: ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        AssertErrorCode(result, "target_not_allowlisted");
    }

    [Fact]
    public async Task ExecuteAsync_SubscriptionAllowlist_CaseInsensitive()
    {
        var metadata = new AzureResourceMetadata("vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", null, 0);
        var upperCaseResourceId =
            "/subscriptions/AABBCCDD-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Compute/virtualMachines/vm";
        var sut = CreateSut(CreateReaderMock(metadata).Object,
            allowedSubscriptions: ["aabbccdd-0000-0000-0000-000000000000"]);

        var result = await sut.ExecuteAsync(MakePayload(upperCaseResourceId));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleAllowedSubscriptions_AcceptsAny()
    {
        var metadata = new AzureResourceMetadata("vm-1", "Microsoft.Compute/virtualMachines",
            "eastus", "Succeeded", null, 0);
        var sut = CreateSut(CreateReaderMock(metadata).Object,
            allowedSubscriptions:
            [
                "11111111-1111-1111-1111-111111111111",
                "00000000-0000-0000-0000-000000000000",
            ]);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NonAllowlistedSubscription_IncludesResourceIdInResponse()
    {
        var sut = CreateSut(CreateReaderMock().Object,
            allowedSubscriptions: ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]);

        var result = await sut.ExecuteAsync(MakePayload(ValidResourceId));

        Assert.False(result.Success);
        using var doc = JsonDocument.Parse(result.ResponseJson);
        var root = doc.RootElement;
        Assert.Equal("target_not_allowlisted", root.GetProperty("reason").GetString());
        Assert.Equal(ValidResourceId, root.GetProperty("resourceId").GetString());
    }
}

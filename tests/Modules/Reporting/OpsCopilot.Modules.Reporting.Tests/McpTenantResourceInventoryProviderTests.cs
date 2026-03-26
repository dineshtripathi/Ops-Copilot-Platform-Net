using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Reporting.Infrastructure;
using OpsCopilot.Reporting.Infrastructure.McpClient;
using Xunit;

namespace OpsCopilot.Modules.Reporting.Tests;

/// <summary>
/// Unit tests for <see cref="McpTenantResourceInventoryProvider"/>.
/// All MCP tool calls are intercepted via a mock <see cref="IReportingMcpHostClient"/>.
/// </summary>
public sealed class McpTenantResourceInventoryProviderTests
{
    private const string TenantId = "tenant-test";

    private static McpTenantResourceInventoryProvider CreateSut(IReportingMcpHostClient client) =>
        new(client, NullLogger<McpTenantResourceInventoryProvider>.Instance);

    // ── list_subscriptions failures ────────────────────────────────────────

    [Fact]
    public async Task GetInventoryAsync_WhenSubscriptionsToolReturnsNonJson_ReturnsNull()
    {
        var client = new Mock<IReportingMcpHostClient>(MockBehavior.Strict);
        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("not-valid-json");

        var result = await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetInventoryAsync_WhenSubscriptionsToolReturnsOkFalse_ReturnsNull()
    {
        var client = new Mock<IReportingMcpHostClient>(MockBehavior.Strict);
        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":false,"error":"unauthorized"}""");

        var result = await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetInventoryAsync_WhenSubscriptionsEmptyArray_ReturnsNull()
    {
        var client = new Mock<IReportingMcpHostClient>(MockBehavior.Strict);
        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"subscriptions":[]}""");

        var result = await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        Assert.Null(result);
    }

    // ── successful full round-trip ──────────────────────────────────────────

    [Fact]
    public async Task GetInventoryAsync_WhenAllToolsSucceed_MapsToInventoryCorrectly()
    {
        var client = new Mock<IReportingMcpHostClient>(MockBehavior.Strict);

        // list_subscriptions
        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"subscriptions":[{"subscriptionId":"sub-1","displayName":"My Sub","state":"Enabled"}]}""");

        // list_resource_groups
        client.Setup(c => c.CallToolAsync("list_resource_groups", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"resourceGroups":[{"subscriptionId":"sub-1","name":"rg-prod","location":"eastus","provisioningState":"Succeeded"}]}""");

        // list_app_insights
        client.Setup(c => c.CallToolAsync("list_app_insights", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"components":[{"subscriptionId":"sub-1","resourceGroup":"rg-prod","name":"myai","location":"eastus","kind":"web","workspaceResourceId":"/workspaces/law-1"}]}""");

        // list_log_analytics_workspaces
        client.Setup(c => c.CallToolAsync("list_log_analytics_workspaces", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"workspaces":[{"subscriptionId":"sub-1","resourceGroup":"rg-prod","name":"law-1","location":"eastus","customerId":"abc-123","retentionInDays":"90","sku":"PerGB2018"}]}""");

        var result = await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        Assert.NotNull(result);
        Assert.Equal(TenantId, result.TenantId);

        Assert.Single(result.ResourceGroups);
        Assert.Equal("rg-prod",    result.ResourceGroups[0].Name);
        Assert.Equal("eastus",     result.ResourceGroups[0].Location);
        Assert.Equal("Succeeded",  result.ResourceGroups[0].ProvisioningState);

        Assert.Single(result.AppInsightsComponents);
        Assert.Equal("myai",       result.AppInsightsComponents[0].Name);
        Assert.Equal("rg-prod",    result.AppInsightsComponents[0].ResourceGroup);
        Assert.Equal("web",        result.AppInsightsComponents[0].Kind);
        Assert.Equal("/workspaces/law-1", result.AppInsightsComponents[0].WorkspaceResourceId);

        Assert.Single(result.LogAnalyticsWorkspaces);
        Assert.Equal("law-1",      result.LogAnalyticsWorkspaces[0].Name);
        Assert.Equal("abc-123",    result.LogAnalyticsWorkspaces[0].CustomerId);
        Assert.Equal(90,           result.LogAnalyticsWorkspaces[0].RetentionInDays);
        Assert.Equal("PerGB2018",  result.LogAnalyticsWorkspaces[0].Sku);
    }

    // ── downstream tool failures return empty lists, not null ──────────────

    [Fact]
    public async Task GetInventoryAsync_WhenResourceGroupsToolFails_ReturnsInventoryWithEmptyList()
    {
        var client = new Mock<IReportingMcpHostClient>(MockBehavior.Strict);

        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"subscriptions":[{"subscriptionId":"sub-1"}]}""");

        client.Setup(c => c.CallToolAsync("list_resource_groups", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":false,"error":"access denied"}""");

        client.Setup(c => c.CallToolAsync("list_app_insights", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"components":[]}""");

        client.Setup(c => c.CallToolAsync("list_log_analytics_workspaces", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"workspaces":[]}""");

        var result = await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        Assert.NotNull(result);
        Assert.Empty(result.ResourceGroups);
        Assert.Empty(result.AppInsightsComponents);
        Assert.Empty(result.LogAnalyticsWorkspaces);
    }

    [Fact]
    public async Task GetInventoryAsync_WhenAppInsightsToolFails_ReturnsInventoryWithEmptyAiList()
    {
        var client = new Mock<IReportingMcpHostClient>(MockBehavior.Strict);

        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"subscriptions":[{"subscriptionId":"sub-1"}]}""");

        client.Setup(c => c.CallToolAsync("list_resource_groups", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"resourceGroups":[{"subscriptionId":"sub-1","name":"rg-1","location":"westus","provisioningState":"Succeeded"}]}""");

        client.Setup(c => c.CallToolAsync("list_app_insights", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("bad-json-for-ai");

        client.Setup(c => c.CallToolAsync("list_log_analytics_workspaces", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"workspaces":[]}""");

        var result = await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        Assert.NotNull(result);
        Assert.Single(result.ResourceGroups);
        Assert.Empty(result.AppInsightsComponents);
        Assert.Empty(result.LogAnalyticsWorkspaces);
    }

    // ── subscriptionIds passed correctly ──────────────────────────────────

    [Fact]
    public async Task GetInventoryAsync_PassesSubscriptionIdsToResourceTools()
    {
        var client = new Mock<IReportingMcpHostClient>();

        client.Setup(c => c.CallToolAsync("list_subscriptions", It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"subscriptions":[{"subscriptionId":"sub-aaa"},{"subscriptionId":"sub-bbb"}]}""");

        client.Setup(c => c.CallToolAsync(It.IsIn("list_resource_groups", "list_app_insights", "list_log_analytics_workspaces"),
                                          It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync("""{"ok":true,"resourceGroups":[],"components":[],"workspaces":[]}""");

        await CreateSut(client.Object).GetInventoryAsync(TenantId, default);

        client.Verify(c => c.CallToolAsync(
            It.IsIn("list_resource_groups", "list_app_insights", "list_log_analytics_workspaces"),
            It.Is<Dictionary<string, object?>>(d =>
                d.ContainsKey("subscriptionIds") &&
                (d["subscriptionIds"] as string)!.Contains("sub-aaa") &&
                (d["subscriptionIds"] as string)!.Contains("sub-bbb")),
            It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }
}

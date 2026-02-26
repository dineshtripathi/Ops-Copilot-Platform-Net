using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Application.Services;
using OpsCopilot.Connectors.Infrastructure.Connectors;
using OpsCopilot.Connectors.Infrastructure.Extensions;

namespace OpsCopilot.Modules.Connectors.Tests;

/// <summary>
/// Tests for Slice 26: Connectors MVP — registry, resolution, and concrete connectors.
/// </summary>
public class ConnectorTests
{
    // ── Helper ──────────────────────────────────────────────────

    private static IConnectorRegistry BuildRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConnectorsModule();
        using var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<IConnectorRegistry>();
    }

    // ── 1. Get observability connector by name (AC-2) ───────────

    [Fact]
    public void Registry_GetObservabilityConnector_ReturnsAzureMonitor()
    {
        var registry = BuildRegistry();
        var connector = registry.GetObservabilityConnector("azure-monitor");

        Assert.NotNull(connector);
        Assert.Equal("azure-monitor", connector.Descriptor.Name);
        Assert.Equal(ConnectorKind.Observability, connector.Descriptor.Kind);
    }

    // ── 2. Get runbook connector by name (AC-2) ─────────────────

    [Fact]
    public void Registry_GetRunbookConnector_ReturnsInMemoryRunbook()
    {
        var registry = BuildRegistry();
        var connector = registry.GetRunbookConnector("in-memory-runbook");

        Assert.NotNull(connector);
        Assert.Equal("in-memory-runbook", connector.Descriptor.Name);
        Assert.Equal(ConnectorKind.Runbook, connector.Descriptor.Kind);
    }

    // ── 3. Get action-target connector by name (AC-2) ───────────

    [Fact]
    public void Registry_GetActionTargetConnector_ReturnsStaticActionTarget()
    {
        var registry = BuildRegistry();
        var connector = registry.GetActionTargetConnector("static-action-target");

        Assert.NotNull(connector);
        Assert.Equal("static-action-target", connector.Descriptor.Name);
        Assert.Equal(ConnectorKind.ActionTarget, connector.Descriptor.Kind);
    }

    // ── 4–6. Unknown names return null (AC-5) ───────────────────

    [Fact]
    public void Registry_GetObservabilityConnector_UnknownName_ReturnsNull()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.GetObservabilityConnector("does-not-exist"));
    }

    [Fact]
    public void Registry_GetRunbookConnector_UnknownName_ReturnsNull()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.GetRunbookConnector("does-not-exist"));
    }

    [Fact]
    public void Registry_GetActionTargetConnector_UnknownName_ReturnsNull()
    {
        var registry = BuildRegistry();
        Assert.Null(registry.GetActionTargetConnector("does-not-exist"));
    }

    // ── 7. Case-insensitive lookup (AC-3) ───────────────────────

    [Theory]
    [InlineData("AZURE-MONITOR")]
    [InlineData("Azure-Monitor")]
    [InlineData("azure-monitor")]
    public void Registry_Lookup_IsCaseInsensitive(string name)
    {
        var registry = BuildRegistry();
        var connector = registry.GetObservabilityConnector(name);

        Assert.NotNull(connector);
        Assert.Equal("azure-monitor", connector.Descriptor.Name);
    }

    // ── 8. ListAll returns all 3 connectors (AC-6) ─────────────

    [Fact]
    public void Registry_ListAll_ReturnsAllRegisteredConnectors()
    {
        var registry = BuildRegistry();
        var all = registry.ListAll();

        Assert.Equal(3, all.Count);
    }

    // ── 9. ListByKind filters correctly (AC-6) ──────────────────

    [Theory]
    [InlineData(ConnectorKind.Observability, 1)]
    [InlineData(ConnectorKind.Runbook, 1)]
    [InlineData(ConnectorKind.ActionTarget, 1)]
    public void Registry_ListByKind_ReturnsCorrectCount(ConnectorKind kind, int expectedCount)
    {
        var registry = BuildRegistry();
        var filtered = registry.ListByKind(kind);

        Assert.Equal(expectedCount, filtered.Count);
        Assert.All(filtered, d => Assert.Equal(kind, d.Kind));
    }

    // ── 10. AzureMonitor CanQuery supported / unsupported (AC-7) ─

    [Theory]
    [InlineData("log-query", true)]
    [InlineData("metric-query", true)]
    [InlineData("alert-read", true)]
    [InlineData("unsupported-query", false)]
    public void AzureMonitor_CanQuery_ReturnsExpected(string queryType, bool expected)
    {
        var connector = new AzureMonitorObservabilityConnector();
        Assert.Equal(expected, connector.CanQuery(queryType));
    }

    // ── 11. InMemoryRunbook CanSearch (AC-7) ────────────────────

    [Theory]
    [InlineData("markdown", true)]
    [InlineData("plain-text", true)]
    [InlineData("unsupported", false)]
    public void InMemoryRunbook_CanSearch_ReturnsExpected(string contentType, bool expected)
    {
        var connector = new InMemoryRunbookConnector();
        Assert.Equal(expected, connector.CanSearch(contentType));
    }

    // ── 12. StaticActionTarget SupportsActionType (AC-7) ────────

    [Theory]
    [InlineData("restart-service", true)]
    [InlineData("scale-resource", true)]
    [InlineData("run-diagnostic", true)]
    [InlineData("toggle-feature-flag", true)]
    [InlineData("unsupported-action", false)]
    public void StaticActionTarget_SupportsActionType_ReturnsExpected(string actionType, bool expected)
    {
        var connector = new StaticActionTargetConnector();
        Assert.Equal(expected, connector.SupportsActionType(actionType));
    }

    // ── 13. ConnectorDescriptor shape (AC-1) ────────────────────

    [Fact]
    public void ConnectorDescriptor_HasExpectedProperties()
    {
        var connector = new AzureMonitorObservabilityConnector();
        var d = connector.Descriptor;

        Assert.False(string.IsNullOrWhiteSpace(d.Name));
        Assert.Equal(ConnectorKind.Observability, d.Kind);
        Assert.False(string.IsNullOrWhiteSpace(d.Description));
        Assert.NotNull(d.Capabilities);
        Assert.NotEmpty(d.Capabilities);
    }

    // ── 14. Empty registry returns no connectors (AC-5) ─────────

    [Fact]
    public void Registry_NoConnectors_ListAllIsEmpty()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IConnectorRegistry>();
        Assert.Empty(registry.ListAll());
    }

    // ── 15. DI resolves IConnectorRegistry (AC-4) ───────────────

    [Fact]
    public void DI_AddConnectorsModule_ResolvesRegistry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddConnectorsModule();
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IConnectorRegistry>();
        Assert.NotNull(registry);
        Assert.IsType<ConnectorRegistry>(registry);
    }

    // ── 16. Duplicate registration: last-wins, no throw (AC-3) ──

    [Fact]
    public void Registry_DuplicateNames_DoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IObservabilityConnector, AzureMonitorObservabilityConnector>();
        services.AddSingleton<IObservabilityConnector, AzureMonitorObservabilityConnector>();
        services.AddSingleton<IConnectorRegistry, ConnectorRegistry>();
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IConnectorRegistry>();
        var connector = registry.GetObservabilityConnector("azure-monitor");

        Assert.NotNull(connector);
    }

    // ── 17. CanQuery is case-insensitive on connector (AC-3) ────

    [Fact]
    public void AzureMonitor_CanQuery_IsCaseInsensitive()
    {
        var connector = new AzureMonitorObservabilityConnector();

        Assert.True(connector.CanQuery("LOG-QUERY"));
        Assert.True(connector.CanQuery("Log-Query"));
        Assert.True(connector.CanQuery("log-query"));
    }
}

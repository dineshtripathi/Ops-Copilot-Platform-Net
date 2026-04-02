using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Tenancy.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Tenancy.Tests;

/// <summary>Slice 199 — unit tests for <see cref="LiveConnectorHealthValidator"/>.</summary>
public sealed class LiveConnectorHealthValidatorTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static ConnectorDescriptor Descriptor(string name) =>
        new(name, ConnectorKind.Runbook, "test connector", []);

    private static ConnectorHealthReport HealthyReport(string name) =>
        new(name, IsHealthy: true, DateTimeOffset.UtcNow);

    private static ConnectorHealthReport UnhealthyReport(string name) =>
        new(name, IsHealthy: false, DateTimeOffset.UtcNow, "no credential");

    private static LiveConnectorHealthValidator Build(
        Mock<IConnectorRegistry> registry,
        Mock<IConnectorHealthCheck> healthCheck) =>
        new(registry.Object, healthCheck.Object,
            NullLogger<LiveConnectorHealthValidator>.Instance);

    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_EmptyRegistry_ReturnsEmptyList()
    {
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll()).Returns([]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);

        var sut = Build(registry, healthCheck);
        var result = await sut.ValidateConnectorsAsync(TenantId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateAsync_AllHealthy_ReturnsEmptyList()
    {
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll()).Returns([Descriptor("A"), Descriptor("B")]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);
        healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), "A", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(HealthyReport("A"));
        healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), "B", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(HealthyReport("B"));

        var sut = Build(registry, healthCheck);
        var result = await sut.ValidateConnectorsAsync(TenantId);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ValidateAsync_OneUnhealthy_ReturnsConnectorName()
    {
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll()).Returns([Descriptor("OK"), Descriptor("FAIL")]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);
        healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), "OK", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(HealthyReport("OK"));
        healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), "FAIL", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(UnhealthyReport("FAIL"));

        var sut = Build(registry, healthCheck);
        var result = await sut.ValidateConnectorsAsync(TenantId);

        var name = Assert.Single(result);
        Assert.Equal("FAIL", name);
    }

    [Fact]
    public async Task ValidateAsync_AllUnhealthy_ReturnsAllNames()
    {
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll())
                .Returns([Descriptor("X"), Descriptor("Y"), Descriptor("Z")]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);
        foreach (var name in new[] { "X", "Y", "Z" })
        {
            var n = name;
            healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), n, It.IsAny<CancellationToken>()))
                       .ReturnsAsync(UnhealthyReport(n));
        }

        var sut = Build(registry, healthCheck);
        var result = await sut.ValidateConnectorsAsync(TenantId);

        Assert.Equal(3, result.Count);
        Assert.Contains("X", result);
        Assert.Contains("Y", result);
        Assert.Contains("Z", result);
    }

    [Fact]
    public async Task ValidateAsync_HealthCheckThrows_IncludesConnectorAsUnhealthy()
    {
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll()).Returns([Descriptor("BOOM"), Descriptor("OK")]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);
        healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), "BOOM", It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new InvalidOperationException("connection refused"));
        healthCheck.Setup(h => h.CheckAsync(TenantId.ToString(), "OK", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(HealthyReport("OK"));

        var sut = Build(registry, healthCheck);
        // Must not propagate — exception is caught and treated as unhealthy
        var result = await sut.ValidateConnectorsAsync(TenantId);

        var name = Assert.Single(result);
        Assert.Equal("BOOM", name);
    }

    [Fact]
    public async Task ValidateAsync_PassesTenantIdAsString()
    {
        var specificId = Guid.Parse("ABCDEF12-1234-5678-9ABC-DEF012345678");
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll()).Returns([Descriptor("C")]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);
        healthCheck.Setup(h => h.CheckAsync(specificId.ToString(), "C", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(HealthyReport("C"))
                   .Verifiable();

        var sut = Build(registry, healthCheck);
        await sut.ValidateConnectorsAsync(specificId);

        healthCheck.VerifyAll();
    }

    [Fact]
    public async Task ValidateAsync_ReturnsReadOnlyList()
    {
        var registry = new Mock<IConnectorRegistry>(MockBehavior.Strict);
        registry.Setup(r => r.ListAll()).Returns([]);

        var healthCheck = new Mock<IConnectorHealthCheck>(MockBehavior.Strict);

        var sut = Build(registry, healthCheck);
        var result = await sut.ValidateConnectorsAsync(TenantId);

        Assert.IsAssignableFrom<IReadOnlyList<string>>(result);
    }
}

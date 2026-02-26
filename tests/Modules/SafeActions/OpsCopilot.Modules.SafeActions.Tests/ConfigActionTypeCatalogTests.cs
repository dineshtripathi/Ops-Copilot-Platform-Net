using Microsoft.Extensions.Configuration;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Infrastructure.Policies;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

public class ConfigActionTypeCatalogTests
{
    // ─── Helpers ──────────────────────────────────────────────────────

    private static ConfigActionTypeCatalog CreateCatalog(
        Dictionary<string, string?>? data = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(data ?? [])
            .Build();

        return new ConfigActionTypeCatalog(config);
    }

    // ─── IsAllowlisted ───────────────────────────────────────────────

    [Fact]
    public void IsAllowlisted_EmptyConfig_ReturnsTrue_AllowAll()
    {
        var catalog = CreateCatalog();

        Assert.True(catalog.IsAllowlisted("anything"));
        Assert.True(catalog.IsAllowlisted("restart_pod"));
    }

    [Fact]
    public void IsAllowlisted_PopulatedConfig_KnownEnabledType_ReturnsTrue()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
        });

        Assert.True(catalog.IsAllowlisted("restart_pod"));
    }

    [Fact]
    public void IsAllowlisted_PopulatedConfig_UnknownType_ReturnsFalse()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
        });

        Assert.False(catalog.IsAllowlisted("delete_everything"));
    }

    [Fact]
    public void IsAllowlisted_PopulatedConfig_DisabledType_ReturnsFalse()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "false",
        });

        Assert.False(catalog.IsAllowlisted("restart_pod"));
    }

    [Fact]
    public void IsAllowlisted_CaseInsensitive_ReturnsTrue()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "Restart_Pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
        });

        Assert.True(catalog.IsAllowlisted("restart_pod"));
        Assert.True(catalog.IsAllowlisted("RESTART_POD"));
    }

    // ─── Get ─────────────────────────────────────────────────────────

    [Fact]
    public void Get_KnownType_ReturnsCorrectDefinition()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
        });

        var def = catalog.Get("restart_pod");

        Assert.NotNull(def);
        Assert.Equal("restart_pod", def.ActionType);
        Assert.Equal(ActionRiskTier.High, def.RiskTier);
        Assert.True(def.Enabled);
    }

    [Fact]
    public void Get_UnknownType_ReturnsNull()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
        });

        Assert.Null(catalog.Get("no_such_type"));
    }

    [Fact]
    public void Get_EmptyConfig_ReturnsNull()
    {
        var catalog = CreateCatalog();

        Assert.Null(catalog.Get("restart_pod"));
    }

    // ─── List ────────────────────────────────────────────────────────

    [Fact]
    public void List_ReturnsAllDefinitions()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
            ["SafeActions:ActionTypes:1:ActionType"] = "http_probe",
            ["SafeActions:ActionTypes:1:RiskTier"] = "Low",
            ["SafeActions:ActionTypes:1:Enabled"] = "true",
            ["SafeActions:ActionTypes:2:ActionType"] = "dangerous_op",
            ["SafeActions:ActionTypes:2:RiskTier"] = "Medium",
            ["SafeActions:ActionTypes:2:Enabled"] = "false",
        });

        var list = catalog.List();

        Assert.Equal(3, list.Count);
    }

    // ─── Diagnostics ─────────────────────────────────────────────────

    [Fact]
    public void Diagnostics_DefinitionCount_And_EnabledCount()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:RiskTier"] = "High",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
            ["SafeActions:ActionTypes:1:ActionType"] = "http_probe",
            ["SafeActions:ActionTypes:1:RiskTier"] = "Low",
            ["SafeActions:ActionTypes:1:Enabled"] = "false",
        });

        Assert.Equal(2, catalog.DefinitionCount);
        Assert.Equal(1, catalog.EnabledCount);
    }

    // ─── RiskTier Parsing ────────────────────────────────────────────

    [Fact]
    public void Get_DefaultsToLow_WhenRiskTierMissing()
    {
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            ["SafeActions:ActionTypes:0:ActionType"] = "restart_pod",
            ["SafeActions:ActionTypes:0:Enabled"] = "true",
        });

        var def = catalog.Get("restart_pod");

        Assert.NotNull(def);
        Assert.Equal(ActionRiskTier.Low, def.RiskTier);
    }
}

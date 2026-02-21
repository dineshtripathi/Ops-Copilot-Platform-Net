using OpsCopilot.AgentRuns.Infrastructure.McpClient;
using Xunit;

namespace OpsCopilot.Integration.Tests;

/// <summary>
/// Tests for <see cref="McpStdioKqlToolClient.BuildChildEnvironment"/>.
///
/// These tests exercise the real method against actual environment variables —
/// no mocks, no fakes.  Each test sets/clears real env vars in the current
/// process and verifies the returned dictionary.
///
/// Because <c>Environment.SetEnvironmentVariable</c> affects the whole process,
/// tests in this class MUST NOT run in parallel.
/// </summary>
[Collection("EnvironmentVariables")] // serialise — env vars are process-global
public sealed class BuildChildEnvironmentTests : IDisposable
{
    // Track env vars we touch so we can restore them in Dispose.
    private readonly List<(string Name, string? Original)> _touched = [];

    // ── Well-known forwarding ─────────────────────────────────────────────

    [Theory]
    [InlineData("PATH")]
    [InlineData("AZURE_TENANT_ID")]
    [InlineData("ASPNETCORE_ENVIRONMENT")]
    [InlineData("DOTNET_ENVIRONMENT")]
    [InlineData("WORKSPACE_ID")]
    [InlineData("IDENTITY_ENDPOINT")]
    [InlineData("MSI_ENDPOINT")]
    [InlineData("DOTNET_ROOT")]
    [InlineData("SystemRoot")]
    [InlineData("COMSPEC")]
    [InlineData("APPDATA")]
    [InlineData("LOCALAPPDATA")]
    public void WellKnownVar_IsForwarded_WhenSet(string name)
    {
        SetEnv(name, "test-value-123");

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        Assert.True(result.ContainsKey(name), $"Expected '{name}' in child env.");
        Assert.Equal("test-value-123", result[name]);
    }

    [Theory]
    [InlineData("AZURE_TENANT_ID")]
    [InlineData("WORKSPACE_ID")]
    [InlineData("DOTNET_ROOT")]
    public void WellKnownVar_IsOmitted_WhenNotSet(string name)
    {
        // Ensure the var is NOT set.
        SetEnv(name, null);

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        Assert.False(result.ContainsKey(name),
            $"'{name}' should not appear when it is not set.");
    }

    [Fact]
    public void EmptyValue_IsNotForwarded()
    {
        SetEnv("AZURE_TENANT_ID", "");

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        Assert.False(result.ContainsKey("AZURE_TENANT_ID"),
            "Empty string should not be forwarded.");
    }

    // ── AzureAuth__* prefix forwarding ────────────────────────────────────

    [Fact]
    public void AzureAuthPrefixVars_AreForwarded()
    {
        SetEnv("AzureAuth__Mode", "ExplicitChain");
        SetEnv("AzureAuth__TenantId", "00000000-0000-0000-0000-000000000000");

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        Assert.True(result.ContainsKey("AzureAuth__Mode"));
        Assert.Equal("ExplicitChain", result["AzureAuth__Mode"]);
        Assert.True(result.ContainsKey("AzureAuth__TenantId"));
        Assert.Equal("00000000-0000-0000-0000-000000000000", result["AzureAuth__TenantId"]);
    }

    [Fact]
    public void AzureAuthPrefixVars_CaseInsensitive()
    {
        // The prefix check should be case-insensitive per OrdinalIgnoreCase.
        SetEnv("azureauth__custom", "value");

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        // The dictionary uses OrdinalIgnoreCase so key lookup is case-insensitive.
        Assert.True(result.ContainsKey("azureauth__custom"),
            "AzureAuth prefix matching should be case-insensitive.");
    }

    // ── Arbitrary vars are NOT forwarded ──────────────────────────────────

    [Fact]
    public void ArbitraryEnvVar_IsNotForwarded()
    {
        SetEnv("MY_RANDOM_APP_SETTING_XYZ", "should-not-appear");

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        Assert.False(result.ContainsKey("MY_RANDOM_APP_SETTING_XYZ"),
            "Arbitrary env vars must not leak into the child process.");
    }

    // ── Dictionary characteristics ────────────────────────────────────────

    [Fact]
    public void ReturnedDictionary_IsCaseInsensitive()
    {
        SetEnv("PATH", "/usr/bin");

        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        // Verify lookups work with different casing.
        if (result.ContainsKey("PATH"))
        {
            Assert.True(result.ContainsKey("path"),
                "Dictionary should use case-insensitive key comparison.");
        }
    }

    [Fact]
    public void PATH_IsAlwaysForwarded_OnCurrentPlatform()
    {
        // PATH is virtually always set on any OS; this is a sanity check
        // that the method works end-to-end without any artificial setup.
        var result = McpStdioKqlToolClient.BuildChildEnvironment();

        Assert.True(result.ContainsKey("PATH"),
            "PATH should be forwarded on every development/CI machine.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void SetEnv(string name, string? value)
    {
        // Record original value for cleanup.
        var original = Environment.GetEnvironmentVariable(name);
        _touched.Add((name, original));

        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        // Restore all env vars we touched.
        foreach (var (name, original) in _touched)
            Environment.SetEnvironmentVariable(name, original);
    }
}

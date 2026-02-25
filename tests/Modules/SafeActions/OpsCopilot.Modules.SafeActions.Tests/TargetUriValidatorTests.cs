using OpsCopilot.SafeActions.Infrastructure.Validators;
using Xunit;

namespace OpsCopilot.Modules.SafeActions.Tests;

/// <summary>
/// Unit tests for <see cref="TargetUriValidator"/>.
/// Validates SSRF protection: scheme, hostname, IP-literal, and DNS-resolution rules.
/// </summary>
public class TargetUriValidatorTests
{
    private readonly TargetUriValidator _sut = new();

    // ── Acceptance: valid HTTPS URLs ────────────────────────────────

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://api.github.com/repos")]
    [InlineData("https://status.azure.com/en-us/status")]
    public void Validate_Accepts_Valid_Https_Urls(string url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.True(isValid, $"Expected valid but got: {reason}");
        Assert.Null(reason);
    }

    // ── Rejection: null / empty / whitespace ────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_Rejects_Null_Or_Whitespace(string? url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains("null or whitespace", reason!);
    }

    // ── Rejection: non-URI strings ──────────────────────────────────

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("://missing-scheme")]
    public void Validate_Rejects_Invalid_Uri(string url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains("not a valid absolute URI", reason!);
    }

    // ── Rejection: non-HTTPS schemes ────────────────────────────────

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    public void Validate_Rejects_NonHttps_Scheme(string url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains("only HTTPS is allowed", reason!);
    }

    // ── Rejection: localhost ────────────────────────────────────────

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("https://LOCALHOST/api")]
    [InlineData("https://localhost:8443")]
    public void Validate_Rejects_Localhost(string url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains("localhost is blocked", reason!);
    }

    // ── Rejection: *.internal hostnames ─────────────────────────────

    [Fact]
    public void Validate_Rejects_Internal_Hostname()
    {
        var (isValid, reason) = _sut.Validate("https://secret.internal");

        Assert.False(isValid);
        Assert.Contains("*.internal hostnames are blocked", reason!);
    }

    // ── Rejection: IP-literal loopback ──────────────────────────────

    [Theory]
    [InlineData("https://127.0.0.1")]
    [InlineData("https://[::1]")]
    public void Validate_Rejects_Loopback_Ip(string url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains("blocked", reason!);
    }

    // ── Rejection: private IP ranges ────────────────────────────────

    [Theory]
    [InlineData("https://10.0.0.1",       "10.0.0.0/8")]
    [InlineData("https://10.255.255.255",  "10.0.0.0/8")]
    [InlineData("https://172.16.0.1",      "172.16.0.0/12")]
    [InlineData("https://172.31.255.255",  "172.16.0.0/12")]
    [InlineData("https://192.168.0.1",     "192.168.0.0/16")]
    [InlineData("https://192.168.255.255", "192.168.0.0/16")]
    public void Validate_Rejects_Private_Ip(string url, string expectedRange)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains(expectedRange, reason!);
    }

    // ── Rejection: Azure IMDS / link-local ──────────────────────────

    [Theory]
    [InlineData("https://169.254.169.254")]
    [InlineData("https://169.254.0.1")]
    public void Validate_Rejects_LinkLocal_Imds(string url)
    {
        var (isValid, reason) = _sut.Validate(url);

        Assert.False(isValid);
        Assert.Contains("link-local/IMDS", reason!);
    }
}

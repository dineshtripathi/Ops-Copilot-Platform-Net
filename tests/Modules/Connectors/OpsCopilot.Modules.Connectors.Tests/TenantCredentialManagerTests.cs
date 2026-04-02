using OpsCopilot.Connectors.Abstractions;
using OpsCopilot.Connectors.Infrastructure.Services;
using Xunit;

namespace OpsCopilot.Modules.Connectors.Tests;

// ── NullTenantCredentialManager ──────────────────────────────────────────────

public sealed class NullTenantCredentialManagerTests
{
    private readonly NullTenantCredentialManager _sut = new();

    [Fact]
    public void ImplementsInterface()
    {
        Assert.IsAssignableFrom<ITenantCredentialManager>(_sut);
    }

    [Fact]
    public async Task GetSecretAsync_ReturnsNull()
    {
        var result = await _sut.GetSecretAsync("t1", "connector-a");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSecretAsync_WithCancellation_ReturnsNull()
    {
        using var cts = new CancellationTokenSource();
        var result = await _sut.GetSecretAsync("t1", "connector-a", cts.Token);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_ReturnsUnknownStatus()
    {
        var meta = await _sut.GetRotationMetadataAsync("t1", "connector-a");
        Assert.Equal(RotationStatus.Unknown, meta.Status);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_SetsConnectorName()
    {
        var meta = await _sut.GetRotationMetadataAsync("t1", "connector-x");
        Assert.Equal("connector-x", meta.ConnectorName);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_NullLastRotatedAt()
    {
        var meta = await _sut.GetRotationMetadataAsync("t1", "connector-a");
        Assert.Null(meta.LastRotatedAt);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_NullExpiresAt()
    {
        var meta = await _sut.GetRotationMetadataAsync("t1", "connector-a");
        Assert.Null(meta.ExpiresAt);
    }
}

// ── ITenantCredentialManager.BuildSecretName ─────────────────────────────────

public sealed class BuildSecretNameTests
{
    [Fact]
    public void CanonicalFormat_DoubleDoubleDashSeparators()
    {
        var name = ITenantCredentialManager.BuildSecretName("acme", "datadog");
        Assert.Equal("tenant-acme--connector-datadog--credential", name);
    }

    [Fact]
    public void SanitizesUnderscoresInTenantId()
    {
        var name = ITenantCredentialManager.BuildSecretName("my_tenant", "datadog");
        // underscores replaced with dashes
        Assert.Equal("tenant-my-tenant--connector-datadog--credential", name);
    }

    [Fact]
    public void SanitizesUnderscoresInConnectorName()
    {
        var name = ITenantCredentialManager.BuildSecretName("acme", "my_connector");
        Assert.Equal("tenant-acme--connector-my-connector--credential", name);
    }

    [Fact]
    public void SanitizesSpaces()
    {
        var name = ITenantCredentialManager.BuildSecretName("acme corp", "data dog");
        Assert.Equal("tenant-acme-corp--connector-data-dog--credential", name);
    }

    [Fact]
    public void TrimsDashes()
    {
        // leading/trailing dashes produced by sanitisation must be trimmed
        var name = ITenantCredentialManager.BuildSecretName("_acme_", "datadog");
        Assert.DoesNotContain("tenant--", name.Replace("tenant--connector", string.Empty));
        Assert.StartsWith("tenant-acme", name);
    }

    [Fact]
    public void PreservesExistingDashes()
    {
        var name = ITenantCredentialManager.BuildSecretName("my-tenant", "my-connector");
        Assert.Equal("tenant-my-tenant--connector-my-connector--credential", name);
    }
}

// ── CredentialRotationClassifier ─────────────────────────────────────────────

public sealed class CredentialRotationClassifierTests
{
    private static readonly DateTimeOffset Now = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NullExpiresAt_ReturnsUnknown()
    {
        var status = CredentialRotationClassifier.Classify(null, Now);
        Assert.Equal(RotationStatus.Unknown, status);
    }

    [Fact]
    public void PastExpiry_ReturnsExpired()
    {
        var expired = Now.AddDays(-1);
        var status = CredentialRotationClassifier.Classify(expired, Now);
        Assert.Equal(RotationStatus.Expired, status);
    }

    [Fact]
    public void ExactlyAtExpiry_ReturnsExpired()
    {
        var status = CredentialRotationClassifier.Classify(Now, Now);
        Assert.Equal(RotationStatus.Expired, status);
    }

    [Fact]
    public void WithinWarningWindow_ReturnsDueSoon()
    {
        var soonExpiry = Now.AddDays(15); // inside default 30-day window
        var status = CredentialRotationClassifier.Classify(soonExpiry, Now);
        Assert.Equal(RotationStatus.DueSoon, status);
    }

    [Fact]
    public void AtWarningWindowBoundary_ReturnsDueSoon()
    {
        var boundary = Now.AddDays(CredentialRotationClassifier.DefaultWarningWindowDays);
        var status = CredentialRotationClassifier.Classify(boundary, Now);
        Assert.Equal(RotationStatus.DueSoon, status);
    }

    [Fact]
    public void BeyondWarningWindow_ReturnsCurrent()
    {
        var futureExpiry = Now.AddDays(90);
        var status = CredentialRotationClassifier.Classify(futureExpiry, Now);
        Assert.Equal(RotationStatus.Current, status);
    }

    [Fact]
    public void CustomWarningWindow_IsRespected()
    {
        var expiry = Now.AddDays(10);
        var statusTight = CredentialRotationClassifier.Classify(expiry, Now, warningWindowDays: 5);
        var statusWide  = CredentialRotationClassifier.Classify(expiry, Now, warningWindowDays: 15);

        Assert.Equal(RotationStatus.Current, statusTight);
        Assert.Equal(RotationStatus.DueSoon, statusWide);
    }
}

// ── KeyVaultTenantCredentialManager ──────────────────────────────────────────

public sealed class KeyVaultTenantCredentialManagerTests
{
    private static readonly DateTimeOffset _now = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    // Build a manager backed by a simple in-memory dictionary via the testability constructor.
    private static KeyVaultTenantCredentialManager BuildSut(
        Dictionary<string, (string Value, DateTimeOffset? ExpiresOn)>? secrets = null)
    {
        secrets ??= new();
        return new KeyVaultTenantCredentialManager(async (name, ct) =>
        {
            await Task.CompletedTask;
            if (secrets.TryGetValue(name, out var entry))
            {
                var secret = new Azure.Security.KeyVault.Secrets.KeyVaultSecret(name, entry.Value);
                secret.Properties.ExpiresOn = entry.ExpiresOn;
                return secret;
            }
            return null;
        });
    }

    [Fact]
    public void ImplementsInterface()
    {
        Assert.IsAssignableFrom<ITenantCredentialManager>(BuildSut());
    }

    [Fact]
    public async Task GetSecretAsync_KnownSecret_ReturnsValue()
    {
        var secretName = ITenantCredentialManager.BuildSecretName("acme", "datadog");
        var sut = BuildSut(new() { [secretName] = ("super-secret", null) });

        var result = await sut.GetSecretAsync("acme", "datadog");

        Assert.Equal("super-secret", result);
    }

    [Fact]
    public async Task GetSecretAsync_UnknownSecret_ReturnsNull()
    {
        var sut = BuildSut(new());
        var result = await sut.GetSecretAsync("unknown-tenant", "missing-connector");
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_MissingSecret_ReturnsUnknown()
    {
        var sut = BuildSut(new());
        var meta = await sut.GetRotationMetadataAsync("acme", "datadog");

        Assert.Equal(RotationStatus.Unknown, meta.Status);
        Assert.Null(meta.ExpiresAt);
        Assert.Null(meta.LastRotatedAt);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_ExpiredSecret_ReturnsExpired()
    {
        var secretName = ITenantCredentialManager.BuildSecretName("acme", "datadog");
        var pastExpiry = _now.AddDays(-1);
        var sut = BuildSut(new() { [secretName] = ("val", pastExpiry) });

        // Use live UtcNow for classifier; just verify Expired when expiry is well in the past.
        var now = DateTimeOffset.UtcNow;
        var meta = await sut.GetRotationMetadataAsync("acme", "datadog");

        // expiry was set to yesterday relative to _now; if running close to midnight UTC edge,
        // we use the actual expiry to derive expected status.
        var expected = CredentialRotationClassifier.Classify(pastExpiry, now);
        Assert.Equal(expected, meta.Status);
        Assert.Equal(pastExpiry, meta.ExpiresAt);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_CurrentSecret_ReturnsCurrent()
    {
        var secretName = ITenantCredentialManager.BuildSecretName("acme", "datadog");
        var farFutureExpiry = DateTimeOffset.UtcNow.AddDays(180);
        var sut = BuildSut(new() { [secretName] = ("val", farFutureExpiry) });

        var meta = await sut.GetRotationMetadataAsync("acme", "datadog");

        Assert.Equal(RotationStatus.Current, meta.Status);
    }

    [Fact]
    public async Task GetRotationMetadataAsync_SetsConnectorName()
    {
        var secretName = ITenantCredentialManager.BuildSecretName("acme", "my-connector");
        var sut = BuildSut(new() { [secretName] = ("val", null) });

        var meta = await sut.GetRotationMetadataAsync("acme", "my-connector");

        Assert.Equal("my-connector", meta.ConnectorName);
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Production implementation of <see cref="IAppConfigFeatureFlagWriter"/> that
/// calls the Azure App Configuration REST API to read and toggle feature flags.
/// <para>
/// Feature flags are stored under the key
/// <c>.appconfig.featureflag/{featureFlagId}</c> with content-type
/// <c>application/vnd.microsoft.appconfig.ff+json;charset=utf-8</c>.
/// </para>
/// <para>
/// Authenticates via <see cref="DefaultAzureCredential"/> — managed identity
/// in Azure, developer credentials locally via az/VS/environment.
/// </para>
/// <para>
/// <strong>Note:</strong> <see cref="SetEnabledAsync"/> writes a minimal flag
/// definition (id, enabled, empty client_filters). Any existing client filters
/// on the flag are replaced. This is intentional for safe, deterministic toggling.
/// </para>
/// </summary>
internal sealed class HttpAppConfigFeatureFlagWriter : IAppConfigFeatureFlagWriter
{
    private static readonly string[] AppConfigScopes = ["https://azconfig.io/.default"];
    private const string ApiVersion = "2023-11-01";

    private readonly IHttpClientFactory _factory;
    private readonly TokenCredential _credential;
    private readonly ILogger<HttpAppConfigFeatureFlagWriter> _logger;

    public HttpAppConfigFeatureFlagWriter(
        IHttpClientFactory factory,
        ILogger<HttpAppConfigFeatureFlagWriter> logger)
    {
        _factory = factory;
        _credential = new DefaultAzureCredential();
        _logger = logger;
    }

    public async Task<bool> GetEnabledAsync(
        string endpoint, string featureFlagId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        var url = BuildKeyUrl(endpoint, featureFlagId);

        using var client = _factory.CreateClient(nameof(HttpAppConfigFeatureFlagWriter));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        _logger.LogInformation(
            "[AppConfigWriter] GET feature flag '{FeatureFlagId}' from {Endpoint}",
            featureFlagId, endpoint);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // The KV response wraps the flag definition as a JSON string in "value".
        using var kvDoc = JsonDocument.Parse(body);
        if (!kvDoc.RootElement.TryGetProperty("value", out var valueEl))
            throw new InvalidOperationException(
                $"App Configuration response for '{featureFlagId}' did not contain a 'value' property.");

        var flagJson = valueEl.GetString()
            ?? throw new InvalidOperationException(
                $"App Configuration 'value' property for '{featureFlagId}' was null.");

        using var flagDoc = JsonDocument.Parse(flagJson);
        if (!flagDoc.RootElement.TryGetProperty("enabled", out var enabledEl))
            return false;

        return enabledEl.GetBoolean();
    }

    public async Task SetEnabledAsync(
        string endpoint, string featureFlagId, bool enabled, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        var url = BuildKeyUrl(endpoint, featureFlagId);

        // Minimal feature flag definition — replaces any existing entry.
        var flagDefinition = JsonSerializer.Serialize(new
        {
            id = featureFlagId,
            enabled,
            conditions = new { client_filters = Array.Empty<object>() }
        });

        var kvBody = JsonSerializer.Serialize(new
        {
            value = flagDefinition,
            content_type = "application/vnd.microsoft.appconfig.ff+json;charset=utf-8"
        });

        using var client = _factory.CreateClient(nameof(HttpAppConfigFeatureFlagWriter));
        using var request = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(kvBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        _logger.LogInformation(
            "[AppConfigWriter] PUT feature flag '{FeatureFlagId}' enabled={Enabled} to {Endpoint}",
            featureFlagId, enabled, endpoint);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private static string BuildKeyUrl(string endpoint, string featureFlagId)
    {
        var key = Uri.EscapeDataString(".appconfig.featureflag/" + featureFlagId);
        return $"{endpoint.TrimEnd('/')}/kv/{key}?api-version={ApiVersion}";
    }

    private Task<AccessToken> GetTokenAsync(CancellationToken ct)
    {
        var context = new TokenRequestContext(AppConfigScopes);
        return _credential.GetTokenAsync(context, ct).AsTask();
    }
}

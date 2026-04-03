using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Production implementation of <see cref="IAzureScaleWriter"/> that issues
/// ARM REST API calls to read and set the instance capacity of a scale resource.
/// <para>
/// Supports <c>Microsoft.Compute/virtualMachineScaleSets</c> via the
/// <c>2024-07-01</c> ARM API version.
/// </para>
/// <para>
/// Authenticates via <see cref="DefaultAzureCredential"/> — managed identity
/// in Azure, developer credentials locally via az/VS/environment.
/// </para>
/// </summary>
internal sealed class HttpArmScaleWriter : IAzureScaleWriter
{
    private static readonly string[] ArmScopes = ["https://management.azure.com/.default"];
    private const string ArmApiVersion = "api-version=2024-07-01";

    private readonly IHttpClientFactory _factory;
    private readonly TokenCredential _credential;
    private readonly ILogger<HttpArmScaleWriter> _logger;

    public HttpArmScaleWriter(
        IHttpClientFactory factory,
        ILogger<HttpArmScaleWriter> logger)
    {
        _factory = factory;
        _credential = new DefaultAzureCredential();
        _logger = logger;
    }

    public async Task<int> GetCapacityAsync(string resourceId, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        var url = $"https://management.azure.com{resourceId}?{ArmApiVersion}";

        using var client = _factory.CreateClient(nameof(HttpArmScaleWriter));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        _logger.LogInformation("[ArmScaleWriter] GET capacity {ResourceId}", resourceId);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("sku", out var sku) &&
            sku.TryGetProperty("capacity", out var cap) &&
            cap.TryGetInt32(out var capacity))
        {
            return capacity;
        }

        throw new InvalidOperationException(
            $"ARM response for '{resourceId}' did not contain sku.capacity.");
    }

    public async Task SetCapacityAsync(string resourceId, int capacity, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct).ConfigureAwait(false);
        var url = $"https://management.azure.com{resourceId}?{ArmApiVersion}";

        var body = JsonSerializer.Serialize(new { sku = new { capacity } });

        using var client = _factory.CreateClient(nameof(HttpArmScaleWriter));
        using var request = new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token.Token);

        _logger.LogInformation(
            "[ArmScaleWriter] PATCH capacity={Capacity} {ResourceId}", capacity, resourceId);

        using var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private Task<AccessToken> GetTokenAsync(CancellationToken ct)
    {
        var context = new TokenRequestContext(ArmScopes);
        return _credential.GetTokenAsync(context, ct).AsTask();
    }
}

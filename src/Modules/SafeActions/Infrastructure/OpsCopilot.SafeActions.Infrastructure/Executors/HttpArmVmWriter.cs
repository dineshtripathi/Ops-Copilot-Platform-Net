using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Production implementation of <see cref="IAzureVmWriter"/> that issues
/// ARM REST API calls for VM write operations.
/// <para>
/// Authenticates via <see cref="DefaultAzureCredential"/> — managed identity
/// in Azure, developer credentials locally via az/VS/environment.
/// </para>
/// <para>
/// Registered via <c>AddHttpClient(nameof(HttpArmVmWriter))</c> so the HTTP
/// client is managed by <see cref="System.Net.Http.IHttpClientFactory"/>.
/// Kept as a singleton: <see cref="DefaultAzureCredential"/> caches tokens internally.
/// </para>
/// </summary>
internal sealed class HttpArmVmWriter : IAzureVmWriter
{
    private static readonly string[] ArmScopes = ["https://management.azure.com/.default"];
    private const string ArmApiVersion = "api-version=2024-03-01";

    private readonly IHttpClientFactory _factory;
    private readonly TokenCredential _credential;
    private readonly ILogger<HttpArmVmWriter> _logger;

    public HttpArmVmWriter(
        IHttpClientFactory factory,
        ILogger<HttpArmVmWriter> logger)
    {
        _factory = factory;
        _credential = new DefaultAzureCredential();
        _logger = logger;
    }

    public async Task RestartAsync(string resourceId, CancellationToken ct)
    {
        var tokenContext = new TokenRequestContext(ArmScopes);
        var token = await _credential.GetTokenAsync(tokenContext, ct).ConfigureAwait(false);

        var url = $"https://management.azure.com{resourceId}/restart?{ArmApiVersion}";

        using var client = _factory.CreateClient(nameof(HttpArmVmWriter));
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);

        _logger.LogInformation("[ArmVmWriter] POST restart {ResourceId}", resourceId);

        var response = await client.SendAsync(request, ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new HttpRequestException(
                $"ARM restart failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}",
                null,
                response.StatusCode);
        }

        _logger.LogInformation(
            "[ArmVmWriter] Restart accepted for {ResourceId}, HTTP {StatusCode}",
            resourceId, (int)response.StatusCode);
    }
}

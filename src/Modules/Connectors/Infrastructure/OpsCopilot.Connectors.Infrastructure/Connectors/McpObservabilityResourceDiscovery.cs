using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpsCopilot.Connectors.Abstractions;

namespace OpsCopilot.Connectors.Infrastructure.Connectors;

/// <summary>
/// Calls the McpHost "discover_observability_resources" tool to enumerate
/// Log Analytics workspace / App Insights component correlations.
///
/// Results are cached after the first successful call so that subsequent
/// triage requests in the same process lifetime do not incur repeated ARM queries.
///
/// Returns an empty list when discovery fails or the MCP tool is unavailable —
/// callers continue without a <c>_ResourceId</c> filter in that scenario.
/// </summary>
internal sealed class McpObservabilityResourceDiscovery : IObservabilityResourceDiscovery
{
    private readonly IMcpToolConnector _toolConnector;
    private readonly ILogger<McpObservabilityResourceDiscovery> _logger;

    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private IReadOnlyList<ObservabilityResourcePair>? _cached;

    public McpObservabilityResourceDiscovery(
        IMcpToolConnector toolConnector,
        ILogger<McpObservabilityResourceDiscovery> logger)
    {
        _toolConnector = toolConnector ?? throw new ArgumentNullException(nameof(toolConnector));
        _logger        = logger        ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ObservabilityResourcePair>> DiscoverAsync(
        CancellationToken ct = default)
    {
        // Fast path — already cached from a previous call.
        if (_cached is not null)
            return _cached;

        await _cacheLock.WaitAsync(ct);
        try
        {
            // Double-check inside the lock.
            if (_cached is not null)
                return _cached;

            _logger.LogInformation(
                "[ObservabilityResourceDiscovery] Discovering LAW/App Insights pairs via MCP tool");

            var json = await _toolConnector.CallToolAsync(
                "discover_observability_resources",
                new Dictionary<string, object?> { ["subscriptionIds"] = string.Empty },
                ct);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning(
                    "[ObservabilityResourceDiscovery] Tool returned empty response; caching empty list");
                _cached = [];
                return _cached;
            }

            _cached = ParseResponse(json);
            _logger.LogInformation(
                "[ObservabilityResourceDiscovery] Discovered {Count} LAW/AI pair(s)", _cached.Count);
            return _cached;
        }
        catch (OperationCanceledException)
        {
            // Do not cache — allow retry on next request.
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ObservabilityResourceDiscovery] Discovery failed; caching empty list to avoid repeated failures");
            _cached = [];
            return _cached;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private static IReadOnlyList<ObservabilityResourcePair> ParseResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
            return [];

        if (!root.TryGetProperty("pairs", out var pairsEl) ||
            pairsEl.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<ObservabilityResourcePair>();
        foreach (var item in pairsEl.EnumerateArray())
        {
            var customerId = TryString(item, "workspaceCustomerId");

            // Only the workspace customer ID is required; App Insights fields are optional
            // (workspaces with no linked AI component are returned with empty strings).
            if (string.IsNullOrWhiteSpace(customerId))
                continue;

            var subId = TryString(item, "subscriptionId") ?? string.Empty;
            var name  = TryString(item, "appInsightsName") ?? string.Empty;
            var path  = TryString(item, "appInsightsResourcePath") ?? string.Empty;

            results.Add(new ObservabilityResourcePair(customerId, name, path)
            {
                SubscriptionId = subId,
            });
        }

        return results;
    }

    private static string? TryString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var el) ? el.GetString() : null;
}

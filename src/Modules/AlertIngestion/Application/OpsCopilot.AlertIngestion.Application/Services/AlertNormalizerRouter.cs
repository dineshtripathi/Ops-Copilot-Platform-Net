using System.Text.Json;
using OpsCopilot.AlertIngestion.Application.Abstractions;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Services;

/// <summary>
/// Routes incoming alert payloads to the correct <see cref="IAlertNormalizer"/>
/// based on the declared provider key.
/// </summary>
public sealed class AlertNormalizerRouter
{
    private readonly IReadOnlyDictionary<string, IAlertNormalizer> _normalizers;

    public AlertNormalizerRouter(IEnumerable<IAlertNormalizer> normalizers)
    {
        _normalizers = normalizers
            .ToDictionary(n => n.ProviderKey, n => n, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> when a normalizer exists for the given provider.
    /// </summary>
    public bool IsSupported(string provider) => _normalizers.ContainsKey(provider);

    /// <summary>
    /// Normalize the payload using the registered provider normalizer.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no normalizer is registered for the given provider.
    /// </exception>
    public NormalizedAlert Normalize(string provider, JsonElement payload)
    {
        if (!_normalizers.TryGetValue(provider, out var normalizer))
            throw new InvalidOperationException($"No normalizer registered for provider '{provider}'.");

        return normalizer.Normalize(provider, payload);
    }
}

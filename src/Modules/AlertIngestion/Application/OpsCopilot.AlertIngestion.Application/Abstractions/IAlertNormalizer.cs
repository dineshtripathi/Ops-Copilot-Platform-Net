using System.Text.Json;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Abstractions;

/// <summary>
/// Transforms a provider-specific JSON payload into the canonical
/// <see cref="NormalizedAlert"/> model.
/// </summary>
public interface IAlertNormalizer
{
    /// <summary>Provider key this normalizer handles (e.g. "azure_monitor").</summary>
    string ProviderKey { get; }

    /// <summary>Returns true when this normalizer can handle the given provider.</summary>
    bool CanHandle(string provider);

    /// <summary>
    /// Parse the raw JSON element and return a populated <see cref="NormalizedAlert"/>.
    /// </summary>
    NormalizedAlert Normalize(string provider, JsonElement payload);
}

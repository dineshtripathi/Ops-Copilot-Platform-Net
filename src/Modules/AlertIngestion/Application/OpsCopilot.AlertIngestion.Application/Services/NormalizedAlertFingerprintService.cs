using System.Security.Cryptography;
using System.Text;
using OpsCopilot.AlertIngestion.Domain.Models;

namespace OpsCopilot.AlertIngestion.Application.Services;

/// <summary>
/// Deterministic fingerprint based on normalized alert fields:
/// provider | title | resourceId | severity | sourceType.
/// SHA-256, upper-case hex, 64 characters.
/// </summary>
public static class NormalizedAlertFingerprintService
{
    public static string Compute(NormalizedAlert alert)
    {
        var input = string.Join("|",
            alert.Provider,
            alert.Title,
            alert.ResourceId,
            alert.Severity,
            alert.SourceType);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash);
    }
}

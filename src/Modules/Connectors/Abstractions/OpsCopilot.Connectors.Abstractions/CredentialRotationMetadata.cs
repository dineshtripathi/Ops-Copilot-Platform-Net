namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Domain model describing when a connector credential was last rotated and when it expires.
/// Populated by a future Key Vault secret-metadata provider; fields are nullable when the
/// underlying provider does not expose expiry information.
/// </summary>
/// <param name="ConnectorName">The connector this metadata belongs to.</param>
/// <param name="LastRotatedAt">When the secret was last set; <c>null</c> if unknown.</param>
/// <param name="ExpiresAt">When the secret expires; <c>null</c> if no expiry is set.</param>
/// <param name="Status">Derived rotation status based on expiry proximity.</param>
public sealed record CredentialRotationMetadata(
    string          ConnectorName,
    DateTimeOffset? LastRotatedAt,
    DateTimeOffset? ExpiresAt,
    RotationStatus  Status);

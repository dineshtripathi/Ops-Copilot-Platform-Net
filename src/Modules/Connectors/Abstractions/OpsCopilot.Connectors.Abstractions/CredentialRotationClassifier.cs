namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Classifies a credential's <see cref="RotationStatus"/> from its expiry date.
/// Used by <see cref="ITenantCredentialManager"/> implementations to derive
/// proactive expiry warnings without embedding the logic in each implementation.
/// </summary>
public static class CredentialRotationClassifier
{
    /// <summary>
    /// Default number of days before expiry at which <see cref="RotationStatus.DueSoon"/> is returned.
    /// </summary>
    public const int DefaultWarningWindowDays = 30;

    /// <summary>
    /// Returns the <see cref="RotationStatus"/> for a credential given its expiry date.
    /// </summary>
    /// <param name="expiresAt">
    ///   The expiry timestamp, or <c>null</c> when expiry information is unavailable.
    /// </param>
    /// <param name="now">The reference instant (normally <see cref="DateTimeOffset.UtcNow"/>).</param>
    /// <param name="warningWindowDays">
    ///   Days before expiry at which the status transitions to <see cref="RotationStatus.DueSoon"/>.
    ///   Defaults to <see cref="DefaultWarningWindowDays"/>.
    /// </param>
    /// <returns>
    ///   <see cref="RotationStatus.Unknown"/> when <paramref name="expiresAt"/> is <c>null</c>;
    ///   <see cref="RotationStatus.Expired"/> when the credential is past expiry;
    ///   <see cref="RotationStatus.DueSoon"/> when expiry is within the warning window;
    ///   <see cref="RotationStatus.Current"/> otherwise.
    /// </returns>
    public static RotationStatus Classify(
        DateTimeOffset? expiresAt,
        DateTimeOffset  now,
        int             warningWindowDays = DefaultWarningWindowDays)
    {
        if (expiresAt is null)
            return RotationStatus.Unknown;

        if (now >= expiresAt.Value)
            return RotationStatus.Expired;

        if (expiresAt.Value - now <= TimeSpan.FromDays(warningWindowDays))
            return RotationStatus.DueSoon;

        return RotationStatus.Current;
    }
}

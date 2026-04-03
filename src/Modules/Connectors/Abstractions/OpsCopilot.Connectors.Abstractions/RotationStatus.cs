namespace OpsCopilot.Connectors.Abstractions;

/// <summary>
/// Indicates the rotation state of a connector credential.
/// </summary>
public enum RotationStatus
{
    /// <summary>Rotation state cannot be determined (e.g. no expiry metadata available).</summary>
    Unknown,

    /// <summary>The credential is current — no rotation action required.</summary>
    Current,

    /// <summary>The credential is due for rotation within the warning window.</summary>
    DueSoon,

    /// <summary>The credential has expired and must be rotated immediately.</summary>
    Expired,
}

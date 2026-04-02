namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Abstraction for scaling Azure resources via ARM REST.
/// Implemented by <see cref="HttpArmScaleWriter"/> in production.
/// Mockable in tests — no SDK or credentials here.
/// </summary>
internal interface IAzureScaleWriter
{
    /// <summary>Returns the current instance count of the scale resource.</summary>
    Task<int> GetCapacityAsync(string resourceId, CancellationToken ct);

    /// <summary>Sets the instance count to <paramref name="capacity"/>.</summary>
    Task SetCapacityAsync(string resourceId, int capacity, CancellationToken ct);
}

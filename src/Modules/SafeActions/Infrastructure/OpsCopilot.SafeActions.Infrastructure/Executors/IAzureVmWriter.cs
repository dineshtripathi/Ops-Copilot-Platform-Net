namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Abstraction for Azure VM write operations via the ARM REST API.
/// Enables unit testing of <see cref="ArmRestartActionExecutor"/>
/// without live Azure credentials or HTTP calls.
/// </summary>
internal interface IAzureVmWriter
{
    /// <summary>
    /// Issues an ARM POST to restart the specified virtual machine.
    /// </summary>
    /// <param name="resourceId">Fully-qualified ARM resource ID of the VM.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="System.Net.Http.HttpRequestException">
    /// Thrown when the ARM REST call returns a non-success HTTP status.
    /// </exception>
    Task RestartAsync(string resourceId, CancellationToken ct);
}

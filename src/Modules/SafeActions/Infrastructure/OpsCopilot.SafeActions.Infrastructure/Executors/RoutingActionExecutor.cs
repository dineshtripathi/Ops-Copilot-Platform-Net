using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;

namespace OpsCopilot.SafeActions.Infrastructure.Executors;

/// <summary>
/// Composite executor that routes to the appropriate downstream executor
/// based on action type and feature-flag configuration.
/// <para>
/// Routing rules (evaluated in order — first match wins):
/// <list type="bullet">
///   <item><c>azure_resource_get</c> + <c>SafeActions:EnableAzureReadExecutions=true</c>
///         → <see cref="AzureResourceGetActionExecutor"/></item>
///   <item><c>azure_monitor_query</c> + <c>SafeActions:EnableAzureMonitorReadExecutions=true</c>
///         → <see cref="AzureMonitorQueryActionExecutor"/></item>
///   <item><c>http_probe</c> + <c>SafeActions:EnableRealHttpProbe=true</c>
///         → <see cref="HttpProbeActionExecutor"/></item>
///   <item>Everything else → <see cref="DryRunActionExecutor"/></item>
/// </list>
/// </para>
/// This is the single <see cref="IActionExecutor"/> registered in DI.
/// </summary>
internal sealed class RoutingActionExecutor : IActionExecutor
{
    private const string HttpProbeActionType = "http_probe";
    private const string AzureResourceGetActionType = "azure_resource_get";
    private const string AzureMonitorQueryActionType = "azure_monitor_query";

    private readonly DryRunActionExecutor _dryRun;
    private readonly HttpProbeActionExecutor _httpProbe;
    private readonly AzureResourceGetActionExecutor _azureGet;
    private readonly AzureMonitorQueryActionExecutor _azureMonitorQuery;
    private readonly bool _enableRealHttpProbe;
    private readonly bool _enableAzureRead;
    private readonly bool _enableAzureMonitorRead;
    private readonly ILogger<RoutingActionExecutor> _logger;

    public RoutingActionExecutor(
        DryRunActionExecutor dryRun,
        HttpProbeActionExecutor httpProbe,
        AzureResourceGetActionExecutor azureGet,
        AzureMonitorQueryActionExecutor azureMonitorQuery,
        IConfiguration configuration,
        ILogger<RoutingActionExecutor> logger)
    {
        _dryRun = dryRun;
        _httpProbe = httpProbe;
        _azureGet = azureGet;
        _azureMonitorQuery = azureMonitorQuery;
        _enableRealHttpProbe = configuration.GetValue<bool>("SafeActions:EnableRealHttpProbe");
        _enableAzureRead = configuration.GetValue<bool>("SafeActions:EnableAzureReadExecutions");
        _enableAzureMonitorRead = configuration.GetValue<bool>("SafeActions:EnableAzureMonitorReadExecutions");
        _logger = logger;
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        string actionType, string payloadJson, CancellationToken ct = default)
    {
        if (ShouldRouteToAzureGet(actionType))
        {
            _logger.LogInformation(
                "[RoutingExecutor] Routing {ActionType} to AzureResourceGetActionExecutor",
                actionType);
            return _azureGet.ExecuteAsync(payloadJson, ct);
        }

        if (ShouldRouteToAzureMonitorQuery(actionType))
        {
            _logger.LogInformation(
                "[RoutingExecutor] Routing {ActionType} to AzureMonitorQueryActionExecutor",
                actionType);
            return _azureMonitorQuery.ExecuteAsync(payloadJson, ct);
        }

        if (ShouldRouteToRealProbe(actionType))
        {
            _logger.LogInformation(
                "[RoutingExecutor] Routing {ActionType} to HttpProbeActionExecutor", actionType);
            return _httpProbe.ExecuteAsync(payloadJson, ct);
        }

        _logger.LogInformation(
            "[RoutingExecutor] Routing {ActionType} to DryRunActionExecutor", actionType);
        return _dryRun.ExecuteAsync(actionType, payloadJson, ct);
    }

    public Task<ActionExecutionResult> RollbackAsync(
        string actionType, string rollbackPayloadJson, CancellationToken ct = default)
    {
        if (ShouldRouteToAzureGet(actionType))
        {
            _logger.LogInformation(
                "[RoutingExecutor] Routing {ActionType} rollback to AzureResourceGetActionExecutor",
                actionType);
            return _azureGet.RollbackAsync(rollbackPayloadJson, ct);
        }

        if (ShouldRouteToAzureMonitorQuery(actionType))
        {
            _logger.LogInformation(
                "[RoutingExecutor] Routing {ActionType} rollback to AzureMonitorQueryActionExecutor",
                actionType);
            return _azureMonitorQuery.RollbackAsync(rollbackPayloadJson, ct);
        }

        if (ShouldRouteToRealProbe(actionType))
        {
            _logger.LogInformation(
                "[RoutingExecutor] Routing {ActionType} rollback to HttpProbeActionExecutor",
                actionType);
            return _httpProbe.RollbackAsync(rollbackPayloadJson, ct);
        }

        _logger.LogInformation(
            "[RoutingExecutor] Routing {ActionType} rollback to DryRunActionExecutor",
            actionType);
        return _dryRun.RollbackAsync(actionType, rollbackPayloadJson, ct);
    }

    private bool ShouldRouteToAzureGet(string actionType)
    {
        return _enableAzureRead &&
               string.Equals(actionType, AzureResourceGetActionType, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRouteToAzureMonitorQuery(string actionType)
    {
        return _enableAzureMonitorRead &&
               string.Equals(actionType, AzureMonitorQueryActionType, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldRouteToRealProbe(string actionType)
    {
        return _enableRealHttpProbe &&
               string.Equals(actionType, HttpProbeActionType, StringComparison.OrdinalIgnoreCase);
    }
}

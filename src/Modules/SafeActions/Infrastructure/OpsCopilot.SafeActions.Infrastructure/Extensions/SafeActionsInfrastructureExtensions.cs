using Microsoft.EntityFrameworkCore;
using OpsCopilot.SafeActions.Infrastructure.McpClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.SafeActions.Application.Abstractions;
using OpsCopilot.SafeActions.Domain.Repositories;
using OpsCopilot.SafeActions.Infrastructure.Executors;
using OpsCopilot.SafeActions.Infrastructure.Persistence;
using OpsCopilot.SafeActions.Infrastructure.Policies;
using OpsCopilot.SafeActions.Infrastructure.Validators;

namespace OpsCopilot.SafeActions.Infrastructure.Extensions;

public static class SafeActionsInfrastructureExtensions
{
    public static IServiceCollection AddSafeActionsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── SQL connection ──────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("Sql")
                            ?? configuration["SQL_CONNECTION_STRING"]
                            ?? throw new InvalidOperationException(
                                "SQL connection string not configured. " +
                                "Set ConnectionStrings:Sql or SQL_CONNECTION_STRING.");

        services.AddDbContext<SafeActionsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "safeActions");
            }));

        // ── Repository ──────────────────────────────────────────────
        services.AddScoped<IActionRecordRepository, SqlActionRecordRepository>();

        // ── SSRF validator ──────────────────────────────────────────
        services.AddSingleton<TargetUriValidator>();

        // ── McpHost client (shares one child process across executors) ─
        var mcpOptions = BuildMcpHostOptions(configuration);
        services.AddSingleton(mcpOptions);
        services.AddSingleton<SafeActionsMcpHostClient>();
        services.AddSingleton<IAzureResourceReader, McpArmResourceReader>();
        services.AddSingleton<IAzureMonitorLogsReader, McpBackedLogsReader>();

        // ── Executors ───────────────────────────────────────────────
        // Dry-run executor: deterministic, zero side-effects (Slice 8)
        services.AddSingleton<DryRunActionExecutor>();

        // Real HTTP-probe executor: outbound HTTPS GET with safety controls
        services.AddHttpClient<HttpProbeActionExecutor>();

        // Azure resource GET executor: read-only ARM metadata (Slice 10)
        services.AddSingleton<AzureResourceGetActionExecutor>();

        // Azure Monitor query executor: read-only KQL queries (Slice 11)
        services.AddSingleton<AzureMonitorQueryActionExecutor>();

        // ARM VM restart executor: issues POST .../restart via ARM REST API (Slice 187)
        // SafeActions:EnableArmWrite=true required — disabled by default.
        services.AddHttpClient(nameof(HttpArmVmWriter));
        services.AddSingleton<IAzureVmWriter, HttpArmVmWriter>();
        services.AddSingleton<ArmRestartActionExecutor>();

        // ARM VMSS scale executor: GET capacity + PATCH new count (Slice 188)
        // SafeActions:EnableArmWrite=true required — disabled by default.
        services.AddHttpClient(nameof(HttpArmScaleWriter));
        services.AddSingleton<IAzureScaleWriter, HttpArmScaleWriter>();
        services.AddSingleton<ArmScaleActionExecutor>();

        // App Configuration feature flag executor: GET current + PUT enabled flag (Slice 189)
        // SafeActions:EnableAppConfigWrite=true required — disabled by default.
        services.AddHttpClient(nameof(HttpAppConfigFeatureFlagWriter));
        services.AddSingleton<IAppConfigFeatureFlagWriter, HttpAppConfigFeatureFlagWriter>();
        services.AddSingleton<AppConfigFeatureFlagExecutor>();

        // Routing executor: composite — reads feature flags, delegates
        // to the appropriate downstream executor.
        // SafeActions:EnableAzureReadExecutions=true          → azure_resource_get        → real Azure GET
        // SafeActions:EnableAzureMonitorReadExecutions=true    → azure_monitor_query       → real KQL query
        // SafeActions:EnableArmWrite=true                      → arm_restart               → real ARM POST restart
        // SafeActions:EnableArmWrite=true                      → arm_scale                 → real ARM PATCH scale
        // SafeActions:EnableAppConfigWrite=true                → app_config_feature_flag   → real App Config write
        // SafeActions:EnableRealHttpProbe=true                 → http_probe                → real probe
        // Everything else (or flags=false)                     → dry-run
        services.AddSingleton<IActionExecutor, RoutingActionExecutor>();

        // ── Policy (tenant-aware governance-backed) ─────────────────
        services.AddScoped<ISafeActionPolicy, GovernanceBackedSafeActionPolicy>();

        // ── Tenant execution policy (strict: empty/missing = DENY) ──
        services.AddSingleton<ITenantExecutionPolicy, ConfigDrivenTenantExecutionPolicy>();

        // ── ActionType catalog (allow-all when config empty) ────────
        services.AddSingleton<IActionTypeCatalog, ConfigActionTypeCatalog>();

        // ── Governance bridge (delegates to BuildingBlocks contracts) ────
        services.AddScoped<IGovernancePolicyClient, GovernancePolicyClient>();

        // ── Startup diagnostics (counts only, no tenant names) ──────
        using var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>()
                       .CreateLogger("SafeActions.Startup");
        var tenantPolicy = sp.GetRequiredService<ITenantExecutionPolicy>()
                              as ConfigDrivenTenantExecutionPolicy;
        if (tenantPolicy is not null)
        {
            logger.LogInformation(
                "Tenant execution policy loaded: {ActionTypeCount} action type(s), "
                + "{TenantEntryCount} total tenant entries",
                tenantPolicy.ConfiguredActionTypeCount,
                tenantPolicy.TotalTenantEntryCount);
        }

        var catalogInstance = sp.GetRequiredService<IActionTypeCatalog>()
                                 as ConfigActionTypeCatalog;
        if (catalogInstance is not null)
        {
            logger.LogInformation(
                "ActionType catalog loaded: {DefinitionCount} definition(s), "
                + "{EnabledCount} enabled",
                catalogInstance.DefinitionCount,
                catalogInstance.EnabledCount);
        }

        return services;
    }

    private static McpHostOptions BuildMcpHostOptions(IConfiguration configuration)
    {
        // Config keys: McpKql:ServerCommand (or MCP_KQL_SERVER_COMMAND env var)
        // McpKql:WorkDir (or MCP_KQL_SERVER_WORKDIR)
        // McpKql:TimeoutSeconds (or MCP_KQL_TIMEOUT_SECONDS)
        var serverCommand = configuration["McpKql:ServerCommand"]
                         ?? configuration["MCP_KQL_SERVER_COMMAND"];

        string   executable = "dotnet";
        string[] arguments  = ["run", "--project", "src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"];

        if (!string.IsNullOrWhiteSpace(serverCommand))
        {
            var parts = serverCommand.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            executable = parts[0];
            arguments  = parts.Length > 1
                ? parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                : [];
        }

        var workDir = configuration["McpKql:WorkDir"]
                   ?? configuration["MCP_KQL_SERVER_WORKDIR"];

        var timeoutRaw = configuration["McpKql:TimeoutSeconds"]
                      ?? configuration["MCP_KQL_TIMEOUT_SECONDS"];
        var timeout = int.TryParse(timeoutRaw, out var t) ? t : 30;

        return new McpHostOptions
        {
            Executable       = executable,
            Arguments        = arguments,
            WorkingDirectory = workDir,
            TimeoutSeconds   = timeout,
        };
    }
}

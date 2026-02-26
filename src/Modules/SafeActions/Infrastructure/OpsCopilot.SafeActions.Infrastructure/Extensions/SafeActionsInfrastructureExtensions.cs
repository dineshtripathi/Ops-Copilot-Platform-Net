using Azure.Identity;
using Azure.Monitor.Query;
using Azure.ResourceManager;
using Microsoft.EntityFrameworkCore;
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

        // ── Azure ARM reader (read-only, DefaultAzureCredential) ────
        services.AddSingleton(_ =>
        {
            var tenantId = configuration["SafeActions:AzureTenantId"];
            var options = string.IsNullOrWhiteSpace(tenantId)
                ? new DefaultAzureCredentialOptions()
                : new DefaultAzureCredentialOptions { TenantId = tenantId };
            return new ArmClient(new DefaultAzureCredential(options));
        });
        services.AddSingleton<IAzureResourceReader, ArmResourceReader>();

        // ── Executors ───────────────────────────────────────────────
        // Dry-run executor: deterministic, zero side-effects (Slice 8)
        services.AddSingleton<DryRunActionExecutor>();

        // Real HTTP-probe executor: outbound HTTPS GET with safety controls
        services.AddHttpClient<HttpProbeActionExecutor>();

        // Azure resource GET executor: read-only ARM metadata (Slice 10)
        services.AddSingleton<AzureResourceGetActionExecutor>();

        // Azure Monitor query client (read-only, DefaultAzureCredential)
        services.AddSingleton(_ =>
        {
            var tenantId = configuration["SafeActions:AzureTenantId"];
            var options = string.IsNullOrWhiteSpace(tenantId)
                ? new DefaultAzureCredentialOptions()
                : new DefaultAzureCredentialOptions { TenantId = tenantId };
            return new LogsQueryClient(new DefaultAzureCredential(options));
        });
        services.AddSingleton<IAzureMonitorLogsReader, LogsQueryClientReader>();

        // Azure Monitor query executor: read-only KQL queries (Slice 11)
        services.AddSingleton<AzureMonitorQueryActionExecutor>();

        // Routing executor: composite — reads feature flags, delegates
        // to the appropriate downstream executor.
        // SafeActions:EnableAzureReadExecutions=true          → azure_resource_get  → real Azure GET
        // SafeActions:EnableAzureMonitorReadExecutions=true    → azure_monitor_query → real KQL query
        // SafeActions:EnableRealHttpProbe=true                 → http_probe          → real probe
        // Everything else (or flags=false)                     → dry-run
        services.AddSingleton<IActionExecutor, RoutingActionExecutor>();

        // ── Policy (default = allow-all; swap per-tenant later) ─────
        services.AddSingleton<ISafeActionPolicy, DefaultSafeActionPolicy>();

        // ── Tenant execution policy (strict: empty/missing = DENY) ──
        services.AddSingleton<ITenantExecutionPolicy, ConfigDrivenTenantExecutionPolicy>();

        // ── ActionType catalog (allow-all when config empty) ────────
        services.AddSingleton<IActionTypeCatalog, ConfigActionTypeCatalog>();

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
}

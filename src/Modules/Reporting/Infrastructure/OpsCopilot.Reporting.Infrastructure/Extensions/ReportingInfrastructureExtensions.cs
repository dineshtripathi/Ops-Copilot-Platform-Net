using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.BuildingBlocks.Contracts.Packs;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Application.Services;
using OpsCopilot.Reporting.Infrastructure.AzureChange;
using OpsCopilot.Reporting.Infrastructure.McpClient;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Queries;
using OpsCopilot.Reporting.Infrastructure.ServiceBus;

namespace OpsCopilot.Reporting.Infrastructure.Extensions;

public static class ReportingInfrastructureExtensions
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── McpHost client (shares one child process across all Reporting adapters) ─
        var mcpOptions = BuildMcpHostOptions(configuration);
        services.AddSingleton(mcpOptions);
        services.AddSingleton<ReportingMcpHostClient>();
        services.AddSingleton<IReportingMcpHostClient>(sp => sp.GetRequiredService<ReportingMcpHostClient>());

        var connectionString = configuration.GetConnectionString("Sql")
                            ?? configuration["SQL_CONNECTION_STRING"]
                            ?? throw new InvalidOperationException(
                                "SQL connection string not configured. " +
                                "Set ConnectionStrings:Sql or SQL_CONNECTION_STRING.");

        services.AddDbContext<ReportingReadDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            }));

        services.AddScoped<IReportingQueryService, ReportingQueryService>();
        services.AddSingleton<IPlatformReportingQueryService, PlatformReportingQueryService>();
        services.AddScoped<IAgentRunsReportingQueryService, AgentRunsReportingQueryService>();
        // Slice 98: deterministic evidence-quality evaluator — stateless, no I/O
        services.AddSingleton<IEvidenceQualityEvaluator, EvidenceQualityEvaluator>();
        var sbNamespace = configuration["Reporting:ServiceBus:FullyQualifiedNamespace"];
        if (!string.IsNullOrWhiteSpace(sbNamespace))
        {
            services.AddSingleton<IServiceBusEvidenceProvider>(sp =>
            {
                var tenantId   = configuration["Reporting:ServiceBus:TenantId"];
                var credOptions = string.IsNullOrWhiteSpace(tenantId)
                    ? new DefaultAzureCredentialOptions()
                    : new DefaultAzureCredentialOptions { TenantId = tenantId };
                var adminClient = new ServiceBusAdministrationClient(
                    sbNamespace, new DefaultAzureCredential(credOptions));
                var queueSource = new ServiceBusQueueInfoSource(adminClient);
                return new AzureServiceBusEvidenceProvider(
                    queueSource,
                    sp.GetRequiredService<ILogger<AzureServiceBusEvidenceProvider>>());
            });
        }
        else
        {
            services.AddSingleton<IServiceBusEvidenceProvider, NullServiceBusEvidenceProvider>();
        }

        var azSubId = configuration["Reporting:AzureChange:SubscriptionId"];
        if (!string.IsNullOrWhiteSpace(azSubId))
        {
            services.AddSingleton<IAzureChangeEvidenceProvider>(sp =>
            {
                var deploymentSource = new McpDeploymentSource(
                    sp.GetRequiredService<ReportingMcpHostClient>(),
                    sp.GetRequiredService<ILogger<McpDeploymentSource>>());
                return new AzureChangeEvidenceProvider(
                    deploymentSource,
                    sp.GetRequiredService<ILogger<AzureChangeEvidenceProvider>>());
            });
        }
        else
        {
            services.AddSingleton<IAzureChangeEvidenceProvider, NullAzureChangeEvidenceProvider>();
        }

        // Slice 94: connectivity evidence — reads from ReportingReadDbContext, no external SDK, always registered
        services.AddScoped<IConnectivityEvidenceProvider, ConnectivityEvidenceProvider>();

        // Slice 95: auth/identity evidence — reads from ReportingReadDbContext, no external SDK, always registered
        services.AddScoped<IAuthEvidenceProvider, AuthEvidenceProvider>();

        // Slice 107: live App Insights / Azure Monitor evidence via governed pack execution
        services.AddScoped<IObservabilityEvidenceProvider, ObservabilityEvidenceProvider>();
        services.AddScoped<ITenantEstateProvider, McpTenantEstateProvider>();
        services.AddScoped<ITenantResourceInventoryProvider, McpTenantResourceInventoryProvider>();

        // Slice 97: deterministic proposal drafting — pure Application layer logic, no Azure SDK, always registered
        services.AddScoped<IProposalDraftingService, DeterministicProposalEngine>();

        // Slice 99: deterministic decision-pack builder — stateless, no I/O
        services.AddSingleton<IDecisionPackBuilder, DecisionPackBuilder>();

        return services;
    }

    private static McpHostOptions BuildMcpHostOptions(IConfiguration configuration)
    {
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

        // McpArm:TimeoutSeconds lets ARM-based calls (list_subscriptions, list_resource_groups, …)
        // use a larger budget than the KQL timeout — first call includes McpHost process startup.
        var timeoutRaw = configuration["McpArm:TimeoutSeconds"]
                      ?? configuration["McpKql:TimeoutSeconds"]
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

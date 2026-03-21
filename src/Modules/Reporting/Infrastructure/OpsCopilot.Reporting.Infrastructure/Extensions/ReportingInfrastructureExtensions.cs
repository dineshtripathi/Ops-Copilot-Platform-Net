using Azure.Identity;
using Azure.Messaging.ServiceBus.Administration;
using Azure.ResourceManager;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.Reporting.Application.Abstractions;
using OpsCopilot.Reporting.Application.Services;
using OpsCopilot.Reporting.Infrastructure.AzureChange;
using OpsCopilot.Reporting.Infrastructure.Persistence;
using OpsCopilot.Reporting.Infrastructure.Queries;
using OpsCopilot.Reporting.Infrastructure.ServiceBus;

namespace OpsCopilot.Reporting.Infrastructure.Extensions;

public static class ReportingInfrastructureExtensions
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
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
                var tenantId    = configuration["Reporting:AzureChange:TenantId"];
                var credOptions = string.IsNullOrWhiteSpace(tenantId)
                    ? new DefaultAzureCredentialOptions()
                    : new DefaultAzureCredentialOptions { TenantId = tenantId };
                var armClient        = new ArmClient(new DefaultAzureCredential(credOptions));
                var deploymentSource = new AzureDeploymentSource(armClient);
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

        // Slice 97: deterministic proposal drafting — pure Application layer logic, no Azure SDK, always registered
        services.AddScoped<IProposalDraftingService, DeterministicProposalEngine>();

        // Slice 99: deterministic decision-pack builder — stateless, no I/O
        services.AddSingleton<IDecisionPackBuilder, DecisionPackBuilder>();

        return services;
    }
}

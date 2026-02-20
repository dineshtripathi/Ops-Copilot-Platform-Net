using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Infrastructure.McpClient;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;

namespace OpsCopilot.AgentRuns.Infrastructure.Extensions;

public static class AgentRunsInfrastructureExtensions
{
    /// <summary>
    /// Registers EF Core (SQL Server), IAgentRunRepository, and the typed
    /// HttpClient for IKqlToolClient (→ McpHost /mcp/tools/kql_query).
    ///
    /// Required configuration keys:
    ///   SQL_CONNECTION_STRING — Azure SQL connection string.
    ///     In Container Apps: injected via Key Vault reference app setting.
    ///     For local dev: set as environment variable.
    ///   MCP_HOST_BASEURL — Base URL of the McpHost container app.
    /// </summary>
    public static IServiceCollection AddAgentRunsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connStr = configuration["SQL_CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "SQL_CONNECTION_STRING is not configured. " +
                "Set it as an environment variable or Key Vault reference.");

        services.AddDbContext<AgentRunsDbContext>(options =>
            options.UseSqlServer(connStr, sql =>
                sql.EnableRetryOnFailure(maxRetryCount: 3)));

        services.AddScoped<IAgentRunRepository, SqlAgentRunRepository>();

        var mcpBaseUrl = configuration["MCP_HOST_BASEURL"]
            ?? throw new InvalidOperationException(
                "MCP_HOST_BASEURL is not configured. " +
                "Set it to the McpHost container app base URL.");

        services.AddHttpClient<IKqlToolClient, McpHttpKqlToolClient>(client =>
        {
            client.BaseAddress = new Uri(mcpBaseUrl);
            client.Timeout     = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}

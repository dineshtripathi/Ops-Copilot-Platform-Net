using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Infrastructure.McpClient;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;

namespace OpsCopilot.AgentRuns.Infrastructure.Extensions;

public static class AgentRunsInfrastructureExtensions
{
    /// <summary>
    /// Registers EF Core (SQL Server), IAgentRunRepository, and the
    /// MCP stdio client for IKqlToolClient (→ McpHost child process).
    ///
    /// Required configuration keys:
    ///   SQL_CONNECTION_STRING — Azure SQL connection string.
    ///     In Container Apps: injected via Key Vault reference app setting.
    ///     For local dev: set as environment variable.
    ///
    /// Optional configuration keys:
    ///   MCP_KQL_SERVER_COMMAND  — Full command to start McpHost. Parsed as
    ///     "executable arg1 arg2 …". Defaults to:
    ///       "dotnet run --project src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"
    ///     For production Container Apps override to:
    ///       "dotnet /app/OpsCopilot.McpHost.dll"
    ///   MCP_KQL_SERVER_WORKDIR  — Working directory for the child process.
    ///     Auto-discovered from solution root when omitted in dev.
    ///   MCP_KQL_TIMEOUT_SECONDS — Per-call timeout in seconds. Default: 30.
    /// </summary>
    public static IServiceCollection AddAgentRunsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── SQL / EF Core ─────────────────────────────────────────────────────
        var connStr = configuration["SQL_CONNECTION_STRING"]
            ?? throw new InvalidOperationException(
                "SQL_CONNECTION_STRING is not configured. " +
                "Set it as an environment variable or Key Vault reference.");

        services.AddDbContext<AgentRunsDbContext>(options =>
            options.UseSqlServer(connStr, sql =>
                sql.EnableRetryOnFailure(maxRetryCount: 3)));

        services.AddScoped<IAgentRunRepository, SqlAgentRunRepository>();

        // ── MCP stdio client ──────────────────────────────────────────────────
        var mcpOptions = BuildMcpKqlServerOptions(configuration);

        // Singleton: one child process shared for the application lifetime.
        services.AddSingleton(mcpOptions);
        services.AddSingleton<IKqlToolClient>(sp =>
            new McpStdioKqlToolClient(
                mcpOptions,
                sp.GetRequiredService<ILogger<McpStdioKqlToolClient>>()));

        return services;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds <see cref="McpKqlServerOptions"/> from configuration.
    /// Supports a flat command string via <c>MCP_KQL_SERVER_COMMAND</c>:
    ///   first token → Executable, remaining tokens → Arguments.
    /// Paths with spaces are not supported in the flat command string;
    /// use the default and override <c>WorkingDirectory</c> instead.
    /// </summary>
    private static McpKqlServerOptions BuildMcpKqlServerOptions(IConfiguration configuration)
    {
        var cmdStr     = configuration["MCP_KQL_SERVER_COMMAND"];
        var workDir    = configuration["MCP_KQL_SERVER_WORKDIR"];
        var timeoutStr = configuration["MCP_KQL_TIMEOUT_SECONDS"];
        var timeout    = int.TryParse(timeoutStr, out var t) ? t : 30;

        if (!string.IsNullOrWhiteSpace(cmdStr))
        {
            // Parse flat command string: "dotnet run --project foo" →
            //   Executable = "dotnet", Arguments = ["run", "--project", "foo"]
            var tokens = cmdStr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new McpKqlServerOptions
            {
                Executable       = tokens[0],
                Arguments        = tokens[1..],
                WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
                TimeoutSeconds   = timeout,
            };
        }

        // Defaults: dotnet run from solution root (local dev).
        return new McpKqlServerOptions
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
            TimeoutSeconds   = timeout,
        };
    }
}

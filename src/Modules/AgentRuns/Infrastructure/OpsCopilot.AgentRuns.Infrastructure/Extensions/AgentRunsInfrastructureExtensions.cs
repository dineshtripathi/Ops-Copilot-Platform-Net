using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Infrastructure.McpClient;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.AgentRuns.Infrastructure.Sessions;

namespace OpsCopilot.AgentRuns.Infrastructure.Extensions;

public static class AgentRunsInfrastructureExtensions
{
    /// <summary>
    /// Registers EF Core (SQL Server), IAgentRunRepository, and the
    /// MCP stdio client for IKqlToolClient (→ McpHost child process).
    ///
    /// ── SQL connection string ───────────────────────────────────────────────
    /// Resolved from (first non-empty wins):
    ///   ConnectionStrings:Sql  — standard dotnet config section / User Secret
    ///   SQL_CONNECTION_STRING  — legacy flat env var (backward compat)
    ///
    /// ── MCP KQL server ─────────────────────────────────────────────────────
    /// Resolved from (first non-empty wins):
    ///   McpKql:ServerCommand   — config section (appsettings / User Secrets / Key Vault)
    ///                            Double-underscore env var: McpKql__ServerCommand
    ///   MCP_KQL_SERVER_COMMAND — legacy flat env var (backward compat)
    ///
    ///   McpKql:TimeoutSeconds  — config section
    ///   MCP_KQL_TIMEOUT_SECONDS— legacy flat env var
    ///
    ///   McpKql:WorkDir         — override working directory for child process
    ///   MCP_KQL_SERVER_WORKDIR — legacy flat env var
    ///
    /// Default ServerCommand (when none of the above are set):
    ///   "dotnet run --project src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj"
    ///
    /// Production override example (Container Apps app setting):
    ///   McpKql__ServerCommand=dotnet /app/OpsCopilot.McpHost.dll
    /// </summary>
    public static IServiceCollection AddAgentRunsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ── SQL / EF Core ─────────────────────────────────────────────────────
        // Accept both the standard section key and the legacy flat env var.
        var connStr = configuration["ConnectionStrings:Sql"]
                   ?? configuration["SQL_CONNECTION_STRING"]
                   ?? throw new InvalidOperationException(
                       "SQL connection string is not configured. " +
                       "Set 'ConnectionStrings:Sql' via User Secrets or Key Vault, " +
                       "or set the 'SQL_CONNECTION_STRING' environment variable. " +
                       "For local development, ensure ASPNETCORE_ENVIRONMENT=Development " +
                       "so User Secrets are loaded.");

        services.AddDbContext<AgentRunsDbContext>(options =>
            options.UseSqlServer(connStr, sql =>
            {
                sql.EnableRetryOnFailure(maxRetryCount: 3);
                // Keep __EFMigrationsHistory in the same schema as the module tables.
                // Without this, EF defaults to dbo which causes history mismatches.
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "agentRuns");
            }));

        services.AddScoped<IAgentRunRepository, SqlAgentRunRepository>();
        services.AddScoped<BuildingBlocks.Contracts.AgentRuns.IAgentRunCreator, Adapters.AgentRunCreatorAdapter>();

        // ── Session store (in-memory for MVP, swap for Redis later) ───────
        // WARNING: Sessions are lost on process restart and are NOT shared
        // across multiple host instances. Replace with Redis/SQL-backed
        // implementation before multi-replica or production deployment.
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<ISessionStore, InMemorySessionStore>();

        // ── MCP stdio client ──────────────────────────────────────────────────
        var mcpOptions = BuildMcpKqlServerOptions(configuration);

        // Singleton: one child process shared for the application lifetime.
        services.AddSingleton(mcpOptions);
        services.AddSingleton<IKqlToolClient>(sp =>
            new McpStdioKqlToolClient(
                mcpOptions,
                sp.GetRequiredService<ILogger<McpStdioKqlToolClient>>()));

        // Runbook search MCP client — same McpHost binary, own child process.
        services.AddSingleton<IRunbookSearchToolClient>(sp =>
            new McpStdioRunbookToolClient(
                mcpOptions,
                sp.GetRequiredService<ILogger<McpStdioRunbookToolClient>>()));

        return services;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds <see cref="McpKqlServerOptions"/> from configuration.
    ///
    /// Priority for each value (first non-empty wins):
    ///   ServerCommand : McpKql:ServerCommand → MCP_KQL_SERVER_COMMAND → built-in default
    ///   TimeoutSeconds: McpKql:TimeoutSeconds → MCP_KQL_TIMEOUT_SECONDS → 30
    ///   WorkingDir    : McpKql:WorkDir        → MCP_KQL_SERVER_WORKDIR  → auto-discover
    ///
    /// Flat command strings are parsed into Executable + Arguments by splitting
    /// on whitespace.  Paths containing spaces are not supported in the flat
    /// command string; use the built-in default and override only WorkingDir.
    /// </summary>
    private static McpKqlServerOptions BuildMcpKqlServerOptions(IConfiguration configuration)
    {
        // ── ServerCommand ───────────────────────────────────────────────────
        // Section key first (double-underscore env var: McpKql__ServerCommand),
        // then legacy flat key (MCP_KQL_SERVER_COMMAND), then built-in default.
        var cmdStr = configuration["McpKql:ServerCommand"]
                  ?? configuration["MCP_KQL_SERVER_COMMAND"];

        // ── WorkingDirectory ────────────────────────────────────────────────
        var workDir = configuration["McpKql:WorkDir"]
                   ?? configuration["MCP_KQL_SERVER_WORKDIR"];

        // ── TimeoutSeconds ──────────────────────────────────────────────────
        var timeoutStr = configuration["McpKql:TimeoutSeconds"]
                      ?? configuration["MCP_KQL_TIMEOUT_SECONDS"];
        var timeout = int.TryParse(timeoutStr, out var t) ? t : 30;

        if (!string.IsNullOrWhiteSpace(cmdStr))
        {
            // Parse flat command string: "dotnet /app/McpHost.dll" →
            //   Executable = "dotnet", Arguments = ["/app/McpHost.dll"]
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

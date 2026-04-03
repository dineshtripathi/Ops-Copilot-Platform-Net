using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpsCopilot.AgentRuns.Application.Abstractions;
using OpsCopilot.AgentRuns.Domain.Repositories;
using OpsCopilot.AgentRuns.Infrastructure.AI;
using OpsCopilot.AgentRuns.Infrastructure.McpClient;
using OpsCopilot.BuildingBlocks.Contracts.Governance;
using OpsCopilot.Governance.Application.Services;
using OpsCopilot.AgentRuns.Infrastructure.Memory;
using OpsCopilot.AgentRuns.Infrastructure.Persistence;
using OpsCopilot.AgentRuns.Infrastructure.Routing;
using OpsCopilot.AgentRuns.Infrastructure.Sessions;
using StackExchange.Redis;

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
        services.AddScoped<IModelRoutingPolicy,   TenantConfigModelRoutingPolicy>();

        // SQL-backed token tracking — overrides Governance module's InMemory fallback.
        // Last registration wins: GovernanceApplicationExtensions (registered first in ApiHost)
        // registers InMemoryTokenUsageAccumulator; these scoped registrations replace it.
        services.AddScoped<ISessionTokenQuery, AgentRunSessionTokenQuery>();
        services.AddScoped<ITokenUsageAccumulator, SqlTokenUsageAccumulator>();
        // IPromptVersionService is registered by AddPromptingModule (Prompting.Infrastructure)

        // ── Session store (config-driven: InMemory or Redis) ──────────────
        // Provider selection: AgentRuns:SessionStore:Provider
        //   "Redis"    → StackExchange.Redis IConnectionMultiplexer + RedisSessionStore
        //   "InMemory" → default; process-local ConcurrentDictionary (dev/test)
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        var sessionProvider = configuration["AgentRuns:SessionStore:Provider"];

        if (string.Equals(sessionProvider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            var redisConn = configuration["AgentRuns:SessionStore:ConnectionString"]
                         ?? throw new InvalidOperationException(
                             "AgentRuns:SessionStore:Provider is 'Redis' but no ConnectionString is configured. "
                             + "Set 'AgentRuns:SessionStore:ConnectionString' via User Secrets, Key Vault, or environment variable.");

            services.AddSingleton<IConnectionMultiplexer>(
                ConnectionMultiplexer.Connect(redisConn));
            services.AddSingleton<ISessionStore, RedisSessionStore>();
        }
        else
        {
            // Default: in-memory — suitable for local dev and single-instance.
            services.AddSingleton<ISessionStore, InMemorySessionStore>();
        }

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

        // ── Incident recall ───────────────────────────────────────────────────
        // Always register the concrete dependencies so both FallbackIncidentMemoryService
        // and HybridIncidentMemoryService can be resolved regardless of which is the
        // active IIncidentMemoryService binding.
        services.AddScoped<SqlIncidentMemoryService>();
        services.AddScoped<LiveKqlIncidentMemoryService>();
        services.AddScoped<RagBackedIncidentMemoryService>();

        // AI-primary path (§6.1 Hard Invariant): when a production vector backend is
        // configured (AzureAISearch or Qdrant), use HybridIncidentMemoryService which
        // chains SQL → RAG → KQL.  For local / InMemory-vector dev, fall back to the
        // simpler SQL → KQL FallbackIncidentMemoryService.
        var ragBackend = configuration["Rag:VectorBackend"] ?? "InMemory";
        var useHybrid  = !string.Equals(ragBackend, "InMemory", StringComparison.OrdinalIgnoreCase);

        if (useHybrid)
        {
            services.AddScoped<IIncidentMemoryService, HybridIncidentMemoryService>();
            // Enable vector indexing so completed runs feed back into the RAG store.
            services.AddSingleton<IIncidentMemoryIndexer, RagBackedIncidentMemoryIndexer>();
        }
        else
        {
            services.AddScoped<IIncidentMemoryService, FallbackIncidentMemoryService>();
        }

        // Backward-compat: legacy explicit opt-in overrides the automatic selection above
        // (last-wins DI convention).  Operators who set AgentRuns:IncidentRecall:Enabled=true
        // on an InMemory vector backend will still get RAG-backed recall as before.
        if (bool.TryParse(configuration["AgentRuns:IncidentRecall:Enabled"], out var legacyRecallEnabled) && legacyRecallEnabled)
        {
            services.AddSingleton<IIncidentMemoryService, RagBackedIncidentMemoryService>();
            services.AddSingleton<IIncidentMemoryIndexer, RagBackedIncidentMemoryIndexer>();
        }

        // ── LLM / IChatClient (config-driven, optional) ───────────────────────
        // AI:Provider = "AzureOpenAI"  → Azure OpenAI / AI Foundry, Managed Identity or API key
        // AI:Provider = "GitHubModels" → GitHub Models endpoint, GITHUB_TOKEN / User Secret
        // AI:Provider = "" or absent   → skip; IChatClient stays unregistered → graceful degradation
        var llmOpts = configuration.GetSection("AI").Get<LlmOptions>() ?? new LlmOptions();

        if (string.Equals(llmOpts.Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(llmOpts.AzureOpenAI.Endpoint))
                throw new InvalidOperationException(
                    "AI:AzureOpenAI:Endpoint is required when AI:Provider is 'AzureOpenAI'. " +
                    "Set this value in appsettings.json, User Secrets, or Key Vault.");

            AzureOpenAIClient aoaiRaw = string.IsNullOrWhiteSpace(llmOpts.AzureOpenAI.ApiKey)
                ? new AzureOpenAIClient(new Uri(llmOpts.AzureOpenAI.Endpoint), new DefaultAzureCredential())
                : new AzureOpenAIClient(new Uri(llmOpts.AzureOpenAI.Endpoint), new AzureKeyCredential(llmOpts.AzureOpenAI.ApiKey));

            services.AddSingleton<IChatClient>(
                aoaiRaw.GetChatClient(llmOpts.AzureOpenAI.DeploymentName).AsIChatClient());
        }
        else if (string.Equals(llmOpts.Provider, "GitHubModels", StringComparison.OrdinalIgnoreCase))
        {
            var token = string.IsNullOrWhiteSpace(llmOpts.GitHubModels.Token)
                ? Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? ""
                : llmOpts.GitHubModels.Token;

            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(
                    "GitHub token is not configured for AI:Provider 'GitHubModels'. " +
                    "Set 'AI:GitHubModels:Token' via User Secrets, or set the 'GITHUB_TOKEN' environment variable.");

            var ghRaw = new AzureOpenAIClient(
                new Uri(llmOpts.GitHubModels.Endpoint),
                new AzureKeyCredential(token));

            services.AddSingleton<IChatClient>(
                ghRaw.GetChatClient(llmOpts.GitHubModels.ModelId).AsIChatClient());
        }
        // else: no provider — IChatClient unregistered; orchestrators degrade gracefully

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

        // ── ServerUrl ───────────────────────────────────────────────────────
        var serverUrl = configuration["McpKql:ServerUrl"]
                     ?? configuration["MCP_KQL_SERVER_URL"];

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
                ServerUrl        = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl,
            };
        }

        // Defaults: dotnet run from solution root (local dev).
        return new McpKqlServerOptions
        {
            WorkingDirectory = string.IsNullOrWhiteSpace(workDir) ? null : workDir,
            TimeoutSeconds   = timeout,
            ServerUrl        = string.IsNullOrWhiteSpace(serverUrl) ? null : serverUrl,
        };
    }
}

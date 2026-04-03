using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace OpsCopilot.McpHost.Tools;

/// <summary>
/// MCP tools for interacting with OpsCopilot Safe Actions via the ApiHost REST API.
///
/// Safe actions are proposed changes (restarts, scaling, config edits) that flow through
/// OpsCopilot's governance pipeline and require explicit operator approval before execution.
///
/// Config: <c>ApiHost:BaseUrl</c> — base URL of the running OpsCopilot.ApiHost instance.
/// When not configured both tools return <c>ok=false</c> with a descriptive error.
/// </summary>
[McpServerToolType]
public sealed class SafeActionsTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    // ── safe_action_list ──────────────────────────────────────────────────────

    [McpServerTool(Name = "safe_action_list")]
    [Description(
        "Lists safe action records from OpsCopilot for a given tenant, optionally " +
        "filtered by agent run ID. Safe actions are proposed changes (restarts, " +
        "scaling, config updates) awaiting operator approval.")]
    public static async Task<string> ListAsync(
        // DI-injected — NOT part of the MCP JSON schema:
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        // MCP parameters — appear in the tool's JSON schema:
        [Description("Tenant ID that owns the safe actions (required).")] string tenantId,
        [Description("Optional run ID (GUID) to filter actions for a specific agent run. " +
                     "Omit to list all recent actions for the tenant.")] string? runId = null,
        [Description("Maximum number of records to return (1–50). Defaults to 20.")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var log = loggerFactory.CreateLogger<SafeActionsTool>();

        // ── Input validation ──────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(tenantId))
            return Fail("tenantId is required.");

        if (limit < 1 || limit > 50)
            return Fail("limit must be between 1 and 50.");

        if (runId is not null && !Guid.TryParse(runId, out _))
            return Fail("runId must be a valid GUID when provided.");

        var baseUrl = configuration["ApiHost:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("ApiHost:BaseUrl is not configured. " +
                        "Set it in appsettings.json to enable safe actions.");

        // ── Call ApiHost GET /safe-actions ────────────────────────────────────
        try
        {
            var qs = new StringBuilder($"?limit={limit}");
            if (runId is not null)
                qs.Append("&runId=").Append(Uri.EscapeDataString(runId));

            var requestUrl = baseUrl.TrimEnd('/') + "/safe-actions" + qs;

            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-tenant-id", tenantId);

            var response = await client.GetAsync(requestUrl, cancellationToken);
            var body     = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("[SafeActionList] ApiHost returned {Status} — {Body}",
                    (int)response.StatusCode, body);
                return Fail($"ApiHost returned HTTP {(int)response.StatusCode}: {body}");
            }

            var actions = JsonDocument.Parse(body).RootElement;
            return JsonSerializer.Serialize(new { ok = true, tenantId, actions }, JsonOpts);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[SafeActionList] Request failed");
            return Fail(ex.Message);
        }
    }

    // ── safe_action_propose ───────────────────────────────────────────────────

    [McpServerTool(Name = "safe_action_propose")]
    [Description(
        "Proposes a new safe action in OpsCopilot. The action enters Proposed status " +
        "and requires explicit operator approval before execution. Returns the action " +
        "record ID so the operator can review and approve or reject via the UI.")]
    public static async Task<string> ProposeAsync(
        // DI-injected — NOT part of the MCP JSON schema:
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        // MCP parameters — appear in the tool's JSON schema:
        [Description("Tenant ID that will own the safe action (required).")] string tenantId,
        [Description("Run ID (GUID) of the agent run proposing the action.")] string runId,
        [Description("Action type identifier (e.g., restart_service, scale_out, update_config_setting).")] string actionType,
        [Description("JSON payload describing the proposed change. Must be valid JSON.")] string proposedPayloadJson,
        [Description("Optional JSON payload describing how to roll back this action if approved.")] string? rollbackPayloadJson = null,
        [Description("Optional plain-text guidance for operators on how to roll back manually.")] string? manualRollbackGuidance = null,
        CancellationToken cancellationToken = default)
    {
        var log = loggerFactory.CreateLogger<SafeActionsTool>();

        // ── Input validation ──────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(tenantId))
            return Fail("tenantId is required.");

        if (!Guid.TryParse(runId, out _))
            return Fail("runId must be a valid GUID.");

        if (string.IsNullOrWhiteSpace(actionType))
            return Fail("actionType is required.");

        if (string.IsNullOrWhiteSpace(proposedPayloadJson))
            return Fail("proposedPayloadJson is required.");

        // Validate that proposedPayloadJson is well-formed JSON.
        try { JsonDocument.Parse(proposedPayloadJson); }
        catch { return Fail("proposedPayloadJson must be valid JSON."); }

        var baseUrl = configuration["ApiHost:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            return Fail("ApiHost:BaseUrl is not configured. " +
                        "Set it in appsettings.json to enable safe actions.");

        // ── Call ApiHost POST /safe-actions ───────────────────────────────────
        try
        {
            var requestBody = new
            {
                runId              = Guid.Parse(runId),
                actionType,
                proposedPayloadJson,
                rollbackPayloadJson,
                manualRollbackGuidance,
            };

            var jsonBody = JsonSerializer.Serialize(requestBody, JsonOpts);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("x-tenant-id", tenantId);

            var requestUrl = baseUrl.TrimEnd('/') + "/safe-actions";
            var response   = await client.PostAsync(requestUrl, content, cancellationToken);
            var body       = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("[SafeActionPropose] ApiHost returned {Status} — {Body}",
                    (int)response.StatusCode, body);
                return Fail($"ApiHost returned HTTP {(int)response.StatusCode}: {body}");
            }

            var record = JsonDocument.Parse(body).RootElement;
            return JsonSerializer.Serialize(
                new { ok = true, tenantId, actionRecordId = record.TryGetProperty("actionRecordId", out var id) ? id.GetString() : null, record },
                JsonOpts);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "[SafeActionPropose] Request failed");
            return Fail(ex.Message);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Fail(string error)
        => JsonSerializer.Serialize(new { ok = false, error }, JsonOpts);
}

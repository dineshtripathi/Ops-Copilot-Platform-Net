# API Reference

All endpoints are served from the **ApiHost** (`src/Hosts/OpsCopilot.ApiHost`) on port `5000` by default.

> **Auth**: Most endpoints require a bearer token unless `SafeActions:AllowAnonymousActorFallback` is `true` (dev only).

---

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/healthz` | Liveness probe — returns `"healthy"` |
| `POST` | `/ingest/alert` | Ingest an alert payload and create a triage session |
| `POST` | `/agent/triage` | Trigger deterministic triage for an existing session |
| `GET` | `/safe-actions` | List pending and historical safe-actions for the caller's tenant |
| `GET` | `/safe-actions/{id}` | Get a specific safe-action by ID |
| `POST` | `/safe-actions/{id}/approve` | Approve a queued safe-action (High-risk requires this) |
| `POST` | `/safe-actions/{id}/reject` | Reject a queued safe-action |
| `DELETE` | `/safe-actions/{id}` | Cancel a queued safe-action |
| `GET` | `/reports` | List triage reports for the caller's tenant |
| `GET` | `/reports/{id}` | Retrieve a specific triage report |
| `GET` | `/reports/{id}/evidence` | Retrieve raw evidence results for a report |
| `POST` | `/evaluation/run` | Run all built-in evaluation scenarios (returns pass/fail) |
| `GET` | `/evaluation/scenarios` | List available evaluation scenarios and metadata |
| `GET` | `/evaluation/{id}` | Get results for a specific evaluation run |
| `GET` | `/tenants` | List configured tenants (admin only) |
| `POST` | `/tenants` | Register a new tenant |
| `GET` | `/tenants/{tenantId}` | Get tenant details and policy |
| `PUT` | `/tenants/{tenantId}` | Update tenant policy (allowed tools, token budget) |
| `DELETE` | `/tenants/{tenantId}` | Remove a tenant configuration |

---

## Alert Ingestion — Request Shape

```jsonc
// POST /ingest/alert
{
  "tenantId": "<guid>",
  "alertName": "HighCpuUsage",
  "severity": "Sev2",
  "targetResource": "/subscriptions/.../resourceGroups/my-rg/providers/...",
  "workspaceId": "<log-analytics-workspace-guid>",
  "firedAt": "2025-01-15T10:30:00Z",
  "customProperties": {
    "k8s.namespace": "production",
    "k8s.pod": "api-server-xyz"
  }
}
```

---

## Safe-Action Request Shape

```jsonc
// Returned by GET /safe-actions/{id}
{
  "id": "<guid>",
  "tenantId": "<guid>",
  "actionType": "restart_pod",
  "riskTier": "High",
  "status": "PendingApproval",
  "requestedAt": "2025-01-15T10:31:00Z",
  "requestedBy": "triage-agent",
  "parameters": {
    "namespace": "production",
    "pod": "api-server-xyz"
  }
}
```

---

## Policy Denial Reason Codes

When a governance policy blocks an action, the response includes a `policyDenialReason` field:

| Code | Meaning |
|------|---------|
| `TOOL_NOT_ALLOWED` | The requested tool is not in the tenant's `AllowedTools` list |
| `EXECUTION_DISABLED` | `SafeActions:EnableExecution` is `false` (Mode A) |
| `TENANT_NOT_ALLOWED` | Tenant is not in `AllowedExecutionTenants` |
| `THROTTLE_EXCEEDED` | The tenant has exceeded `ExecutionThrottleMaxAttemptsPerWindow` |
| `SUBSCRIPTION_NOT_ALLOWED` | Target subscription not in `AllowedAzureSubscriptionIds` |
| `WORKSPACE_NOT_ALLOWED` | Target workspace not in `AllowedLogAnalyticsWorkspaceIds` |

---

## MCP Host — KQL Tool

The **McpHost** exposes a single MCP tool: `kql_query`.

```jsonc
// Tool input schema
{
  "query":       "string  (required) — KQL query text",
  "workspaceId": "string  (optional) — override the default workspace"
}
```

```jsonc
// Tool output
{
  "rows":            [ { "<col>": "<value>", ... } ],
  "rowCount":        42,
  "truncated":       false,
  "timespanApplied": "PT1H"
}
```

Guardrails applied by McpHost on every query:
- Row cap: **200 rows**
- Payload cap: **20 KB**
- Timeout: **5 seconds**
- Timespan injection: `| where TimeGenerated > ago(1h)` when no timespan is specified

---

## Evaluation Framework

```powershell
# Run all 11 built-in scenarios
curl -X POST http://localhost:5000/evaluation/run

# Check most-recent results
curl http://localhost:5000/evaluation/scenarios
```

Scenarios are organized by domain:

| Domain | Count | Description |
|--------|-------|-------------|
| AlertIngestion | 4 | Ingest, normalize, link, and persist alerts |
| SafeActions | 4 | Approve, reject, throttle, and deny-policy flows |
| Reporting | 3 | Report creation, evidence attachment, retrieval |

All scenarios run in-process with no external dependencies — no Azure credentials required.

---

## Further Reading

| Resource | Description |
|---|---|
| [getting-started.md](getting-started.md) | Quick start for each deployment mode |
| [configuration.md](configuration.md) | All `appsettings.json` keys and defaults |
| [governance.md](governance.md) | Governance policy design |
| [src/Hosts/OpsCopilot.McpHost/README.md](../src/Hosts/OpsCopilot.McpHost/README.md) | Full MCP host documentation |

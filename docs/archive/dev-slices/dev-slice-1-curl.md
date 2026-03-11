# Dev Slice 1 — curl smoke-test guide

This document covers the two public endpoints added in Dev Slice 1 of OpsCopilot.

---

## Prerequisites

| What | Where |
|------|-------|
| ApiHost running locally | `https://localhost:7070` (or the Container App URL) |
| SQL Server accessible | Connection string in `SQL_CONNECTION_STRING` env var |
| McpHost running locally | Base URL in `MCP_HOST_BASEURL` env var |
| Log Analytics workspace | Resource/customer ID in `WORKSPACE_ID` env var |

Start ApiHost locally:
```bash
cd src/Hosts/OpsCopilot.ApiHost
dotnet run
```

Start McpHost locally (separate terminal):
```bash
cd src/Hosts/OpsCopilot.McpHost
MCP_HOST_BASEURL=http://localhost:5071 dotnet run
```

---

## 1. POST /ingest/alert

Ingests a raw alert payload, computes a SHA-256 fingerprint, and creates a `Pending` AgentRun ledger entry.

### Request

```bash
curl -s -X POST https://localhost:7070/ingest/alert \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: contoso" \
  -H "x-correlation-id: req-abc-001" \
  --data-raw '{
    "payload": "{\"alertId\":\"alert-001\",\"severity\":\"High\",\"resourceId\":\"/subscriptions/sub-xyz/resourceGroups/prod-rg/providers/Microsoft.Compute/virtualMachines/vm-web-01\",\"description\":\"CPU usage exceeded 95% for 15 minutes\"}"
  }' | jq .
```

### Expected response (HTTP 200)

```json
{
  "runId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "fingerprint": "A3F1E9C2D8B74605F2A19E3B0C56D78F1E4A2B3D5C6E7F891023456789ABCDE0"
}
```

### Validation

- `runId` — stable `Guid` for this alert session.
- `fingerprint` — 64-character uppercase hex string. Repeat the call with the **same payload**: you get the **same fingerprint**.

---

## 2. POST /agent/triage

Runs a full AI triage cycle:  
`fingerprint → KQL via McpHost → ToolCall recorded → AgentRun completed`.

### Request

```bash
# Use the runId from the previous call (any Guid is accepted — the triage creates a new run)
curl -s -X POST https://localhost:7070/agent/triage \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: contoso" \
  --data-raw '{
    "alertPayload": "{\"alertId\":\"alert-001\",\"severity\":\"High\",\"description\":\"CPU usage exceeded 95%\"}",
    "timeRangeMinutes": 60
  }' | jq .
```

### Expected response (HTTP 200 — Completed)

```json
{
  "runId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Completed",
  "summary": "{\"rowCount\":12}",
  "citations": [
    {
      "workspaceId": "ws-prod-001",
      "executedQuery": "union traces, exceptions | where timestamp > ago(60m) | take 20",
      "timespan": "PT60M",
      "executedAtUtc": "2025-07-10T14:23:01Z"
    }
  ]
}
```

### Expected response (HTTP 200 — Degraded, when McpHost is down)

```json
{
  "runId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
  "status": "Degraded",
  "summary": null,
  "citations": [
    {
      "workspaceId": "ws-prod-001",
      "executedQuery": "union traces, exceptions | where timestamp > ago(60m) | take 20",
      "timespan": "PT60M",
      "executedAtUtc": "2025-07-10T14:23:01Z"
    }
  ]
}
```

> **Note:** Even on `Degraded`, `citations` is non-empty — the array carries evidence of what was attempted.  
> This is a core design guarantee: **no response leaves without citations**.

---

## 3. Health probes

Both hosts expose a `/healthz` endpoint:

```bash
# ApiHost
curl https://localhost:7070/healthz

# McpHost
curl http://localhost:5071/healthz
```

Both return `"healthy"` when the process is up.

---

## 4. Database verification

After calling both endpoints, verify the SQL ledger directly:

```sql
-- AgentRuns table
SELECT RunId, TenantId, AlertFingerprint, Status, CreatedAtUtc, CompletedAtUtc
FROM [agentRuns].[AgentRuns]
ORDER BY CreatedAtUtc DESC;

-- ToolCalls table (one row per kql_query call)
SELECT tc.RunId, tc.ToolName, tc.Status, tc.DurationMs, tc.CreatedAtUtc
FROM [agentRuns].[ToolCalls] tc
ORDER BY tc.CreatedAtUtc DESC;
```

A successful triage produces:
- 1 row in `AgentRuns` with `Status = 'Completed'`
- 1 row in `ToolCalls` with `ToolName = 'kql_query'`, `Status = 'Success'`

---

## 5. MCP hard-boundary verification

To confirm the hard boundary is enforced:

```bash
# ApiHost must NOT expose /mcp/tools/kql_query — expect 404
curl -s -o /dev/null -w "%{http_code}" \
  -X POST https://localhost:7070/mcp/tools/kql_query \
  -H "Content-Type: application/json" \
  -d '{}'
# Expected: 404

# McpHost exposes it — expect 200 or 400 (validation error)
curl -s -o /dev/null -w "%{http_code}" \
  -X POST http://localhost:5071/mcp/tools/kql_query \
  -H "Content-Type: application/json" \
  -d '{}'
# Expected: 400 (missing required fields)
```

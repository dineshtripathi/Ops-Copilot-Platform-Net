# OpsCopilot.McpHost

A **proper Model Context Protocol (MCP) tool server** using the official `ModelContextProtocol`
C# SDK. Transport: **stdio** (JSON-RPC over stdin/stdout).

## What it exposes

| Tool | Description |
|------|-------------|
| `kql_query` | Execute KQL against Azure Log Analytics; returns rows + citation metadata |

### kql_query input schema

```json
{
  "type": "object",
  "properties": {
    "workspaceId": {
      "type": "string",
      "description": "Log Analytics workspace GUID (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx)"
    },
    "kql": {
      "type": "string",
      "description": "KQL query to execute"
    },
    "timespan": {
      "type": "string",
      "description": "ISO 8601 duration, e.g. PT2H (2h), P1D (1 day), PT30M (30 min)"
    }
  },
  "required": ["workspaceId", "kql", "timespan"]
}
```

### kql_query response shape (JSON string)

```jsonc
{
  "ok": true,
  "workspaceId": "12345678-...",
  "executedQuery": "union traces ...",
  "timespan": "PT2H",
  "executedAtUtc": "2026-02-20T14:00:00.000+00:00",
  "status": "Success",
  "tables": [
    {
      "name": "PrimaryResult",
      "columns": [
        { "name": "timestamp", "type": "datetime" },
        { "name": "message",   "type": "string" }
      ],
      "rows": [
        ["2026-02-20T13:55:01.000+00:00", "NullReferenceException in Worker"],
        ["2026-02-20T13:54:42.000+00:00", "CPU spike detected"]
      ]
    }
  ],
  "error": null
}
```

On failure `ok` is `false`, `tables` is `[]`, and `error` contains `"[ErrorType] message"`.

---

## Running locally

### Prerequisites

- .NET 10 SDK
- Azure CLI (`az`) — for `az login`

### Steps

```bash
# 1. Authenticate (DefaultAzureCredential picks this up)
az login

# 2. Run the MCP server (stays running, waits for MCP client on stdin)
dotnet run --project src/Hosts/OpsCopilot.McpHost
```

The process writes nothing to stdout until an MCP client connects.
All application logs go to **stderr**.

### Environment variables

| Variable | Required | Description |
|----------|----------|-------------|
| `WORKSPACE_ID` | Optional | Default workspace GUID for quick testing |
| `AZURE_CLIENT_ID` | Optional | Service principal / managed identity client ID |
| `AZURE_TENANT_ID` | Optional | Override tenant for DefaultAzureCredential |

---

## How an MCP client connects

Any MCP-compatible client uses the stdio transport with this server descriptor:

```json
{
  "command": "dotnet",
  "args": ["run", "--project", "src/Hosts/OpsCopilot.McpHost"]
}
```

For production Container Apps deployment:

```json
{
  "command": "/app/OpsCopilot.McpHost"
}
```

---

## Testing manually (no MCP client)

You can send raw MCP JSON-RPC directly to the process stdin:

```bash
# In one terminal, start the server
dotnet run --project src/Hosts/OpsCopilot.McpHost <<'EOF'
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test","version":"0.1"}}}
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
EOF
```

Expected: the `tools/list` response includes a `kql_query` tool entry.

---

## Contract tests

The `tests/McpContractTests/OpsCopilot.Mcp.ContractTests/` project starts this server
as a child process and validates the MCP tool registration without requiring Azure
credentials:

```bash
dotnet test tests/McpContractTests/OpsCopilot.Mcp.ContractTests
```

---

## Architecture note — ApiHost integration (next slice)

The `AgentRuns.Infrastructure` project currently contains `McpHttpKqlToolClient`
which POSTs to a REST endpoint (`/mcp/tools/kql_query`). That was a pre-MCP
placeholder. In the next slice, it will be replaced by an `McpClientKqlToolClient`
that starts this process via `StdioClientTransport` and calls the `kql_query` tool
using the proper MCP protocol.

**The MCP boundary is preserved**: ApiHost will never reference `Azure.Monitor.Query`
or call Log Analytics directly — the call always goes through this server.

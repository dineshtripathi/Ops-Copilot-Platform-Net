# Security Policy

This document describes the security model, threat boundaries, and responsible disclosure process for OpsCopilot.

---

## Table of Contents

- [Security Model Overview](#security-model-overview)
- [Deployment Modes & Trust Boundaries](#deployment-modes--trust-boundaries)
- [Execution Danger Zone — SafeActions](#execution-danger-zone--safeactions)
- [Governance & Policy Enforcement](#governance--policy-enforcement)
- [Policy Denial Reason Codes](#policy-denial-reason-codes)
- [Configuration Hardening](#configuration-hardening)
- [MCP Hard Boundary](#mcp-hard-boundary)
- [Threat Model Summary](#threat-model-summary)
- [Dependency Security](#dependency-security)
- [Responsible Disclosure](#responsible-disclosure)
- [License](#license)

---

## Security Model Overview

OpsCopilot is an AI-powered operations platform that can **read telemetry**, **search runbooks**, and **execute remediation actions** against live infrastructure. Security is enforced through multiple independent layers:

1. **Deployment Mode** — controls what capabilities are enabled at all
2. **Tenant Execution Policy** — per-tenant allow/deny for execution
3. **Governance Resolution** — 3-tier policy (SQL → config override → defaults) determining allowed tools, token budgets, and session TTL
4. **Action Type Catalog** — classifies actions by risk level
5. **Idempotency & Replay Guard** — prevents duplicate execution
6. **Throttling** — rate-limits execution attempts

Each layer is independent — a request must pass **all** layers to execute.

---

## Deployment Modes & Trust Boundaries

| Mode | Name | What's Enabled | Trust Level |
|---|---|---|---|
| **A** | Local Dev | All execution off. Read-only stubs. Safe for development. | Untrusted |
| **B** | Azure Read-Only | Probes + AzureMonitor reads. No mutations. | Partially trusted |
| **C** | Controlled Execution | Full execution with approvals, governance, and throttling. | Trusted (with guardrails) |

**Mode A** is the default in `appsettings.Development.json` — all dangerous flags are `false`:

```json
{
  "SafeActions": {
    "EnableExecution": false,
    "EnableRealHttpProbe": false,
    "EnableAzureReadExecutions": false,
    "EnableAzureMonitorReadExecutions": false
  }
}
```

> **Rule:** Never set `EnableExecution: true` without also configuring tenant allow-lists, governance policies, and throttling.

---

## Execution Danger Zone — SafeActions

The SafeActions module is the **only** path through which OpsCopilot can mutate external infrastructure. Every execution request passes through this guard chain:

```
Request
  │
  ├─ EnableExecution == false?  ──→ 501 Not Implemented
  │
  ├─ Tenant execution policy    ──→ 400 tenant_not_authorized_for_action
  │     (ConfigDrivenTenantExecutionPolicy)
  │
  ├─ Governance check           ──→ 400 governance_tool_denied
  │     (AllowedTools allowlist)       governance_budget_exceeded
  │     (GovernanceDenialMapper)
  │
  ├─ Action type check          ──→ 400 action_type_not_allowed
  │     (ConfigActionTypeCatalog)
  │
  ├─ Idempotency/Replay guard   ──→ 409 Conflict (duplicate)
  │
  ├─ Throttle check             ──→ 429 Too Many Requests + Retry-After
  │
  └─ Execute ✓
```

### Critical Configuration Keys

| Key | Type | Default (Dev) | Impact |
|---|---|---|---|
| `SafeActions:EnableExecution` | `bool` | `false` | Master kill-switch. `false` = 501 for all executions. |
| `SafeActions:EnableRealHttpProbe` | `bool` | `false` | Controls live HTTP probes. |
| `SafeActions:EnableAzureReadExecutions` | `bool` | `false` | Controls Azure resource read operations. |
| `SafeActions:EnableAzureMonitorReadExecutions` | `bool` | `false` | Controls Azure Monitor KQL queries. |
| `SafeActions:AllowedAzureSubscriptionIds` | `string[]` | `[]` | Subscription allowlist. Empty = none allowed. |
| `SafeActions:AllowedLogAnalyticsWorkspaceIds` | `string[]` | `[]` | Workspace allowlist. Empty = none allowed. |
| `SafeActions:AllowedExecutionTenants` | `object` | `{}` | Per-tenant execution enablement. Empty = none allowed. |
| `SafeActions:EnableExecutionThrottling` | `bool` | `false` | Enables rate limiting on execution attempts. |
| `SafeActions:ExecutionThrottleWindowSeconds` | `int` | `60` | Sliding window for throttle counting. |
| `SafeActions:ExecutionThrottleMaxAttemptsPerWindow` | `int` | `5` | Max execution attempts per window before 429. |

### Action Types by Risk Level

| Action Type | Risk Level | Default State |
|---|---|---|
| `restart_pod` | **High** | Enabled |
| `http_probe` | Low | Enabled |
| `dry_run` | Low | Enabled |
| `azure_resource_get` | Medium | Enabled |
| `azure_monitor_query` | Medium | Enabled |

---

## Governance & Policy Enforcement

Governance controls **which tools** a tenant can use, **how many tokens** they can spend per session, and **session lifetime**.

### 3-Tier Resolution

1. **SQL-backed tenant config** (via `ITenantConfigResolver`) — highest priority
2. **Config-file overrides** (`Governance:TenantOverrides` section) — middle priority
3. **Global defaults** (`Governance:Defaults` section) — lowest priority

The resolved output is `ResolvedGovernanceOptions`:

| Property | Type | Default |
|---|---|---|
| `AllowedTools` | `List<string>` | `["kql_query", "runbook_search"]` |
| `TokenBudget` | `int?` | `null` (unlimited) |
| `SessionTtlMinutes` | `int` | `30` |

### GovernanceDenialMapper

If the requested tool is not in the tenant's resolved `AllowedTools`, or the session token budget is exceeded, the `GovernanceDenialMapper` raises a `PolicyDeniedException` with the appropriate reason code.

---

## Policy Denial Reason Codes

When a request is denied, the API returns HTTP 400 with a structured response containing one of these reason codes:

| Reason Code | Meaning | Typical Cause |
|---|---|---|
| `governance_tool_denied` | Requested tool is not in the tenant's AllowedTools list | Governance policy restriction |
| `governance_budget_exceeded` | Session token budget has been exhausted | Token budget limit hit |
| `action_type_not_allowed` | The action type is disabled or unrecognised | Action type not in catalog or disabled |
| `tenant_not_authorized_for_action` | Tenant is not in the execution allow-list | Missing from `AllowedExecutionTenants` |
| `throttled` | Too many execution attempts in the current window | Rate limit exceeded (429 + Retry-After) |
| `missing_tenant` | Required `x-tenant-id` header is missing | Client did not send tenant identifier |

Response format:

```json
{
  "reasonCode": "governance_tool_denied",
  "message": "Tool 'restart_pod' is not in the allowed tools list for tenant abc-123."
}
```

---

## Configuration Hardening

### Production Checklist

- [ ] `SafeActions:EnableExecution` — set to `true` **only** after all other controls are configured
- [ ] `SafeActions:AllowedExecutionTenants` — explicitly list every tenant allowed to execute
- [ ] `SafeActions:AllowedAzureSubscriptionIds` — restrict to production subscription IDs
- [ ] `SafeActions:AllowedLogAnalyticsWorkspaceIds` — restrict to production workspace IDs
- [ ] `SafeActions:EnableExecutionThrottling` — set to `true` with appropriate window/limit
- [ ] `Governance:Defaults:AllowedTools` — set minimal toolset (principle of least privilege)
- [ ] `Governance:Defaults:TokenBudget` — set a finite budget per session
- [ ] `Actor:AllowActorHeaderFallback` — set to `false` in production
- [ ] `Actor:AllowAnonymousActorFallback` — set to `false` in production
- [ ] `KeyVault:VaultUri` — point to your Azure Key Vault for secrets

### Secret Management

- **Never store secrets in `appsettings.json`** — use Azure Key Vault or environment variables.
- The `KeyVault:VaultUri` setting should point to your Key Vault instance.
- See [docs/local-dev-secrets.md](docs/local-dev-secrets.md) for local development secret management.

---

## MCP Hard Boundary

The ApiHost **never** references `Azure.Monitor.Query` or executes KQL directly. All KQL operations are isolated in the McpHost process, which communicates via MCP stdio protocol.

This separation ensures:
- The API surface has no direct Azure Monitor dependency
- KQL execution is sandboxed in a separate process
- The McpHost can be locked down or disabled independently

---

## Threat Model Summary

For the full threat model, see [docs/threat-model.md](docs/threat-model.md).

| Threat | Mitigation |
|---|---|
| Unauthorised execution against production | Multi-layer guard chain: master switch → tenant policy → governance → action type → idempotency → throttle |
| Tenant impersonation | `x-tenant-id` header validation; `AllowActorHeaderFallback: false` in production |
| Token budget exhaustion | Per-session budget enforcement via governance resolution |
| Replay attacks | Idempotency guard rejects duplicate execution requests (409 Conflict) |
| Denial of service via execution spam | Throttling with sliding window (429 + Retry-After header) |
| Cross-tenant data leakage | Module-level tenant isolation; governance resolved per-tenant |
| Uncontrolled AI tool usage | Governance AllowedTools allowlist restricts which tools the agent can invoke |
| KQL injection | KQL isolated in McpHost process; ApiHost has no KQL dependency |
| Secret exposure | Azure Key Vault for production secrets; no secrets in config files |

---

## Dependency Security

- Monitor NuGet dependencies for known CVEs.
- Keep the .NET SDK and runtime up to date.
- Review third-party dependencies before adoption.
- Azure SDK dependencies should track the latest stable release train.

---

## Responsible Disclosure

If you discover a security vulnerability in OpsCopilot, please report it responsibly:

**Email:** `security@placeholder.invalid`

Please include:
- A description of the vulnerability
- Steps to reproduce
- Potential impact assessment
- Any suggested mitigation

We will acknowledge receipt within **48 hours** and aim to provide a resolution timeline within **5 business days**.

> **Do not** open a public GitHub issue for security vulnerabilities. Use the email address above.

---

## License

This project is licensed under the **MIT License**.

Copyright © 2026 OpsCopilot Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

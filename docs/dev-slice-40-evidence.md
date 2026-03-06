# Dev Slice 40 — Tenant Workspace Resolution for Pack Evidence Execution

## Summary

Slice 40 introduces **tenant-aware workspace resolution** for Mode-B+ evidence execution.
Previously, `PackEvidenceExecutor` attempted to read a single global `Packs:WorkspaceId`
config key that was never populated, causing `InvalidOperationException` at runtime.

This slice replaces that broken path with `ITenantWorkspaceResolver`, which looks up the
workspace per-tenant from configuration (key `Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId`
with a global fallback at `Observability:LogAnalyticsWorkspaceId`) and validates the GUID
against `SafeActions:AllowedLogAnalyticsWorkspaceIds` when that allowlist is configured.

---

## Final Canonical Config Keys

| Key | Type | Default (prod) | Default (dev) | Notes |
|-----|------|---------------|---------------|-------|
| `Packs:EvidenceExecutionEnabled` | `bool` | `false` | `true` | Feature flag — both gates must pass |
| `Packs:DeploymentMode` | `string` | `"A"` | `"B"` | `"A"`/`"B"`/`"C"` — mode < B skips execution |
| `Packs:EvidenceMaxRows` | `int` | `50` | `50` | Per-item row cap |
| `Packs:EvidenceMaxChars` | `int` | `4000` | `4000` | Per-item JSON payload cap (chars) |
| `Packs:AzureMonitorQueryTimeoutMs` | `int` | `5000` | `5000` | **NEW** — explicitly added to both config files |

### Workspace Resolution Config (per-tenant)

| Key Pattern | Description |
|-------------|-------------|
| `Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId` | Per-tenant workspace GUID |
| `Observability:LogAnalyticsWorkspaceId` | Global fallback workspace GUID |
| `SafeActions:AllowedLogAnalyticsWorkspaceIds` | Allowlist (empty = allow any valid GUID) |

---

## Workspace Resolution Contract

**Interface**: `OpsCopilot.Packs.Application.Abstractions.ITenantWorkspaceResolver`

```csharp
WorkspaceResolutionResult Resolve(string tenantId);
```

**Result record**:

```csharp
record WorkspaceResolutionResult(bool Success, string? WorkspaceId, string? ErrorCode);
```

### Resolution algorithm

1. Look up `Tenants:{tenantId}:Observability:LogAnalyticsWorkspaceId` in `IConfiguration`
2. Fall back to `Observability:LogAnalyticsWorkspaceId` if tenant key absent
3. If value absent or whitespace → `ErrorCode = "missing_workspace"`
4. If value not a valid GUID → `ErrorCode = "missing_workspace"`

> **Note (Slice 41 reconciliation):** Steps 3 and 4 intentionally share the error code
> `missing_workspace`. Both represent "no usable workspace available" — the distinction
> between absent config and malformed GUID is visible in logs but not in the error code
> returned to callers. This is by design; a separate `invalid_workspace_format` code may
> be added in a future slice if callers need to distinguish the two cases.
5. If `SafeActions:AllowedLogAnalyticsWorkspaceIds` is non-empty and GUID not in list → `ErrorCode = "workspace_not_allowlisted"`
6. Otherwise → `Success = true`, `WorkspaceId = <resolved GUID>`

### Behaviour on resolution failure

- The executor does **not** throw or fail the triage response
- It iterates all eligible packs/collectors and adds a `PackEvidenceItem` with `ErrorMessage`
  set to the human-readable description, and an `errors[]` entry containing the raw error code
- `PackEvidenceResults` in `TriageResponse` will be non-null (contains items with errors)
- HTTP response is still `200 OK` with `status = "Completed"`

---

## Files Changed

### New

| File | Description |
|------|-------------|
| `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/ITenantWorkspaceResolver.cs` | Interface + `WorkspaceResolutionResult` record |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/TenantWorkspaceResolver.cs` | IConfiguration-backed implementation |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/TenantWorkspaceResolverTests.cs` | 6 unit tests for workspace resolver |

### Modified

| File | Change |
|------|--------|
| `src/BuildingBlocks/Contracts/.../Packs/PackEvidenceExecutionResult.cs` | Added `TenantId` param to `PackEvidenceExecutionRequest` |
| `src/Modules/Packs/Infrastructure/.../PackEvidenceExecutor.cs` | Triple-gate (mode, flag, workspace); workspace failure → per-item errors; injected `ITenantWorkspaceResolver` |
| `src/Modules/Packs/Infrastructure/.../Extensions/PacksInfrastructureExtensions.cs` | Registered `TenantWorkspaceResolver` |
| `src/Modules/AgentRuns/Presentation/.../Endpoints/AgentRunEndpoints.cs` | Passes `tenantId` from header to `PackEvidenceExecutionRequest` |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.json` | Added `Packs:AzureMonitorQueryTimeoutMs: 5000` |
| `src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json` | Added `Packs:AzureMonitorQueryTimeoutMs: 5000` |
| `src/Modules/Connectors/.../AzureMonitorObservabilityQueryExecutor.cs` | KQL-audit comment added (no changes needed — already safe) |
| `tests/Modules/Packs/.../PackEvidenceExecutorTests.cs` | Updated existing 14 tests + added 2 new (tests 15, 16) |
| `tests/Modules/AgentRuns/.../TriageEvidenceIntegrationTests.cs` | Added 2 new Slice 40 integration tests (S40.1, S40.2) |
| `docs/http/OpsCopilot.Api.http` | Added TOC entry + Section AO (4 requests: AO1–AO4) |

---

## Tests Added (Slice 40 new tests)

### `TenantWorkspaceResolverTests.cs` — 6 unit tests

| # | Test | Scenario |
|---|------|----------|
| 1 | `Resolve_MissingWorkspaceKey_ReturnsMissingWorkspace` | No config key present |
| 2 | `Resolve_InvalidGuid_ReturnsMissingWorkspace` | Key present but value is not a GUID |
| 3 | `Resolve_ValidGuid_InAllowlist_ReturnsSuccess` | Valid GUID, in allowlist |
| 4 | `Resolve_ValidGuid_NotInAllowlist_ReturnsNotAllowlisted` | Valid GUID, allowlist non-empty, GUID absent |
| 5 | `Resolve_ValidGuid_EmptyAllowlist_ReturnsSuccess` | Valid GUID, no allowlist configured |
| 6 | `Resolve_GlobalFallbackKey_UsedWhenTenantKeyAbsent` | No tenant-specific key, global fallback used |

### `PackEvidenceExecutorTests.cs` — 2 new tests (tests 15 & 16)

| # | Test | Scenario |
|---|------|----------|
| 15 | `ExecuteAsync_MissingWorkspace_ProducesPerItemErrors` | Resolver returns `missing_workspace` → per-item errors for all eligible collectors |
| 16 | `ExecuteAsync_WorkspaceNotAllowlisted_ProducesPerItemErrors` | Resolver returns `workspace_not_allowlisted` → per-item errors |

### `TriageEvidenceIntegrationTests.cs` — 2 new tests

| # | Test | Scenario |
|---|------|----------|
| S40.1 | `Slice40_ModeB_TenantIdPassedToExecutor_ReturnsResults` | Mode B, tenant configured → `tenantId` from `x-tenant-id` header forwarded to executor |
| S40.2 | `Slice40_MissingWorkspace_PerItemErrors_Returns200` | Executor returns workspace-error items → 200 OK, status=Completed, items with `errorMessage` |

---

## Build / Test Totals

```
dotnet build OpsCopilot.sln -warnaserror
  Build succeeded.  0 Warning(s)  0 Error(s)

dotnet test OpsCopilot.sln --no-build
  Governance      31/31
  Connectors      30/30
  Evaluation      15/15
  AlertIngestion  31/31
  Reporting       27/27
  Packs          123/123
  AgentRuns       81/81
  Tenancy         17/17
  SafeActions    368/368
  Integration     24/24
  McpContract      8/8
  ─────────────────────
  Total          755/755  ✓  0 failures
```

---

## Non-Negotiables Verification

| Constraint | Status |
|-----------|--------|
| No new routes | ✓ — no new endpoints |
| No DB schema changes | ✓ — no migrations |
| No breaking DTO changes | ✓ — Additive only: `PackEvidenceExecutionRequest` gained `TenantId` (default `null`); existing callers unaffected. `TenantId` is **internal** — not exposed in `PackEvidenceResultDto` or `TriageResponse` (HTTP response DTOs unchanged). |
| Mode A behavior unchanged | ✓ — mode gate still fires before resolver |
| 0 warnings / 0 errors build | ✓ |
| All tests pass | ✓ — 755/755 |

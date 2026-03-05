# Dev Slice 42 — Pack Evidence Execution Observability + Correlation (STRICT)

## Summary

Slice 42 adds **structured telemetry** and **end-to-end CorrelationId propagation** to the
pack evidence execution pipeline. Nine `System.Diagnostics.Metrics` counters under the
`OpsCopilot.Packs` meter instrument every gate and query outcome — attempt, skip,
workspace-resolution-failure, collector success/failure/truncation, and query
blocked/timeout/failure. An optional `CorrelationId` (defaulting to a new GUID when
absent) now flows through the entire evidence path, appearing in every counter tag and
structured log scope.

**Mode A behavior is unchanged.** No new routes, no schema changes, no breaking DTOs.

---

## Deliverables

### A — CorrelationId end-to-end (Mode B/C)

`PackEvidenceExecutionRequest` gained an additive `CorrelationId` property (default `null`).
The value is accepted as an optional parameter; when absent, the executor generates a GUID
via `Guid.NewGuid().ToString("N")`. The identifier is threaded through the executor into
every telemetry tag and ILogger scope. Existing callers are unaffected — the parameter has
a default value and the HTTP response surface is unchanged.

### B — Structured logging scopes

`PackEvidenceExecutor` adds `ILogger.BeginScope` with `{TenantId}`, `{ErrorCode}`,
`{CorrelationId}`, `{PackId}`, and `{CollectorId}` at appropriate instrumentation points.
No secrets or full payloads are logged — values are redact-safe identifiers only.

### C — IPacksTelemetry interface + PacksTelemetry implementation

| File | Layer | Description |
|------|-------|-------------|
| `IPacksTelemetry.cs` | Application/Abstractions | 9 void methods — one per counter |
| `PacksTelemetry.cs` | Presentation/Telemetry | Sealed class, IDisposable, `Meter("OpsCopilot.Packs", "1.0.0")`, 9 `Counter<long>` fields |

**Counter inventory:**

| Counter name | Tags |
|-------------|------|
| `packs.evidence.attempts` | mode, tenant_id, correlation_id |
| `packs.evidence.skipped` | mode, tenant_id |
| `packs.evidence.workspace_resolution_failed` | tenant_id, error_code, correlation_id |
| `packs.evidence.collector.success` | pack_id, collector_id, tenant_id, correlation_id |
| `packs.evidence.collector.failure` | pack_id, collector_id, tenant_id, error_code, correlation_id |
| `packs.evidence.collector.truncated` | pack_id, collector_id, truncate_reason, correlation_id |
| `packs.evidence.query.blocked` | pack_id, collector_id, tenant_id, correlation_id |
| `packs.evidence.query.timeout` | pack_id, collector_id, tenant_id, correlation_id |
| `packs.evidence.query.failed` | pack_id, collector_id, tenant_id, error_code, correlation_id |

### D — DI wiring

`PacksPresentationExtensions.AddPacksPresentation()` registers
`AddSingleton<IPacksTelemetry, PacksTelemetry>()`.

> **Placement note:** `PacksTelemetry` lives in the Presentation layer, matching the
> existing SafeActions telemetry placement to keep observability close to HTTP host
> composition.

### E — PackEvidenceExecutor instrumentation

12 telemetry call sites across Gates 1–3, the success path, error paths, timeout,
and truncation. Constructor accepts `IPacksTelemetry` as 7th parameter.

### F — .http Section AP (AP1–AP4)

| Request | Scenario | Expected |
|---------|----------|----------|
| AP1 | ModeB + explicit correlationId | Evidence results present, correlationId in telemetry |
| AP2 | ModeB without correlationId | Evidence runs, null correlation in telemetry |
| AP3 | ModeC + correlationId | Multi-pack evidence with correlation |
| AP4 | ModeA | Evidence skipped, packs.evidence.skipped counter fires |

### G — Dead code removal

`PackEvidenceExecutor.TruncateResult()` private method removed. Inline truncation retained
at the single call site.

---

## Files Changed

| File | Change | Type |
|------|--------|------|
| `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IPacksTelemetry.cs` | New interface — 9 telemetry methods | Created |
| `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Telemetry/PacksTelemetry.cs` | Sealed impl — Meter + 9 Counters | Created |
| `src/BuildingBlocks/Contracts/OpsCopilot.BuildingBlocks.Contracts/Packs/PackEvidenceExecutionResult.cs` | Added CorrelationId to request record | Modified |
| `src/Modules/Packs/Presentation/OpsCopilot.Packs.Presentation/Extensions/PacksPresentationExtensions.cs` | DI registration for IPacksTelemetry | Modified |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/PackEvidenceExecutor.cs` | 12 telemetry call sites + dead code removal | Modified |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PackEvidenceExecutorTests.cs` | 16 existing tests updated + 10 new telemetry tests | Modified |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/PacksTelemetryIntegrationTests.cs` | 9 integration tests using MeterListener | Created |
| `docs/http/OpsCopilot.Api.http` | Section AP (AP1–AP4) appended | Modified |
| `docs/dev-slice-42-evidence.md` | This file | Created |

---

## Test Totals

```
dotnet build OpsCopilot.sln -warnaserror
  Build succeeded.  0 Warning(s)  0 Error(s)

Packs module tests:
  Existing (updated)       16/16
  New telemetry unit       10/10
  New telemetry integration 9/9
  ─────────────────────────────
  Packs total             142/142  ✓  0 failures

Full solution:
  Governance               31/31
  Connectors               30/30
  AlertIngestion           31/31
  SafeActions             368/368
  Tenancy                  17/17
  Reporting                27/27
  AgentRuns                81/81
  Evaluation               15/15
  Packs                   142/142
  Integration              24/24
  MCP Contract              8/8
  ─────────────────────────────
  Grand total             774/774  ✓  0 failures
```

---

## Non-Negotiables Verification

| Constraint | Status |
|-----------|--------|
| No runtime behavior change (Mode A) | ✓ — Mode A path unchanged; telemetry is additive |
| No new routes | ✓ |
| No DB schema / migration changes | ✓ |
| No breaking DTO changes | ✓ — Additive only: CorrelationId with default null |
| No SafeActions changes | ✓ |
| No governance changes | ✓ |
| No Azure writes | ✓ — Counters are local metrics, no external calls |
| No secrets in logs | ✓ — Only IDs and error codes in tags/scopes |
| Mode A deterministic and offline | ✓ |
| 0 warnings / 0 errors build | ✓ |
| All Packs tests pass | ✓ — 142/142 |

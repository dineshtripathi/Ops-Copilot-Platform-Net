# Dev Slice 41 — Slice 40 Reconciliation + STRICT Hygiene (No Behavior Change)

## Summary

Slice 41 is a **docs-only reconciliation slice**. It audits the Slice 40 working tree for
out-of-scope changes, verifies the `.http` Section AO matches runtime reality, and fixes
two documentation contradictions in `dev-slice-40-evidence.md`.

**Zero runtime behavior was changed.** No production code, tests, migrations, build scripts,
or CI files were touched.

---

## Pre-Work Findings

### 1. Out-of-scope file check

The Slice 41 spec flagged `scripts/` and `docs/architecture/` as potentially modified by
Slice 40. **Result: both verified clean.**

| File | `git diff HEAD` | Action |
|------|----------------|--------|
| `scripts/Test-DependencyConformance.ps1` | Empty (zero diff) | None — no reversion needed |
| `docs/architecture/DEPENDENCY_CONFORMANCE.md` | Empty (zero diff) | None — no reversion needed |

### 2. `PackEvidenceExecutionRequest.TenantId` visibility audit

| DTO | Contains `TenantId`? | Layer |
|-----|---------------------|-------|
| `PackEvidenceExecutionRequest` | Yes (additive, default `null`) | Internal — flows header → executor |
| `PackEvidenceResultDto` | **No** (8 properties, no TenantId) | HTTP response DTO |
| `TriageResponse` | **No** (18 properties, no TenantId) | HTTP response record |

**Conclusion:** `TenantId` is internal-only. It enters via the `x-tenant-id` HTTP header,
is passed to `PackEvidenceExecutor`, but is never leaked into any HTTP response body.

### 3. `.http` Section AO verification

All four requests (AO1–AO4) verified against `AgentRunEndpoints.cs`:

| Request | Route | Header | Scenario | Matches? |
|---------|-------|--------|----------|----------|
| AO1 | `POST /agent/triage` | `x-tenant-id: contoso` | Tenant configured → results | ✓ |
| AO2 | `POST /agent/triage` | `x-tenant-id: orphan-tenant` | Missing workspace → per-item errors | ✓ |
| AO3 | `POST /agent/triage` | `x-tenant-id: blocked-tenant` | Not allowlisted → per-item errors | ✓ |
| AO4 | `POST /agent/triage` | `x-tenant-id: any` | Mode A → resolver skipped | ✓ |

**Conclusion:** Section AO is accurate. No changes needed.

---

## Contradictions Fixed in `dev-slice-40-evidence.md`

### Fix 1: Non-Negotiables — "No breaking DTO changes" row

**Before:**
> `✓ — TenantId added with default null; existing callers unaffected`

**After:**
> `✓ — Additive only: PackEvidenceExecutionRequest gained TenantId (default null);
> existing callers unaffected. TenantId is internal — not exposed in
> PackEvidenceResultDto or TriageResponse (HTTP response DTOs unchanged).`

**Rationale:** The original wording claimed "no DTO changes" while the request record
*did* gain a parameter. The updated wording is precise: it acknowledges the additive
contract change, confirms it's non-breaking, and explicitly states the HTTP response
surface is unchanged.

### Fix 2: Resolution algorithm — `missing_workspace` error code overload

**Before:** Steps 3 and 4 both specified `ErrorCode = "missing_workspace"` without
acknowledging that two distinct failure modes share one code.

**After:** Added a `> Note (Slice 41 reconciliation)` block documenting that:
- Both conditions represent "no usable workspace available"
- The distinction is visible in logs but not in the error code
- A separate `invalid_workspace_format` code may be added if callers need to distinguish

---

## Files Changed

| File | Change | Type |
|------|--------|------|
| `docs/dev-slice-40-evidence.md` | Fixed Non-Negotiables row + added error-code note | Modified |
| `docs/dev-slice-41-evidence.md` | This file | Created |

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
| No runtime behavior change | ✓ — zero production code touched |
| No new routes | ✓ |
| No DB schema changes | ✓ |
| No SafeActions changes | ✓ |
| No new NuGet packages | ✓ |
| No build script / CI changes | ✓ |
| `scripts/` untouched | ✓ — verified zero diff from HEAD |
| `docs/architecture/` untouched | ✓ — verified zero diff from HEAD |
| 0 warnings / 0 errors build | ✓ |
| All tests pass | ✓ — 755/755 |

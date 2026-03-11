# Dev Slice 55.1 Evidence — Triage Session Create/Resume + AgentRun Ledgering

## Section 1 — Pre-flight Checks (Hard Gate)

All six checks were confirmed via codebase reads before any implementation began.

| # | Check | Finding | Status |
|---|-------|---------|--------|
| 1 | Session create/resume routing | Fully implemented in `TriageOrchestrator.cs` — 4 return-site paths confirmed: allowlist-denied (~line 177), budget-denied (~line 192), tool-threw (~line 251), happy-path (~line 337). Tenant-mismatch guarded by `InvalidOperationException` check. | ✅ Routing present |
| 2 | AgentRun ledgering gaps | `AgentRun.cs` confirmed 9 properties with no token fields. `IAgentRunRepository.cs` confirmed 5 methods, `UpdateTokenUsageAsync` absent. | ❌ → ✅ Fixed by Tasks 5–6 |
| 3 | Governance guardrails | G1 allowlist deny + G2 budget deny both emit `AgentRunPolicyEvent` via `AppendPolicyEventAsync`. G3 degraded path falls through to happy-path with `sessionReasonCode` set. Session audit event on create/resume confirmed in orchestrator. | ✅ Guardrails intact |
| 4 | TriageResult/Response shape | `TriageResult.cs` confirmed 9-field record, `SessionReasonCode` absent. `TriageResponse.cs` confirmed missing field. Both records required extension. | ❌ → ✅ Fixed by Tasks 1–4 |
| 5 | Session TTL / policy seams | `SessionExpiredFallback` path confirmed — orchestrator checks `SessionExpiresAtUtc < DateTimeOffset.UtcNow` and falls through to new-session creation. TTL value flows from `ISessionStore` without orchestrator coupling. | ✅ Policy seam present |
| 6 | Test coverage | 4 session tests found (`SessionResume`, `SessionExpired_CreatesNewSession`, `SessionNotFound_CreatesNewSession`, `NoSessionId_CreatesNewSession_UsedSessionContextFalse`) — none asserted `SessionReasonCode`. | ⚠️ → ✅ Fixed by Task 7 |

---

## Section 2 — Changes

### Task 1 — `TriageResult.cs` (add `SessionReasonCode` as 10th parameter)

**Path:** `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Orchestration/TriageResult.cs`

Before: 9-field positional record ending at `bool UsedSessionContext`.

After:
```csharp
public sealed record TriageResult(
    Guid                              RunId,
    AgentRunStatus                    Status,
    string?                           SummaryJson,
    IReadOnlyList<KqlCitation>        Citations,
    IReadOnlyList<RunbookCitation>    RunbookCitations,
    Guid?                             SessionId,
    bool                              IsNewSession,
    DateTimeOffset?                   SessionExpiresAtUtc,
    bool                              UsedSessionContext,
    string                            SessionReasonCode);   // NEW — 10th param
```

---

### Task 2 — `TriageOrchestrator.cs` (wire all 4 return sites)

**Path:** `src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Orchestration/TriageOrchestrator.cs`

All 4 `return new TriageResult(...)` sites updated to pass `sessionReasonCode` as the 10th positional argument. No new control-flow changes; additive argument addition only.

| Return site | Exit path | `sessionReasonCode` value |
|-------------|-----------|---------------------------|
| ~line 177 | Allowlist denied | `"AllowlistDenied"` |
| ~line 192 | Budget denied | `"BudgetDenied"` |
| ~line 251 | Tool threw | varies (propagated from session logic) |
| ~line 337 | Happy-path | `sessionReasonCode` local variable |

---

### Task 3 — `TriageResponse.cs` (add `SessionReasonCode` at position 10)

**Path:** `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Contracts/TriageResponse.cs`

Before: 15-field record; pack fields at positions 10–15.

After: 16-field record; `string? SessionReasonCode = null` at position 10; pack fields shifted to positions 11–16. All optional pack fields retain `= null` defaults — no breaking change to existing callers.

---

### Task 4 — `AgentRunEndpoints.cs` (map `SessionReasonCode` in POST handler)

**Path:** `src/Modules/AgentRuns/Presentation/OpsCopilot.AgentRuns.Presentation/Endpoints/AgentRunEndpoints.cs`

The POST `/api/agent-runs` handler now passes `SessionReasonCode: result.SessionReasonCode` when constructing the `TriageResponse`. All optional pack fields use named-argument syntax. `MapSessionEndpoints()` (~line 248) was NOT modified.

---

### Task 5 — `AgentRun.cs` (add `RunType` + token fields)

**Path:** `src/Modules/AgentRuns/Domain/OpsCopilot.AgentRuns.Domain/Entities/AgentRun.cs`

Before: 9 properties ending with `public Guid? SessionId { get; private set; }`.

After — 4 nullable fields added after `SessionId`:
```csharp
    public Guid?   SessionId    { get; private set; }

    // Populated by UpdateTokenUsageAsync (set post-completion)
    public string? RunType      { get; private set; }
    public int?    InputTokens  { get; private set; }
    public int?    OutputTokens { get; private set; }
    public int?    TotalTokens  { get; private set; }
```

`Create()` factory and `Complete()` guard were NOT modified.

---

### Task 6 — `IAgentRunRepository.cs` (add `UpdateTokenUsageAsync` stub)

**Path:** `src/Modules/AgentRuns/Domain/OpsCopilot.AgentRuns.Domain/Repositories/IAgentRunRepository.cs`

Added after `GetRecentRunsBySessionAsync`:
```csharp
    /// <summary>Records LLM token usage for a completed run. Idempotent if called more than once.</summary>
    Task UpdateTokenUsageAsync(
        Guid runId, int inputTokens, int outputTokens, int totalTokens,
        CancellationToken ct = default);
```

No call site wired in this slice — the orchestrator does NOT call this method.

---

### Task 7 — `TriageOrchestratorTests.cs` (add `SessionReasonCode` assertions)

**Path:** `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageOrchestratorTests.cs`

4 targeted `Assert.Equal` calls added — one per session test:

| Test method | After assertion | Inserted assertion |
|-------------|----------------|--------------------|
| `SessionResume_...` | `Assert.Equal(existingSessionId, result.SessionId);` | `Assert.Equal("SessionResumed", result.SessionReasonCode);` |
| `SessionExpired_CreatesNewSession` | `Assert.Equal(newSessionId, result.SessionId);` | `Assert.Equal("SessionExpiredFallback", result.SessionReasonCode);` |
| `SessionNotFound_CreatesNewSession` | `Assert.True(result.IsNewSession);` | `Assert.Equal("SessionNotFoundFallback", result.SessionReasonCode);` |
| `NoSessionId_CreatesNewSession_UsedSessionContextFalse` | `Assert.False(result.UsedSessionContext);` | `Assert.Equal("SessionCreated", result.SessionReasonCode);` |

`CreateHappyPathRepo` helper uses `MockBehavior.Strict` with 4 setups (Create, AppendTool, AppendPolicy, Complete). `UpdateTokenUsageAsync` is NOT set up — confirming the method is not called from the orchestrator flow.

---

### Task 7b — `SqlAgentRunRepository.cs` (implement `UpdateTokenUsageAsync` stub)

**Path:** `src/Modules/AgentRuns/Infrastructure/OpsCopilot.AgentRuns.Infrastructure/Persistence/SqlAgentRunRepository.cs`

Added after `GetRecentRunsBySessionAsync` to satisfy the `IAgentRunRepository` interface contract:
```csharp
    /// <inheritdoc />
    /// <remarks>
    /// Stub — token usage persistence is not wired in this slice.
    /// The method is intentionally a no-op; rows will be updated in a future slice.
    /// </remarks>
    public Task UpdateTokenUsageAsync(
        Guid runId, int inputTokens, int outputTokens, int totalTokens,
        CancellationToken ct = default)
        => Task.CompletedTask;
```

---

## Section 3 — Gate Results

### Build gate

```
dotnet build OpsCopilot.sln -warnaserror --nologo

Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Test gate

```
dotnet test OpsCopilot.sln --nologo --no-build

Passed!  - Failed: 0, Passed:  30, Total:  30  — OpsCopilot.Modules.Connectors.Tests
Passed!  - Failed: 0, Passed:  31, Total:  31  — OpsCopilot.Modules.Governance.Tests
Passed!  - Failed: 0, Passed:  15, Total:  15  — OpsCopilot.Modules.Evaluation.Tests
Passed!  - Failed: 0, Passed:  31, Total:  31  — OpsCopilot.Modules.AlertIngestion.Tests
Passed!  - Failed: 0, Passed:  86, Total:  86  — OpsCopilot.Modules.AgentRuns.Tests
Passed!  - Failed: 0, Passed:  27, Total:  27  — OpsCopilot.Modules.Reporting.Tests
Passed!  - Failed: 0, Passed: 303, Total: 303  — OpsCopilot.Modules.Packs.Tests
Passed!  - Failed: 0, Passed:  17, Total:  17  — OpsCopilot.Modules.Tenancy.Tests
Passed!  - Failed: 0, Passed: 368, Total: 368  — OpsCopilot.Modules.SafeActions.Tests
Passed!  - Failed: 0, Passed:  24, Total:  24  — OpsCopilot.Integration.Tests
Passed!  - Failed: 0, Passed:   8, Total:   8  — OpsCopilot.Mcp.ContractTests

Total: 940 passed / 0 failed / 0 skipped  ✅  (baseline maintained)
```

---

## Section 4 — Constraints Verified

| Constraint | Status |
|-----------|--------|
| `MapSessionEndpoints()` NOT modified | ✅ |
| `MockBehavior.Strict` — `UpdateTokenUsageAsync` not called from orchestrator | ✅ |
| `AgentRun.Complete()` guard NOT modified | ✅ |
| All token fields are `int?` (nullable) | ✅ |
| `TriageResponse` optional pack fields retain `= null` defaults at positions 11–16 | ✅ |
| Exactly 4 return sites wired (no re-edit, no new sites added) | ✅ |
| No DB schema changes / no migrations | ✅ |
| No `MapSessionEndpoints()` route changes | ✅ |
| Logs contain no secrets, payloads, or connection strings | ✅ |

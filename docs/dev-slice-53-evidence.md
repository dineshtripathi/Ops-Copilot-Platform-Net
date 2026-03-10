# Dev Slice 53 — Durable Proposal Recording with Retry + Dead-Letter

## Objective

Wrap `PackSafeActionRecorder` in a decorator (`DurablePackSafeActionRecorder`) that retries only
`"Failed"` items up to a configurable number of attempts with per-attempt delays, and dead-letters
any item that exhausts all retries. All domain types, retry policy, and dead-letter store follow
Clean Architecture layer boundaries.

## Files Changed

### Created

| File | Purpose |
|------|---------|
| `src/Modules/Packs/Domain/OpsCopilot.Packs.Domain/Models/ProposalRecordingAttempt.cs` | Sealed record capturing one (failed) attempt: `AttemptId`, `TenantId`, `TriageRunId`, `PackName`, `ActionId`, `ActionType`, `ParametersJson`, `AttemptNumber`, `AttemptedAt`, `ErrorMessage`, `IsDeadLettered` |
| `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IProposalRecordingRetryPolicy.cs` | Contract: `MaxAttempts`, `ShouldRetry(int)`, `GetDelay(int)` |
| `src/Modules/Packs/Application/OpsCopilot.Packs.Application/Abstractions/IProposalDeadLetterStore.cs` | Contract: `AddAsync(attempt, ct)`, `GetAllAsync(ct)` |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/InMemoryProposalDeadLetterStore.cs` | Thread-safe `ConcurrentBag<>` implementation; does not survive process restart |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/DefaultProposalRecordingRetryPolicy.cs` | MaxAttempts=3; delays \[0 s, 1 s, 2 s\]; out-of-range index clamped to last entry |
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/DurablePackSafeActionRecorder.cs` | `internal sealed` decorator; fast-path when `FailedCount==0`; retries only `Status=="Failed"` items; dead-letters after exhaustion |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/DurablePackSafeActionRecorderTests.cs` | 8 unit tests for decorator behaviour |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/InMemoryProposalDeadLetterStoreTests.cs` | 3 unit tests for dead-letter store |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/DefaultProposalRecordingRetryPolicyTests.cs` | 9 unit tests for retry policy |

### Modified

| File | Change |
|------|--------|
| `src/Modules/Packs/Infrastructure/OpsCopilot.Packs.Infrastructure/Extensions/PacksInfrastructureExtensions.cs` | Registers `PackSafeActionRecorder` as concrete; registers `DefaultProposalRecordingRetryPolicy`, `InMemoryProposalDeadLetterStore`; resolves `IPackSafeActionRecorder` via factory lambda wrapping it in `DurablePackSafeActionRecorder` |

## Key Design Decisions

- **Decorator pattern**: `PackSafeActionRecorder` registered as concrete type; `IPackSafeActionRecorder`
  resolved as the durable wrapper. No callers change.
- **Retry only `"Failed"` items**: `"PolicyDenied"` and `"Skipped"` are not failures — they are
  terminal decisions. The inner `FailedCount` will be `0` for these, so the decorator fast-paths
  and returns immediately without any retry or dead-letter.
- **Attempt numbering**: Attempt 1 is the initial call to `inner`; retries start at attempt 2.
  `ShouldRetry(2)` must return `true` to allow the first retry.
- **`with` expression for sub-requests**: Only failed proposals are re-submitted on each retry;
  the `request with { Proposals = failedSubset }` preserves `TenantId`, `TriageRunId`, and
  `DeploymentMode` without copying them manually.
- **Dead-letter record**: `IsDeadLettered=true`, `AttemptNumber=MaxAttempts`, `AttemptedAt=UtcNow`.
  `ParametersJson` is taken from the matching proposal in the original request; `ErrorMessage` is
  taken from the last failed record.
- **`internal sealed`**: Decorator is an infrastructure detail, not a public contract. Exposed to
  tests via `InternalsVisibleTo` already present in the project file.

## Build Summary

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Test Summary

| Assembly | Passed | Failed | Skipped |
|----------|--------|--------|---------|
| Governance.Tests | 31 | 0 | 0 |
| Connectors.Tests | 30 | 0 | 0 |
| Evaluation.Tests | 15 | 0 | 0 |
| AlertIngestion.Tests | 31 | 0 | 0 |
| Reporting.Tests | 27 | 0 | 0 |
| **Packs.Tests** | **293** | **0** | **0** |
| AgentRuns.Tests | 81 | 0 | 0 |
| Tenancy.Tests | 17 | 0 | 0 |
| SafeActions.Tests | 368 | 0 | 0 |
| Integration.Tests | 24 | 0 | 0 |
| Mcp.ContractTests | 8 | 0 | 0 |
| **Grand Total** | **925** | **0** | **0** |

Previous baseline (Slice 52): 906 passing. Net gain: **+19 tests**.

## New Tests Added (19 total)

### DurablePackSafeActionRecorderTests (8)
1. `RecordAsync_NoFailures_ReturnsFastPath`
2. `RecordAsync_FailedItem_RetriedOnce_Succeeds`
3. `RecordAsync_AllRetriesExhausted_ItemDeadLettered`
4. `RecordAsync_PolicyDeniedItem_FastPath_InnerCalledOnce`
5. `RecordAsync_SkippedItem_FastPath_InnerCalledOnce`
6. `RecordAsync_GetDelay_CalledWithAttemptNumber_OnFirstRetry`
7. `RecordAsync_MixedResult_OnlyFailedItemRetried` *(in DurablePackSafeActionRecorderTests)*
8. `RecordAsync_TwoFailures_GetDelay_CalledForEachRetry` *(in DurablePackSafeActionRecorderTests)*

### InMemoryProposalDeadLetterStoreTests (3)
1. `GetAllAsync_WhenEmpty_ReturnsEmptyList`
2. `AddAsync_SingleAttempt_GetAllAsync_ReturnsThatAttempt`
3. `AddAsync_MultipleAttempts_GetAllAsync_ReturnsAll`

### DefaultProposalRecordingRetryPolicyTests (9)
1. `MaxAttempts_IsThree`
2. `ShouldRetry_Attempt1_ReturnsTrue`
3. `ShouldRetry_Attempt2_ReturnsTrue`
4. `ShouldRetry_Attempt3_ReturnsTrue`
5. `ShouldRetry_Attempt4_ReturnsFalse`
6. `GetDelay_Attempt1_ReturnsZero`
7. `GetDelay_Attempt2_ReturnsOneSecond`
8. `GetDelay_Attempt3_ReturnsTwoSeconds`
9. `GetDelay_AttemptBeyondTable_ClampsToTwoSeconds`

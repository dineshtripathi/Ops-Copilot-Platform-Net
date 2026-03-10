# Dev Slice 53.5 — Durable Dead-Letter Persistence & Background Replay

## Summary

Replaced the in-memory `InMemoryProposalDeadLetterStore` with a durable SQL-backed
`SqlProposalDeadLetterRepository`, added a background `ProposalDeadLetterReplayWorker`,
and delivered full unit-test coverage for the new infrastructure.

---

## Build

```
dotnet build OpsCopilot.sln --no-incremental -v minimal
```

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:13.77
```

---

## Tests

```
dotnet test OpsCopilot.sln --no-build --logger "console;verbosity=minimal"
```

| Assembly | Passed |
|---|---|
| OpsCopilot.Modules.Governance.Tests | 31 |
| OpsCopilot.Modules.Connectors.Tests | 30 |
| OpsCopilot.Modules.Evaluation.Tests | 15 |
| OpsCopilot.Modules.AlertIngestion.Tests | 31 |
| OpsCopilot.Modules.Reporting.Tests | 27 |
| OpsCopilot.Modules.AgentRuns.Tests | 81 |
| OpsCopilot.Modules.Tenancy.Tests | 17 |
| OpsCopilot.Modules.Packs.Tests | 303 |
| OpsCopilot.Modules.SafeActions.Tests | 368 |
| OpsCopilot.Integration.Tests | 24 |
| OpsCopilot.Mcp.ContractTests | 8 |
| **Grand Total** | **935** |

**Failed: 0 / Skipped: 0**

Prior baseline (Slice 53): 925 passing.  
Net gain: +10 (7 new repo tests + 6 new worker tests − 3 replaced in-memory tests).

---

## Files Changed

### Infrastructure (prior sessions)

| File | Change |
|---|---|
| `src/Modules/Packs/Domain/.../Entities/ProposalDeadLetterEntry.cs` | New domain entity |
| `src/Modules/Packs/Application/.../Abstractions/IProposalDeadLetterRepository.cs` | New repository interface |
| `src/Modules/Packs/Infrastructure/.../Persistence/PacksDbContext.cs` | Added `DbSet<ProposalDeadLetterEntry>` |
| `src/Modules/Packs/Infrastructure/.../Persistence/SqlProposalDeadLetterRepository.cs` | New SQL implementation |
| `src/Modules/Packs/Infrastructure/.../PacksInfrastructureExtensions.cs` | Swapped DI registration to SQL repo |
| `src/Hosts/OpsCopilot.WorkerHost/Workers/ProposalDeadLetterReplayWorker.cs` | New background worker |
| EF migrations (×3) | Schema: `proposal_dead_letters` table |

### This Session

| File | Change |
|---|---|
| `src/Modules/Packs/Infrastructure/.../Persistence/SqlProposalDeadLetterRepository.cs` | Fixed: `Guid.NewGuid()` added as first arg in `IProposalDeadLetterStore.AddAsync` (was 10-arg call to 11-param constructor) |
| `src/Hosts/OpsCopilot.WorkerHost/Workers/ProposalDeadLetterReplayWorker.cs` | Fixed: `ProcessPendingEntriesAsync` visibility `private` → `internal` (enables unit testing via `InternalsVisibleTo`) |
| `src/Hosts/OpsCopilot.WorkerHost/OpsCopilot.WorkerHost.csproj` | Added: `<InternalsVisibleTo Include="OpsCopilot.Modules.Packs.Tests" />` and `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/OpsCopilot.Modules.Packs.Tests.csproj` | Added: `Microsoft.EntityFrameworkCore.InMemory 9.0.2` + `ProjectReference` to WorkerHost |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/InMemoryProposalDeadLetterStoreTests.cs` | Replaced: old 3 in-memory tests → new `SqlProposalDeadLetterRepositoryTests` with 7 EF InMemory tests |
| `tests/Modules/Packs/OpsCopilot.Modules.Packs.Tests/ProposalDeadLetterReplayWorkerTests.cs` | Created: 6 worker tests (Moq-based, covers success / fail-below-max / fail-at-max / throw-below-max / throw-at-max / no-pending scenarios) |

---

## New Tests

### `SqlProposalDeadLetterRepositoryTests` (7 tests, EF InMemory)

| Test | Assertion |
|---|---|
| `AddAsync_Entry_IsPersisted` | Entry appears in `GetPendingAsync` after add |
| `ExistsAsync_KnownAttemptId_ReturnsTrue` | Returns `true` for known attempt |
| `ExistsAsync_UnknownAttemptId_ReturnsFalse` | Returns `false` for unknown attempt |
| `GetPendingAsync_ExcludesSucceededAndExhausted` | Succeeded + exhausted entries are not returned |
| `MarkReplaySucceededAsync_RemovesEntryFromPending` | Entry no longer in pending list |
| `MarkReplayFailedAsync_EntryRemainsInPending` | Entry remains in pending with error recorded |
| `MarkReplayExhaustedAsync_RemovesEntryFromPending` | Entry no longer in pending list |

### `ProposalDeadLetterReplayWorkerTests` (6 tests, Moq)

| Test | Assertion |
|---|---|
| `ProcessPendingEntries_NoPendingEntries_DoesNothing` | `RecordAsync` never called |
| `ProcessPendingEntries_RecordSucceeds_MarksSucceeded` | `MarkReplaySucceededAsync` called once |
| `ProcessPendingEntries_RecordFails_BelowMaxAttempts_MarksReplayFailed` | `MarkReplayFailedAsync` called; no exhaustion |
| `ProcessPendingEntries_RecordFails_AtMaxAttempts_MarksReplayExhausted` | `MarkReplayExhaustedAsync` called; no failed |
| `ProcessPendingEntries_RecordThrows_BelowMaxAttempts_MarksReplayFailed` | Exception message forwarded to `MarkReplayFailedAsync` |
| `ProcessPendingEntries_RecordThrows_AtMaxAttempts_MarksReplayExhausted` | Exception message forwarded to `MarkReplayExhaustedAsync` |

---

## Design Notes

- **Exhaustion check**: `entry.ReplayAttempts + 1 >= MaxReplayAttempts (3)` uses the pre-`MarkReplayStarted` value, producing correct boundary behaviour (attempt 3 of 3 exhausts).
- **`IDbContextFactory<T>`**: Repository uses `CreateDbContextAsync` (EF extension over `CreateDbContext()`); test `TestDbContextFactory` implements only the interface's `CreateDbContext()` method.
- **No runtime behaviour change**: `IProposalDeadLetterStore` (legacy interface) still implemented; only DI registration swapped to SQL in infrastructure extensions.

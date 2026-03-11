# Dev Slice 56 — Tenant-Aware IChatClient Model Routing + Prompt/Token Ledgering

## Summary

Wired `Microsoft.Extensions.AI` `IChatClient` (9.5.0) into the triage path and persisted six new ledger columns on `AgentRun`: `ModelId`, `PromptVersionId`, `InputTokens`, `OutputTokens`, `TotalTokens`, `EstimatedCost`. All failures are swallowed so that the run still completes even when the LLM is unavailable.

---

## Build Gate

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Command: `dotnet build --no-incremental -warnaserror`

---

## Test Gate

| Assembly | Passed | Failed |
|---|---|---|
| OpsCopilot.Modules.AgentRuns.Tests | **88** (+2 new) | 0 |
| OpsCopilot.Modules.Connectors.Tests | 30 | 0 |
| OpsCopilot.Modules.Evaluation.Tests | 15 | 0 |
| OpsCopilot.Modules.AlertIngestion.Tests | 31 | 0 |
| OpsCopilot.Modules.Reporting.Tests | 27 | 0 |
| OpsCopilot.Modules.Packs.Tests | 303 | 0 |
| OpsCopilot.Modules.SafeActions.Tests | 368 | 0 |
| OpsCopilot.Modules.Tenancy.Tests | 17 | 0 |
| OpsCopilot.Integration.Tests | 24 | 0 |
| OpsCopilot.Mcp.ContractTests | 8 | 0 |
| **Total** | **911** | **0** |

---

## EF Migration

**Name:** `AddLedgerColumnsToAgentRun`

Adds six columns to the `AgentRuns` table: `ModelId` (nvarchar(200), nullable), `PromptVersionId` (nvarchar(100), nullable), `InputTokens` (int, nullable), `OutputTokens` (int, nullable), `TotalTokens` (int, nullable), `EstimatedCost` (decimal(18,6), nullable).

---

## New Abstractions

| File | Description |
|---|---|
| `IModelRoutingPolicy.cs` | Selects a `ModelDescriptor` for a given tenant ID |
| `ModelDescriptor.cs` | Value type carrying `ModelId` string |
| `IPromptVersionService.cs` | Returns `PromptVersionInfo` for a named prompt |
| `PromptVersionInfo.cs` | Value type carrying `VersionId` + `PromptText` |
| `NullModelRoutingPolicy.cs` | Null-object: returns `ModelDescriptor("default")` |
| `NullPromptVersionService.cs` | Null-object: returns `PromptVersionInfo("0.0.0", "")` |

---

## Changed Files

### Domain
- `src/Modules/AgentRuns/Domain/Entities/AgentRun.cs` — added `ModelId`, `PromptVersionId`, `InputTokens`, `OutputTokens`, `TotalTokens`, `EstimatedCost` properties and `SetLedgerMetadata` method
- `src/Modules/AgentRuns/Domain/Repositories/IAgentRunRepository.cs` — added `UpdateRunLedgerAsync` (7th method)

### Application
- `src/Modules/AgentRuns/Application/Application.csproj` — added `Microsoft.Extensions.AI` 9.5.0
- `src/Modules/AgentRuns/Application/Abstractions/` — 6 new files (see above)
- `src/Modules/AgentRuns/Application/Orchestration/TriageOrchestrator.cs` — injected `IChatClient?`, `IModelRoutingPolicy?`, `IPromptVersionService?`; calls `GetResponseAsync` and writes ledger; swallows exceptions
- `src/Modules/AgentRuns/Application/Orchestration/TriageResult.cs` — added 6 optional ledger tail properties

### Infrastructure
- `src/Modules/AgentRuns/Infrastructure/Infrastructure.csproj` — added `Microsoft.Extensions.AI` 9.5.0
- `src/Modules/AgentRuns/Infrastructure/Persistence/SqlAgentRunRepository.cs` — implemented `UpdateRunLedgerAsync`
- `src/Modules/AgentRuns/Infrastructure/Persistence/AgentRunsDbContext.cs` — EF column configs for 3 numeric columns
- `src/Modules/AgentRuns/Infrastructure/Persistence/Migrations/` — migration `AddLedgerColumnsToAgentRun`
- `src/Modules/AgentRuns/Infrastructure/DependencyInjection/AgentRunsApplicationExtensions.cs` — DI registrations for `NullModelRoutingPolicy` and `NullPromptVersionService`

### Presentation
- `src/Modules/AgentRuns/Presentation/Endpoints/AgentRunEndpoints.cs` — mapped 6 ledger fields onto `TriageResponse`
- `src/Modules/AgentRuns/Presentation/Models/TriageResponse.cs` — added 6 nullable ledger response properties

### Tests
- `tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/TriageOrchestratorTests.cs` — added 2 new tests:
  - `RunAsync_WithChatClient_PopulatesLedgerFields`
  - `RunAsync_ChatClientThrows_LedgerIncomplete_RunStillCompletes`

---

## Design Notes

- `IChatClient`, `IModelRoutingPolicy`, and `IPromptVersionService` are optional constructor parameters (default `null`). When null, the Null-object implementations are used transparently.
- `estimatedCost` is currently always `0m` — a placeholder for a future pricing calculation.
- Token counts are cast from `long?` to `int?` (UsageDetails uses `long?`).
- All LLM failures are caught and logged; the triage run completes as `Completed` regardless.

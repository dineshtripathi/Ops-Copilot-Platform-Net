# Slice 105 Evidence — Azure Alert Context Propagation

## Objective

Populate the 8 Azure alert context columns (`AlertProvider`, `AlertSourceType`, `IsExceptionSignal`,
`AzureSubscriptionId`, `AzureResourceGroup`, `AzureResourceId`, `AzureApplication`,
`AzureWorkspaceId`) that were added to the `AgentRun` entity in Slice 104 but were always `NULL`
because the pipeline never propagated the data from the incoming `AlertPayloadDto`.

---

## Constraints Respected

- No new HTTP routes
- No schema changes or migrations (columns already exist from Slice 104)
- No breaking DTO changes (existing endpoint request/response unchanged)
- No secrets in logs
- No config keys invented
- `TriageOrchestrator.RunAsync` signature is additive: `RunContext? context = null` is optional

---

## Root Cause

`AgentRunEndpoints.MapAlertRunEndpoints` called `orchestrator.RunAsync(...)` with hardcoded
`subscriptionId: null, resourceGroup: null` and never built a `RunContext`. The orchestrator then
called `_repo.CreateRunAsync(..., context: null)` on every invocation, leaving all 8 columns NULL.

---

## Changes

### `AgentRunEndpoints.cs`
Three changes:

1. **Added using** `OpsCopilot.AgentRuns.Domain.Models`

2. **Added static helper** `ParseArmResourceId` after `BridgeJsonOpts`:
   ```csharp
   private static (string? SubscriptionId, string? ResourceGroup) ParseArmResourceId(string? resourceId)
   ```
   Splits the ARM resource ID path on `/` and walks segments looking for `subscriptions` and
   `resourceGroups` tokens (case-insensitive). Returns `(null, null)` for null/empty input.

3. **In the triage endpoint handler**: after fingerprint calculation, builds `RunContext` from
   `AlertPayloadDto` fields, then passes `context: runContext` to `orchestrator.RunAsync`:

   | `AlertPayloadDto` field | `RunContext` param |
   |---|---|
   | `AlertSource` | `AlertProvider` |
   | `SignalType` | `AlertSourceType` |
   | `ResourceId` | `AzureResourceId` |
   | `ServiceName` | `AzureApplication` |
   | `workspaceId` (resolver param) | `AzureWorkspaceId` |
   | Parsed from `ResourceId` ARM path | `AzureSubscriptionId` |
   | Parsed from `ResourceId` ARM path | `AzureResourceGroup` |
   | (default `false`) | `IsExceptionSignal` |

   The existing `subscriptionId: null, resourceGroup: null` named parameters are retained as-is;
   the DeploymentDiff fallback in the orchestrator picks them up from `context` instead.

### `TriageOrchestrator.cs`
Four changes:

1. **Added using** `OpsCopilot.AgentRuns.Domain.Models`

2. **`RunAsync` signature**: added `RunContext? context = null` after `sessionId`

3. **`_repo.CreateRunAsync` call**: changed from 4-param to 5-param:
   ```csharp
   await _repo.CreateRunAsync(tenantId, alertFingerprint, session.SessionId, context, ct)
   ```

4. **DeploymentDiff section**: uses effective-fallback variables so that when `subscriptionId` is
   passed as `null` from the endpoint, the context values are used instead:
   ```csharp
   var effectiveSubscriptionId = subscriptionId ?? context?.AzureSubscriptionId;
   var effectiveResourceGroup  = resourceGroup  ?? context?.AzureResourceGroup;
   if (_deploymentDiff is not null && effectiveSubscriptionId is not null)
   // ...
   var ddRequest = new DeploymentDiffRequest(tenantId, effectiveSubscriptionId, effectiveResourceGroup, timeRangeMinutes);
   ```

### No changes needed downstream

`IAgentRunRepository`, `SqlAgentRunRepository`, `AgentRun.Create`, and EF Core mapping all already
handled `context` correctly from Slice 104.

---

## Tests

### `TriageOrchestratorTests.cs`
- Added `using OpsCopilot.AgentRuns.Domain.Models;`
- All **8** existing `CreateRunAsync` mock setups updated from 4-param to 5-param:
  ```csharp
  .Setup(r => r.CreateRunAsync(TenantId, AlertFingerprint,
      It.IsAny<Guid?>(), It.IsAny<RunContext?>(), It.IsAny<CancellationToken>()))
  ```
- **New test**: `RunAsync_WithContext_ContextPassedToCreateRunAsync` — uses Callback to capture the
  `RunContext` passed to `CreateRunAsync` and asserts all 7 populated fields

### Other test files updated (mock-only changes)
Four additional test classes had `CreateRunAsync` strict-mock setups with 4 params:

| File | Change |
|---|---|
| `RunbookAclFilterTests.cs` | Added using + updated 1 mock |
| `KqlGovernedEvidenceIntegrationTests.cs` | Added using + updated 1 mock |
| `TriageEvidenceIntegrationTests.cs` | Added using + updated 2 mocks |
| `RunbookCitationIntegrationTests.cs` | Added using + updated 1 mock |

---

## Build & Test Gates

```
dotnet build src/Modules/AgentRuns/Application/...Application.csproj -warnaserror
→ Build succeeded. 0 Warning(s) 0 Error(s)

dotnet build src/Modules/AgentRuns/Presentation/...Presentation.csproj -warnaserror
→ Build succeeded. 0 Warning(s) 0 Error(s)

dotnet test tests/Modules/AgentRuns/OpsCopilot.Modules.AgentRuns.Tests/...Tests.csproj
→ Passed! Failed: 0, Passed: 135, Skipped: 0, Total: 135
```

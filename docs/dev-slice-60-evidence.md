# Slice 60 — `deployment_diff` MCP Tool Integration

**Status:** Complete  
**Committed:** TBD  
**Parent slice:** 59.2 (open-source hygiene)

---

## Objective

Add a `deployment_diff` MCP tool that exposes governed change evidence to the TriageOrchestrator via the MCP host, enabling evidence-backed triage with Azure Resource Graph queries.

---

## Scope Constraints (honoured)

- No HTTP routes added or changed.
- No DB schema changes or migrations.
- No breaking DTO changes (additive only).
- No `.github/workflows/*` modifications.
- No auto-approval or auto-execution of SafeActions.
- No secrets in logs, docs, or tests.

---

## Changes Made

### Abstractions / Contracts

| File | Change |
|------|--------|
| `src/BuildingBlocks/Contracts/…/IDeploymentDiffTool.cs` | New interface `IDeploymentDiffTool` with `QueryRecentChangesAsync` |
| `src/BuildingBlocks/Contracts/…/DeploymentChange.cs` | New record for ARM change data |

### Application Layer

| File | Change |
|------|--------|
| `src/Modules/AgentRuns/Application/…/TriageResult.cs` | Extended with `DeploymentChanges` collection property |
| `src/Modules/AgentRuns/Application/…/TriageOrchestrator.cs` | `RunAsync` gains optional `subscriptionId`, `resourceGroup`, `sessionId` params; conditionally calls `IDeploymentDiffTool` and stores result on `TriageResult` |

### Presentation Layer

| File | Change |
|------|--------|
| `src/Modules/AgentRuns/Presentation/…/AgentRunEndpoints.cs` | Updated call site to pass new named params correctly |

### MCP Host

| File | Change |
|------|--------|
| `src/Hosts/OpsCopilot.McpHost/Tools/DeploymentDiffTool.cs` | New MCP tool class implementing `IDeploymentDiffTool`; uses Azure Resource Graph `TenantResource.GetResourcesAsync` via `armClient.GetTenants().GetAllAsync()` |
| `src/Hosts/OpsCopilot.McpHost/Program.cs` | DI registration: `services.AddSingleton<IDeploymentDiffTool, DeploymentDiffTool>()` |
| `src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj` | Added `Azure.ResourceManager.ResourceGraph 1.0.0` GA package reference |

### Tests

| File | Change |
|------|--------|
| `tests/Modules/AgentRuns/…/TriageOrchestratorTests.cs` | 8 new unit tests covering: deployment_diff enabled/disabled, partial params, error resilience, sessionId propagation |

---

## Key Technical Decision

**Problem:** `Azure.ResourceManager.ResourceGraph 1.0.0` extension method `GetResourcesAsync` only accepts a `TenantResource` receiver. `TenantResource` constructors are `protected` (intended for mocking only) and `ArmClient` has no `GetTenantResource(ResourceIdentifier)` method (absent from the public API despite prose in XML docs). The `TenantResourceExtensionClient` (which has a public constructor) is itself `internal`.

**Solution:** Obtain `TenantResource` via `armClient.GetTenants().GetAllAsync()` — enumerate the tenant collection and take the first entry. This is the only supported public API path to a live `TenantResource` instance.

Subscription scoping is handled by `queryContent.Subscriptions.Add(subscriptionId)`, not by the `TenantResource` receiver, so there is no functional difference — the tenant receiver is only needed as the call target for the extension method.

---

## Build Gate

```
dotnet build OpsCopilot.sln -warnaserror --nologo
→ Build succeeded. 0 Warning(s). 0 Error(s).
```

## Test Gate

```
dotnet test OpsCopilot.sln --no-build --nologo
→ All assemblies: Failed: 0, Passed: 984+, Skipped: 0
  OpsCopilot.Modules.AgentRuns.Tests: 134 passed (includes 8 new deployment_diff tests)
  OpsCopilot.Mcp.ContractTests: 8 passed
```

# Slice 26 — Connectors MVP (STRICT, Minimal Foundation, No Overbuild)

## Evidence Document

| Item | Detail |
| --- | --- |
| **Slice** | 26 |
| **Title** | Connectors MVP (STRICT, Minimal Foundation, No Overbuild) |
| **Branch** | main |
| **Baseline commit** | `102e468` (Slice 25) |
| **Baseline tests** | 481 |
| **Final tests** | 511 (481 + 30 new) |
| **Status** | All acceptance criteria met |

---

## Acceptance Criteria

| AC | Description | Status | Evidence |
| --- | --- | --- | --- |
| AC-1 | `ConnectorKind` enum with Observability, Runbook, ActionTarget | ✅ | `Abstractions/ConnectorKind.cs` — 3-value enum |
| AC-2 | `ConnectorDescriptor` record with Name, Kind, Description, Capabilities | ✅ | `Abstractions/ConnectorDescriptor.cs` — sealed record, 4 properties |
| AC-3 | `IObservabilityConnector` interface with Descriptor, SupportedQueryTypes, CanQuery() | ✅ | `Abstractions/IObservabilityConnector.cs` — 3 members |
| AC-4 | `IRunbookConnector` interface with Descriptor, SupportedContentTypes, CanSearch() | ✅ | `Abstractions/IRunbookConnector.cs` — 3 members |
| AC-5 | `IActionTargetConnector` interface with Descriptor, SupportedActionTypes, SupportsActionType() | ✅ | `Abstractions/IActionTargetConnector.cs` — 3 members |
| AC-6 | `IConnectorRegistry` with typed Get*Connector(name), ListAll(), ListByKind() — null for unknown | ✅ | `Abstractions/IConnectorRegistry.cs` — 5 methods, returns null for unknown names |
| AC-7 | `ConnectorRegistry` implementation: DI-injected, OrdinalIgnoreCase lookup, last-wins duplicates | ✅ | `Application/Services/ConnectorRegistry.cs` — singleton, `BuildLookup<T>()` with `StringComparer.OrdinalIgnoreCase`, `dict[key] = value` (last-wins) |
| AC-8 | ≥3 concrete connectors: AzureMonitor (Observability), InMemoryRunbook (Runbook), StaticActionTarget (ActionTarget) | ✅ | 3 connectors in `Infrastructure/Connectors/` — azure-monitor (3 query types), in-memory-runbook (2 content types), static-action-target (4 action types) |
| AC-9 | Module wired via `AddConnectorsModule()` in ApiHost — no HTTP endpoints (internal-only) | ✅ | `Program.cs` — `.AddConnectorsModule()` after `.AddEvaluationModule()`, no `MapConnectorEndpoints()` |
| AC-10 | No schema changes, no Worker/MCP changes | ✅ | No EF migrations, no `DbContext` references, no Worker/MCP files modified |
| AC-11 | No reflection, no plugin loading, no credential management | ✅ | All registrations explicit singleton DI, no `Assembly.Load*`, no `ICredentialProvider` |
| AC-12 | ≥12 new tests, all passing | ✅ | 17 test methods (30 test cases) in `ConnectorTests.cs`, all passing |
| AC-13 | .http section AB noting internal-only module | ✅ | `docs/http/OpsCopilot.Api.http` — Section AB header with comment |

---

## New / Modified Files

### Abstractions (6 new files + 1 csproj)
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/OpsCopilot.Connectors.Abstractions.csproj`
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/ConnectorKind.cs`
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/ConnectorDescriptor.cs`
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IObservabilityConnector.cs`
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IRunbookConnector.cs`
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IActionTargetConnector.cs`
- `src/Modules/Connectors/Abstractions/OpsCopilot.Connectors.Abstractions/IConnectorRegistry.cs`

### Application (2 new files + 1 csproj modified)
- `src/Modules/Connectors/Application/OpsCopilot.Connectors.Application/OpsCopilot.Connectors.Application.csproj` (added DI.Abstractions 9.0.2, Logging.Abstractions 9.0.2, ProjectReference to Abstractions)
- `src/Modules/Connectors/Application/OpsCopilot.Connectors.Application/Services/ConnectorRegistry.cs`
- `src/Modules/Connectors/Application/OpsCopilot.Connectors.Application/Extensions/ConnectorApplicationExtensions.cs`

### Infrastructure (4 new files + 1 csproj modified)
- `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/OpsCopilot.Connectors.Infrastructure.csproj` (added ProjectReference to Application)
- `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Connectors/AzureMonitorObservabilityConnector.cs`
- `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Connectors/InMemoryRunbookConnector.cs`
- `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Connectors/StaticActionTargetConnector.cs`
- `src/Modules/Connectors/Infrastructure/OpsCopilot.Connectors.Infrastructure/Extensions/ConnectorInfrastructureExtensions.cs`

### Host (2 modified)
- `src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj` (added Connectors.Infrastructure ProjectReference)
- `src/Hosts/OpsCopilot.ApiHost/Program.cs` (added `using` + `.AddConnectorsModule()`)

### Tests (2 new)
- `tests/Modules/Connectors/OpsCopilot.Modules.Connectors.Tests/OpsCopilot.Modules.Connectors.Tests.csproj`
- `tests/Modules/Connectors/OpsCopilot.Modules.Connectors.Tests/ConnectorTests.cs` (17 test methods, 30 test cases)

### .http (1 modified)
- `docs/http/OpsCopilot.Api.http` (added Section AB header + index entry)

### Deleted
- 4 × `Class1.cs` placeholders (Abstractions, Application, Infrastructure, Tests)

---

## Test Results

```
Passed!  - Failed: 0, Passed:  30 - Connectors (NEW)
Passed!  - Failed: 0, Passed:  69 - AgentRuns
Passed!  - Failed: 0, Passed:  31 - AlertIngestion
Passed!  - Failed: 0, Passed:  15 - Evaluation
Passed!  - Failed: 0, Passed:  14 - Reporting
Passed!  - Failed: 0, Passed: 320 - SafeActions
Passed!  - Failed: 0, Passed:  24 - Integration
Passed!  - Failed: 0, Passed:   8 - MCP Contract
─────────────────────────────────────
Total:   511 passed, 0 failed
```

---

## Connector Details

| Connector | Kind | Name | Capabilities |
| --- | --- | --- | --- |
| `AzureMonitorObservabilityConnector` | Observability | `azure-monitor` | log-query, metric-query, alert-read |
| `InMemoryRunbookConnector` | Runbook | `in-memory-runbook` | markdown, plain-text |
| `StaticActionTargetConnector` | ActionTarget | `static-action-target` | restart-service, scale-resource, run-diagnostic, toggle-feature-flag |

---

## Design Notes

- **3-layer architecture**: Abstractions → Application → Infrastructure (no Domain, no Presentation). Connectors is the cross-module contract surface — its Abstractions project is what other modules reference.
- **No HTTP endpoints**: Entirely internal module. Other modules consume connectors via `IConnectorRegistry` through DI.
- **No persistence**: No database, no EF, no schema changes.
- **No reflection/plugin loading**: All registrations are explicit singleton DI in `ConnectorInfrastructureExtensions`.
- **Case-insensitive**: Registry uses `StringComparer.OrdinalIgnoreCase` for name-based lookup; connectors use the same for capability checks.
- **Duplicate-safe**: `BuildLookup<T>()` uses last-wins semantics (`dict[key] = value`) — no exception on duplicate names.
- **Logging**: `ConnectorRegistry` logs connector counts at startup via `ILogger<ConnectorRegistry>` (framework package only).
- **New NuGet packages**: `Microsoft.Extensions.DependencyInjection.Abstractions 9.0.2` + `Microsoft.Extensions.Logging.Abstractions 9.0.2` (both already used by other Application layers).

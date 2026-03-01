# Contributing to OpsCopilot

Thank you for considering a contribution to OpsCopilot! This guide covers everything you need to get started, from setting up your development environment to submitting a pull request.

---

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Solution Structure](#solution-structure)
- [Module Guidelines](#module-guidelines)
- [Adding a New Module](#adding-a-new-module)
- [Adding an Evaluation Scenario](#adding-an-evaluation-scenario)
- [Adding a Connector](#adding-a-connector)
- [Creating a Pack](#creating-a-pack)
- [Coding Conventions](#coding-conventions)
- [Dependency Rules](#dependency-rules)
- [Testing Requirements](#testing-requirements)
- [Pull Request Checklist](#pull-request-checklist)
- [Commit Messages](#commit-messages)
- [License](#license)

---

## Code of Conduct

All contributors are expected to follow a professional and respectful standard of behaviour. Be kind, be constructive, and assume good intent.

---

## Getting Started

1. **Fork & Clone**

   ```bash
   git clone https://github.com/<your-fork>/ops-copilot-platform.git
   cd ops-copilot-platform
   ```

2. **Prerequisites**

   | Tool | Version |
   |---|---|
   | .NET SDK | 10.0+ |
   | Docker | 24+ (optional, for container workloads) |
   | Azure CLI | 2.60+ (optional, for Mode B/C) |

3. **Restore & Build**

   ```bash
   dotnet restore OpsCopilot.sln
   dotnet build OpsCopilot.sln
   ```

4. **Run Tests**

   ```bash
   dotnet test OpsCopilot.sln
   ```

   All tests must pass before submitting a PR.

5. **Run Locally (Mode A)**

   ```bash
   dotnet run --project src/Hosts/OpsCopilot.ApiHost
   ```

   See [docs/running-locally.md](docs/running-locally.md) for full local development instructions.

---

## Solution Structure

```
src/
├── BuildingBlocks/          # Shared kernel (Contracts, Domain, Application, Infrastructure)
├── Hosts/
│   ├── OpsCopilot.ApiHost/  # HTTP entry point (Minimal APIs)
│   ├── OpsCopilot.McpHost/  # MCP stdio server (KQL)
│   └── OpsCopilot.WorkerHost/ # Background processing
└── Modules/
    ├── AgentRuns/
    ├── AlertIngestion/
    ├── Connectors/
    ├── Evaluation/
    ├── Governance/
    ├── Prompting/
    ├── Rag/
    ├── Reporting/
    ├── SafeActions/
    └── Tenancy/
tests/
├── Integration/
├── McpContractTests/
└── Modules/                 # Unit tests mirroring src/Modules/
```

Each module follows the **Vertical Slice** pattern with four layers:

| Layer | Project Suffix | Responsibility |
|---|---|---|
| Domain | `.Domain` | Entities, value objects, domain events |
| Application | `.Application` | Use cases, orchestrators, interfaces |
| Infrastructure | `.Infrastructure` | Persistence, external integrations |
| Presentation | `.Presentation` | Endpoint definitions (Minimal APIs) |

---

## Module Guidelines

- Each module is **self-contained** — it owns its entities, migrations, endpoints, and tests.
- Modules communicate through **BuildingBlocks.Contracts** (shared interfaces and DTOs), never by referencing another module's internal types directly.
- Follow the [Dependency Rules](docs/pdd/DEPENDENCY_RULES.md) strictly.

---

## Adding a New Module

1. Create the four-layer project structure under `src/Modules/<ModuleName>/`:
   - `<ModuleName>.Domain/`
   - `<ModuleName>.Application/`
   - `<ModuleName>.Infrastructure/`
   - `<ModuleName>.Presentation/`

2. Add corresponding test projects under `tests/Modules/<ModuleName>/`.

3. Wire into `Program.cs` (ApiHost):
   - Add `builder.Services.Add<ModuleName>Module(builder.Configuration);` in the service registration block.
   - Add `app.Map<ModuleName>Endpoints();` in the endpoint mapping block.
   - Add `app.Use<ModuleName>Migrations();` if your module owns database tables.

4. Follow the existing registration order convention in `Program.cs`:
   ```
   AddAgentRunsModule → AddAlertIngestionModule → AddTenancyModule
   → AddGovernanceModule → AddSafeActionsModule → AddReportingModule
   → AddEvaluationModule → AddConnectorsModule → [your module]
   ```

5. Add configuration entries to `appsettings.json` and `appsettings.Development.json` as needed.

---

## Adding an Evaluation Scenario

The Evaluation module supports self-test scenarios that validate module behaviour.

1. Implement the `IEvaluationScenario` interface:

   ```csharp
   public interface IEvaluationScenario
   {
       string ScenarioId { get; }
       string Module { get; }
       string Name { get; }
       string Category { get; }
       string Description { get; }
       Task<EvaluationResult> Execute();
   }
   ```

2. Register your scenario in your module's DI wiring:

   ```csharp
   services.AddSingleton<IEvaluationScenario, MyNewScenario>();
   ```

3. The `EvaluationScenarioCatalog` discovers all `IEvaluationScenario` implementations automatically. No further registration is needed.

4. Verify your scenario appears at `GET /evaluation/scenarios` and runs successfully at `GET /evaluation/run`.

**Existing scenarios (11 total):**

| Module | Scenario |
|---|---|
| AlertIngestion | AzureMonitorParse, DatadogProvider, EmptyPayloadRejection, FingerprintDeterminism |
| SafeActions | ActionTypeClassification, EmptyActionList, DryRunGuard, ReplayDetection |
| Reporting | SummaryTotals, RecentLimitClamp, TenantFilter |

---

## Adding a Connector

OpsCopilot uses three connector interfaces for external integrations:

| Interface | Purpose |
|---|---|
| `IObservabilityConnector` | Read telemetry data (metrics, logs, traces) |
| `IRunbookConnector` | Search and retrieve operational runbooks |
| `IActionTargetConnector` | Execute actions against external systems |

### Steps

1. Implement one or more connector interfaces in your module or the `Connectors` module.

2. Register your connector with the `IConnectorRegistry`:

   ```csharp
   services.AddSingleton<IObservabilityConnector, MyObservabilityConnector>();
   ```

3. The `ConnectorRegistry` aggregates all registered connectors and makes them available to the runtime.

**Existing connector implementations:**

| Implementation | Interface | Lifetime |
|---|---|---|
| `AzureMonitorObservabilityConnector` | `IObservabilityConnector` | Singleton |
| `InMemoryRunbookConnector` | `IRunbookConnector` | Singleton |
| `StaticActionTargetConnector` | `IActionTargetConnector` | Singleton |

---

## Creating a Pack

See [PACKS.md](PACKS.md) for the full pack specification, directory layout, `pack.json` schema, and examples.

Quick steps:

1. Create `packs/<pack-id>/pack.json` following the schema in PACKS.md.
2. Add queries, runbooks, governance defaults, and an optional README.
3. Submit a PR following the checklist below.

---

## Coding Conventions

| Convention | Rule |
|---|---|
| Language | C# 13 / .NET 10.0 |
| Nullable references | Enabled project-wide |
| Naming | PascalCase for public members, `_camelCase` for private fields |
| Async | All I/O-bound methods must be `async Task<T>` / `async ValueTask<T>` |
| Records | Prefer `sealed record` for DTOs and value objects |
| Exceptions | Domain-specific exceptions (e.g. `PolicyDeniedException`) with structured data |
| Logging | Use structured logging with `ILogger<T>` — no string interpolation in log templates |
| Configuration | Bind via `IOptions<T>` / `IOptionsSnapshot<T>` — never read `IConfiguration` directly in domain/application layers |

---

## Dependency Rules

See [docs/pdd/DEPENDENCY_RULES.md](docs/pdd/DEPENDENCY_RULES.md) for the full dependency conformance rules.

Key principles:

- **Presentation → Application → Domain** (never the reverse)
- **Infrastructure → Application** (implements application interfaces)
- **Modules never reference other modules directly** — use BuildingBlocks.Contracts
- **Hosts reference Presentation + Infrastructure** for wiring only
- **BuildingBlocks.Domain has zero external dependencies**

---

## Testing Requirements

| Requirement | Details |
|---|---|
| Unit tests | Required for all domain logic, orchestrators, and validators |
| Test location | `tests/Modules/<ModuleName>/` mirroring `src/Modules/<ModuleName>/` |
| Naming | `<ClassUnderTest>Tests.cs` or `<Feature>Tests.cs` |
| Assertions | Use a fluent assertion library |
| Coverage | Aim for meaningful coverage — focus on business rules, not boilerplate |
| Integration tests | Place in `tests/Integration/` — these may require Docker or Azure credentials |
| Contract tests | MCP contract tests live in `tests/McpContractTests/` |

All tests must pass (`dotnet test OpsCopilot.sln`) before merging.

---

## Pull Request Checklist

Before submitting a PR, ensure:

- [ ] `dotnet build OpsCopilot.sln` succeeds with zero warnings
- [ ] `dotnet test OpsCopilot.sln` — all tests pass
- [ ] New public APIs have XML doc comments
- [ ] New modules follow the four-layer structure (Domain / Application / Infrastructure / Presentation)
- [ ] New endpoints are wired in `Program.cs` (both service registration and endpoint mapping)
- [ ] Configuration keys are documented in `appsettings.json` (with safe defaults) and `appsettings.Development.json`
- [ ] Dependency rules are satisfied (no upward or cross-module references)
- [ ] New evaluation scenarios are registered and appear at `GET /evaluation/scenarios`
- [ ] Evidence documentation is added under `docs/` if introducing a new slice/feature
- [ ] Pack contributions include a valid `pack.json` manifest

---

## Commit Messages

Use clear, descriptive commit messages:

```
<type>(<scope>): <short summary>

<optional body>
```

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `chore`, `ci`

Examples:
- `feat(safe-actions): add rollback endpoint`
- `fix(governance): correct token budget overflow`
- `docs: update README quick-start section`
- `test(evaluation): add DryRunGuard scenario`

---

## License

TBD — license has not yet been decided. All contributions are made with the understanding that the license will be determined and applied to the entire repository, including all contributions, retroactively.

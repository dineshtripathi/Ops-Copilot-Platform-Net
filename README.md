# Ops Copilot Platform

Ops Copilot is a modular .NET platform for operations triage, alert ingestion, governance, reporting, and safe action orchestration.

## Solution Layout

- `src/BuildingBlocks/` – Shared cross-cutting libraries (Application, Contracts, Domain, Infrastructure)
- `src/Hosts/`
  - `OpsCopilot.ApiHost/` – Main API host
  - `OpsCopilot.McpHost/` – MCP host and operational tools
  - `OpsCopilot.WorkerHost/` – Background processing host
- `src/Modules/` – Bounded modules (AgentRuns, AlertIngestion, Connectors, Evaluation, Governance, Prompting, Rag, Reporting, SafeActions, Tenancy)
- `tests/` – Integration, module, and MCP contract test projects
- `infrastructure/` – Azure deployment artifacts (Bicep)
- `.github/workflows/` – CI/CD workflows, including infra deployment
- `Doc/PROJECT_VISION.md` – Product vision and target architecture
- `Doc/pdd/` – Product/design decision documentation

## Prerequisites

- .NET SDK 10.0+
- PowerShell 5.1+ or PowerShell 7+

## Quick Start

```powershell
# Restore and build full solution
dotnet build OpsCopilot.sln

# Run API host
dotnet run --project src/Hosts/OpsCopilot.ApiHost/OpsCopilot.ApiHost.csproj

# Run MCP host
dotnet run --project src/Hosts/OpsCopilot.McpHost/OpsCopilot.McpHost.csproj
```

## Testing

```powershell
dotnet test OpsCopilot.sln
```

## Infrastructure

Azure infrastructure assets are under `infrastructure/`.
See `infrastructure/README.md` for deployment details, cost guardrails, and optional Azure AI Foundry enablement.

## Module Ownership & Status

Use this table as a living ownership and maturity tracker. Keep dependency direction aligned with `Doc/pdd/DEPENDENCY_RULES.md`.

| Module | Owning Team | Tech Lead | Status | Notes |
| --- | --- | --- | --- | --- |
| AgentRuns | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| AlertIngestion | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Connectors | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Evaluation | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Governance | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Prompting | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Rag | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Reporting | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| SafeActions | _TBD_ | _TBD_ | Planned / In Progress / Production | |
| Tenancy | _TBD_ | _TBD_ | Planned / In Progress / Production | |

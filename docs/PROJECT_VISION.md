# Product Vision — Ops Copilot Platform

## 1) Product Definition
Ops Copilot is a governed, auditable platform for incident triage, investigation support, and operational optimization. It combines modular domain services, policy controls, and AI-assisted workflows to reduce MTTR, improve operational consistency, and surface measurable cost and reliability outcomes.

## 2) Goals
- Reduce incident response time through structured triage and evidence-driven investigation.
- Improve operator productivity via guided recommendations and reusable run patterns.
- Enforce governance and safety for all assistant-driven operations.
- Preserve auditability of runs, decisions, and remediation actions.
- Provide a modular architecture that supports tenant-aware scaling and extension.

## 3) Non-Goals
- Fully autonomous high-risk remediation without approvals.
- Cross-cloud abstraction in MVP beyond agreed connector boundaries.
- Custom model training/fine-tuning as part of core platform delivery.
- Replacing existing operational ownership models or change control processes.

## 4) Success Measures
- MTTR trend improvement across onboarded services.
- % incidents resolved with Copilot-assisted workflows.
- Reduction in repeated incident classes via captured run insights.
- Adoption metrics: active tenants, active modules, session continuity.
- Governance metrics: policy-compliant runs and approved action ratio.

## 5) Architecture Principles

### 5.1 Clean Architecture
Dependency direction is strictly inward and follows the rules in `docs/pdd/DEPENDENCY_RULES.md`.

### 5.2 Modular Monolith Boundaries
Each module owns its domain, application orchestration, infrastructure adapters, and optional presentation surface.

### 5.3 Host Composition Rules
- `ApiHost`: composes presentation layers for API traffic.
- `WorkerHost`: composes background workflows/schedulers.
- `McpHost`: exposes MCP tools and connector-backed operations.

### 5.4 Contracts-First Interop
Shared interactions happen through stable contracts and abstractions, not direct cross-module infrastructure coupling.

## 6) Canonical Repository Shape

### 6.1 Source
- `src/BuildingBlocks` (4): Domain, Application, Infrastructure, Contracts.
- `src/Modules` (35 projects across 10 modules):
	- AlertIngestion (Domain, Application, Infrastructure, Presentation)
	- AgentRuns (Domain, Application, Infrastructure, Presentation)
	- Rag (Domain, Application, Infrastructure)
	- Tenancy (Domain, Application, Infrastructure, Presentation)
	- Governance (Domain, Application, Infrastructure, Presentation)
	- Connectors (Abstractions, Application, Infrastructure)
	- Prompting (Domain, Application, Infrastructure)
	- SafeActions (Domain, Application, Infrastructure, Presentation)
	- Evaluation (Application, Infrastructure)
	- Reporting (Domain, Application, Infrastructure, Presentation)
- `src/Hosts` (3): ApiHost, WorkerHost, McpHost.

### 6.2 Tests
- `tests/Modules` (10 module test projects)
- `tests/Integration` (1 integration test project)
- `tests/McpContractTests` (1 MCP contract test project)

## 7) Functional Capabilities

### 7.1 Incident Triage & Investigation
- Ingest normalized alert context.
- Build investigation runs with traceable evidence.
- Provide summarized operator guidance and next-step recommendations.

### 7.2 Governance & Safety
- Enforce policy gates for risky operations.
- Require approvals for governed safe actions.
- Record immutable run metadata for auditing.

### 7.3 Connector and Tooling Extensibility
- Support connector abstractions for external systems.
- Expose bounded tool interfaces via MCP host.
- Keep host/tool composition decoupled from module internals.

### 7.4 Reporting and Evaluation
- Track operational outcomes and run quality signals.
- Support evaluation pipelines for prompt/agent behavior quality.

## 8) Data & Compliance Direction
- Retain only required run context and telemetry by policy.
- Protect sensitive data through redaction and access controls.
- Keep environment and tenant boundaries explicit in all persisted run artifacts.
- Ensure policy and access checks are enforceable at service boundaries.

## 9) Delivery Phases

### Phase 1 — Foundation
- Establish canonical repo shape and dependency-guarded project graph.
- Baseline host composition and module scaffolds.

### Phase 2 — Core Operations
- Implement alert ingestion, agent runs, and safe actions.
- Activate governance policy flow and MCP contract surfaces.

### Phase 3 — Intelligence and Scale
- Expand reporting/evaluation loops.
- Improve connector coverage and tenant onboarding maturity.

## 10) Risks and Mitigations
- **Risk:** Cross-module coupling drift.
	- **Mitigation:** Enforce `docs/pdd/DEPENDENCY_RULES.md` in PR validation.
- **Risk:** Policy bypass in host composition.
	- **Mitigation:** Centralize policy checks in application workflows.
- **Risk:** Tooling sprawl and unstable interfaces.
	- **Mitigation:** Keep contracts-first boundaries and versioned abstractions.

## 11) Decision Authority
If implementation choices conflict with the dependency rules or canonical repository shape, follow `docs/pdd/DEPENDENCY_RULES.md` and this vision document as the source of truth.


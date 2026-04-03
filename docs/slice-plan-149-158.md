# OpsCopilot — Slice Plan 149–158 (Ground-Truth Audit)

> **Audit date:** Post-Slice 148  
> **Build baseline:** 0 errors · 0 warnings · 1326 tests passing  
> **Last commit:** `0bb2d58` (`MafActivityEnvelope.cs`)  
> **Method:** Systematic read of every .cs file in all 11 modules + 3 hosts. Zero TODO / FIXME / NotImplementedException found.

---

## True Gap Register

These gaps were confirmed by reading actual source code. None are speculative.

| ID | Severity | Description | Evidence File(s) |
|----|----------|-------------|-----------------|
| G1 | 🔴 PROD BLOCKER | **No API authentication/authorization.** All endpoints open — `/agent/triage`, `/safe-actions/*`, `/reports/*`, `/tenants/*`, `/evaluation/*`, `/runbooks/*`. Zero `AddAuthentication`, `AddAuthorization`, `RequireAuthorization` calls in ApiHost Program.cs. | `src/Hosts/OpsCopilot.ApiHost/Program.cs` |
| G2 | 🔴 PROD BLOCKER | **VectorStore only has `DevInMemoryVectorStoreCollection`.** When `Rag:UseVectorRunbooks=true` or `Rag:UseVectorMemory=true`, a ConcurrentDictionary-backed collection is registered. `GetAsync(filter, top)` returns `EmptyAsyncEnumerable` — semantic runbook search and incident memory are silent no-ops in production. No Azure AI Search, Qdrant, or any durable vector store registered anywhere. | `src/Hosts/OpsCopilot.ApiHost/Infrastructure/VectorStoreExtensions.cs` |
| G3 | 🟠 OPS-READINESS | **No rate limiting.** No `AddRateLimiter` / `UseRateLimiter` / `RequireRateLimiting` anywhere. High-cost endpoints (`/agent/triage`, `/evaluation/run`) can be called without throttle, which spins up LLM calls and MCP child processes per request. | `src/Hosts/OpsCopilot.ApiHost/Program.cs` |
| G4 | 🟠 OPS-READINESS | **No distributed tracing / OpenTelemetry.** Only `builder.Logging.AddConsole()` in ApiHost. No `AddOpenTelemetry()`, no OTLP export, no `ActivitySource`, no metrics pipeline. Cannot correlate a triage run across ApiHost → McpHost in any APM tool. | `src/Hosts/OpsCopilot.ApiHost/Program.cs` |
| G5 | 🟠 OPS-COMPLETENESS | **WorkerHost has only one worker.** `ProposalDeadLetterReplayWorker` is the only `BackgroundService`. No alert ingestion pump, no scheduled evaluation runner, no reporting aggregation. WorkerHost registers `AddConnectorsModule + AddPacksModule` only — AlertIngestion, Evaluation, and Reporting modules not wired. | `src/Hosts/OpsCopilot.WorkerHost/Program.cs` |
| G6 | 🟡 TESTING | **No `WorkerHost.Tests` project.** 14 test projects exist (11 module + ApiHost.Tests + McpContractTests + Integration.Tests). `ProposalDeadLetterReplayWorker.ProcessPendingEntriesAsync()` is `internal` and testable but has no test class. | `tests/Hosts/` (only `ApiHost.Tests/` present) |
| G7 | 🟡 TESTING | **Integration tests cover only 2 scenarios.** `tests/Integration/` contains `BuildChildEnvironmentTests.cs` + `McpStdioKqlToolClientIntegrationTests.cs` only. No E2E triage flow, no alert ingestion end-to-end, no SafeActions approval flow, no Blazor page smoke test. | `tests/Integration/OpsCopilot.Integration.Tests/` |
| G8 | 🟡 SECURITY | **McpHost inbound `/mcp` endpoint has no authentication.** McpHost has correct outbound Azure auth (`AzureAuth:Mode` → `DefaultAzureCredential` chain). But the SSE endpoint at `:8081/mcp` itself has zero bearer-token / API-key check — any network-reachable process can invoke KQL queries and ARM reads. | `src/Hosts/OpsCopilot.McpHost/Program.cs` |
| G9 | 🟡 FEATURE | **Evaluation is a local test harness, not an AI quality pipeline.** `EvaluationRunner` is 32 lines, deterministic, no I/O, no LLM calls. All 11 scenarios are unit-test style logic checks (payload parsing, filter math, action classification). No LLM-graded evaluation, no groundedness/relevance scoring, no ROUGE/BLEU, no baseline comparison. | `src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/` |
| G10 | 🟡 FEATURE | **Prompting module is a single service class.** `Prompting.Application/Services/PromptRegistryService.cs` is the only Application-layer file. No A/B testing framework, no canary prompt deployment, no quality-gate before promotion, no automatic rollback path. | `src/Modules/Prompting/Application/OpsCopilot.Prompting.Application/Services/PromptRegistryService.cs` |

### Confirmed NOT Gaps

| Claim | Reality |
|-------|---------|
| `ITargetScopeEvaluator` not implemented | `ConfigTargetScopeEvaluator` in Packs.Infrastructure, registered as singleton |
| MAF not wired | `builder.AddAgent<TriageAgentActivityHandler>()` in ApiHost Program.cs |
| AG-UI not implemented | `AgUiEvents.cs` with typed event records in AgentRuns.Presentation |
| VectorStoreCollection not registered | Registered — but only with DevInMemory backing (G2 above) |
| Health checks missing | `AddOpsCopilotHealthChecks` + `MapOpsCopilotHealthChecks` (Slice 140) |
| StuckRunWatchdog missing | Registered in ApiHost Program.cs |
| Governance policies not implemented | All 4 policies present: DegradedMode, Session, TokenBudget, ToolAllowlist |
| SafeActions executors missing | All 6 executors: AzureMonitorQuery, AzureResourceGet, DryRun, HttpProbe, Routing, Stub |

---

## Slice Plan 149–158

### Dependency Order

```
149 (auth) ──┐
150 (vstore)─┤
             ├──► 151 (rate limit — uses auth claims)
152 (OTel)   │          │
153 (McpAuth)│          │
154 (Worker) ├──► 155 (WorkerHost.Tests)
             │          │
             └──► 156 (Integration tests — needs auth from 149)
157 (AI eval)  ← standalone
158 (Prompting A/B) ← standalone
```

---

### Slice 149 — API Authentication & Authorization
**Gap:** G1 | **Severity:** 🔴 Production blocker | **Must land before:** 151, 156

#### Objective
Add Entra ID JWT bearer authentication and authorization to all ApiHost endpoints. All handler endpoints become protected. MAF bot endpoint remains configurable.

#### Scope
- `OpsCopilot.ApiHost` only — no changes to modules or McpHost
- Additive: `AddAuthentication`, `AddAuthorization`, `UseAuthentication`, `UseAuthorization` middleware
- Config keys (verify in `appsettings.json` before adding): `Authentication:Entra:TenantId`, `Authentication:Entra:Audience`

#### Acceptance Criteria
- [ ] `POST /agent/triage` returns 401 without valid bearer token  
- [ ] `GET /reports/dashboard` returns 401 without valid bearer token  
- [ ] `GET /healthz/live` returns 200 without auth (health probes must remain public)  
- [ ] `POST /api/agent/messages` behaviour controlled by existing `requireAuth` parameter  
- [ ] `ApiHost.Tests` validates 401 on protected endpoints with no/invalid token  
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass

#### Key files to change
```
src/Hosts/OpsCopilot.ApiHost/Program.cs
src/Hosts/OpsCopilot.ApiHost/appsettings.json       ← add Authentication:Entra section
src/Hosts/OpsCopilot.ApiHost/appsettings.Development.json
tests/Hosts/OpsCopilot.ApiHost.Tests/               ← auth integration tests
```

#### Notes
- `app.MapAgentEndpoints(requireAuth: false, ...)` already has the parameter — set to `true` once real auth is in place
- Use `AddJwtBearer` with `Authority` + `Audience` from configuration; **never hardcode values**
- Development: allow `appsettings.Development.json` to set `Authentication:DevBypass: true` to skip validation locally (never in staging/prod)

---

### Slice 150 — Azure AI Search Vector Store Backend
**Gap:** G2 | **Severity:** 🔴 Production blocker | **No dependencies**

#### Objective
Replace `DevInMemoryVectorStoreCollection` with a real durable vector store backend when configured. Keep the in-memory fallback for development.

#### Scope
- `VectorStoreExtensions.cs` in ApiHost.Infrastructure — conditional registration logic
- New config section: `Rag:VectorBackend` (`"AzureAISearch"` | `"InMemory"`)
- No changes to Rag module abstractions or domain — the `VectorStoreCollection<TKey, TRecord>` interface stays the same

#### Acceptance Criteria
- [ ] When `Rag:VectorBackend=AzureAISearch` → `AzureAISearchVectorStoreCollection<>` is registered using `Rag:AzureAISearch:Endpoint` and index names
- [ ] When `Rag:VectorBackend=InMemory` (default) → `DevInMemoryVectorStoreCollection` used (current dev path)
- [ ] Missing `Rag:AzureAISearch:Endpoint` with `VectorBackend=AzureAISearch` logs a startup warning and falls back to in-memory (graceful degradation)
- [ ] `VectorRunbookRetrievalService.SearchAsync()` returns results (not empty) against a real index in integration test
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass (in-memory path still works)

#### Key files to change
```
src/Hosts/OpsCopilot.ApiHost/Infrastructure/VectorStoreExtensions.cs
src/Hosts/OpsCopilot.ApiHost/appsettings.json               ← add Rag:VectorBackend + Rag:AzureAISearch section
tests/Modules/Rag/OpsCopilot.Rag.Tests/                     ← update/extend collection tests
```

#### Config keys to add
```json
"Rag": {
  "VectorBackend": "InMemory",
  "AzureAISearch": {
    "Endpoint": "",
    "RunbooksIndexName": "opscopilot-runbooks",
    "MemoryIndexName": "opscopilot-incident-memory"
  }
}
```

---

### Slice 151 — Rate Limiting
**Gap:** G3 | **Severity:** 🟠 Ops-readiness | **Depends on:** Slice 149 (auth before rate limit middleware ordering)

#### Objective
Add fixed-window rate limiting to all ApiHost endpoint groups, keyed by authenticated client identity (sub claim) where available, IP address as fallback.

#### Scope
- ApiHost only — `Program.cs` additions, no module changes
- Per-policy limits: triage (expensive, LLM + MCP call) vs. read-only reporting endpoints

#### Acceptance Criteria
- [ ] `POST /agent/triage` → limited to configurable requests per window (default: 10/min)
- [ ] `GET /reports/*` → higher limit (default: 120/min)
- [ ] `POST /ingest/alert` → limited to 60/min (ingest noise protection)
- [ ] Returns HTTP 429 with `Retry-After` header when exceeded
- [ ] Limits configurable via `RateLimit:Triage:MaxRequests`, `RateLimit:Triage:WindowSeconds`
- [ ] Health probes exempt
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass

#### Key files to change
```
src/Hosts/OpsCopilot.ApiHost/Program.cs
src/Hosts/OpsCopilot.ApiHost/appsettings.json       ← add RateLimit section
tests/Hosts/OpsCopilot.ApiHost.Tests/               ← 429 response tests
```

---

### Slice 152 — OpenTelemetry Distributed Tracing & Metrics
**Gap:** G4 | **Severity:** 🟠 Ops-readiness | **No dependencies**

#### Objective
Add OpenTelemetry traces and metrics to ApiHost (and optionally McpHost). Export via OTLP to a configurable endpoint (e.g. Application Insights via OTLP, Jaeger, or stdout in dev).

#### Scope
- `AddOpenTelemetry()` in ApiHost Program.cs
- `ActivitySource` spans on `TriageOrchestrator.RunAsync()` and `SafeActionOrchestrator.ProposeAsync()`/`ExecuteAsync()`
- Metrics: `opscopilot.triage.runs` counter, `opscopilot.triage.latency_ms` histogram, `opscopilot.policy.denials` counter
- McpHost: trace propagation headers on outbound MCP stdio calls

#### Acceptance Criteria
- [ ] `app.RunAsync()` exports traces when `Telemetry:OtlpEndpoint` is configured
- [ ] TriageOrchestrator creates a `triage.run` span with `run.id`, `alert.source`, `mode` attributes
- [ ] SafeActionOrchestrator creates spans for propose / execute / rollback phases
- [ ] Metrics are registered and incremented at correct points
- [ ] When `Telemetry:OtlpEndpoint` is empty → console exporter used (dev-safe default)
- [ ] No secrets in trace attributes (only identifiers — run ID, tenant ID, error codes)
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass

#### Key files to change
```
src/Hosts/OpsCopilot.ApiHost/Program.cs
src/Modules/AgentRuns/Application/OpsCopilot.AgentRuns.Application/Orchestration/TriageOrchestrator.cs
src/Modules/SafeActions/Application/OpsCopilot.SafeActions.Application/Orchestration/SafeActionOrchestrator.cs
src/Hosts/OpsCopilot.ApiHost/appsettings.json       ← add Telemetry:OtlpEndpoint
```

---

### Slice 153 — McpHost Inbound Endpoint Authentication
**Gap:** G8 | **Severity:** 🟡 Security | **No dependencies**

#### Objective
Add API-key or bearer token validation to the McpHost SSE endpoint (`/mcp`). Any caller without a valid key is rejected 401.

#### Scope
- McpHost only — no ApiHost changes
- Minimal middleware: validate `Authorization: Bearer <key>` or `X-Api-Key: <key>` header against a configured secret
- Config-driven: `McpAuth:ApiKey` (or Entra audience for bearer if preferred)

#### Acceptance Criteria
- [ ] `GET /mcp` (SSE handshake) without valid key → 401
- [ ] `GET /mcp` with valid configured key → SSE session established
- [ ] McpStdioKqlToolClientIntegrationTests updated to supply key in test configuration
- [ ] Key value is **never logged**
- [ ] When `McpAuth:ApiKey` is empty → startup warning logged; endpoint remains open with log noise (dev-safe fallback)
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass

#### Key files to change
```
src/Hosts/OpsCopilot.McpHost/Program.cs
src/Hosts/OpsCopilot.McpHost/appsettings.json       ← add McpAuth section
tests/Integration/OpsCopilot.Integration.Tests/McpStdioKqlToolClientIntegrationTests.cs
```

---

### Slice 154 — WorkerHost: AlertIngestion Background Worker
**Gap:** G5 | **Severity:** 🟠 Ops-completeness | **No dependencies**

#### Objective
Add an `AlertIngestionWorker` to WorkerHost that polls (or subscribes) for incoming alert payloads and dispatches them through `IAlertTriageDispatcher`, completing the automated triage loop without operator HTTP interaction.

#### Scope
- New `AlertIngestionWorker.cs` in `OpsCopilot.WorkerHost/Workers/`
- WorkerHost `Program.cs` wires `AddAlertIngestionModule()` and registers the new worker
- Worker polls a configurable source (Azure Service Bus queue or simple in-memory queue) — interface-backed so swap is possible
- `IAlertTriageDispatcher` is the existing abstraction (already replaced in ApiHost with `TriageOrchestratorDispatcher`)

#### Acceptance Criteria
- [ ] `AlertIngestionWorker` implements `BackgroundService`
- [ ] Worker polls at `AlertIngestion:Worker:PollIntervalSeconds` (default: 30)
- [ ] Uses `IAlertNormalizer` + `IAlertTriageDispatcher` (no direct orchestrator calls)
- [ ] Worker handles transient failures with retry + backoff; dead-letters after `MaxRetries`
- [ ] WorkerHost `Program.cs` registers `AlertIngestionWorker` alongside `ProposalDeadLetterReplayWorker`
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass

#### Key files to change
```
src/Hosts/OpsCopilot.WorkerHost/Program.cs
src/Hosts/OpsCopilot.WorkerHost/Workers/AlertIngestionWorker.cs        ← new
src/Hosts/OpsCopilot.WorkerHost/appsettings.Development.json
```

---

### Slice 155 — WorkerHost.Tests Project
**Gap:** G6 | **Severity:** 🟡 Testing | **Depends on:** Slice 154

#### Objective
Create `tests/Hosts/OpsCopilot.WorkerHost.Tests` test project, covering both existing and new workers.

#### Scope
- New test project mirroring `ApiHost.Tests` structure
- Test `ProposalDeadLetterReplayWorker.ProcessPendingEntriesAsync()`: happy path, max-retries enforcement, no-op when queue is empty
- Test `AlertIngestionWorker.ExecuteAsync()`: dispatches on poll hit, handles exceptions without crashing host

#### Acceptance Criteria
- [ ] `OpsCopilot.WorkerHost.Tests.csproj` added to `OpsCopilot.sln`
- [ ] ≥ 8 new tests (≥ 3 for each worker)
- [ ] Uses `IHostedService` test pattern — start/stop via `CancellationTokenSource`
- [ ] All mocks via `Moq` (consistent with all other module test projects)
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior + new tests pass (1326 + new count)

#### Key files to create
```
tests/Hosts/OpsCopilot.WorkerHost.Tests/
    OpsCopilot.WorkerHost.Tests.csproj
    Workers/ProposalDeadLetterReplayWorkerTests.cs
    Workers/AlertIngestionWorkerTests.cs
```

---

### Slice 156 — Integration Test E2E Coverage Expansion
**Gap:** G7 | **Severity:** 🟡 Testing | **Depends on:** Slice 149 (auth tokens needed for E2E calls)

#### Objective
Expand integration tests from 2 scenarios to cover the three critical operational flows: alert ingest → triage dispatch, SafeActions propose → approve → execute, and Reporting query.

#### Scope
- New test classes in `tests/Integration/OpsCopilot.Integration.Tests/`
- Uses WebApplicationFactory<Program> (ApiHost) with in-memory module registrations
- No real Azure calls — all external deps mocked via test doubles

#### Acceptance Criteria
- [ ] `AlertIngestionE2ETests`: POST `/ingest/alert` → verify `IAlertTriageDispatcher.DispatchAsync()` called with normalised payload
- [ ] `SafeActionsApprovalE2ETests`: POST `/safe-actions/propose` → GET `/safe-actions/{id}` (Pending) → POST `/safe-actions/{id}/approve` → GET `/safe-actions/{id}` (Approved/Executed)
- [ ] `DashboardReportingE2ETests`: GET `/reports/dashboard` returns well-typed `DashboardDto` (not empty)
- [ ] All tests use authenticated JWT (mock token) consistent with Slice 149 auth
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior + new tests pass

#### Key files to create
```
tests/Integration/OpsCopilot.Integration.Tests/
    AlertIngestionE2ETests.cs
    SafeActionsApprovalE2ETests.cs
    DashboardReportingE2ETests.cs
```

---

### Slice 157 — AI Quality Evaluation Pipeline
**Gap:** G9 | **Severity:** 🟡 Feature | **No dependencies**

#### Objective
Extend the Evaluation module from a local test harness to a real AI quality pipeline: LLM-graded scenarios that score triage response quality (groundedness, relevance, coherence).

#### Scope
- New `LlmGradedScenario` base class alongside existing deterministic scenarios
- `GroundednessScorer` and `RelevanceScorer` services using the same `IEmbeddingGenerator` already wired
- New `POST /evaluation/run/async` endpoint that runs scenarios and persists results (non-blocking)
- Evaluation results persisted to SQL (new `EvaluationResult` aggregate — check if migration needed)

#### Acceptance Criteria
- [ ] At least 2 LLM-graded scenario implementations: triage response for a known alert, runbook retrieval groundedness
- [ ] `EvaluationScenarioCatalog.GetAllScenarios()` returns both deterministic and LLM-graded scenarios
- [ ] `POST /evaluation/run/async` returns `202 Accepted` with `Location` header
- [ ] `GET /evaluation/run/{id}` returns scored results once complete
- [ ] Scores are `float` 0.0–1.0; below configurable threshold produces `Failed` status
- [ ] `NullEmbeddingGenerator` (dev mode) produces neutral scores (0.5) rather than crashing
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass; ≥ 4 new tests covering scorer logic

#### Key files to change/create
```
src/Modules/Evaluation/Application/OpsCopilot.Evaluation.Application/
    Scenarios/LlmGraded/              ← new directory
    Services/GroundednessScorer.cs    ← new
    Services/RelevanceScorer.cs       ← new
src/Modules/Evaluation/Presentation/OpsCopilot.Evaluation.Presentation/Endpoints/EvaluationEndpoints.cs
tests/Modules/Evaluation/OpsCopilot.Evaluation.Tests/
```

---

### Slice 158 — Prompting A/B Framework
**Gap:** G10 | **Severity:** 🟡 Feature | **No dependencies**

#### Objective
Add a canary/A/B testing framework to the Prompting module so operators can deploy a new prompt version to a traffic percentage, evaluate quality gates, and promote or rollback.

#### Scope
- `CanaryPromptStrategy` wrapping `PromptRegistryService` — routes X% of requests to candidate version
- `PromotionGate` service — validates candidate version passes score threshold before full promotion
- New endpoints: `POST /prompting/versions/{version}/promote`, `POST /prompting/versions/{version}/rollback`
- Traffic split config: `Prompting:Canary:CandidateVersion`, `Prompting:Canary:TrafficPercent`

#### Acceptance Criteria
- [ ] When `Prompting:Canary:TrafficPercent=10`, ~10% of calls receive candidate version (deterministic via request hash)
- [ ] `POST /prompting/versions/{version}/promote` sets candidate as active if `PromotionGate.Evaluate()` passes
- [ ] `POST /prompting/versions/{version}/rollback` resets active version to previous
- [ ] Gate threshold configurable: `Prompting:PromotionGate:MinGroundednessScore`
- [ ] Build: 0 errors · 0 warnings
- [ ] All prior tests pass; ≥ 6 new tests for canary routing logic and promotion gate

#### Key files to change/create
```
src/Modules/Prompting/Application/OpsCopilot.Prompting.Application/Services/
    CanaryPromptStrategy.cs           ← new
    PromotionGateService.cs           ← new
src/Modules/Prompting/Presentation/OpsCopilot.Prompting.Presentation/Endpoints/
    PromptingEndpoints.cs             ← new (promote/rollback routes)
tests/Modules/Prompting/OpsCopilot.Prompting.Tests/
```

---

## Summary Table

| Slice | Gap | Title | Severity | Depends On | Est. Files Changed |
|-------|-----|-------|----------|------------|--------------------|
| 149 | G1 | API Auth & Authorization | 🔴 PROD BLOCKER | — | ~4 |
| 150 | G2 | Azure AI Search Vector Store | 🔴 PROD BLOCKER | — | ~3 |
| 151 | G3 | Rate Limiting | 🟠 Ops-readiness | 149 | ~3 |
| 152 | G4 | OpenTelemetry Tracing & Metrics | 🟠 Ops-readiness | — | ~4 |
| 153 | G8 | McpHost Inbound Authentication | 🟡 Security | — | ~3 |
| 154 | G5 | WorkerHost AlertIngestion Worker | 🟠 Ops-completeness | — | ~3 |
| 155 | G6 | WorkerHost.Tests Project | 🟡 Testing | 154 | ~4 (new project) |
| 156 | G7 | Integration Test E2E Coverage | 🟡 Testing | 149 | ~3 (new tests) |
| 157 | G9 | AI Quality Evaluation Pipeline | 🟡 Feature | — | ~6 |
| 158 | G10 | Prompting A/B Framework | 🟡 Feature | — | ~5 |

---

## Non-Negotiables (CLAUDE.md §2) — applies to every slice

- No HTTP route changes beyond the slice scope  
- No breaking DTO changes (additive-only)  
- No DB schema changes without an explicit migration file  
- No secrets in logs, docs, or tests  
- No modification of `.github/workflows/*`  
- Every slice: build gate (`-warnaserror`) + test gate (all prior tests pass)

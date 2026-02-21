# Product Design Document (PDD)
## Ops Copilot — Incident + Cost Leakage Killer (Azure + .NET)

### Document Status
- **Status:** Locked direction / build spec  
- **Audience:** Developers + AI pair-programmers (Claude Opus / Codex) + Tech reviewers  
- **Rule:** If any implementation suggestion conflicts with this PDD, **reject it** and follow this PDD.

---

## 0) One-sentence Product Definition
Ops Copilot is a **governed, auditable agent platform** that ingests alerts, investigates incidents using **agentic AI + MCP tools + RAG**, proposes safe remediations with approvals/rollback, and proves **measurable cost savings and MTTR improvements**.

---

## 1) Goals, Non-Goals, and Success Metrics

### 1.1 Goals (Must Achieve)
1) **Reduce MTTR** by accelerating triage + investigation (evidence-driven, tool-backed).  
2) **Reduce cloud waste** by scheduled scans + tracked savings ledger.  
3) **Enterprise safety**: policy guardrails, PII redaction, ACL-based doc access control, approvals, audit trail, token budgets.  
4) **Repeatability**: multi-tenant model, onboarding discovery + baselines, connector abstraction.

### 1.2 Non-Goals (Explicitly Out of Scope)
- Building a universal “do everything” platform on day one (multi-cloud, every connector, marketplace).
- Unbounded autonomous remediation (no auto-executing risky actions).
- Training/fine-tuning custom LLMs as part of MVP.
- Replacing existing SRE processes; this augments them with evidence and automation.

### 1.3 Success Metrics (Measured from Ledger + Dashboard)
- MTTR trend improvement (before/after adoption)
- % incidents resolved with Copilot assistance
- Verified savings: identified vs actioned vs saved (£)
- Reduction in repeated incidents (pattern detection + incident memory)
- Trust metrics: operator feedback scores, citation validity rates
- Spend control: token use within tenant budgets

---

## 2) “Hard Invariants” (Non-Negotiable Rules)
These are **must-not-change** constraints. Any deviation is derailment.

### 2.1 Safety & Governance Invariants
1) **No hallucinated facts:** Every factual claim must cite evidence (tool call ID or doc chunk ID).  
2) **Policy-as-code governs tool access**: allowlists, parameter constraints, output limits, degraded-mode rules.  
3) **Approvals required** for risky actions; “Safe Actions” never execute silently.  
4) **Rollback required** for any executed action type that supports rollback; rollback is approval-gated too.  
5) **PII redaction by default** before storing in the ledger; raw access is privileged + audited.  
6) **Document-level ACL enforced**: retrieval must filter by caller claims/roles/groups.

### 2.2 Operational Invariants
7) **Token budget enforcement** exists at orchestration level (per-run ceilings + per-tenant caps).  
8) **Idempotency and run locks** prevent duplicate runs/tickets and wasted spend.  
9) **Ledger is immutable audit trail** with DR/backup + point-in-time restore.  
10) **Embedding versioning**: no mixing vector spaces; upgrades require versioned migration/backfill.

### 2.3 Architecture Invariants
11) **MCP** is the tool boundary (agent ↔ tools).  
12) **AG-UI** is the streaming UI boundary (agent ↔ user/app).  
13) **Microsoft Agent Framework** is the orchestration foundation; do not replace with ad-hoc loops.  
14) **Microsoft.Extensions.AI** is the model abstraction layer (`IChatClient` pipeline).  
15) **Microsoft.Extensions.VectorData** is the vector abstraction layer (swap stores behind interfaces).

---

## 3) Target Users and Use Cases

### 3.1 Primary Users
- SRE / Ops engineers / on-call responders
- Platform engineers (cost, governance, policy)
- Engineering managers (reliability + savings reporting)

### 3.2 Primary Use Cases
- Alert arrives → triage → investigation → evidence-based recommendation
- Follow-up conversation (“check last 7 days”, “compare region”, “dig deeper into DB errors”)
- Scheduled scans → cost anomalies & governance drift → tickets/PR drafts
- Onboarding new tenant/environment → discovery + baselines
- Post-incident → incident memory update + runbook draft PR

---

## 4) System Architecture Overview

### 4.1 High-level layers
**Protocol Layer**
- MCP (tools)
- AG-UI (UI streaming)

**Agent Layer**
- Agent Framework orchestrations (sessions, workflows)

**AI Abstractions**
- Microsoft.Extensions.AI (`IChatClient` pipeline middleware)
- Microsoft.Extensions.VectorData (vector store abstraction)

**Retrieval**
- Azure AI Search (governed corpus)
- Qdrant (operational memory)

**Infra**
- Azure Container Apps (default)
- Key Vault, SQL/Postgres, Redis, App Insights/Log Analytics

---

## 5) Core Data Model (Canonical Entities)

### 5.1 Tenant
- tenantId, name, status
- environments (dev/test/prod)
- connector bindings
- policies (guardrails, budgets, retention)
- baselines per service/environment
- model routing policy

### 5.2 AlertPayload (Normalized)
- source, severity, timestamp
- resourceId/service, summary
- links, correlation hints
- fingerprint/idempotency key

### 5.3 AgentRun (Ledger root)
- runId, tenantId, env, userId/sessionId
- runType (triage/investigate/cost/scan)
- promptVersionId + modelId
- token usage + estimated cost
- status (success/degraded/incomplete/budget_hit)
- evidence map + citations
- feedback (rating + comments)

### 5.4 ToolCall (Ledger child)
- toolName, inputs (redacted if needed), outputs (redacted)
- timing, retries, errors
- evidenceId

### 5.5 ActionRecord (Safe Actions)
- proposed action, approvals, execution payload, outcome
- rollback payload + rollback status

### 5.6 KnowledgeChunk (RAG)
- docId, docVersion, chunkId
- embeddingModelId/version, createdAt
- ACL metadata (tier + allowed groups/roles)
- source link (repo path/URL/commit)

---

## 6) Component Specification (Formal Modules)

### 6.1 Agent API (ASP.NET Core)
Endpoints:
- `/agent/triage`, `/agent/investigate`, `/agent/cost`, `/session/{id}`

Responsibilities:
- enforce TenantContext + Policy Engine
- execute Agent Framework workflows
- enforce Token Budget Manager
- enforce Citation Integrity Validator
- redact outputs before storage/response
- emits AG-UI streaming events

### 6.2 Alert Ingestion Service (Webhook Receiver + Normalizer)
Endpoint:
- `/ingest/alert` (production entry point)

Responsibilities:
- per-source adapters → normalized AlertPayload
- idempotency dedupe (fingerprint + time window)
- optional queue to Service Bus then Agent API

### 6.3 Tenant Registry + Tenant Config Service
Responsibilities:
- tenant lifecycle: create → validate → activate → suspend
- stores tenant config schema (subscriptions, connectors, policies, baselines)
- TenantContext middleware injects tenant/env/user into every run

### 6.4 Connector Registry / Abstraction Layer
Responsibilities:
- interface categories: Observability, Deployment, Ticketing, Cost, Docs
- runtime resolution by tenant config
- MCP tools remain stable; connectors vary per tenant

### 6.5 Session Manager (Redis recommended)
Responsibilities:
- session state store + TTL (default 30 mins idle)
- optimistic concurrency
- cleanup via TTL + hygiene job

### 6.6 MCP Tool Servers
Minimum set:
- `kql_query`, `runbook_search`, `deployment_diff`, `cost_query`, `safe_actions`

Requirements:
- health checks
- timeout behavior + degraded mode integration
- no direct credential storage (uses Credential Manager references)

### 6.7 RAG Ingestion + Indexing Pipeline
Responsibilities:
- chunking, metadata extraction, dedupe
- embeddings generation
- dual write to Azure AI Search + Qdrant
- embedding versioning + migration/backfill pipeline

### 6.8 Vector Store Access Layer (Composite Retrieval + ACL Enforcement)
Responsibilities:
- use VectorData abstractions
- query Azure AI Search + Qdrant, merge/dedupe
- **consume ACL filters from 6.17** and enforce at query time
- strict citation mapping

### 6.9 Policy Engine (Policy-as-Code)
Policy domains:
- tool allowlists + param constraints
- output limits (rows/bytes)
- approval-required mapping
- degraded-mode behavior
- idempotency windows
- token budgets + ceilings
- retention defaults
- session TTL defaults

### 6.10 Token Budget Manager
Responsibilities:
- enforce per-run ceiling + per-tenant daily/monthly caps
- record token usage per step (prompt/completion/total) + estimated cost

### 6.11 Prompt Registry Service (Runtime Versioning + A/B)
Responsibilities:
- resolve `(tenantId, agentType, env)` → promptVersionId + content
- A/B rollout + sticky assignment per session/user
- link eval outcomes to prompt versions

### 6.12 Idempotency & Run Lock Manager
Responsibilities:
- dedupe identical alert triage runs
- lock scheduled scans per tenant/scanType
- prevent duplicate tickets/PR drafts

### 6.13 Safe Actions Execution + Rollback Manager
Responsibilities:
- execute approved actions; store execute + rollback payloads
- rollback requires approval and is auditable
- minimum: rollback for core action types; otherwise store manual rollback guidance

### 6.14 Agent Run Ledger + Evidence Store (DR-backed)
Responsibilities:
- immutable audit trail
- geo-redundant storage where supported
- point-in-time restore enabled
- retention aligned to tenant policy (min 90 days)
- periodic restore tests logged

### 6.15 Evaluation System (Offline + Online)
Offline (CI):
- golden scenarios; tool correctness; groundedness; safety; format

Online:
- feedback scores; retrieval confidence; drift monitoring

Links:
- outcomes tied to prompt versions + model versions

### 6.16 Reporting & Dashboard
Outputs:
- MTTR trends, verified savings, top incident categories
- docs health score, reliability scorecards (phase)

### 6.17 RAG Access Control (ACL) Service — Metadata + Rules Owner
**Boundary/Ownership:**
- **Owns** ACL metadata schema + ACL rules/policies (single source of truth)
- Provides `GetAclFilter(tenantId, userClaims, env)` → filter expression + aclPolicyId
- Admin/update ACL metadata workflows

**Enforcement:** performed by Vector Store Access Layer (6.8)

### 6.18 Tenant Credential Manager
Responsibilities:
- Key Vault namespacing: `tenant-{id}--connector-{name}--credential`
- prefer Managed Identity; secrets only fallback
- SQL stores Key Vault references only
- rotation tracking + proactive expiry warnings
- connector credential health checks

### 6.19 Onboarding Orchestration (Discovery + Baselines + Validation)
Responsibilities:
- Resource Graph discovery + dependency sampling
- baseline generation + tenant config population
- connector health validation
- periodic baseline refresh

---

## 7) Technology Stack (Locked)

### Runtime / App
- C#, .NET 10 LTS
- ASP.NET Core (Agent API, ingestion, AG-UI)
- Worker Services (scheduled scans, ingestion)

### Agent & AI Abstractions
- Microsoft Agent Framework
- Microsoft.Extensions.AI (`IChatClient`)
- Microsoft.Extensions.VectorData

### Protocols
- MCP (tools)
- AG-UI (UI streaming)

### Retrieval
- Azure AI Search (governed corpus)
- Qdrant (operational memory)

### Infra
- Azure Container Apps (default)
- ACR, Key Vault, Storage, Service Bus (optional)
- App Insights + Log Analytics

### Data
- Azure SQL or PostgreSQL (ledger + config + savings + prompts)
- Redis (sessions + caches + locks)

### CI/CD + IaC
- GitHub Actions or Azure DevOps
- OIDC to Azure
- Bicep or Terraform
- Docker

---

## 8) MVP Definition (No cuts, but sequenced)

### 8.1 MVP Deliverables (must exist by “enterprise-grade first release”)
- `/ingest/alert` + one adapter (Azure Monitor/App Insights)
- Triage agent + sessions
- RAG over runbooks (Azure AI Search) + Qdrant incident memory
- MCP tools: `kql_query`, `runbook_search`, `deployment_diff`
- Guardrails: allowlist + approvals + PII redaction + citation validator
- Token budgets: per-run ceiling + per-tenant caps
- Prompt registry (version selection; A/B optional)
- Idempotency + run locks
- Ledger + operator feedback
- Credential manager + connector health checks
- Onboarding orchestration
- One scheduled digest (docs health or recurring patterns)

### 8.2 MVP Sequencing (critical path vs parallel)
**Critical path (first demo-ready slice):**
1) Alert ingestion → 2) Triage agent → 3) MCP tools (kql + runbook) → 4) Ledger → 5) Guardrails

**Parallel / fast-follow (allowed during MVP):**
- prompt registry wiring, token cap expansion, ACL depth, onboarding automation, digest scheduling

---

## 9) “Do Not Derail” Change Control

### 9.1 Allowed Changes
- implementation details that preserve invariants
- adding connectors behind the connector abstraction
- adding tools via MCP without changing Agent API contracts

### 9.2 Forbidden Changes (require explicit human approval)
- removing/weakening any invariants (citations, approvals, ACL, redaction, budgets, idempotency)
- replacing Agent Framework / Extensions.AI / VectorData foundations
- changing the product goal (MTTR + savings + governance) into a generic chatbot


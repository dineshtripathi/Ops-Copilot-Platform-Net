# Dependency Conformance Guard

> Automated CI gate that prevents architecture-layer violations from reaching `main`.

## What it checks

The conformance script enforces the rules defined in
[docs/pdd/DEPENDENCY_RULES.md](../../docs/pdd/DEPENDENCY_RULES.md) by parsing
every `.csproj` file under `src/` and validating each `<ProjectReference>`
against the following rule categories:

| Category | IDs | Description |
|----------|-----|-------------|
| **Host** | H1–H4 | ApiHost may only reference Presentation + BuildingBlocks. McpHost must not reference module Infrastructure. |
| **Layer** | L1–L9 | Domain is innermost (no outward refs). Application must not reference Infrastructure/Presentation. Presentation must not reference Infrastructure. Infrastructure must not reference Presentation. |
| **Cross-module** | X1–X8 | Module layers must not reference another module's layers (use BuildingBlocks.Contracts for cross-cutting concerns). |
| **MCP boundary** | M1 | Warning if McpHost contains Web SDK or ASP.NET Core references that could bypass stdio transport. |

### Full rule reference

| Rule | Description |
|------|-------------|
| H1 | ApiHost → module Application forbidden |
| H2 | ApiHost → module Infrastructure forbidden |
| H3 | ApiHost → module Domain forbidden |
| H4 | McpHost → module Infrastructure forbidden |
| L1 | Domain → Application forbidden |
| L2 | Domain → Infrastructure forbidden |
| L3 | Domain → Presentation forbidden |
| L4 | Domain → Host projects forbidden |
| L5 | Application → Infrastructure forbidden |
| L6 | Application → Presentation forbidden |
| L7 | Application → Host projects forbidden |
| L8 | Presentation → Infrastructure forbidden |
| L9 | Infrastructure → Presentation forbidden |
| X1 | Cross-module: Application → another Application |
| X2 | Cross-module: Application → another Domain |
| X3 | Cross-module: Infrastructure → another Application |
| X4 | Cross-module: Infrastructure → another Domain |
| X5 | Cross-module: Infrastructure → another Infrastructure |
| X6 | Cross-module: Presentation → another Application |
| X7 | Cross-module: Presentation → another Domain |
| X8 | Cross-module: Presentation → another Presentation |
| M1 | McpHost uses Web SDK / ASP.NET Core (warning) |

## How to run locally

### PowerShell (Windows / macOS / Linux with pwsh)

```powershell
# From the repo root
./scripts/Test-DependencyConformance.ps1

# JSON output (useful for tooling integration)
./scripts/Test-DependencyConformance.ps1 -Format json
```

### Bash (requires pwsh installed)

```bash
./scripts/test-dependency-conformance.sh
./scripts/test-dependency-conformance.sh -Format json
```

### Exit codes

| Code | Meaning |
|------|---------|
| 0 | **PASS** — no violations detected |
| 1 | **FAIL** — one or more violations found |

## CI integration

The conformance check runs automatically in the **CI — Build, Conformance &
Test** workflow (`.github/workflows/ci.yml`) on every push to `main` and every
pull request targeting `main`.

It runs **before** `dotnet build` / `dotnet test` so violations fail fast
without wasting build minutes.

```yaml
- name: Dependency conformance check
  shell: pwsh
  run: ./scripts/Test-DependencyConformance.ps1
```

## How to interpret failures

A failure looks like:

```
  RESULT: FAIL  (1 violation(s))

  Host:ApiHost
    [H2] -> Mod:Reporting:Infrastructure  (ApiHost must not reference module Infrastructure)
```

**What to do:**

1. Identify the violated rule ID (e.g., `H2`).
2. Check the table above (or `DEPENDENCY_RULES.md`) for the rule meaning.
3. Fix the `<ProjectReference>` in the source `.csproj` to use the correct
   layer. For example, if ApiHost needs functionality from Reporting, create or
   use a `Reporting.Presentation` facade and reference that instead.

## Allowed exceptions (allowlist)

Some references intentionally deviate from the strict layer rules.
These are documented in the `$Allowlist` hash table inside
`scripts/Test-DependencyConformance.ps1`:

| Source | Target | Rule | Reason |
|--------|--------|------|--------|
| AgentRuns.Presentation | AgentRuns.Infrastructure | L8 | Composition root — wires EF Core DbContext + migration runner via DI |
| Rag.Presentation | Rag.Infrastructure | L8 | Composition root — wires RAG infrastructure stack via DI |
| SafeActions.Presentation | SafeActions.Infrastructure | L8 | Composition root — wires EF Core DbContext + migration runner via DI |

### Adding a new exception

1. Add an entry to the `$Allowlist` hash in `Test-DependencyConformance.ps1`.
2. Document the reason here and in the PR description.
3. Get reviewer approval — exceptions indicate an architectural trade-off.

## How to add or update rules

1. Update the source of truth: `docs/pdd/DEPENDENCY_RULES.md`.
2. Update the `Test-Reference` function and `$RuleDescriptions` in the
   conformance script.
3. Update this document's rule table.
4. Run locally to verify existing code still passes (or add allowlist entries
   for intentional deviations).

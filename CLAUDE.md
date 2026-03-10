# CLAUDE.md — OpsCopilot Dev Rules (STRICT)

## Purpose
This file defines how Claude should work in this repo: slice-based development, strict constraints, evidence docs, and test gating.

---

## 1) Operating Mode
- Work in **small slices** (e.g., Slice 49, Slice 50…).
- Each slice must have:
  - a clear objective
  - strict constraints
  - acceptance criteria (ACs)
  - evidence doc in `docs/dev-slice-XX-evidence.md`
  - build/test gates

---

## 2) Non-Negotiables (STRICT)
Unless the slice explicitly allows it, **DO NOT**:
- add or change HTTP routes
- introduce breaking DTO changes (additive-only is OK)
- change DB schema or add migrations
- change runtime behavior outside the slice scope
- add background workers / queues / service bus
- auto-approve or auto-execute SafeActions
- introduce secrets into logs, docs, examples, or tests
- modify `.github/workflows/*` (read-only). Use `templates/` if needed.

If something is missing/unknown, **search the codebase** and either:
- use the real names/keys/types, or
- add `TODO` comments rather than guessing.

---

## 3) Codebase Hygiene
- Prefer **interfaces in Contracts** for cross-module dependencies.
- Keep module boundaries clean:
  - Presentation depends on Contracts, not Infrastructure
  - Composition root (ApiHost) wires implementations
- Keep logs safe:
  - log identifiers + error codes only
  - never log payload bodies, secrets, tokens, connection strings

---

## 4) Configuration Rules
- **Never invent config keys.**
- Always verify keys in:
  - `src/Hosts/**/appsettings*.json`
  - `*Options.cs` / `*Settings.cs`
- If a key is not found, add:
  `<!-- TODO: verify config key in appsettings.json -->`

---

## 5) Tests and Gates (Required)
For every slice with code changes:
1. Build gate (treat warnings as errors):
   ```powershell
   dotnet build OpsCopilot.sln -warnaserror
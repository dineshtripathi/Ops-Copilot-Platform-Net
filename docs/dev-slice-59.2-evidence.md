# Slice 59.2 — Open-Source Repo Hygiene

**Status:** Complete  
**Committed:** TBD  
**Parent slice:** 59.1 (AI package alignment)

---

## Objective

Prepare the repository for public / open-source visibility by:

1. Scanning all documentation for real credentials or sensitive values.
2. Replacing any suspicious identifiers with safe placeholders.
3. Archiving 59 accumulated dev-slice evidence documents to keep the `docs/` root readable.
4. Ensuring local developer tooling config (`.claude/`) is not tracked by git.

---

## Scope Constraints (honoured)

- No HTTP routes added or changed.
- No DB schema changes.
- No runtime code changes.
- No `.github/workflows/*` modifications.

---

## Changes Made

### 1. Sensitive Content Scan — `docs/local-dev-secrets.md`

Three real-looking identifiers were replaced with safe placeholder text:

| Location | Before | After |
|---|---|---|
| `docs/local-dev-secrets.md` line 30 | `6b530cc6-14bb-4fad-9577-3a349209ae1c` (Log Analytics Workspace ID) | `<your-log-analytics-workspace-id>` |
| `docs/local-dev-secrets.md` line 36 | `3f8d1a2e-9c4b-4e77-b8f3-d0c5e6a7f901` (UserSecrets project GUID) | `<project-user-secrets-id>` |
| `docs/local-dev-secrets.md` expected-output block | `6b530cc6-...` (truncated, in expected output) | `<your-log-analytics-workspace-id>` |

All other GUID-like patterns found across the docs corpus were confirmed to be:
- RFC example GUIDs (e.g. `3fa85f64-5717-4562-b3fc-2c963f66afa6` from Swagger UI)
- Obvious placeholder patterns (e.g. `aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee`)
- Conceptual field name references (`AllowedAzureSubscriptionIds`, `TenantId`) — no real values

`docs/local-dev-auth.md` uses `<YOUR_TENANT_ID>` and `<YOUR_SUBSCRIPTION_ID>` placeholder patterns throughout — already clean.

### 2. Dev-Slice Evidence Docs Archived

All 59 `docs/dev-slice-*` files moved to `docs/archive/dev-slices/`:

```
docs/archive/dev-slices/
  dev-slice-1-curl.md
  dev-slice-9-evidence.md
  dev-slice-10-evidence.md
  ... (59 total)
  dev-slice-59.1-evidence.md
```

`docs/` root is now limited to current reference documentation only (9 files).

### 3. `.claude/` Added to `.gitignore`

`.claude/settings.local.json` was being tracked by git. This file contains developer-machine-specific Claude tool permissions and should not be committed.

Added to `.gitignore` (lines 15–16):
```gitignore
# Claude AI local tool config — developer-machine-specific permissions
.claude/
```

File removed from git index with `git rm --cached .claude/settings.local.json`.

### 4. `apihost-error.log` — No Action Required

Confirmed not tracked by git (`git ls-files apihost-error.log` returned empty). The existing `*.log` pattern in `.gitignore` already prevents it from being committed.

### 5. `CLAUDE.md` — Classified KEEP

`CLAUDE.md` defines the slice-based AI development workflow for this repo. Decision: **keep**.

Rationale:
- Content is not sensitive (development methodology rules only).
- AI dev workflow files are an established OSS practice.
- Serves as operating guide for AI-assisted contributors.

---

## Files Changed

| File | Change |
|---|---|
| `docs/local-dev-secrets.md` | 3 GUID scrubs |
| `docs/archive/dev-slices/` (new dir) | Destination for 59 dev-slice docs |
| `docs/dev-slice-{1–59.1}-*` | Moved to archive (59 files) |
| `.gitignore` | Added `.claude/` entry |
| `.claude/settings.local.json` | Removed from git index (still present locally) |

---

## Verification

```
Root .md files : CLAUDE.md, CONTRIBUTING.md, PACKS.md, PROJECT_VISION.md, README.md, SECURITY.md
docs/ top-level: architecture.md, deploying-on-azure.md, governance.md, local-dev-auth.md,
                 local-dev-secrets.md, PROJECT_VISION.md, README.md, running-locally.md, threat-model.md
Archive count  : 59 files in docs/archive/dev-slices/
.gitignore     : .claude/ on line 16 ✓
git ls-files .claude/ : (empty — untracked) ✓
```

---

## Build Gate

No production code was changed; build gate not required for documentation-only hygiene work. Runtime behaviour is identical to Slice 59.1.

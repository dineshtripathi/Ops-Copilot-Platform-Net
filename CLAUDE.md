# OpsCopilot — Claude Code Instructions (STRICT)

## Rules
- DO NOT change runtime behavior unless explicitly requested.
- DO NOT add new routes unless explicitly requested.
- DO NOT change DB schema or migrations unless explicitly requested.
- DO NOT break DTOs (additive-only changes allowed only when requested).
- DO NOT change SafeActions behavior unless explicitly requested.
- Keep Mode A deterministic and offline.

## Build gates (must be green)
- dotnet build OpsCopilot.sln -warnaserror
- dotnet test OpsCopilot.sln --no-build --verbosity minimal

## Output format
- Files changed (Created/Modified/Deleted)
- Build summary (warnings/errors)
- Test totals (per assembly + grand total)
- Evidence doc: docs/dev-slice-XX-evidence.md

## Repo layout
- src/ and tests/ are code; docs/http/OpsCopilot.Api.http is the manual test file
- packs/ contains pack content (JSON/KQL/Markdown)

## Defaults
- Prefer deterministic reason codes.
- Prefer config-driven behavior with safe defaults.
- Never log secrets or full payloads; truncate/redact.
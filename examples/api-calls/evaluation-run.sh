#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────
# Trigger the evaluation runner — executes all 11 built-in
# evaluation scenarios and returns a summary
# Requires: API host running on http://localhost:5100
# ──────────────────────────────────────────────────────────────
set -euo pipefail

BASE_URL="${OPSCOPILOT_URL:-http://localhost:5100}"
CORR_ID="example-eval-$(date +%s)"

# ── Run all scenarios ────────────────────────────────────────
echo "=== Running all evaluation scenarios ==="
curl -s -X POST "${BASE_URL}/evaluation/run" \
  -H "Content-Type: application/json" \
  -H "x-correlation-id: ${CORR_ID}" \
  -d '{}' | jq .

# Expected 200 — evaluation run summary
# {
#   "totalScenarios": 11,
#   "passed": 11,
#   "failed": 0,
#   "results": [
#     { "scenarioId": "AI-001", "module": "AlertIngestion", "name": "Azure Monitor payload parsing", "passed": true },
#     { "scenarioId": "AI-002", "module": "AlertIngestion", "name": "Datadog provider detection",    "passed": true },
#     { "scenarioId": "AI-003", "module": "AlertIngestion", "name": "Empty payload rejection",       "passed": true },
#     { "scenarioId": "AI-004", "module": "AlertIngestion", "name": "Fingerprint determinism",       "passed": true },
#     { "scenarioId": "SA-001", "module": "SafeActions",    "name": "Action type risk classification","passed": true },
#     { "scenarioId": "SA-002", "module": "SafeActions",    "name": "Empty action list handling",    "passed": true },
#     { "scenarioId": "SA-003", "module": "SafeActions",    "name": "Dry-run guard enforcement",     "passed": true },
#     { "scenarioId": "SA-004", "module": "SafeActions",    "name": "Replay detection",              "passed": true },
#     { "scenarioId": "RP-001", "module": "Reporting",      "name": "Summary totals accuracy",       "passed": true },
#     { "scenarioId": "RP-002", "module": "Reporting",      "name": "Recent limit clamping",         "passed": true },
#     { "scenarioId": "RP-003", "module": "Reporting",      "name": "Tenant-scoped filtering",       "passed": true }
#   ]
# }

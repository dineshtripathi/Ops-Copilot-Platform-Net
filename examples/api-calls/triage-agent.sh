#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────
# Kick off a triage agent run for an ingested alert
# Requires: API host running on http://localhost:5100
# ──────────────────────────────────────────────────────────────
set -euo pipefail

BASE_URL="${OPSCOPILOT_URL:-http://localhost:5100}"
TENANT="${OPSCOPILOT_TENANT:-tenant-contoso}"
CORR_ID="example-triage-$(date +%s)"

curl -s -X POST "${BASE_URL}/agent/triage" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: ${TENANT}" \
  -H "x-correlation-id: ${CORR_ID}" \
  -d '{
    "alertPayload": {
      "alertRule":        "HighCpuAlert",
      "severity":         "Sev1",
      "monitorCondition": "Fired",
      "target":           "/subscriptions/00000000/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-web01"
    },
    "timeRangeMinutes": 30
  }' | jq .

# Expected 200 — agent completed
# {
#   "status": "Completed",
#   "runId": "<guid>",
#   "summary": "Agent triaged the alert …"
# }

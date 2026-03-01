#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────
# Request a safe-action execution (Mode C — controlled execution)
# Requires:
#   - API host running on http://localhost:5100
#   - EnableExecution = true
#   - Tenant listed in AllowedExecutionTenants
# ──────────────────────────────────────────────────────────────
set -euo pipefail

BASE_URL="${OPSCOPILOT_URL:-http://localhost:5100}"
TENANT="${OPSCOPILOT_TENANT:-tenant-contoso}"
CORR_ID="example-safe-action-$(date +%s)"
IDEMPOTENCY_KEY="sa-$(uuidgen 2>/dev/null || cat /proc/sys/kernel/random/uuid)"

curl -s -X POST "${BASE_URL}/safe-actions/execute" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: ${TENANT}" \
  -H "x-correlation-id: ${CORR_ID}" \
  -H "x-idempotency-key: ${IDEMPOTENCY_KEY}" \
  -d '{
    "actionType": "http_probe",
    "target": "https://vm-web01.internal:8080/healthz",
    "parameters": {
      "timeoutSeconds": 10,
      "expectedStatusCode": 200
    }
  }' | jq .

# Expected 200 — action executed
# {
#   "executionId": "<guid>",
#   "actionType": "http_probe",
#   "status": "Completed",
#   "result": { ... }
# }
#
# Expected 400 — governance denied
# {
#   "reasonCode": "governance_tool_denied",
#   "message": "Tool 'safe_action_execute' is not in the AllowedTools for tenant 'tenant-contoso'."
# }
#
# Expected 429 — throttled
# Retry-After header included

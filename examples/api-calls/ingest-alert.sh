#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────
# Ingest an Azure Monitor common-alert-schema payload
# Requires: API host running on http://localhost:5100
# ──────────────────────────────────────────────────────────────
set -euo pipefail

BASE_URL="${OPSCOPILOT_URL:-http://localhost:5100}"
TENANT="${OPSCOPILOT_TENANT:-tenant-contoso}"
CORR_ID="example-ingest-$(date +%s)"

curl -s -X POST "${BASE_URL}/ingest/alert" \
  -H "Content-Type: application/json" \
  -H "x-tenant-id: ${TENANT}" \
  -H "x-correlation-id: ${CORR_ID}" \
  -d '{
    "schemaId": "azureMonitorCommonAlertSchema",
    "data": {
      "essentials": {
        "alertId":  "/subscriptions/00000000-0000-0000-0000-000000000000/providers/Microsoft.AlertsManagement/alerts/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
        "alertRule":"HighCpuAlert",
        "severity": "Sev1",
        "monitorCondition":"Fired",
        "alertTargetIDs":[
          "/subscriptions/00000000/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-web01"
        ]
      }
    }
  }' | jq .

# Expected 200 — accepted alert with fingerprint
# {
#   "fingerprintId": "<sha256-hex>",
#   "status": "Accepted"
# }

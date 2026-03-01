# Runbook — Restart Pod / VM

> **Risk Tier:** High  
> **Action Type:** `restart_pod`  
> **Pack:** azure-vm  

## Purpose

Restart a misbehaving pod or virtual machine after automated triage
confirms the host is in a degraded state (CPU spike, memory pressure, or
unresponsive health probe).

## Pre-conditions

| Check                        | How to verify                        |
| ---------------------------- | ------------------------------------ |
| Tenant is authorised         | `AllowedExecutionTenants` contains the tenant ID |
| Action type enabled          | `ActionTypes` entry for `restart_pod` has `Enabled = true` |
| Governance tool allow-list   | `safe_action_execute` in `AllowedTools` |
| Idempotency key provided     | `x-idempotency-key` header present  |

## Procedure

1. **Confirm alert context** — verify the alert payload includes the
   target resource ID and the failing metric(s).
2. **Run health probe** — execute an `http_probe` against the workload
   endpoint to confirm the host is still unhealthy.
3. **Request restart** — call `POST /api/safe-actions/execute` with
   `actionType = restart_pod` and the target resource identifier.
4. **Validate** — re-run the health probe within 60 seconds to confirm
   recovery.
5. **Escalate if unresolved** — if the probe still fails after restart,
   open an incident ticket and page the on-call engineer.

## Rollback

Restarts are inherently non-reversible. If the workload does not recover:

- Check application logs via `azure_monitor_query`.
- Consider scaling the VM SKU or adding replicas.
- Engage the service owner per the escalation matrix.

## Governance Notes

- In **Mode A** (local dev), `restart_pod` is always disabled (`EnableExecution = false`).
- In **Mode B** (read-only), `restart_pod` should remain `Enabled = false`.
- In **Mode C** (controlled execution), `restart_pod` is `Enabled = true`
  and subject to throttle limits (`ThrottleMaxAttempts` / `ThrottleWindowSeconds`).

# OpsCopilot — Examples

Ready-to-use snippets and configuration samples for common OpsCopilot tasks.

| File | Description |
| ---- | ----------- |
| [api-calls/ingest-alert.sh](api-calls/ingest-alert.sh) | cURL — ingest an Azure Monitor alert |
| [api-calls/triage-agent.sh](api-calls/triage-agent.sh) | cURL — kick off a triage agent run |
| [api-calls/safe-action-execute.sh](api-calls/safe-action-execute.sh) | cURL — request a safe-action execution |
| [api-calls/evaluation-run.sh](api-calls/evaluation-run.sh) | cURL — trigger the evaluation runner |
| [config/governance-multi-tenant.jsonc](config/governance-multi-tenant.jsonc) | Governance config with two tenant overrides |
| [config/safe-actions-mode-b.jsonc](config/safe-actions-mode-b.jsonc) | SafeActions config for Mode B (read-only) |
| [config/safe-actions-mode-c.jsonc](config/safe-actions-mode-c.jsonc) | SafeActions config for Mode C (controlled execution) |
| [packs/azure-vm/pack.json](packs/azure-vm/pack.json) | Starter pack manifest for Azure VM workloads |
| [packs/azure-vm/queries/cpu-spike.kql](packs/azure-vm/queries/cpu-spike.kql) | KQL — detect CPU spikes > 90 % |
| [packs/azure-vm/queries/memory-pressure.kql](packs/azure-vm/queries/memory-pressure.kql) | KQL — detect sustained memory pressure |
| [packs/azure-vm/runbooks/restart-pod.md](packs/azure-vm/runbooks/restart-pod.md) | Runbook — restart a pod/VM |
| [packs/azure-vm/governance/defaults.json](packs/azure-vm/governance/defaults.json) | Pack-level governance defaults |
| [bicep/tenant-rg.bicep](bicep/tenant-rg.bicep) | Bicep — minimal tenant resource group |

## How to use

1. **API calls** — open a terminal, make sure the API host is running on
   `http://localhost:5100`, and execute the shell script.
2. **Config snippets** — copy the relevant JSONC block into your
   `appsettings.Development.json` (or `appsettings.json` for production).
3. **Packs** — drop the `azure-vm/` folder into a `packs/` directory at the
   repo root and load it via the pack-loading pipeline (see [PACKS.md](../PACKS.md)).
4. **Bicep** — deploy with `az deployment sub create` (see
   [docs/deploying-on-azure.md](../docs/deploying-on-azure.md)).

---

> **License:** MIT — see [LICENSE](../LICENSE)

# Local Development — Secrets Setup

This document describes how to configure secrets for local development without
committing any sensitive values to the repository.

---

## How secrets are loaded

Configuration is resolved in this order (last source wins):

| Priority | Source | Notes |
|---|---|---|
| 1 | `appsettings.json` | Non-secret defaults. **Committed.** |
| 2 | `appsettings.Development.json` | Dev log levels. **Committed.** |
| 3 | **User Secrets** | Local machine only. Never in repo. |
| 4 | Environment Variables | Container Apps / CI. |
| 5 | Azure Key Vault | When `KeyVault:VaultUri` is set. |

---

## One-time local setup

Run the following commands from the **solution root** (where `OpsCopilot.sln` is):

```powershell
cd src/Hosts/OpsCopilot.ApiHost

# Log Analytics workspace GUID
dotnet user-secrets set "WORKSPACE_ID" "6b530cc6-14bb-4fad-9577-3a349209ae1c"

# SQL Server connection string — LocalDB for local dev
dotnet user-secrets set "SQL_CONNECTION_STRING" \
  "Server=(localdb)\mssqllocaldb;Database=OpsCopilot;Trusted_Connection=True;MultipleActiveResultSets=true"
```

> **Note:** User Secrets are stored at  
> `%APPDATA%\Microsoft\UserSecrets\3f8d1a2e-9c4b-4e77-b8f3-d0c5e6a7f901\secrets.json`
> on Windows, completely outside the repository.

---

## Optional overrides via User Secrets

These have sane defaults in `appsettings.json` and only need to be set if you
want to override them locally.

```powershell
# Use a pre-published McpHost binary instead of 'dotnet run'
dotnet user-secrets set "McpKql:ServerCommand" "dotnet /path/to/OpsCopilot.McpHost.dll"

# Increase the per-call timeout (seconds)
dotnet user-secrets set "McpKql:TimeoutSeconds" "60"
```

---

## Verify secrets are set

```powershell
cd src/Hosts/OpsCopilot.ApiHost
dotnet user-secrets list
```

Expected output:

```
WORKSPACE_ID = 6b530cc6-...
SQL_CONNECTION_STRING = Server=(localdb)\...
```

---

## Azure Key Vault (production / staging)

For non-local environments, secrets are stored in Azure Key Vault and fetched
via Managed Identity. No credentials are needed in any config file.

### Required Key Vault secrets

| Key Vault secret name | Maps to config key | Description |
|---|---|---|
| `WORKSPACE-ID` | `WORKSPACE_ID` | Log Analytics workspace GUID |
| `SQL-CONNECTION-STRING` | `SQL_CONNECTION_STRING` | Azure SQL connection string |

> Key Vault uses `-` as separator in secret names; the config provider maps
> them to `:` (sections) or flat keys automatically.

### Enable Key Vault

Set the vault URI via an app setting (Container Apps, App Service env var):

```bash
az containerapp update \
  --name  ca-opscopilot-apihost-dev \
  --resource-group rg-opscopilot-dev \
  --set-env-vars "KeyVault__VaultUri=https://kv-opscopilot-dev.vault.azure.net/"
```

Or via environment variable anywhere:

```bash
export KeyVault__VaultUri=https://kv-opscopilot-dev.vault.azure.net/
```

The startup log will confirm:
```
[Config] Azure Key Vault provider: ENABLED — vault=https://kv-opscopilot-dev.vault.azure.net/
```

### Required RBAC

The Container App's system-assigned Managed Identity needs the
`Key Vault Secrets User` role on the vault:

```bash
# Get the principal ID of the Container App's MI
PRINCIPAL_ID=$(az containerapp show \
  --name ca-opscopilot-apihost-dev \
  --resource-group rg-opscopilot-dev \
  --query identity.principalId -o tsv)

# Assign the role
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee "$PRINCIPAL_ID" \
  --scope "/subscriptions/<sub-id>/resourceGroups/rg-opscopilot-dev/providers/Microsoft.KeyVault/vaults/kv-opscopilot-dev"
```

---

## What NOT to commit

The `.gitignore` blocks the following patterns:

```
appsettings.*.Local.json
appsettings.Local.json
appsettings.Secrets.json
*.secrets.json
secrets.json
.env.local
.env.*.local
```

If you accidentally create one of these files, `git status` will not show it.
Always run `dotnet user-secrets` instead of editing files directly.

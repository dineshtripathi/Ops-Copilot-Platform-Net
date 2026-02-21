# Local-Development Azure Authentication

> **Audience**: developers running `OpsCopilot.McpHost` from their workstation.

---

## TL;DR

```bash
# Option A – Azure CLI (recommended)
az login --tenant <YOUR_TENANT_ID>
az account set --subscription <YOUR_SUBSCRIPTION_ID>
dotnet run --project src/Hosts/OpsCopilot.McpHost

# Option B – Azure PowerShell
Connect-AzAccount -Tenant <YOUR_TENANT_ID>
Set-AzContext -SubscriptionId <YOUR_SUBSCRIPTION_ID>
dotnet run --project src/Hosts/OpsCopilot.McpHost
```

---

## How McpHost authenticates locally

McpHost reads `AzureAuth:Mode` from configuration.  
In Development the default is **`ExplicitChain`**, which builds a
`ChainedTokenCredential` from only the sources you enable:

| Config key                              | Default (dev) | Description |
|-----------------------------------------|:------------:|-------------|
| `AzureAuth:Mode`                        | `ExplicitChain` | `ExplicitChain` or `DefaultAzureCredential` |
| `AzureAuth:TenantId`                    | *(empty)*    | AAD tenant; set for multi-tenant orgs |
| `AzureAuth:UseAzureCliCredential`       | `true`       | Include `az` CLI token |
| `AzureAuth:UseAzurePowerShellCredential`| `true`       | Include PowerShell `Az` module token |
| `AzureAuth:UseAzureDeveloperCliCredential`| `false`    | Include `azd` CLI token |
| `AzureAuth:CredentialProcessTimeoutSeconds` | `60`     | Max time to wait for each credential process |

These values live in `appsettings.Development.json`; override them via
environment variables (`AzureAuth__TenantId`) or `dotnet user-secrets`.

---

## Step-by-step setup

### 1. Azure CLI

```bash
# Install (if not present)
winget install Microsoft.AzureCLI        # Windows
brew install azure-cli                   # macOS

# Authenticate
az login --tenant <YOUR_TENANT_ID>

# Confirm identity
az account show --query "{name:name, id:id, tenantId:tenantId}"

# Verify a token can be minted for Log Analytics
az account get-access-token --resource https://api.loganalytics.io
```

### 2. Azure PowerShell (alternative)

```powershell
# Install the module (one-time)
Install-Module Az.Accounts -Scope CurrentUser -Force

# Persist tokens across PowerShell sessions (optional, required
# if McpHost's PowerShell credential runs in a new process)
Enable-AzContextAutosave -Scope CurrentUser

# Authenticate
Connect-AzAccount -Tenant <YOUR_TENANT_ID>

# Switch subscription
Set-AzContext -SubscriptionId <YOUR_SUBSCRIPTION_ID>

# Verify
Get-AzAccessToken -ResourceUrl https://api.loganalytics.io
```

### 3. Azure Developer CLI (`azd`) — optional

```bash
azd auth login --tenant-id <YOUR_TENANT_ID>
```

Enable in config:

```json
{
  "AzureAuth": {
    "UseAzureDeveloperCliCredential": true
  }
}
```

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| `AzureCliCredential authentication failed` | `az login` session expired or never run | `az login --tenant <tid>` |
| `AzurePowerShellCredential authentication failed` | No persisted Az context | `Enable-AzContextAutosave -Scope CurrentUser`, then `Connect-AzAccount` |
| `CredentialUnavailableException` from all sources | No credential enabled in config | Ensure at least one `Use*Credential` is `true` |
| Timeout after 60 s | CLI/PS process hangs or token endpoint slow | Increase `CredentialProcessTimeoutSeconds`, check proxy/VPN |
| Wrong tenant / subscription | Token minted for default tenant | Set `AzureAuth:TenantId` **and** run `az account set -s <sub>` |
| `AuthorizationFailed` on query | Identity lacks Log Analytics Reader role | Ask your admin to assign *Log Analytics Reader* on the workspace |

### Verify the token manually

```bash
# Azure CLI
az account get-access-token --resource https://api.loganalytics.io --query accessToken -o tsv

# PowerShell
(Get-AzAccessToken -ResourceUrl https://api.loganalytics.io).Token
```

If the above commands succeed but McpHost still fails, check
`AzureAuth:TenantId` matches the tenant returned by `az account show`.

---

## Config reference (`appsettings.Development.json`)

```json
{
  "AzureAuth": {
    "Mode": "ExplicitChain",
    "TenantId": "",
    "UseAzureCliCredential": true,
    "UseAzurePowerShellCredential": true,
    "UseAzureDeveloperCliCredential": false,
    "CredentialProcessTimeoutSeconds": 60
  }
}
```

## Production config reference (`appsettings.json`)

```json
{
  "AzureAuth": {
    "Mode": "DefaultAzureCredential",
    "TenantId": "",
    "ExcludeManagedIdentityCredential": false,
    "ExcludeAzureCliCredential": true,
    "ExcludeAzurePowerShellCredential": true,
    "ExcludeAzureDeveloperCliCredential": true
  }
}
```

In Container Apps the system-assigned managed identity obtains tokens
automatically — no CLI login is needed.

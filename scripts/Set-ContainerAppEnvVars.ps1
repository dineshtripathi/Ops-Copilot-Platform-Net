#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Injects all required runtime environment variables into the Ops Copilot
    Container Apps for the dev environment.

.NOTES
    Run from the repo root after authenticating with: az login
    Slice 201 + Slice 149 post-deployment configuration script.
#>

param(
    [string]$ResourceGroup  = "rg-opscopilot-platform-dev-uks",
    [string]$TenantId       = "4a72b866-99a4-4388-b881-cef9c8480b1c",
    [string]$AppId          = "dfdb8f36-cf0b-41d0-bcd6-92a063dba954",
    [string]$WorkspaceId    = "08e80da0-bf2b-4bde-9e9d-08e67c12cab6"
)

$Audience   = "api://$AppId"
$ApiHost    = "ca-opscopilot-apihost-dev"
$WorkerHost = "ca-opscopilot-workerhost-dev"

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " OpsCopilot — Container App environment variable injection" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Resource Group : $ResourceGroup"
Write-Host " Tenant ID      : $TenantId"
Write-Host " App ID         : $AppId"
Write-Host " Audience       : $Audience"
Write-Host " Workspace ID   : $WorkspaceId"
Write-Host ""

# ── Helper ────────────────────────────────────────────────────────────────────
function Set-EnvVar {
    param([string]$AppName, [string]$Name, [string]$Value)
    Write-Host "  Setting $Name on $AppName ..." -NoNewline
    az containerapp update `
        --name $AppName `
        --resource-group $ResourceGroup `
        --set-env-vars "$Name=$Value" `
        --output none
    if ($LASTEXITCODE -eq 0) { Write-Host " ✅" -ForegroundColor Green }
    else                     { Write-Host " FAILED (code $LASTEXITCODE)" -ForegroundColor Red }
}

# ── ApiHost env vars ──────────────────────────────────────────────────────────
Write-Host "-- ApiHost ($ApiHost) --" -ForegroundColor Yellow

# Slice 149: disable DevBypass so Entra JWT bearer is enforced
Set-EnvVar $ApiHost "Authentication__DevBypass" "false"

# Slice 149: Entra tenant and audience
# NOTE: These flow via Key Vault (kv-opsctpltfdev secrets already set).
# Setting them here as env-var fallback in case KV provider hasn't refreshed yet.
Set-EnvVar $ApiHost "Authentication__Entra__TenantId" $TenantId
Set-EnvVar $ApiHost "Authentication__Entra__Audience"  $Audience

# Slice 201: workspace scope fix (appsettings.Development.json excluded from image)
Set-EnvVar $ApiHost "SafeActions__AllowedLogAnalyticsWorkspaceIds__0" $WorkspaceId

# ── WorkerHost env vars ───────────────────────────────────────────────────────
Write-Host ""
Write-Host "-- WorkerHost ($WorkerHost) --" -ForegroundColor Yellow

# Slice 201 also applies to WorkerHost (shares commonEnvVars in Bicep)
Set-EnvVar $WorkerHost "SafeActions__AllowedLogAnalyticsWorkspaceIds__0" $WorkspaceId

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Done. Verify with:" -ForegroundColor Cyan
Write-Host "   az containerapp show --name $ApiHost --resource-group $ResourceGroup --query 'properties.template.containers[0].env' -o table"
Write-Host "==========================================================" -ForegroundColor Cyan

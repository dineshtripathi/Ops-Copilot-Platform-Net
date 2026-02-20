#Requires -Version 5.1
<#
.SYNOPSIS
  Deletes OLD OpsCopilot resource groups and misplaced resources
  left over from the initial TenantA/TenantB deployment.

.DESCRIPTION
  Old layout (pre-reset):
    SubA:  rg-opscopilot-a-{env}-uks  (contained misplaced AOAI)
    SubB:  rg-opscopilot-b-{env}-uks

  New layout (post-reset):
    SubA:  rg-opscopilot-platform-{env}-uks
    SubB:  rg-opscopilot-ai-{env}-uks

.EXAMPLE
  .\cleanup.ps1 -Env dev `
    -SubscriptionA b20a7294-6951-4107-88df-d7d320218670 `
    -SubscriptionB bd27a79c-de25-4097-a874-3bb35f2b926a `
    -Target both

.EXAMPLE
  .\cleanup.ps1 -Env dev -Target platform -DryRun
#>
[CmdletBinding(SupportsShouldProcess)]
param(
  [ValidateSet('dev', 'sandbox', 'prod')]
  [string] $Env = 'dev',

  [string] $Region = 'uks',

  [string] $SubscriptionA = '',

  [string] $SubscriptionB = '',

  [ValidateSet('platform', 'ai', 'both')]
  [string] $Target = 'both',

  [switch] $DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Log([string]$Message) {
  Write-Host "[cleanup] $Message"
}

function Write-Warn([string]$Message) {
  Write-Warning "[cleanup] $Message"
}

function Test-RgExists([string]$SubscriptionId, [string]$RgName) {
  $result = az group show --subscription $SubscriptionId --name $RgName 2>$null
  return ($LASTEXITCODE -eq 0)
}

function Remove-OldRg([string]$SubscriptionId, [string]$RgName) {
  if (Test-RgExists -SubscriptionId $SubscriptionId -RgName $RgName) {
    if ($DryRun) {
      Write-Log "[DRY-RUN] Would delete RG '$RgName' in subscription '$SubscriptionId'"
    } else {
      Write-Log "Deleting RG '$RgName' in subscription '$SubscriptionId' ..."
      az group delete `
        --subscription $SubscriptionId `
        --name $RgName `
        --yes `
        --no-wait
      if ($LASTEXITCODE -ne 0) {
        throw "Failed to initiate deletion of RG '$RgName'."
      }
      Write-Log "Delete initiated for '$RgName' (async — check portal for completion)."
    }
  } else {
    Write-Log "RG '$RgName' not found in '$SubscriptionId' — nothing to delete."
  }
}

function Invoke-PlatformCleanup {
  if ([string]::IsNullOrWhiteSpace($SubscriptionA)) {
    Write-Warn '-SubscriptionA not provided; skipping platform cleanup.'
    return
  }
  $oldRg = "rg-opscopilot-a-${Env}-${Region}"
  Write-Log "=== Platform (SubA) cleanup — target RG: $oldRg ==="
  Remove-OldRg -SubscriptionId $SubscriptionA -RgName $oldRg
}

function Invoke-AiCleanup {
  if ([string]::IsNullOrWhiteSpace($SubscriptionB)) {
    Write-Warn '-SubscriptionB not provided; skipping AI cleanup.'
    return
  }
  $oldRg = "rg-opscopilot-b-${Env}-${Region}"
  Write-Log "=== AI (SubB) cleanup — target RG: $oldRg ==="
  Remove-OldRg -SubscriptionId $SubscriptionB -RgName $oldRg
}

# ── Main ──────────────────────────────────────────────────────────────────────
Write-Log "Starting cleanup | Env=$Env Target=$Target DryRun=$DryRun"

switch ($Target) {
  'platform' { Invoke-PlatformCleanup }
  'ai'       { Invoke-AiCleanup }
  'both'     { Invoke-PlatformCleanup; Invoke-AiCleanup }
}

Write-Log 'Cleanup complete.'

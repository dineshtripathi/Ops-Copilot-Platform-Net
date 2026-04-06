#!/usr/bin/env pwsh
# E2E test: ingest → triage → dashboard
param(
    [string]$ApiUrl = "https://ca-opscopilot-apihost-dev.calmmoss-f0b3c934.uksouth.azurecontainerapps.io",
    [string]$TenantId = "cbc3e2ab-192b-49b5-8a3e-ec915ed476ba"
)

$headers = @{
    "x-tenant-id"  = $TenantId
    "Content-Type" = "application/json"
}

function Invoke-Api {
    param($Method, $Path, $Body = $null)
    $uri = "$ApiUrl$Path"
    $params = @{
        Uri             = $uri
        Method          = $Method
        Headers         = $headers
        UseBasicParsing = $true
        TimeoutSec      = 90
        ErrorAction     = "SilentlyContinue"
    }
    if ($Body) { $params["Body"] = $Body }
    $r = Invoke-WebRequest @params
    [PSCustomObject]@{ Status = $r.StatusCode; Body = $r.Content }
}

# Step 0: health check
Write-Host "`n=== STEP 0: Health ===" -ForegroundColor Cyan
$h = Invoke-Api GET "/healthz"
Write-Host "Status: $($h.Status)  Body: $($h.Body)"

# Step 1: Ingest alert
Write-Host "`n=== STEP 1: Ingest Alert ===" -ForegroundColor Cyan
$alertPayload = @{
    schemaId = "azureMonitorCommonAlertSchema"
    data     = @{
        essentials = @{
            alertId          = "/subscriptions/00000000-0000-0000-0000-000000000001/providers/Microsoft.AlertsManagement/alerts/demo-cpu-001"
            alertRule        = "HighCpuAlert"
            severity         = "Sev1"
            monitorCondition = "Fired"
            monitoringService= "Platform"
            signalType       = "Metric"
            firedDateTime    = "2026-04-05T07:00:00Z"
            description      = "CPU exceeded 90% for 5 min on vm-web01"
            alertTargetIDs   = @("/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg-prod/providers/Microsoft.Compute/virtualMachines/vm-web01")
        }
        alertContext = @{}
    }
} | ConvertTo-Json -Depth 10 -Compress

$ingestBody = @{
    provider = "azure_monitor"
    payload  = $alertPayload
} | ConvertTo-Json -Depth 3

$ing = Invoke-Api POST "/ingest/alert" -Body $ingestBody
Write-Host "Status: $($ing.Status)"
Write-Host "Body: $($ing.Body)"

if ($ing.Status -eq 200 -or $ing.Status -eq 202) {
    $ingObj = $ing.Body | ConvertFrom-Json
    $runId = $ingObj.runId
    Write-Host "RunId: $runId | Fingerprint: $($ingObj.fingerprint)" -ForegroundColor Green
} else {
    Write-Host "Ingest failed — aborting" -ForegroundColor Red
    exit 1
}

# Step 2: Triage
Write-Host "`n=== STEP 2: Triage ===" -ForegroundColor Cyan
$triageBody = @{
    workspaceId  = "6b530cc6-14bb-4fad-9577-3a349209ae1c"
    alertPayload = @{
        alertSource      = "AzureMonitor"
        fingerprint      = $ingObj.fingerprint   # from ingest response
        title            = "HighCpuAlert"
        severity         = "Sev1"
        signalType       = "Metric"
        firedAtUtc       = "2026-04-05T07:00:00Z"
    }
    timeRangeMinutes = 30
} | ConvertTo-Json -Depth 5

$tri = Invoke-Api POST "/agent/triage" -Body $triageBody
Write-Host "Status: $($tri.Status)"
Write-Host "Body: $($tri.Body | ConvertFrom-Json | ConvertTo-Json -Depth 8)"

# Step 3: Dashboard
Write-Host "`n=== STEP 3: Dashboard Overview ===" -ForegroundColor Cyan
$dash = Invoke-Api GET "/reports/dashboard/overview"
Write-Host "Status: $($dash.Status)"
Write-Host "Body: $($dash.Body | ConvertFrom-Json | ConvertTo-Json -Depth 6)"

# Step 4: Platform readiness
Write-Host "`n=== STEP 4: Platform Readiness ===" -ForegroundColor Cyan
$readHeaders = @{ "Content-Type" = "application/json" }
$ready = Invoke-WebRequest -Uri "$ApiUrl/reports/platform/readiness" -Headers $readHeaders -UseBasicParsing -TimeoutSec 30
Write-Host "Status: $($ready.StatusCode)"
Write-Host "Body: $($ready.Content | ConvertFrom-Json | ConvertTo-Json -Depth 5)"

# Step 5: Open Blazor UI
Write-Host "`n=== STEP 5: Opening Blazor Dashboard ===" -ForegroundColor Cyan
$dashUrl = "$ApiUrl/app/dashboard?tenantId=$TenantId"
Start-Process $dashUrl
Write-Host "Opened $dashUrl in browser."

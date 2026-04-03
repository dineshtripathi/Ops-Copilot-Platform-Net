<#
.SYNOPSIS
    Smoke test suite for OpsCopilot Container Apps post-deployment.
.DESCRIPTION
    Verifies that all three hosts are reachable and return healthy status.
    Exits with code 0 on success, 1 on any failure.
    Designed to run from GitHub Actions or a local terminal.
.PARAMETER BaseUrl
    Base URL of the ApiHost (e.g. https://ca-opscopilot-apihost-dev.nicebeach-XXXX.uksouth.azurecontainerapps.io).
    The script derives the McpHost URL by replacing -apihost- with -mcphost-.
.PARAMETER TimeoutSeconds
    Per-request timeout in seconds. Default: 30.
.PARAMETER MaxRetries
    Number of retry attempts if a request fails. Default: 3.
.EXAMPLE
    .\scripts\Invoke-SmokeTest.ps1 -BaseUrl https://my-apihost.azurecontainerapps.io
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $BaseUrl,

    [int] $TimeoutSeconds = 30,

    [int] $MaxRetries = 3
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── Helpers ─────────────────────────────────────────────────────────────────

function Invoke-ProbeWithRetry {
    param(
        [string] $Url,
        [string] $Label,
        [int]    $MaxRetries,
        [int]    $TimeoutSeconds
    )

    $attempt = 0
    while ($attempt -lt $MaxRetries) {
        $attempt++
        try {
            $response = Invoke-WebRequest -Uri $Url -TimeoutSec $TimeoutSeconds `
                -UseBasicParsing -SkipHttpErrorCheck
            if ($response.StatusCode -eq 200) {
                Write-Host "  [PASS] $Label  ($Url) → HTTP $($response.StatusCode)" -ForegroundColor Green
                return $true
            }
            Write-Warning "  [FAIL] $Label ($Url) → HTTP $($response.StatusCode) (attempt $attempt/$MaxRetries)"
        }
        catch {
            Write-Warning "  [ERROR] $Label ($Url) → $($_.Exception.Message) (attempt $attempt/$MaxRetries)"
        }
        if ($attempt -lt $MaxRetries) {
            Start-Sleep -Seconds ([Math]::Pow(2, $attempt))   # exponential back-off: 2s, 4s
        }
    }
    return $false
}

# ── Normalise base URL ───────────────────────────────────────────────────────

$ApiBase = $BaseUrl.TrimEnd('/')
# Derive McpHost URL by convention (same FQDN pattern, different container app name)
$McpBase = $ApiBase -replace '-apihost-', '-mcphost-'

# ── Probe list ───────────────────────────────────────────────────────────────

$probes = @(
    @{ Url = "$ApiBase/healthz/live";  Label = "ApiHost liveness" }
    @{ Url = "$ApiBase/healthz/ready"; Label = "ApiHost readiness" }
    @{ Url = "$McpBase/healthz/live";  Label = "McpHost liveness" }
    @{ Url = "$McpBase/healthz/ready"; Label = "McpHost readiness" }
)

# ── Run probes ───────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '═══════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host '  OpsCopilot Smoke Test' -ForegroundColor Cyan
Write-Host "  ApiHost : $ApiBase" -ForegroundColor Cyan
Write-Host "  McpHost : $McpBase" -ForegroundColor Cyan
Write-Host '═══════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''

$failures = 0

foreach ($probe in $probes) {
    $ok = Invoke-ProbeWithRetry -Url $probe.Url -Label $probe.Label `
        -MaxRetries $MaxRetries -TimeoutSeconds $TimeoutSeconds
    if (-not $ok) { $failures++ }
}

Write-Host ''
if ($failures -gt 0) {
    Write-Host "SMOKE TEST FAILED — $failures probe(s) did not return HTTP 200." -ForegroundColor Red
    exit 1
}

Write-Host 'SMOKE TEST PASSED — all probes healthy.' -ForegroundColor Green
exit 0

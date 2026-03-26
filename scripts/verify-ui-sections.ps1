param(
    [string]$RunId     = "61f4e84b-5170-4fac-8d4d-3a1d97837711",
    [string]$SessionId = "1e59c435-c633-4c65-ab21-3c96d730bbc3",
    [string]$TenantId  = "4a72b866-99a4-4388-b881-cef9c8480b1c",
    [string]$BaseUrl   = "http://localhost:5006"
)
$h = @{ "x-tenant-id" = $TenantId; "Accept" = "application/json" }

function Fetch([string]$lbl,[string]$uri,[hashtable]$hdr=@{}) {
    Write-Host ""; Write-Host "=== $lbl ===" -ForegroundColor Cyan
    Write-Host "  URL: $uri"
    try {
        $r = Invoke-RestMethod -Uri $uri -Headers $hdr -TimeoutSec 15 -ErrorAction Stop
        Write-Host "  HTTP 200 OK"
        $r | ConvertTo-Json -Depth 4
    } catch {
        $code = $_.Exception.Response.StatusCode.value__
        Write-Host "  HTTP $code : $($_.Exception.Message)"
    }
}

Fetch "Health GET /healthz"                               "$BaseUrl/healthz"
Fetch "Dashboard GET /reports/dashboard/overview"         "$BaseUrl/reports/dashboard/overview"         $h
Fetch "Agent-Runs Summary GET /reports/agent-runs/summary" "$BaseUrl/reports/agent-runs/summary"        $h
Fetch "Agent-Runs Trend GET /reports/agent-runs/trend"    "$BaseUrl/reports/agent-runs/trend"           $h
Fetch "Agent-Runs Tool-Usage GET /reports/agent-runs/tool-usage" "$BaseUrl/reports/agent-runs/tool-usage" $h
Fetch "Platform Readiness GET /reports/platform/readiness" "$BaseUrl/reports/platform/readiness"        $h
Fetch "Platform Connectors GET /reports/platform/connectors" "$BaseUrl/reports/platform/connectors"     $h
Fetch "Session GET /session/$SessionId"                   "$BaseUrl/session/$SessionId"                 $h
Fetch "SafeActions Summary GET /reports/safe-actions/summary" "$BaseUrl/reports/safe-actions/summary"   $h
Fetch "SafeActions Recent GET /reports/safe-actions/recent"   "$BaseUrl/reports/safe-actions/recent"    $h

Write-Host ""; Write-Host "=== ALL SECTIONS CHECKED ===" -ForegroundColor Green

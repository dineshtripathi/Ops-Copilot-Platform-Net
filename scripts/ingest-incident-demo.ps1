param(
    [string]$WorkspaceId   = "6b530cc6-14bb-4fad-9577-3a349209ae1c",
    [string]$WorkspaceName = "law-opscopilot-platform-dev-uks",
    [string]$ResourceGroup = "rg-opscopilot-platform-dev-uks",
    [string]$LogType       = "OpsCopilotIncidents"
)

$wsKey = az monitor log-analytics workspace get-shared-keys `
    --workspace-name $WorkspaceName -g $ResourceGroup `
    --query "primarySharedKey" -o tsv

$now = [datetime]::UtcNow
$events = @(
    [ordered]@{ TimeGenerated=$now.AddMinutes(-45).ToString("o"); Severity="Critical"; Resource="sql-prod-01"; AlertTitle="High CPU Usage Detected"; Description="CPU utilization at 97% for 5 consecutive minutes. Query plan regression detected after schema change at 18:30 UTC."; Category="Compute"; RecommendedAction="Review active queries and kill blocking sessions" },
    [ordered]@{ TimeGenerated=$now.AddMinutes(-40).ToString("o"); Severity="Warning"; Resource="sql-prod-01"; AlertTitle="Disk I/O Saturation"; Description="Storage I/O at 98% capacity. TempDB activity spike correlated with CPU alert."; Category="Storage"; RecommendedAction="Offload temp tables or increase IOPS" },
    [ordered]@{ TimeGenerated=$now.AddMinutes(-35).ToString("o"); Severity="Warning"; Resource="api-gateway-prod"; AlertTitle="Elevated Response Latency"; Description="P99 latency degraded from 180ms to 4.2s. Correlated with sql-prod-01 CPU spike. 23% of requests timing out."; Category="Network"; RecommendedAction="Enable circuit breaker for downstream sql-prod-01" },
    [ordered]@{ TimeGenerated=$now.AddMinutes(-30).ToString("o"); Severity="Critical"; Resource="sql-prod-01"; AlertTitle="Active Query Blocking Chain"; Description="17 sessions blocked behind SPID 52 holding SCH-M lock. Deadlock graph captured."; Category="Database"; RecommendedAction="Kill SPID 52 or wait for lock release" },
    [ordered]@{ TimeGenerated=$now.AddMinutes(-20).ToString("o"); Severity="Warning"; Resource="func-orderprocessor-prod"; AlertTitle="Function App Exception Spike"; Description="500 errors increased 800% in 10 minutes. Root cause traced to sql-prod-01 timeout on order table reads."; Category="Application"; RecommendedAction="Check function retry policy and dead-letter queue" },
    [ordered]@{ TimeGenerated=$now.AddMinutes(-10).ToString("o"); Severity="Information"; Resource="sql-prod-01"; AlertTitle="CPU Throttling Applied"; Description="Azure SQL auto-throttling reduced CPU to 70%. Blocking chain resolved but latency still elevated."; Category="Compute"; RecommendedAction="Monitor for recurrence; consider scaling up service tier" }
)

$bodyJson   = $events | ConvertTo-Json -Depth 5
$bodyBytes  = [System.Text.Encoding]::UTF8.GetBytes($bodyJson)
$date       = [datetime]::UtcNow.ToString("R")
$stringToSign = "POST`n$($bodyBytes.Length)`napplication/json`nx-ms-date:$date`n/api/logs"
$keyBytes   = [Convert]::FromBase64String($wsKey)
$hmac       = [System.Security.Cryptography.HMACSHA256]::new($keyBytes)
$sig        = [Convert]::ToBase64String($hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($stringToSign)))
$auth       = "SharedKey $($WorkspaceId):$sig"

$uri     = "https://$WorkspaceId.ods.opinsights.azure.com/api/logs?api-version=2016-04-01"
$headers = @{
    "Authorization"        = $auth
    "Log-Type"             = $LogType
    "x-ms-date"            = $date
    "time-generated-field" = "TimeGenerated"
}

try {
    $r = Invoke-WebRequest -Method POST -Uri $uri -Headers $headers `
         -Body $bodyJson -ContentType "application/json" -UseBasicParsing -TimeoutSec 30
    Write-Host "INGEST OK: HTTP $($r.StatusCode) - $($events.Count) events posted to ${LogType}_CL" -ForegroundColor Green
} catch {
    Write-Host "INGEST FAIL: $($_.Exception.Message)" -ForegroundColor Red
}

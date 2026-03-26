param(
    [string]$FingerprintSuffix = (Get-Date -Format "HHmmss")
)

$fingerprint = "fresh-test-$FingerprintSuffix"
Write-Host "Using fingerprint: $fingerprint"

$body = @{
    workspaceId = "6b530cc6-14bb-4fad-9577-3a349209ae1c"
    alertPayload = @{
        alertSource   = "AzureMonitor"
        fingerprint   = $fingerprint
        title         = "High CPU on web-01"
        severity      = "High"
        firedAtUtc    = "2025-07-01T14:30:00Z"
        signalType    = "Metric"
        serviceName   = "web-01"
        environment   = "production"
    }
    timeRangeMinutes = 60
} | ConvertTo-Json -Depth 5

Write-Host "Sending POST to http://localhost:5006/agent/triage ..."

$headers = @{
    "x-tenant-id"  = "4a72b866-99a4-4388-b881-cef9c8480b1c"
    "Content-Type" = "application/json"
}

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5006/agent/triage" `
        -Method Post -Body $body -Headers $headers `
        -TimeoutSec 180 -ErrorAction Stop
    Write-Host "HTTP 200"
    Write-Host "=== RESPONSE ==="
    $response | ConvertTo-Json -Depth 10
} catch {
    Write-Host "HTTP ERROR: $_"
    if ($_.Exception.Response) {
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        Write-Host $reader.ReadToEnd()
    }
}

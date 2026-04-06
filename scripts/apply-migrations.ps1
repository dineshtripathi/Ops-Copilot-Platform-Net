param(
    [string]$ConnectionString = $env:SQL_CONNECTION_STRING
)

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    Write-Error "Connection string not provided. Set the SQL_CONNECTION_STRING environment variable or pass -ConnectionString."
    exit 1
}

$cs = $ConnectionString

Set-Location G:\ops-copilot-platform

$result = & dotnet ef database update 20260418120000_AddRunFeedbackTable `
  --project src\Modules\AgentRuns\Infrastructure\OpsCopilot.AgentRuns.Infrastructure `
  --startup-project src\Hosts\OpsCopilot.ApiHost `
  --context AgentRunsDbContext `
  --connection $cs 2>&1

$result | ForEach-Object { Write-Host $_ }
Write-Host "Exit code: $LASTEXITCODE"

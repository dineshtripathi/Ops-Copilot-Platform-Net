param()

$cs = "Server=tcp:sql-opscopilot-platform-dev-uks.database.windows.net,1433;Database=sqldb-opscopilot-platform-dev;User ID=opsadmin;Password=Transview@73AITest;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;"

Set-Location G:\ops-copilot-platform

$result = & dotnet ef database update 20260418120000_AddRunFeedbackTable `
  --project src\Modules\AgentRuns\Infrastructure\OpsCopilot.AgentRuns.Infrastructure `
  --startup-project src\Hosts\OpsCopilot.ApiHost `
  --context AgentRunsDbContext `
  --connection $cs 2>&1

$result | ForEach-Object { Write-Host $_ }
Write-Host "Exit code: $LASTEXITCODE"

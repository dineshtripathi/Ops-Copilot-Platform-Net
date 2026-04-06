param(
    [string]$ConnectionString = $env:SQL_CONNECTION_STRING
)

if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    Write-Error "Connection string not provided. Set the SQL_CONNECTION_STRING environment variable or pass -ConnectionString."
    exit 1
}

$connStr = $ConnectionString

Add-Type -AssemblyName "System.Data"
$conn = New-Object System.Data.SqlClient.SqlConnection($connStr)
try {
    $conn.Open()
    Write-Host "Connected to SQL Server: $($conn.State)"

    # Check migrations applied
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT MigrationId FROM agentRuns.[__EFMigrationsHistory] ORDER BY MigrationId"
    $reader = $cmd.ExecuteReader()
    Write-Host "`n=== Applied Migrations in agentRuns.__EFMigrationsHistory ==="
    while ($reader.Read()) { Write-Host "  $($reader[0])" }
    $reader.Close()

    # Check actual columns
    $cmd2 = $conn.CreateCommand()
    $cmd2.CommandText = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'agentRuns' AND TABLE_NAME = 'AgentRuns'
ORDER BY ORDINAL_POSITION
"@
    $reader2 = $cmd2.ExecuteReader()
    Write-Host "`n=== Actual Columns in agentRuns.AgentRuns ==="
    while ($reader2.Read()) {
        Write-Host "  $($reader2[0])  [$($reader2[1])]  nullable=$($reader2[2])"
    }
    $reader2.Close()
} catch {
    Write-Host "ERROR: $_"
} finally {
    if ($conn.State -ne 'Closed') { $conn.Close() }
}

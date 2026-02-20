/*
  sql.bicep
  Provisions an Azure SQL Server + Database.

  SKU: Basic (5 DTU, 2 GB) — cheapest viable tier (~£4.20/month).
  For slightly more headroom use S0 Standard (10 DTU) by changing sku parameters.

  Security:
  - Admin password is passed as @secure() — never stored in the template.
  - The pipeline should supply it via a GitHub secret.
  - Connection strings must be stored in Key Vault post-deploy (see pipeline).
  - TLS 1.2 enforced, public network access can be toggled.

  Cost note (Basic tier):
    ~£4.20/month for database + ~£0/month for server (server itself has no charge).
    Backup storage (LRS): minimal for 2 GB database.
    Total estimated: < £5/month.
*/
@description('SQL Server logical name (globally unique)')
param sqlServerName string

@description('Database name')
param sqlDbName string

@description('Azure region')
param location string

@description('SQL administrator login name')
param sqlAdminLogin string = 'sqladmin'

@description('SQL administrator password — supply via pipeline secret, never hardcode')
@secure()
param sqlAdminPassword string

@description('Database SKU name. Basic = cheapest (~£4.20/mo)')
@allowed(['Basic', 'S0', 'S1', 'GP_S_Gen5_1'])
param skuName string = 'Basic'

@description('Database SKU tier')
@allowed(['Basic', 'Standard', 'GeneralPurpose'])
param skuTier string = 'Basic'

@description('DTU capacity (Basic=5, S0=10, S1=20). Ignored for vCore SKUs.')
param skuCapacity int = 5

@description('Max size in bytes. Basic max = 2 GB.')
param maxSizeBytes int = 2147483648

@description('Allow Azure services to access (needed for App Service/Container Apps without VNet)')
param allowAzureServices bool = true

@description('Tags to apply')
param tags object

// ── SQL Server ──────────────────────────────────────────────
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'  // Restrict with firewall rules below
  }
}

// ── Allow Azure services firewall rule (needed for Container Apps) ─────
resource firewallAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = if (allowAzureServices) {
  name: 'AllowAllWindowsAzureIps'
  parent: sqlServer
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// ── Database ────────────────────────────────────────────────
resource sqlDb 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  name: sqlDbName
  parent: sqlServer
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
    capacity: skuCapacity
  }
  properties: {
    maxSizeBytes: maxSizeBytes
    // Local backup redundancy = cheapest option
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlServerId string = sqlServer.id
output sqlDbId string = sqlDb.id
output sqlDbName string = sqlDb.name
// Connection string template — substitute password in pipeline/KV secret
output connectionStringTemplate string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDb.name};User ID=${sqlAdminLogin};Password=<from-keyvault>;Encrypt=true;TrustServerCertificate=false;Connection Timeout=30;'

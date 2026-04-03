/*
  acr.bicep
  Azure Container Registry — private image store for OpsCopilot Container Apps.

  Tier:  Basic  (dev/sandbox) — lowest cost, sufficient for CI/CD cadence.
         Standard (prod)      — 100 GB included storage, geo-replication capable.

  Security:
    - Admin user disabled. Container Apps pull images via managed identity + AcrPull.
    - Public network access enabled by default (restrict to CAE outbound IPs when
      private networking is added in a future slice).

  Cost: Basic ~£4/month idle + £0.003/GB storage + pipeline execution minutes.
        Standard ~£16/month idle. Upgrade only when geo-replication is needed.
*/

@description('Container Registry name (5–50 chars, alphanumeric only, globally unique).')
param registryName string

@description('Azure region.')
param location string

@description('Tags to apply.')
param tags object

@description('ACR SKU tier. Basic = dev/sandbox. Standard = prod.')
@allowed(['Basic', 'Standard', 'Premium'])
param skuName string = 'Basic'

// ── Registry ─────────────────────────────────────────────────────────────────
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  properties: {
    // Admin user disabled — always authenticate via managed identity + AcrPull role.
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    // zoneRedundancy is Premium-only; omit on Basic/Standard.
    zoneRedundancy: skuName == 'Premium' ? 'Enabled' : 'Disabled'
    policies: {
      retentionPolicy: {
        // retentionPolicy can only be enabled on Standard or Premium SKU.
        status: skuName == 'Basic' ? 'disabled' : 'enabled'
        days: 7
      }
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output registryId    string = acr.id
output registryName  string = acr.name
output loginServer   string = acr.properties.loginServer

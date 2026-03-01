/*
  tenant-skeleton.bicep
  Blank subscription-scope skeleton for provisioning a new
  OpsCopilot tenant.  Copy this file and fill in the
  <PLACEHOLDER> values.

  Deploy with:
    az deployment sub create \
      --location <REGION> \
      --template-file tenant-skeleton.bicep \
      --parameters tenantLabel=<TENANT> environment=<ENV>
*/

targetScope = 'subscription'

// ─────────────────────────────────────────────
// PARAMETERS — customise per tenant
// ─────────────────────────────────────────────

@description('Target environment: dev | sandbox | prod')
@allowed(['dev', 'sandbox', 'prod'])
param environment string

@description('Azure region')
param location string = 'uksouth'   // <PLACEHOLDER>

@description('Logical tenant label')
param tenantLabel string

// ─────────────────────────────────────────────
// VARIABLES
// ─────────────────────────────────────────────

var rgName = 'rg-opscopilot-${toLower(tenantLabel)}-${environment}'

var tags = {
  Application: 'OpsCopilot'
  Environment: environment
  Tenant:      tenantLabel
  ManagedBy:   'bicep'
}

// ─────────────────────────────────────────────
// RESOURCE GROUP
// ─────────────────────────────────────────────

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name:     rgName
  location: location
  tags:     tags
}

// ─────────────────────────────────────────────
// ADD MODULES BELOW
// ─────────────────────────────────────────────
// Example: deploy Log Analytics into the new RG
//
// module law 'azure-infra/bicep/modules/logAnalytics.bicep' = {
//   name: 'law-${tenantLabel}'
//   scope: rg
//   params: {
//     lawName:        'law-opscopilot-${toLower(tenantLabel)}'
//     location:       location
//     retentionInDays: 30
//     dailyQuotaGb:   1
//     tags:           tags
//   }
// }

// ─────────────────────────────────────────────
// OUTPUTS
// ─────────────────────────────────────────────

output resourceGroupName string = rg.name
output resourceGroupId   string = rg.id

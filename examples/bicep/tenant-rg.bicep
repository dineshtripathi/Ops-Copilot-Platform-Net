/*
  tenant-rg.bicep
  Minimal subscription-scope template that creates a single
  tenant resource group with standard tags.

  Deploy with:
    az deployment sub create \
      --location uksouth \
      --template-file examples/bicep/tenant-rg.bicep \
      --parameters tenantLabel=TenantA environment=dev

  This file is an *example* — it is NOT used by the production
  infrastructure pipeline.  See azure-infra/bicep/main.bicep
  for the real orchestrator.
*/

targetScope = 'subscription'

// ─────────────────────────────────────────────
// PARAMETERS
// ─────────────────────────────────────────────

@description('Target environment: dev | sandbox | prod')
@allowed(['dev', 'sandbox', 'prod'])
param environment string

@description('Azure region')
param location string = 'uksouth'

@description('Logical tenant label, e.g., TenantA')
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
// OUTPUTS
// ─────────────────────────────────────────────

output resourceGroupName string = rg.name
output resourceGroupId   string = rg.id

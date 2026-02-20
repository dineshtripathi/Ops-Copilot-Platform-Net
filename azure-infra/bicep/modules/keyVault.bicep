/*
  keyVault.bicep
  Deploys a Key Vault with RBAC authorization (recommended over access policies).
  SKU: Standard — Premium/HSM is explicitly prohibited.
  Cost note: ~£0.03 per 10,000 secret operations; negligible for dev/MVP.
*/
@description('Name of the Key Vault (3–24 chars, globally unique)')
param keyVaultName string

@description('Azure region')
param location string

@description('Tags to apply')
param tags object

@description('''
  Object ID of the deploying service principal.
  When provided, grants Key Vault Secrets Officer on the vault so the pipeline
  can write secrets (e.g. SQL connection string) immediately after deployment.
  Leave empty to skip the role assignment (managed elsewhere).
''')
param deployerObjectId string = ''

// Standard SKU is enforced — do NOT change to 'premium'
var kvSku = {
  family: 'A'
  name: 'standard'
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: kvSku
    tenantId: subscription().tenantId
    // RBAC authorization replaces legacy access policies
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7   // minimum: 7 days; saves cost vs 90-day default
    enabledForDeployment: false
    enabledForDiskEncryption: false
    enabledForTemplateDeployment: true
    publicNetworkAccess: 'Enabled'  // restrict further when private endpoints are added
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

// ── Role assignment: Key Vault Secrets Officer for deploying SP ───────────────
// Role ID: b86a8fe4-44ce-4948-aee5-eccb2c155cd7 (Key Vault Secrets Officer)
// Only created when deployerObjectId is supplied by the pipeline.
var kvSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource kvDeployerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(deployerObjectId)) {
  name: guid(keyVault.id, deployerObjectId, kvSecretsOfficerRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsOfficerRoleId)
    principalId: deployerObjectId
    principalType: 'ServicePrincipal'
  }
}

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name

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

output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name

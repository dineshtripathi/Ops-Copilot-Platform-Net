/*
  storage.bicep
  Deploys a Storage Account.
  Default: OFF (enableStorage = false in main.bicep).
  SKU: Standard_LRS — Premium/GRS/ZRS is explicitly prohibited.
  Cost note: ~£0.016/GB/month for blob storage in UK South.

  Naming: Azure storage account names must be 3–24 lowercase alphanumeric chars.
  This module accepts a full name (caller is responsible for uniqueness suffix).
*/
@description('Full storage account name (3–24 lowercase alphanumeric, globally unique)')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Azure region')
param location string

@description('Storage SKU. Must be Standard_LRS for cost compliance.')
@allowed([
  'Standard_LRS'
])
param storageSku string = 'Standard_LRS'

@description('Tags to apply')
param tags object

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: storageSku
  }
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true   // disable when managed identity is fully adopted
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    encryption: {
      services: {
        blob: {
          enabled: true
        }
        file: {
          enabled: true
        }
      }
      keySource: 'Microsoft.Storage'
    }
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output primaryEndpointBlob string = storageAccount.properties.primaryEndpoints.blob

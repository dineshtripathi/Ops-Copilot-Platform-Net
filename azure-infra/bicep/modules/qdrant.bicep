/*
  qdrant.bicep
  Provisions Qdrant vector database as an Azure Container App with persistent Azure Files storage.

  Architecture:
  1. Creates an Azure Files share in the existing platform storage account.
  2. Registers the Azure Files share on the Container Apps Environment (required for volume mounts).
  3. Creates the Qdrant Container App with the volume mounted at /qdrant/storage.

  Cost:
  - Container App (Consumption, scale-to-zero): £0 when idle.
  - Azure Files (Standard LRS) for 1 GB: ~£0.06/month.
  - Total estimated: < £1/month for dev workloads.

  Note: When Qdrant scales to 0, data persists in Azure Files. On next scale-up,
  Qdrant loads indexes from disk. Cold-start latency is ~10-30 seconds.
  For production, set minReplicas=1 to avoid cold starts and data loading lag.
*/
@description('Qdrant Container App name')
param appName string = 'opscopilot-qdrant'

@description('Azure region')
param location string

@description('Container Apps Environment name (must be in same resource group)')
param caeName string

@description('Storage account name (must be in same resource group)')
param storageAccountName string

@description('Azure Files share name to create for Qdrant data')
param shareName string = 'qdrant-storage'

@description('Storage registration name on the CAE (unique across CAE storages)')
param caeStorageName string = 'qdrantstorage'

@description('Qdrant image version. Pin to a specific version for production.')
param qdrantImage string = 'qdrant/qdrant:v1.13.1'

@description('HTTP API port for Qdrant')
param qdrantHttpPort int = 6333

@description('gRPC port for Qdrant')
param qdrantGrpcPort int = 6334

@description('Minimum replicas. 0=scale-to-zero (data safe in Azure Files). Set 1 for prod.')
@minValue(0)
param minReplicas int = 0

@description('Maximum replicas. Keep 1 for a stateful service like Qdrant.')
param maxReplicas int = 1

@description('Tags to apply')
param tags object

// ── Reference existing resources ────────────────────────────

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource cae 'Microsoft.App/managedEnvironments@2023-11-02-preview' existing = {
  name: caeName
}

var storageKey = storageAccount.listKeys().keys[0].value

// ── Azure Files share ────────────────────────────────────────

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-01-01' existing = {
  name: 'default'
  parent: storageAccount
}

resource qdrantShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-01-01' = {
  name: shareName
  parent: fileService
  properties: {
    shareQuota: 1   // 1 GB minimum quota. Dev data well under this.
    accessTier: 'TransactionOptimized'
  }
}

// ── Register Azure Files on Container Apps Environment ───────

resource caeStorage 'Microsoft.App/managedEnvironments/storages@2023-11-02-preview' = {
  name: caeStorageName
  parent: cae
  properties: {
    azureFile: {
      accountName: storageAccountName
      accountKey: storageKey
      shareName: shareName
      accessMode: 'ReadWrite'
    }
  }
  dependsOn: [qdrantShare]
}

// ── Qdrant Container App ──────────────────────────────────────

resource qdrantApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: appName
  location: location
  tags: tags
  properties: {
    managedEnvironmentId: cae.id
    configuration: {
      ingress: {
        external: false            // Internal only — front via APIM/apihost later
        targetPort: qdrantHttpPort
        allowInsecure: false
        traffic: [
          {
            weight: 100
            latestRevision: true
          }
        ]
        additionalPortMappings: [
          {
            targetPort: qdrantGrpcPort
            external: false
          }
        ]
      }
    }
    template: {
      volumes: [
        {
          name: 'qdrant-data'
          storageType: 'AzureFile'
          storageName: caeStorageName
        }
      ]
      containers: [
        {
          name: 'qdrant'
          image: qdrantImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          volumeMounts: [
            {
              volumeName: 'qdrant-data'
              mountPath: '/qdrant/storage'
            }
          ]
          env: [
            {
              name: 'QDRANT__SERVICE__GRPC_PORT'
              value: string(qdrantGrpcPort)
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
  dependsOn: [caeStorage]
}

output qdrantAppId string = qdrantApp.id
output qdrantAppName string = qdrantApp.name
output qdrantFqdn string = qdrantApp.properties.configuration.ingress.fqdn
output qdrantHttpUrl string = 'https://${qdrantApp.properties.configuration.ingress.fqdn}'

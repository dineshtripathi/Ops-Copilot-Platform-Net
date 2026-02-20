/*
  search.bicep
  Provisions an Azure AI Search service.

  Default: NOT deployed (searchProvision=false in main.ai.bicep).
  TOGGLE: Set searchProvision=true to deploy.

  SKU OPTIONS (choose lowest viable for environment):
    free    — 3 indexes, 50 MB, no SLA, 1 per subscription — £0/month (dev/test)
    basic   — 15 indexes, 2 GB, 3 replicas, SLA          — ~£65/month (sandbox/prod min)
    standard — full production                            — ~£195/month (prod only)

  IMPORTANT:
  - Free tier: only 1 free Search service allowed per subscription.
    If the subscription already has a free Search service, change sku to 'basic'.
  - Basic tier: ~£65/month even with zero queries. Only enable when needed.
  - searchProvision defaults to FALSE to protect cost budgets.

  Cost note: With zero queries, the Basic tier still costs ~£65/month.
  Enable only when you are actively using the Search capability.
*/
@description('Azure AI Search service name (globally unique, 2–60 chars, lowercase alphanumeric + hyphens)')
param searchName string

@description('Azure region')
param location string

@description('Search SKU. free=£0 (dev), basic=~£65/mo (prod minimum).')
@allowed(['free', 'basic', 'standard', 'standard2', 'standard3'])
param searchSku string = 'free'

@description('Number of replicas. 1 is minimum (and only option for free and basic).')
@minValue(1)
@maxValue(12)
param replicaCount int = 1

@description('Number of partitions. 1 minimum.')
@minValue(1)
param partitionCount int = 1

@description('Tags to apply')
param tags object

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: searchName
  location: location
  tags: tags
  sku: {
    name: searchSku
  }
  properties: {
    replicaCount: replicaCount
    partitionCount: searchSku == 'free' ? 1 : partitionCount
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    // Disable local auth (API key) in favour of RBAC when ready
    disableLocalAuth: false
    authOptions: {
      aadOrApiKey: {
        aadAuthFailureMode: 'http403'
      }
    }
  }
}

output searchId string = searchService.id
output searchEndpoint string = 'https://${searchService.name}.search.windows.net'
output searchName string = searchService.name

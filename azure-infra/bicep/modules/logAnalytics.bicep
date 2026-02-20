/*
  logAnalytics.bicep
  Deploys a Log Analytics Workspace.
  SKU: PerGB2018 (Pay-As-You-Go) — cheapest available.
  Cost guardrails:
    - retentionInDays: 30 days default (minimum is 30; Interactive retention only goes lower on dedicated tiers)
    - dailyQuotaGb: 0.1 GB/day default (~£0.23/month at £2.30/GB)
      Set to -1 to disable the quota (not recommended in dev/sandbox).
*/
@description('Name of the Log Analytics Workspace')
param lawName string

@description('Azure region')
param location string

@description('Retention in days (30–730). Default 30 for cost saving.')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Daily ingestion quota in GB as a string. Use "-1" to disable the cap. Default "0.1" GB (~£0.23/month) for cost control.')
param dailyQuotaGb string = '0.1'

@description('Tags to apply')
param tags object

resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: lawName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    workspaceCapping: {
      dailyQuotaGb: json(dailyQuotaGb)
    }
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceId string = law.id
output customerId string = law.properties.customerId
output workspaceName string = law.name

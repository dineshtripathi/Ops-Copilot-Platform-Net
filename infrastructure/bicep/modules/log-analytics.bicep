@description('Name of the Log Analytics workspace to deploy.')
param workspaceName string

@description('Azure region for the workspace.')
param location string = resourceGroup().location

@description('Optional tags to stamp onto the workspace.')
param tags object = {}

@description('Number of days to retain logs inside the workspace.')
@allowed([30, 60, 90, 120, 365, 730])
param retentionInDays int = 30

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: workspaceName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: retentionInDays
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output workspaceId string = logAnalytics.id
output customerId string = logAnalytics.properties.customerId

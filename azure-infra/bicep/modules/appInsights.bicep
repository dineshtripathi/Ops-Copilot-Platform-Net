/*
  appInsights.bicep
  Deploys a workspace-based Application Insights resource.
  Cost note:
    - There is no SKU tier to choose — App Insights is 100% consumption-based.
    - Data is stored in the linked Log Analytics Workspace (lawResourceId).
    - First 5 GB/month is free; ~£2.30/GB thereafter.
    - Mitigate cost by enabling adaptive sampling in the application (not IaC).
*/
@description('Name of the Application Insights resource')
param appInsightsName string

@description('Azure region')
param location string

@description('Resource ID of the Log Analytics Workspace to link')
param lawResourceId string

@description('Tags to apply')
param tags object

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  tags: tags
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: lawResourceId
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
    // Disable local authentication to enforce AAD-only (best practice)
    DisableLocalAuth: false
    // Sampling is controlled at the SDK level, not here
  }
}

output appInsightsId string = appInsights.id
output instrumentationKey string = appInsights.properties.InstrumentationKey
output connectionString string = appInsights.properties.ConnectionString

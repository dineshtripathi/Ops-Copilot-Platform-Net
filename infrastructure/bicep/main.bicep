targetScope = 'resourceGroup'

@description('Azure region for core resources.')
param location string = resourceGroup().location

@description('Log Analytics workspace name.')
param workspaceName string

@description('Tags applied to shared resources.')
param tags object = {}

@description('Budget resource name.')
param budgetName string

@description('Budget amount in USD for the selected time window.')
param budgetAmount int

@description('Budget window start date (ISO8601).')
param budgetStartDate string

@description('Budget window end date (ISO8601).')
param budgetEndDate string

@description('Budget alert recipients.')
param budgetContactEmails array

@description('Flag indicating if Azure AI Foundry should be provisioned by the pipeline CLI stage.')
param enableFoundry bool = false

@description('Preview Foundry workspace/account name.')
param foundryName string = ''

@description('Preview Foundry project name.')
param foundryProjectName string = ''

@description('Model endpoint configuration to associate with Foundry projects.')
param modelProvider object = {
  endpointName: ''
  modelName: ''
}

module logAnalytics './modules/log-analytics.bicep' = {
  name: 'logAnalyticsWorkspace'
  params: {
    workspaceName: workspaceName
    location: location
    tags: tags
  }
}

module costBudget './modules/budget.bicep' = {
  name: 'costBudget'
  params: {
    scope: resourceGroup().id
    budgetName: budgetName
    amount: budgetAmount
    startDate: budgetStartDate
    endDate: budgetEndDate
    contactEmails: budgetContactEmails
    tags: tags
  }
}

module foundryConfig './modules/foundry.bicep' = {
  name: 'foundryConfig'
  params: {
    enableFoundry: enableFoundry
    foundryName: foundryName
    projectName: foundryProjectName
    modelProvider: modelProvider
  }
}

output logAnalyticsWorkspaceId string = logAnalytics.outputs.workspaceId
output logAnalyticsCustomerId string = logAnalytics.outputs.customerId
output budgetId string = costBudget.outputs.budgetId
output foundry object = foundryConfig.outputs.foundryConfig

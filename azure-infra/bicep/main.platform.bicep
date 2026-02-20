/*
  main.platform.bicep
  =====================================================================
  Subscription-scope orchestrator for OpsCopilot PLATFORM subscription (SubA).

  Resource Group:  rg-opscopilot-platform-{env}-{region}
  Subscription:    b20a7294-6951-4107-88df-d7d320218670

  Resources:
    - Log Analytics Workspace
    - Application Insights
    - Key Vault (Standard, RBAC)
    - Storage Account (Standard_LRS — optional guard)
    - Azure SQL Server + Database (Basic 5 DTU)
    - Container Apps Environment (Consumption plan)
    - Container Apps:
        * opscopilot-apihost    (external ingress, port 8080)
        * opscopilot-workerhost (no ingress — background)
        * opscopilot-mcphost   (external ingress, port 8081)
        * qdrant               (internal ingress only, via qdrant.bicep)
    - Budget (£80 default)

  Refer to azure-infra/README.md for toggle descriptions and cost notes.
  =====================================================================
*/
targetScope = 'subscription'

// ── Environment ──────────────────────────────────────────────────────────────
@description('Deployment environment.')
@allowed(['dev', 'sandbox', 'prod'])
param environment string = 'dev'

@description('Primary Azure region.')
param location string = 'uksouth'

// ── Resource Group ────────────────────────────────────────────────────────────
@description('Name of the platform resource group.')
param rgName string = 'rg-opscopilot-platform-${environment}-uks'

// ── Observability ─────────────────────────────────────────────────────────────
@description('Log Analytics Workspace name.')
param lawName string = 'law-opscopilot-platform-${environment}-uks'

@description('Application Insights name.')
param appInsightsName string = 'appi-opscopilot-platform-${environment}-uks'

// ── Key Vault ─────────────────────────────────────────────────────────────────
@description('Key Vault name (3–24 chars, globally unique).')
param keyVaultName string = 'kv-opsctpltf${environment}'

// ── Storage ───────────────────────────────────────────────────────────────────
@description('Storage account name prefix (max 14 chars; region suffix appended).')
param storageNamePrefix string = 'stopscopilotpltf'

@description('When false, the storage account module is skipped (useful if Qdrant is disabled too).')
param enableStorage bool = true

// ── SQL ───────────────────────────────────────────────────────────────────────
@description('SQL Server logical name.')
param sqlServerName string = 'sql-opscopilot-platform-${environment}-uks'

@description('SQL Database name.')
param sqlDbName string = 'sqldb-opscopilot-platform-${environment}'

@description('SQL admin username.')
param sqlAdminLogin string = 'opsadmin'

@description('SQL admin password. Must be stored in GitHub secret SQL_ADMIN_PASSWORD_PLATFORM.')
@secure()
param sqlAdminPassword string

// ── Container Apps ────────────────────────────────────────────────────────────
@description('Container Apps Environment name.')
param caeName string = 'cae-opscopilot-platform-${environment}-uks'

@description('Container image to use when bootstrapping. Overridden during app deployment.')
param bootstrapImage string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

// ── Budget ────────────────────────────────────────────────────────────────────
@description('Monthly budget amount in GBP.')
param budgetAmount int = 80

@description('Email addresses that receive budget alerts.')
param budgetEmails array = []

@description('''Object ID of the service principal that runs this deployment.
  Used to grant Key Vault Secrets Officer so the pipeline can write secrets.
  Resolved in CI via: az ad sp show --id $CLIENT_ID --query id -o tsv
''')
param deployerObjectId string = ''

// ── Tags ──────────────────────────────────────────────────────────────────────
@description('Extra tags to merge onto all resources.')
param extraTags object = {}

var baseTags = union({
  environment: environment
  platform: 'opscopilot'
  subscription: 'platform'
  managedBy: 'bicep'
}, extraTags)

// ── Resource Group ────────────────────────────────────────────────────────────
module rg 'modules/rg.bicep' = {
  name: 'deploy-rg-platform'
  params: {
    rgName: rgName
    location: location
    tags: baseTags
  }
}

// Typed existing reference to the platform RG — used as the explicit scope for
// the containerAppRbac module below. The `dependsOn: [rg]` on downstream modules
// already ensures the RG exists before any resource-scoped deployment runs.
resource platformRg 'Microsoft.Resources/resourceGroups@2022-09-01' existing = {
  name: rgName
}

// ── Log Analytics Workspace ───────────────────────────────────────────────────
module law 'modules/logAnalytics.bicep' = {
  name: 'deploy-law-platform'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    lawName: lawName
    location: location
    retentionInDays: environment == 'prod' ? 90 : 30
    dailyQuotaGb: environment == 'prod' ? '-1' : '0.1'
    tags: baseTags
  }
}

// ── Application Insights ──────────────────────────────────────────────────────
module appInsights 'modules/appInsights.bicep' = {
  name: 'deploy-appi-platform'
  scope: resourceGroup(rgName)
  params: {
    appInsightsName: appInsightsName
    location: location
    lawResourceId: law.outputs.workspaceId
    tags: baseTags
  }
}

// ── Key Vault ─────────────────────────────────────────────────────────────────
module kv 'modules/keyVault.bicep' = {
  name: 'deploy-kv-platform'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: baseTags
    deployerObjectId: deployerObjectId
  }
}

// ── Storage Account ───────────────────────────────────────────────────────────
// Used by Qdrant for persistent Azure Files volume.
module storage 'modules/storage.bicep' = if (enableStorage) {
  name: 'deploy-storage-platform'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    storageAccountName: take('${storageNamePrefix}${environment}', 24)
    location: location
    tags: baseTags
  }
}

// ── Azure SQL ─────────────────────────────────────────────────────────────────
module sql 'modules/sql.bicep' = {
  name: 'deploy-sql-platform'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    sqlServerName: sqlServerName
    sqlDbName: sqlDbName
    location: location
    sqlAdminLogin: sqlAdminLogin
    sqlAdminPassword: sqlAdminPassword
    tags: baseTags
  }
}

// ── Container Apps Environment ────────────────────────────────────────────────
module cae 'modules/containerAppsEnv.bicep' = {
  name: 'deploy-cae-platform'
  scope: resourceGroup(rgName)
  params: {
    caeName: caeName
    location: location
    lawResourceId: law.outputs.workspaceId
    lawCustomerId: law.outputs.customerId
    tags: baseTags
  }
}

// ── Container App: API Host ───────────────────────────────────────────────────
module caApiHost 'modules/containerApp.bicep' = {
  name: 'deploy-ca-apihost'
  scope: resourceGroup(rgName)
  params: {
    appName: 'ca-opscopilot-apihost-${environment}'
    location: location
    caeId: cae.outputs.caeId
    image: bootstrapImage
    enableIngress: true
    isExternalIngress: true
    targetPort: 8080
    minReplicas: 0
    maxReplicas: environment == 'prod' ? 5 : 2
    tags: baseTags
  }
}

// ── Container App: Worker Host ────────────────────────────────────────────────
module caWorkerHost 'modules/containerApp.bicep' = {
  name: 'deploy-ca-workerhost'
  scope: resourceGroup(rgName)
  params: {
    appName: 'ca-opscopilot-workerhost-${environment}'
    location: location
    caeId: cae.outputs.caeId
    image: bootstrapImage
    enableIngress: false
    isExternalIngress: false
    targetPort: 8080
    minReplicas: 0
    maxReplicas: environment == 'prod' ? 3 : 1
    tags: baseTags
  }
}

// ── Container App: MCP Host ───────────────────────────────────────────────────
module caMcpHost 'modules/containerApp.bicep' = {
  name: 'deploy-ca-mcphost'
  scope: resourceGroup(rgName)
  params: {
    appName: 'ca-opscopilot-mcphost-${environment}'
    location: location
    caeId: cae.outputs.caeId
    image: bootstrapImage
    enableIngress: true
    isExternalIngress: true
    targetPort: 8081
    minReplicas: 0
    maxReplicas: environment == 'prod' ? 3 : 1
    tags: baseTags
  }
}

// ── Container App: Qdrant ─────────────────────────────────────────────────────
// Only deployed when enableStorage is true (requires the Azure Files storage account).
module qdrant 'modules/qdrant.bicep' = if (enableStorage) {
  name: 'deploy-qdrant'
  scope: resourceGroup(rgName)
  // storage name is passed as a string (not an output ref), so explicit ordering is needed
  dependsOn: [storage]
  params: {
    location: location
    caeName: cae.outputs.caeName
    storageAccountName: take('${storageNamePrefix}${environment}', 24)
    tags: baseTags
  }
}

// ── Container App RBAC ───────────────────────────────────────────────────────
// Grants system-assigned managed identities the minimum roles they need:
//   Key Vault Secrets User  — apihost, workerhost, mcphost
//   Log Analytics Reader    — apihost, mcphost
// Uses deterministic GUID names → safe to re-run (idempotent).
module caRbac 'modules/containerAppRbac.bicep' = {
  name: 'deploy-ca-rbac-platform'
  scope: platformRg
  params: {
    kvResourceId: kv.outputs.keyVaultId
    lawResourceId: law.outputs.workspaceId
    apiHostPrincipalId: caApiHost.outputs.principalId
    workerHostPrincipalId: caWorkerHost.outputs.principalId
    mcpHostPrincipalId: caMcpHost.outputs.principalId
  }
}

// ── Budget ────────────────────────────────────────────────────────────────────
module budget 'modules/budget.bicep' = {
  name: 'deploy-budget-platform'
  params: {
    budgetName: 'budget-opscopilot-platform-${environment}'
    budgetAmount: budgetAmount
    budgetEmails: budgetEmails
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output resourceGroupName string = rgName
output lawId string = law.outputs.workspaceId
output appInsightsId string = appInsights.outputs.appInsightsId
output keyVaultId string = kv.outputs.keyVaultId
output keyVaultUri string = kv.outputs.keyVaultUri
output sqlServerFqdn string = sql.outputs.sqlServerFqdn
output sqlConnectionStringTemplate string = sql.outputs.connectionStringTemplate
output caeId string = cae.outputs.caeId
output caeDefaultDomain string = cae.outputs.caeDefaultDomain
output apiHostFqdn string = caApiHost.outputs.containerAppFqdn
output mcpHostFqdn string = caMcpHost.outputs.containerAppFqdn

#disable-next-line BCP318
output storageName string = enableStorage ? storage.outputs.storageAccountName : ''

#disable-next-line BCP318
output qdrantFqdn string = enableStorage ? qdrant.outputs.qdrantFqdn : ''

#disable-next-line BCP318
output qdrantHttpUrl string = enableStorage ? qdrant.outputs.qdrantHttpUrl : ''

// Managed identity principal IDs (useful for cross-subscription RBAC scripts)
output apiHostPrincipalId string = caApiHost.outputs.principalId
output workerHostPrincipalId string = caWorkerHost.outputs.principalId
output mcpHostPrincipalId string = caMcpHost.outputs.principalId

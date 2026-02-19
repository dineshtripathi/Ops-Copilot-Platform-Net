/*
  main.bicep
  Subscription-scope orchestrator for OpsCopilot infrastructure.

  Deploy with:
    az deployment sub create \
      --location <region> \
      --template-file azure-infra/bicep/main.bicep \
      --parameters @azure-infra/bicep/env/<env>/<tenant>.parameters.json \
      --parameters location=<region> environment=<env>

  IMPORTANT: All resource names and subscription-specific values come from
  parameter files (env/<env>/tenant*.parameters.json).
  NO hardcoded subscription IDs, names, or tenant labels inside this file.
*/
targetScope = 'subscription'

// ─────────────────────────────────────────────
// PARAMETERS
// ─────────────────────────────────────────────

@description('Target environment: dev | sandbox | prod')
@allowed(['dev', 'sandbox', 'prod'])
param environment string

@description('Azure region (e.g., uksouth)')
param location string = 'uksouth'

@description('Logical tenant label, e.g., TenantA or TenantB')
param tenantLabel string

// ── Resource Group ──────────────────────────
@description('Name of the resource group to create')
param rgName string

// ── Log Analytics ────────────────────────────
@description('Name of the Log Analytics Workspace')
param lawName string

@description('Log retention in days (30–730)')
@minValue(30)
@maxValue(730)
param retentionInDays int = 30

@description('Daily ingestion quota in GB. Use -1 to disable quota.')
param dailyQuotaGb int = 0

// ── App Insights ─────────────────────────────
@description('Enable Application Insights (workspace-based)')
param enableAppInsights bool = true

@description('Name of the Application Insights resource')
param appInsightsName string = ''

// ── Key Vault ─────────────────────────────────
@description('Enable Key Vault (Standard, RBAC)')
param enableKeyVault bool = true

@description('Name of the Key Vault (3–24 chars, globally unique)')
param keyVaultName string = ''

// ── Storage ────────────────────────────────────
@description('Enable Storage Account (optional, default OFF)')
param enableStorage bool = false

@description('Storage account name prefix (will have a deterministic suffix appended)')
param storageNamePrefix string = ''

@description('Storage SKU. Standard_LRS enforced.')
@allowed(['Standard_LRS'])
param storageSku string = 'Standard_LRS'

// ── Budget ─────────────────────────────────────
@description('Monthly subscription budget amount (GBP)')
param budgetAmount int = 80

@description('Email addresses for budget breach notifications')
param budgetEmails array = []

// ── Foundry ────────────────────────────────────
@description('Provision the Azure AI Foundry / OpenAI account resource')
param foundryProvision bool = false

@description('Enable Foundry usage (model deployment + RBAC via CLI). Requires foundryProvision=true.')
param foundryEnabled bool = false

@description('Name for the Azure AI / Cognitive Services account')
param foundryName string = ''

@description('Placeholder for Foundry project name (used in CLI steps, not Bicep)')
param foundryProjectName string = ''

// ── Tags ───────────────────────────────────────
@description('Tag overrides. Merged with auto-generated common tags.')
param commonTags object = {}

// ─────────────────────────────────────────────
// VARIABLES
// ─────────────────────────────────────────────

// Build a deterministic storage name: prefix + last 8 chars of subscription ID
// Storage names: lowercase, max 24 chars, alphanumeric only
var subscriptionSuffix = take(replace(subscription().subscriptionId, '-', ''), 8)
var storageAccountName = enableStorage ? take('${toLower(storageNamePrefix)}${subscriptionSuffix}', 24) : ''

var budgetName = 'budget-opscopilot-${toLower(tenantLabel)}-${environment}'

// ─────────────────────────────────────────────
// TAGS (inline — tags.bicep outputs are used as a template reference only)
// ─────────────────────────────────────────────

var baseTags = {
  product: 'OpsCopilot'
  environment: environment
  tenantLabel: tenantLabel
  #disable-next-line use-safe-access
  owner: contains(commonTags, 'owner') ? (commonTags.?owner ?? 'platform-team') : 'platform-team'
  #disable-next-line use-safe-access
  costCenter: contains(commonTags, 'costCenter') ? (commonTags.?costCenter ?? 'opscopilot') : 'opscopilot'
  managedBy: 'github-oidc'
  region: location
}

// Merge auto tags with any overrides passed in commonTags
var effectiveTags = union(baseTags, commonTags)

// ─────────────────────────────────────────────
// RESOURCE GROUP
// ─────────────────────────────────────────────

module rgModule 'modules/rg.bicep' = {
  name: 'rg-deploy'
  params: {
    rgName: rgName
    location: location
    tags: effectiveTags
  }
}

// ─────────────────────────────────────────────
// LOG ANALYTICS WORKSPACE
// ─────────────────────────────────────────────

module lawModule 'modules/logAnalytics.bicep' = {
  name: 'law-deploy'
  scope: resourceGroup(rgName)
  dependsOn: [rgModule]
  params: {
    lawName: lawName
    location: location
    retentionInDays: retentionInDays
    dailyQuotaGb: dailyQuotaGb
    tags: effectiveTags
  }
}

// ─────────────────────────────────────────────
// APPLICATION INSIGHTS
// ─────────────────────────────────────────────

module appInsightsModule 'modules/appInsights.bicep' = if (enableAppInsights) {
  name: 'appi-deploy'
  scope: resourceGroup(rgName)
  dependsOn: [rgModule]
  params: {
    appInsightsName: appInsightsName
    location: location
    lawResourceId: lawModule.outputs.workspaceId
    tags: effectiveTags
  }
}

// ─────────────────────────────────────────────
// KEY VAULT
// ─────────────────────────────────────────────

module keyVaultModule 'modules/keyVault.bicep' = if (enableKeyVault) {
  name: 'kv-deploy'
  scope: resourceGroup(rgName)
  dependsOn: [rgModule]
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: effectiveTags
  }
}

// ─────────────────────────────────────────────
// STORAGE ACCOUNT (optional)
// ─────────────────────────────────────────────

module storageModule 'modules/storage.bicep' = if (enableStorage) {
  name: 'storage-deploy'
  scope: resourceGroup(rgName)
  dependsOn: [rgModule]
  params: {
    storageAccountName: storageAccountName
    location: location
    storageSku: storageSku
    tags: effectiveTags
  }
}

// ─────────────────────────────────────────────
// BUDGET (subscription scope)
// ─────────────────────────────────────────────

module budgetModule 'modules/budget.bicep' = {
  name: 'budget-deploy'
  params: {
    budgetName: budgetName
    budgetAmount: budgetAmount
    budgetEmails: budgetEmails
  }
}

// ─────────────────────────────────────────────
// FOUNDRY / AZURE AI (account only; model deployment via CLI)
// ─────────────────────────────────────────────

module foundryModule 'modules/foundry.bicep' = if (foundryProvision) {
  name: 'foundry-deploy'
  scope: resourceGroup(rgName)
  dependsOn: [rgModule]
  params: {
    foundryName: foundryName
    location: location
    tags: effectiveTags
    customSubDomainName: foundryName
  }
}

// ─────────────────────────────────────────────
// OUTPUTS
// ─────────────────────────────────────────────

output resourceGroupId string = rgModule.outputs.resourceGroupId
output logAnalyticsWorkspaceId string = lawModule.outputs.workspaceId
output logAnalyticsCustomerId string = lawModule.outputs.customerId

// Conditional outputs — guarded with empty-string fallback when module is not deployed.
// BCP318 warnings below are suppressed: the guard condition is identical to the module condition,
// so the access is safe at runtime.
#disable-next-line BCP318
output appInsightsId string = enableAppInsights ? appInsightsModule.outputs.appInsightsId : ''
#disable-next-line BCP318
output keyVaultUri string = enableKeyVault ? keyVaultModule.outputs.keyVaultUri : ''
#disable-next-line BCP318
output storageAccountId string = enableStorage ? storageModule.outputs.storageAccountId : ''

#disable-next-line BCP318
output foundryEndpoint string = foundryProvision ? foundryModule.outputs.foundryEndpoint : ''
#disable-next-line BCP318
output foundryAccountId string = foundryProvision ? foundryModule.outputs.foundryAccountId : ''

// Surfaced for pipeline CLI steps
#disable-next-line BCP318
output foundryAccountName string = foundryProvision ? foundryModule.outputs.foundryAccountName : ''
output foundryProjectName string = foundryProjectName
output foundryEnabled bool = foundryEnabled
output rgName string = rgName

/*
  main.ai.bicep
  =====================================================================
  Subscription-scope orchestrator for OpsCopilot AI subscription (SubB).

  Resource Group:  rg-opscopilot-ai-{env}-{region}
  Subscription:    bd27a79c-de25-4097-a874-3bb35f2b926a

  Resources:
    - Log Analytics Workspace         (optional — enableLaw)
    - Application Insights            (optional — enableAppInsights; requires LAW)
    - Key Vault (Standard, RBAC)
    - Azure OpenAI account            (always created; NO model deployments in Bicep)
    - Azure AI Search                 (optional — searchProvision; default OFF)
    - Budget (£80 default)

  IMPORTANT — Model Deployments:
    Azure OpenAI model deployments are intentionally NOT managed in Bicep.
    The pipeline passes deploymentsEnabled=true to trigger CLI-based deployment
    via `az cognitiveservices account deployment create`.
    This avoids quota/capacity errors during what-if and keeps the Bicep idempotent.

  IMPORTANT — AI Search:
    searchProvision defaults to false (Free SKU = £0, Basic = ~£65/month).
    Set searchProvision=true AND searchSku=basic (or higher) for production.

  Refer to azure-infra/README.md for full toggle documentation.
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
@description('Name of the AI resource group.')
param rgName string = 'rg-opscopilot-ai-${environment}-uks'

// ── Observability (optional) ──────────────────────────────────────────────────
@description('Deploy a Log Analytics Workspace in the AI subscription.')
param enableLaw bool = true

@description('Log Analytics Workspace name.')
param lawName string = 'law-opscopilot-ai-${environment}-uks'

@description('Deploy Application Insights (requires enableLaw=true).')
param enableAppInsights bool = true

@description('Application Insights name.')
param appInsightsName string = 'appi-opscopilot-ai-${environment}-uks'

// ── Key Vault ─────────────────────────────────────────────────────────────────
@description('Key Vault name (3–24 chars, globally unique).')
param keyVaultName string = 'kv-opsctai${environment}'

// ── Azure OpenAI ──────────────────────────────────────────────────────────────
@description('Azure OpenAI account name.')
param aoaiName string = 'aoai-opscopilot-ai-${environment}-uks'

@description('''
  When true the pipeline will run az cognitiveservices account deployment create
  after this Bicep template. Has no effect on the Bicep itself — surfaced here
  purely as a discoverable output for the workflow to branch on.
''')
param deploymentsEnabled bool = false

// ── Azure AI Search ───────────────────────────────────────────────────────────
@description('Set to true to provision an Azure AI Search instance. Default false (cost gate).')
param searchProvision bool = false

@description('Azure AI Search resource name.')
param searchName string = 'srch-opscopilot-ai-${environment}-uks'

@description('AI Search SKU. free = £0 (1 index, 50MB); basic = ~£65/month.')
@allowed(['free', 'basic', 'standard', 'standard2', 'standard3'])
param searchSku string = 'free'

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
  subscription: 'ai'
  managedBy: 'bicep'
}, extraTags)

// ── Resource Group ────────────────────────────────────────────────────────────
module rg 'modules/rg.bicep' = {
  name: 'deploy-rg-ai'
  params: {
    rgName: rgName
    location: location
    tags: baseTags
  }
}

// ── Log Analytics Workspace ───────────────────────────────────────────────────
module law 'modules/logAnalytics.bicep' = if (enableLaw) {
  name: 'deploy-law-ai'
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
module appInsights 'modules/appInsights.bicep' = if (enableLaw && enableAppInsights) {
  name: 'deploy-appi-ai'
  scope: resourceGroup(rgName)
  params: {
    appInsightsName: appInsightsName
    location: location
    #disable-next-line BCP318
    lawResourceId: enableLaw ? law.outputs.workspaceId : ''
    tags: baseTags
  }
}

// ── Key Vault ─────────────────────────────────────────────────────────────────
module kv 'modules/keyVault.bicep' = {
  name: 'deploy-kv-ai'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    keyVaultName: keyVaultName
    location: location
    tags: baseTags
    deployerObjectId: deployerObjectId
  }
}

// ── Azure OpenAI ──────────────────────────────────────────────────────────────
module aoai 'modules/aoai.bicep' = {
  name: 'deploy-aoai'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    aoaiName: aoaiName
    location: location
    tags: baseTags
  }
}

// ── Azure AI Search ───────────────────────────────────────────────────────────
module search 'modules/search.bicep' = if (searchProvision) {
  name: 'deploy-search'
  scope: resourceGroup(rgName)
  dependsOn: [rg]
  params: {
    searchName: searchName
    location: location
    searchSku: searchSku
    tags: baseTags
  }
}

// ── Budget ────────────────────────────────────────────────────────────────────
module budget 'modules/budget.bicep' = {
  name: 'deploy-budget-ai'
  params: {
    budgetName: 'budget-opscopilot-ai-${environment}'
    budgetAmount: budgetAmount
    budgetEmails: budgetEmails
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
output resourceGroupName string = rgName
output keyVaultId string = kv.outputs.keyVaultId
output keyVaultUri string = kv.outputs.keyVaultUri
output aoaiEndpoint string = aoai.outputs.aoaiEndpoint
output aoaiAccountName string = aoai.outputs.aoaiAccountName
output deploymentsEnabled bool = deploymentsEnabled

#disable-next-line BCP318
output lawId string = enableLaw ? law.outputs.workspaceId : ''

#disable-next-line BCP318
output appInsightsId string = (enableLaw && enableAppInsights) ? appInsights.outputs.appInsightsId : ''

#disable-next-line BCP318
output searchEndpoint string = searchProvision ? search.outputs.searchEndpoint : ''

#disable-next-line BCP318
output searchName string = searchProvision ? search.outputs.searchName : ''

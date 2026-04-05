// Resource-group scope is declared explicitly so this module can be called from
// the subscription-scoped main.ai.bicep using `scope: resourceGroup(rgName)`.
targetScope = 'resourceGroup'

/*
  aoaiRbac.bicep
  =====================================================================
  Resource-group-scoped module. Assigns the 'Cognitive Services OpenAI User'
  built-in role to the system-assigned managed identities of the three
  platform Container Apps so they can authenticate to Azure OpenAI via
  DefaultAzureCredential (Managed Identity) without any API key.

  Role assignments created (when principal ID is non-empty):
    ┌─────────────────────────────────┬───────────┬───────────┬──────────┐
    │ Role                            │ apihost   │ workerhost│ mcphost  │
    ├─────────────────────────────────┼───────────┼───────────┼──────────┤
    │ Cognitive Services OpenAI User  │    ✓      │    ✓      │    ✓     │
    └─────────────────────────────────┴───────────┴───────────┴──────────┘

  Built-in role definition GUID (stable across all Azure tenants):
    Cognitive Services OpenAI User: 5e0bd9bd-7b93-4f28-af87-19fc36ad61bd

  Cross-subscription deployment:
    This module targets the AI subscription resource group (rg-opscopilot-ai-*).
    The principal IDs originate from the platform subscription Container Apps.
    Pass them as outputs from the platform deployment:
      platformApiHostPrincipalId    = main.platform.bicep → output apiHostPrincipalId
      platformWorkerHostPrincipalId = main.platform.bicep → output workerHostPrincipalId
      platformMcpHostPrincipalId    = main.platform.bicep → output mcpHostPrincipalId

  Role assignment names use deterministic GUIDs:
    guid(aoaiAccountId, principalId, roleDefinitionId)
  This ensures idempotent re-deploys never fail with "already exists".
  =====================================================================
*/

@description('Name of the Azure OpenAI account in this resource group.')
param aoaiAccountName string

@description('Principal ID of the platform apihost Container App system-assigned managed identity. Leave empty to skip.')
param apiHostPrincipalId string = ''

@description('Principal ID of the platform workerhost Container App system-assigned managed identity. Leave empty to skip.')
param workerHostPrincipalId string = ''

@description('Principal ID of the platform mcphost Container App system-assigned managed identity. Leave empty to skip.')
param mcpHostPrincipalId string = ''

// ── Built-in role definition ID ───────────────────────────────────────────────
var cogServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

// Built-in roles live at the tenant root, not per-subscription.
// Using the /providers/... path (tenant-scoped) avoids "RoleDefinitionDoesNotExist"
// errors that occur when subscriptionResourceId() is used in resource-group-scoped modules.
var cogServicesOpenAiUserRoleDefId = '/providers/Microsoft.Authorization/roleDefinitions/${cogServicesOpenAiUserRoleId}'

// ── Existing AOAI resource reference ──────────────────────────────────────────
resource aoai 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' existing = {
  name: aoaiAccountName
}

// ── Cognitive Services OpenAI User — apihost ──────────────────────────────────
resource aoaiApiHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(apiHostPrincipalId)) {
  name: guid(aoai.id, apiHostPrincipalId, cogServicesOpenAiUserRoleId)
  scope: aoai
  properties: {
    roleDefinitionId: cogServicesOpenAiUserRoleDefId
    principalId: apiHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Cognitive Services OpenAI User — ca-opscopilot-apihost (system MI)'
  }
}

// ── Cognitive Services OpenAI User — workerhost ───────────────────────────────
resource aoaiWorkerHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(workerHostPrincipalId)) {
  name: guid(aoai.id, workerHostPrincipalId, cogServicesOpenAiUserRoleId)
  scope: aoai
  properties: {
    roleDefinitionId: cogServicesOpenAiUserRoleDefId
    principalId: workerHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Cognitive Services OpenAI User — ca-opscopilot-workerhost (system MI)'
  }
}

// ── Cognitive Services OpenAI User — mcphost ──────────────────────────────────
resource aoaiMcpHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(mcpHostPrincipalId)) {
  name: guid(aoai.id, mcpHostPrincipalId, cogServicesOpenAiUserRoleId)
  scope: aoai
  properties: {
    roleDefinitionId: cogServicesOpenAiUserRoleDefId
    principalId: mcpHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Cognitive Services OpenAI User — ca-opscopilot-mcphost (system MI)'
  }
}

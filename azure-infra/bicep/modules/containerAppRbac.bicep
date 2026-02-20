/*
  containerAppRbac.bicep
  =====================================================================
  Resource-group-scoped module. Assigns built-in roles to the system-
  assigned managed identities of the three platform Container Apps.

  Role assignments created:
    ┌────────────────────────┬───────────┬───────────┬──────────┐
    │ Role                   │ apihost   │ workerhost│ mcphost  │
    ├────────────────────────┼───────────┼───────────┼──────────┤
    │ Key Vault Secrets User │    ✓      │    ✓      │    ✓     │
    │ Log Analytics Reader   │    ✓      │           │    ✓     │
    └────────────────────────┴───────────┴───────────┴──────────┘

  Built-in role definition IDs (stable, no subscription prefix needed):
    Key Vault Secrets User  : 4633458b-17de-408a-b874-0445c86b69e0
    Log Analytics Reader    : 73c42c96-874c-492b-b04d-ab87d138a893

  Role assignment names use deterministic GUIDs:
    guid(scopeResourceId, principalId, roleDefinitionId)
  This ensures idempotent re-deploys never fail with "already exists".

  IMPORTANT: The Key Vault MUST have enableRbacAuthorization = true
  (access policies are not used). This module honours that design.
  =====================================================================
*/

@description('Resource ID of the Key Vault (e.g. kv-opsctpltfdev)')
param kvResourceId string

@description('Resource ID of the Log Analytics Workspace')
param lawResourceId string

@description('Principal ID of the apihost system-assigned managed identity')
param apiHostPrincipalId string

@description('Principal ID of the workerhost system-assigned managed identity')
param workerHostPrincipalId string

@description('Principal ID of the mcphost system-assigned managed identity')
param mcpHostPrincipalId string

// ── Built-in role definition IDs ──────────────────────────────────────────────
var kvSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e0'
var lawReaderRoleId     = '73c42c96-874c-492b-b04d-ab87d138a893'

// ── Existing resource references (required for resource-scoped assignments) ───
resource kv 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: last(split(kvResourceId, '/'))
}

resource law 'Microsoft.OperationalInsights/workspaces@2022-10-01' existing = {
  name: last(split(lawResourceId, '/'))
}

// ── Key Vault Secrets User ────────────────────────────────────────────────────

resource kvApiHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, apiHostPrincipalId, kvSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: apiHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Key Vault Secrets User — ca-opscopilot-apihost (system MI)'
  }
}

resource kvWorkerHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, workerHostPrincipalId, kvSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: workerHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Key Vault Secrets User — ca-opscopilot-workerhost (system MI)'
  }
}

resource kvMcpHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, mcpHostPrincipalId, kvSecretsUserRoleId)
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', kvSecretsUserRoleId)
    principalId: mcpHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Key Vault Secrets User — ca-opscopilot-mcphost (system MI)'
  }
}

// ── Log Analytics Reader ──────────────────────────────────────────────────────
// apihost + mcphost need LAW Reader for telemetry queries.
// workerhost is background-only and does not query LAW directly.

resource lawApiHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(law.id, apiHostPrincipalId, lawReaderRoleId)
  scope: law
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', lawReaderRoleId)
    principalId: apiHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Log Analytics Reader — ca-opscopilot-apihost (system MI)'
  }
}

resource lawMcpHost 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(law.id, mcpHostPrincipalId, lawReaderRoleId)
  scope: law
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', lawReaderRoleId)
    principalId: mcpHostPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Log Analytics Reader — ca-opscopilot-mcphost (system MI)'
  }
}

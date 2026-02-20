/*
  aoai.bicep
  Provisions an Azure OpenAI (Cognitive Services) account.

  Design:
  - The ACCOUNT is always created when this module is deployed.
  - Model DEPLOYMENTS are intentionally NOT created here.
    They are created via Azure CLI in the pipeline only when deploymentsEnabled=true.
  - This split prevents accidental token spend and avoids Bicep resource-type instability
    for model deployment versions.

  SKU: S0 — the only generally available SKU for Azure OpenAI.
       The account itself has NO standing monthly charge.
       Cost is 100% token-consumption based.

  deploymentsEnabled=false (default):
    - Account exists and is reachable.
    - No model deployment created → zero token costs.
    - No RBAC granted to application identity → application cannot call it.

  deploymentsEnabled=true:
    - Pipeline creates gpt-4o-mini deployment (capacity=1, cheapest available model).
    - Pipeline grants 'Cognitive Services OpenAI User' to the app identity.
    - Token cost risk: document per 1M tokens — gpt-4o-mini input ~£0.12, output ~£0.47.

  IMPORTANT: uksouth may have limited Azure OpenAI quota.
  If deployment fails due to quota, switch to eastus or swedencentral in the parameters.
*/
@description('Azure OpenAI account name (globally unique, 2–64 chars)')
param aoaiName string

@description('Azure region. Note: uksouth has limited Azure OpenAI quota.')
param location string

@description('Custom subdomain for the endpoint (lowercase alphanumeric + hyphens, globally unique)')
param customSubDomainName string = aoaiName

@description('Tags to apply')
param tags object

resource aoaiAccount 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: aoaiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'   // Only GA SKU. No standing charge — pay per token only.
  }
  properties: {
    customSubDomainName: customSubDomainName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
    disableLocalAuth: false   // Enable key auth for initial setup; switch to MI when ready
    restore: false
  }
}

output aoaiAccountId string = aoaiAccount.id
output aoaiEndpoint string = aoaiAccount.properties.endpoint
output aoaiAccountName string = aoaiAccount.name

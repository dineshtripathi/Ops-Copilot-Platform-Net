/*
  foundry.bicep
  Provisions an Azure AI Services (Cognitive Services) account for Azure AI Foundry / Azure OpenAI.

  Strategy:
    - The ACCOUNT is always provisioned when foundryProvision=true (IaC-managed).
    - The MODEL DEPLOYMENT and RBAC are handled by the GitHub Actions pipeline (CLI steps),
      only when foundryEnabled=true.
    - This split avoids resource-type instability for model deployment in Bicep
      while keeping the account itself reproducible and IaC-managed.

  SKU: S0 (Standard) — the only generally available tier for Azure OpenAI accounts.
       There is no "Free" tier for Azure OpenAI in production regions.
       Cost is 100% token-consumption based (no standing monthly charge for the account itself).
       Use the smallest/cheapest model (e.g., gpt-4o-mini) and keep token usage minimal.

  IMPORTANT: This module only creates the account. When foundryEnabled=false:
    - No model deployment is created (done via CLI conditionally in pipeline).
    - No RBAC is granted (done via CLI conditionally in pipeline).
    - The application cannot call Foundry even if it has the endpoint URL.
*/
@description('Name of the Azure AI / Cognitive Services account')
param foundryName string

@description('Azure region. Must support Azure OpenAI (e.g., uksouth has limited availability — eastus/swedencentral may be needed).')
param location string

@description('Tags to apply')
param tags object

@description('Custom subdomain name for the endpoint (must be globally unique, lowercase alphanumeric + hyphens)')
param customSubDomainName string = foundryName

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: foundryName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'   // Only generally available SKU for Azure OpenAI. No standing monthly charge.
  }
  properties: {
    customSubDomainName: customSubDomainName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
    // Disable local auth keys when managed identity is adopted
    disableLocalAuth: false
    restore: false
  }
}

output foundryAccountId string = foundryAccount.id
output foundryEndpoint string = foundryAccount.properties.endpoint
output foundryAccountName string = foundryAccount.name

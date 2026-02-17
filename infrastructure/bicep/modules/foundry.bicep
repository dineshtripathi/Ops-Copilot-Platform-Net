/**
 * Azure AI Foundry resources do not yet expose a stable Microsoft.ResourceProvider type for Bicep.
 * This module simply captures the desired configuration so pipelines can decide whether to trigger
 * the CLI-based provisioning flow. Once Foundry supports ARM/Bicep, replace this module with real resources.
 */

@description('Enablement flag that indicates whether the Foundry CLI stage should run.')
param enableFoundry bool = false

@description('Azure AI Foundry account/workspace name (preview).')
param foundryName string = ''

@description('Azure AI Foundry project name to host enterprise engineering assets.')
param projectName string = ''

@description('Model provider metadata used to configure endpoint access (e.g. Azure OpenAI deployment).')
param modelProvider object = {
  endpointName: ''
  modelName: ''
}

output foundryConfig object = {
  enabled: enableFoundry
  name: foundryName
  projectName: projectName
  modelProvider: modelProvider
}

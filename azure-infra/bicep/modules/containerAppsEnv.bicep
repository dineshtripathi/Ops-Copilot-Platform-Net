/*
  containerAppsEnv.bicep
  Provisions an Azure Container Apps Managed Environment (Consumption plan).

  Cost: Consumption plan — no standing monthly charge for the environment itself.
  You pay only for vCPU-seconds and memory GiB-seconds consumed by running replicas.
  With minReplicas=0 (scale-to-zero) the cost is £0 when no requests are processed.

  Estimated active cost reference (uksouth, Feb 2026):
    £0.000012/vCPU-second, £0.0000013/GiB-second
    A 0.25 vCPU / 0.5 GiB app idle 22 hrs/day costs < £1/month.
*/
@description('Container Apps Environment name')
param caeName string

@description('Azure region')
param location string

@description('Log Analytics Workspace resource ID')
param lawResourceId string

@description('Log Analytics Workspace customer ID')
param lawCustomerId string

@description('Whether to use zone redundancy (false keeps cost minimal)')
param zoneRedundant bool = false

@description('Tags to apply')
param tags object

// Retrieve LAW shared key at deploy time (secure — not exposed in outputs)
var lawSharedKey = listKeys(lawResourceId, '2022-10-01').primarySharedKey

resource cae 'Microsoft.App/managedEnvironments@2023-11-02-preview' = {
  name: caeName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: lawCustomerId
        sharedKey: lawSharedKey
      }
    }
    zoneRedundant: zoneRedundant
    // Consumption plan — no workloadProfiles array = Consumption-only
  }
}

output caeId string = cae.id
output caeName string = cae.name
output caeDefaultDomain string = cae.properties.defaultDomain
output caeStaticIp string = cae.properties.staticIp

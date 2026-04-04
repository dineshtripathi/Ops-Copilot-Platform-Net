/*
  containerApp.bicep
  Generic Container App module. Supports ingress and background (no-ingress) workloads.

  Cost: Consumption plan — scale-to-zero by default (minReplicas=0).
  No cost when idle. Cost only accrues for actual request processing time.

  Usage:
    - For API/HTTP apps: set isExternalIngress=true, targetPort to the app port.
    - For background workers: set isExternalIngress=false, targetPort to any value (ignored).
*/
@description('Container App name (must be globally unique within the CAE domain)')
param appName string

@description('Azure region')
param location string

@description('Container Apps Environment resource ID')
param caeId string

@description('Container image. Use a public placeholder for initial provisioning.')
param image string = 'mcr.microsoft.com/azuredocs/containerapps-helloworld:latest'

@description('Port the container listens on')
param targetPort int = 80

@description('Expose as external (internet-facing) ingress. Set false for background workers.')
param isExternalIngress bool = true

@description('Enable ingress at all. Set false for pure background workers with no HTTP endpoint.')
param enableIngress bool = true

@description('CPU allocation per replica (vCPU). 0.25 = minimum for Consumption plan.')
param cpu string = '0.25'

@description('Memory allocation per replica. Must match allowed pairs for cpu value.')
param memory string = '0.5Gi'

@description('Minimum replicas. 0 = scale-to-zero (cost-safe default).')
@minValue(0)
param minReplicas int = 0

@description('Maximum replicas. Keep low in dev to cap costs.')
@minValue(1)
param maxReplicas int = 3

@description('Environment variables to inject into the container.')
param envVars array = []

@description('Enable liveness and readiness HTTP probes. Should be true for all ingress-enabled apps.')
param enableHealthProbes bool = true

@description('HTTP path for the liveness probe. App is restarted after failureThreshold consecutive failures.')
param livenessProbePath string = '/healthz/live'

@description('HTTP path for the readiness probe. App is removed from LB rotation until it returns 200.')
param readinessProbePath string = '/healthz/ready'

@description('Tags to apply')
param tags object

@description('ACR login server (e.g. myacr.azurecr.io). When provided the app pulls images via its system-assigned identity. Leave empty for the public bootstrap placeholder.')
param acrLoginServer string = ''

// ── Container App resource ──────────────────────────────────
resource containerApp 'Microsoft.App/containerApps@2023-11-02-preview' = {
  name: appName
  location: location
  tags: tags
  // System-assigned identity is always enabled so the app can authenticate to
  // Azure services (Key Vault, AOAI, LAW) without secrets. RBAC assignments are
  // created separately in containerAppRbac.bicep via main.platform.bicep.
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: caeId
    configuration: {
      // Allow the system-assigned managed identity to pull images from ACR
      // without storing any credential in the app configuration.
      registries: acrLoginServer != '' ? [
        {
          server: acrLoginServer
          identity: 'system'
        }
      ] : []
      ingress: enableIngress
        ? {
            external: isExternalIngress
            targetPort: targetPort
            allowInsecure: false
            traffic: [
              {
                weight: 100
                latestRevision: true
              }
            ]
          }
        : null
    }
    template: {
      containers: [
        {
          name: appName
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: envVars
          // Health probes are only meaningful for ingress-enabled containers.
          // Workers that have no HTTP endpoint should set enableHealthProbes=false.
          probes: (enableIngress && enableHealthProbes) ? [
            {
              type: 'Liveness'
              httpGet: {
                path: livenessProbePath
                port: targetPort
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: readinessProbePath
                port: targetPort
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 5
              failureThreshold: 3
            }
          ] : []
        }
      ]
      scale: {
        minReplicas: minReplicas
        maxReplicas: maxReplicas
      }
    }
  }
}

output containerAppId string = containerApp.id
output containerAppName string = containerApp.name
output containerAppFqdn string = enableIngress ? containerApp.properties.configuration.ingress.fqdn : ''
output latestRevisionName string = containerApp.properties.latestRevisionName
// principalId of the system-assigned managed identity — used by main.platform.bicep
// to create KV Secrets User and Log Analytics Reader role assignments.
output principalId string = containerApp.identity.principalId

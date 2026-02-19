/*
  tags.bicep
  Returns a consistent tag object. Referenced by all resource modules.
  Every resource deployed by OpsCopilot infrastructure MUST use these tags.
*/
@description('Environment name, e.g. dev / sandbox / prod')
param environment string

@description('Logical tenant label, e.g. TenantA / TenantB')
param tenantLabel string

@description('Resource owner (team or person)')
param owner string = 'platform-team'

@description('Cost centre code for billing')
param costCenter string = 'opscopilot'

@description('Azure region')
param region string

output tags object = {
  product: 'OpsCopilot'
  environment: environment
  tenantLabel: tenantLabel
  owner: owner
  costCenter: costCenter
  managedBy: 'github-oidc'
  region: region
}

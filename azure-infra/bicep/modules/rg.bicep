/*
  rg.bicep
  Creates a resource group at subscription scope.
  targetScope must be set in the calling template (main.bicep).
*/
targetScope = 'subscription'

@description('Name of the resource group to create')
param rgName string

@description('Azure region for the resource group')
param location string

@description('Tags to apply to the resource group')
param tags object

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: rgName
  location: location
  tags: tags
}

output resourceGroupId string = rg.id
output resourceGroupName string = rg.name

@description('Azure location for region-scoped resources.')
param location string

@description('Azure AI Foundry hub workspace name.')
param hubName string

@description('Azure AI Foundry project workspace name.')
param projectName string

@description('Storage account resource ID reused by Azure AI Foundry.')
param storageAccountId string

@description('Key Vault resource ID reused by Azure AI Foundry.')
param keyVaultId string

@description('Application Insights resource ID reused by Azure AI Foundry.')
param applicationInsightsId string

@description('Container Registry resource ID reused by Azure AI Foundry.')
param containerRegistryId string

param tags object

resource foundryHub 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: hubName
  location: location
  kind: 'Hub'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    friendlyName: hubName
    storageAccount: storageAccountId
    keyVault: keyVaultId
    applicationInsights: applicationInsightsId
    containerRegistry: containerRegistryId
    publicNetworkAccess: 'Enabled'
  }
}

resource foundryProject 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: projectName
  location: location
  kind: 'Project'
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    friendlyName: projectName
    hubResourceId: foundryHub.id
    publicNetworkAccess: 'Enabled'
  }
}

output hubName string = foundryHub.name
output hubId string = foundryHub.id
output projectName string = foundryProject.name
output projectId string = foundryProject.id

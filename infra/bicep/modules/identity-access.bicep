targetScope = 'resourceGroup'

@description('Container app principals that need access to shared platform resources.')
param containerAppPrincipals array

@description('Azure Container Registry name.')
param containerRegistryName string

@description('Storage account name.')
param storageAccountName string

@description('Cosmos DB account name.')
param cosmosAccountName string

@description('Key Vault name.')
param keyVaultName string

@description('Azure OpenAI account name.')
param aiAccountName string

var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
var storageBlobDataContributorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
var keyVaultSecretsUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
var cognitiveServicesOpenAiUserRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd')
var cosmosBuiltInDataContributorRoleDefinitionName = '00000000-0000-0000-0000-000000000002'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: storageAccountName
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' existing = {
  name: cosmosAccountName
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource aiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' existing = {
  name: aiAccountName
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: containerRegistryName
}

resource acrPullRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in containerAppPrincipals: {
  name: guid(containerRegistry.id, principal.principalId, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    roleDefinitionId: acrPullRoleDefinitionId
    principalId: principal.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource storageBlobDataRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in containerAppPrincipals: {
  name: guid(storageAccount.id, principal.principalId, storageBlobDataContributorRoleDefinitionId)
  scope: storageAccount
  properties: {
    roleDefinitionId: storageBlobDataContributorRoleDefinitionId
    principalId: principal.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource keyVaultSecretsUserRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in containerAppPrincipals: {
  name: guid(keyVault.id, principal.principalId, keyVaultSecretsUserRoleDefinitionId)
  scope: keyVault
  properties: {
    roleDefinitionId: keyVaultSecretsUserRoleDefinitionId
    principalId: principal.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource aiUserRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principal in containerAppPrincipals: {
  name: guid(aiAccount.id, principal.principalId, cognitiveServicesOpenAiUserRoleDefinitionId)
  scope: aiAccount
  properties: {
    roleDefinitionId: cognitiveServicesOpenAiUserRoleDefinitionId
    principalId: principal.principalId
    principalType: 'ServicePrincipal'
  }
}]

resource cosmosDataContributorRoleAssignments 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-11-15' = [for principal in containerAppPrincipals: {
  name: guid(cosmosAccount.id, principal.principalId, cosmosBuiltInDataContributorRoleDefinitionName)
  parent: cosmosAccount
  properties: {
    principalId: principal.principalId
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosBuiltInDataContributorRoleDefinitionName}'
    scope: cosmosAccount.id
  }
}]

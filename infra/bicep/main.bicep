targetScope = 'resourceGroup'

@description('Short environment name such as dev, staging, or prod.')
param environmentName string = 'dev'

@description('Azure location for region-scoped resources.')
param location string = resourceGroup().location

@description('Prefix used for Azure resource naming.')
param namePrefix string = 'cipmvp'

@description('Container image for the API app placeholder.')
param apiImage string = 'mcr.microsoft.com/dotnet/samples:aspnetapp'

@description('Container image for the worker app placeholder.')
param workerImage string = 'mcr.microsoft.com/dotnet/runtime:10.0'

@description('Application runtime environment exposed inside the containers.')
param applicationEnvironment string = 'Production'

@description('Azure AI embeddings deployment name exposed to the runtime.')
param aiEmbeddingsDeployment string = 'text-embedding-3-large'

@description('Azure AI chat deployment name exposed to the runtime.')
param aiChatDeployment string = 'gpt-4.1-mini'

@description('Cosmos DB operational container name exposed to the app runtime.')
param cosmosOperationalContainerName string = 'operational-vectors'

@description('Document path that stores the operational synopsis embedding vector.')
param operationalVectorPath string = '/payload/synopsisVector'

@description('Embedding dimensions for the operational synopsis vector.')
param operationalVectorDimensions int = 128

@description('Feature flag for API Microsoft Entra authentication. Keep false to preserve the current unauthenticated baseline.')
param entraAuthEnabled bool = false

@description('Microsoft Entra authority exposed to the API runtime, for example https://login.microsoftonline.com/<tenant-id>/v2.0.')
param entraAuthority string = ''

@description('Microsoft Entra audience or app ID URI exposed to the API runtime.')
param entraAudience string = ''

@description('Deploy Azure AI Foundry hub and project workspaces.')
param foundryEnabled bool = true

@description('Azure AI Foundry hub workspace name.')
param foundryHubName string = 'cip-${environmentName}-foundry'

@description('Azure AI Foundry project workspace name.')
param foundryProjectName string = 'cip-${environmentName}-project'

var tags = {
  app: 'cip'
  workload: 'mvp'
  environment: environmentName
}

module dataServices './modules/data-services.bicep' = {
  name: 'dataServices'
  params: {
    environmentName: environmentName
    location: location
    namePrefix: namePrefix
    cosmosOperationalContainerName: cosmosOperationalContainerName
    operationalVectorPath: operationalVectorPath
    operationalVectorDimensions: operationalVectorDimensions
    tags: tags
  }
}

module appPlatform './modules/app-platform.bicep' = {
  name: 'appPlatform'
  params: {
    environmentName: environmentName
    location: location
    namePrefix: namePrefix
    apiImage: apiImage
    workerImage: workerImage
    applicationEnvironment: applicationEnvironment
    cosmosAccountEndpoint: dataServices.outputs.cosmosAccountEndpoint
    cosmosDatabaseName: dataServices.outputs.cosmosDatabaseName
    cosmosEventsContainerName: dataServices.outputs.cosmosEventsContainerName
    cosmosOperationalContainerName: dataServices.outputs.cosmosOperationalContainerName
    cosmosLeasesContainerName: dataServices.outputs.cosmosLeasesContainerName
    storageAccountName: dataServices.outputs.storageAccountName
    rawArtifactsContainerName: dataServices.outputs.rawArtifactsContainerName
    renderedArtifactsContainerName: dataServices.outputs.renderedArtifactsContainerName
    aiEndpoint: dataServices.outputs.aiEndpoint
    aiEmbeddingsDeployment: aiEmbeddingsDeployment
    aiEmbeddingsDimensions: operationalVectorDimensions
    aiChatDeployment: aiChatDeployment
    entraAuthEnabled: entraAuthEnabled
    entraAuthority: entraAuthority
    entraAudience: entraAudience
    tags: tags
  }
}

module identityAccess './modules/identity-access.bicep' = {
  name: 'identityAccess'
  params: {
    containerAppPrincipals: [
      {
        name: appPlatform.outputs.apiContainerAppName
        principalId: appPlatform.outputs.apiPrincipalId
      }
      {
        name: appPlatform.outputs.workerContainerAppName
        principalId: appPlatform.outputs.workerPrincipalId
      }
    ]
    containerRegistryName: appPlatform.outputs.containerRegistryName
    storageAccountName: dataServices.outputs.storageAccountName
    cosmosAccountName: dataServices.outputs.cosmosAccountName
    keyVaultName: dataServices.outputs.keyVaultName
    aiAccountName: dataServices.outputs.aiAccountName
  }
}

module foundryWorkspaces './modules/foundry-workspaces.bicep' = if (foundryEnabled) {
  name: 'foundryWorkspaces'
  params: {
    location: location
    hubName: foundryHubName
    projectName: foundryProjectName
    storageAccountId: dataServices.outputs.storageAccountId
    keyVaultId: dataServices.outputs.keyVaultId
    applicationInsightsId: appPlatform.outputs.applicationInsightsId
    containerRegistryId: appPlatform.outputs.containerRegistryId
    tags: tags
  }
}

output apiContainerAppName string = appPlatform.outputs.apiContainerAppName
output workerContainerAppName string = appPlatform.outputs.workerContainerAppName
output staticWebAppName string = appPlatform.outputs.staticWebAppName
output containerRegistryName string = appPlatform.outputs.containerRegistryName
output containerRegistryId string = appPlatform.outputs.containerRegistryId
output containerRegistryLoginServer string = appPlatform.outputs.containerRegistryLoginServer
output apiImageRepository string = appPlatform.outputs.apiImageRepository
output workerImageRepository string = appPlatform.outputs.workerImageRepository
output apiImageRepositoryPath string = appPlatform.outputs.apiImageRepositoryPath
output workerImageRepositoryPath string = appPlatform.outputs.workerImageRepositoryPath
output cosmosAccountName string = dataServices.outputs.cosmosAccountName
output cosmosAccountEndpoint string = dataServices.outputs.cosmosAccountEndpoint
output cosmosOperationalContainerName string = dataServices.outputs.cosmosOperationalContainerName
output storageAccountName string = dataServices.outputs.storageAccountName
output keyVaultUri string = dataServices.outputs.keyVaultUri
output keyVaultName string = dataServices.outputs.keyVaultName
output aiAccountName string = dataServices.outputs.aiAccountName
output aiEndpoint string = dataServices.outputs.aiEndpoint
output foundryHubName string = foundryEnabled ? foundryWorkspaces.outputs.hubName : ''
output foundryHubId string = foundryEnabled ? foundryWorkspaces.outputs.hubId : ''
output foundryProjectName string = foundryEnabled ? foundryWorkspaces.outputs.projectName : ''
output foundryProjectId string = foundryEnabled ? foundryWorkspaces.outputs.projectId : ''

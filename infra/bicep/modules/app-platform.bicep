@description('Short environment name such as dev, staging, or prod.')
param environmentName string

@description('Azure location for region-scoped resources.')
param location string

@description('Prefix used for Azure resource naming.')
param namePrefix string

@description('Container image for the API app placeholder.')
param apiImage string

@description('Container image for the worker app placeholder.')
param workerImage string

@description('Application runtime environment exposed to the containers.')
param applicationEnvironment string

@description('Cosmos DB account endpoint used by the app runtime.')
param cosmosAccountEndpoint string

@description('Cosmos DB database name used by the app runtime.')
param cosmosDatabaseName string

@description('Cosmos DB events container name used by the app runtime.')
param cosmosEventsContainerName string

@description('Cosmos DB operational container name used by the app runtime.')
param cosmosOperationalContainerName string

@description('Cosmos DB leases container name used by the app runtime.')
param cosmosLeasesContainerName string

@description('Storage account name used by the app runtime.')
param storageAccountName string

@description('Storage container for raw artifacts.')
param rawArtifactsContainerName string

@description('Storage container for rendered artifacts.')
param renderedArtifactsContainerName string

@description('Azure AI endpoint used by the app runtime.')
param aiEndpoint string

@description('Azure AI embeddings deployment name used by the app runtime.')
param aiEmbeddingsDeployment string

@description('Azure AI chat deployment name used by the app runtime.')
param aiChatDeployment string

@description('Feature flag for API Microsoft Entra authentication.')
param entraAuthEnabled bool

@description('Microsoft Entra authority exposed to the API runtime.')
param entraAuthority string

@description('Microsoft Entra audience or app ID URI exposed to the API runtime.')
param entraAudience string

param tags object

var logAnalyticsName = toLower('${namePrefix}-${environmentName}-law')
var appInsightsName = toLower('${namePrefix}-${environmentName}-appi')
var containerAppsEnvName = toLower('${namePrefix}-${environmentName}-cae')
var apiContainerAppName = toLower('${namePrefix}-${environmentName}-api')
var workerContainerAppName = toLower('${namePrefix}-${environmentName}-worker')
var staticWebAppName = toLower('${namePrefix}-${environmentName}-web')
var containerRegistryName = take(replace(toLower('${namePrefix}${environmentName}acr${uniqueString(resourceGroup().id)}'), '-', ''), 50)
var apiImageRepository = 'cip/api'
var workerImageRepository = 'cip/worker'
var staticWebAppAllowedOrigin = 'https://${staticWebApp.properties.defaultHostname}'
var sharedRuntimeEnvironmentVariables = [
  {
    name: 'DOTNET_ENVIRONMENT'
    value: applicationEnvironment
  }
  {
    name: 'CIP_ENVIRONMENT'
    value: environmentName
  }
  {
    name: 'AzureResources__Cosmos__RuntimeMode'
    value: 'Cosmos'
  }
  {
    name: 'AzureResources__Cosmos__AccountEndpoint'
    value: cosmosAccountEndpoint
  }
  {
    name: 'AzureResources__Cosmos__DatabaseName'
    value: cosmosDatabaseName
  }
  {
    name: 'AzureResources__Cosmos__EventsContainer'
    value: cosmosEventsContainerName
  }
  {
    name: 'AzureResources__Cosmos__OperationalContainer'
    value: cosmosOperationalContainerName
  }
  {
    name: 'AzureResources__Cosmos__LeasesContainer'
    value: cosmosLeasesContainerName
  }
  {
    name: 'AzureResources__Storage__AccountName'
    value: storageAccountName
  }
  {
    name: 'AzureResources__Storage__RawArtifactsContainer'
    value: rawArtifactsContainerName
  }
  {
    name: 'AzureResources__Storage__RenderedArtifactsContainer'
    value: renderedArtifactsContainerName
  }
  {
    name: 'AzureResources__AI__Endpoint'
    value: aiEndpoint
  }
  {
    name: 'AzureResources__AI__EmbeddingsDeployment'
    value: aiEmbeddingsDeployment
  }
  {
    name: 'AzureResources__AI__ChatDeployment'
    value: aiChatDeployment
  }
]
var apiRuntimeEnvironmentVariables = concat([
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: applicationEnvironment
  }
  {
    name: 'Cors__AllowedOrigins__0'
    value: staticWebAppAllowedOrigin
  }
  {
    name: 'Entra__Enabled'
    value: string(entraAuthEnabled)
  }
  {
    name: 'Entra__Authority'
    value: entraAuthority
  }
  {
    name: 'Entra__Audience'
    value: entraAudience
  }
], sharedRuntimeEnvironmentVariables)

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2025-02-02-preview' = {
  name: containerAppsEnvName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: containerRegistryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
    networkRuleBypassOptions: 'AzureServices'
  }
}

resource apiContainerApp 'Microsoft.App/containerApps@2025-02-02-preview' = {
  name: apiContainerAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
      }
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'cip-api'
          image: apiImage
          env: apiRuntimeEnvironmentVariables
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 2
      }
    }
  }
}

resource workerContainerApp 'Microsoft.App/containerApps@2025-02-02-preview' = {
  name: workerContainerAppName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      registries: [
        {
          server: containerRegistry.properties.loginServer
          identity: 'system'
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'cip-worker'
          image: workerImage
          env: sharedRuntimeEnvironmentVariables
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource staticWebApp 'Microsoft.Web/staticSites@2024-04-01' = {
  name: staticWebAppName
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Standard'
  }
  properties: {
    stagingEnvironmentPolicy: 'Enabled'
    provider: 'None'
  }
}

output apiContainerAppName string = apiContainerAppName
output workerContainerAppName string = workerContainerAppName
output staticWebAppName string = staticWebAppName
output apiPrincipalId string = apiContainerApp.identity.principalId
output workerPrincipalId string = workerContainerApp.identity.principalId
output applicationInsightsName string = appInsights.name
output applicationInsightsId string = appInsights.id
output containerRegistryName string = containerRegistry.name
output containerRegistryId string = containerRegistry.id
output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output apiImageRepository string = apiImageRepository
output workerImageRepository string = workerImageRepository
output apiImageRepositoryPath string = '${containerRegistry.properties.loginServer}/${apiImageRepository}'
output workerImageRepositoryPath string = '${containerRegistry.properties.loginServer}/${workerImageRepository}'

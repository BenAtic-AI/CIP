@description('Short environment name such as dev, staging, or prod.')
param environmentName string

@description('Azure location for region-scoped resources.')
param location string

@description('Prefix used for Azure resource naming.')
param namePrefix string

param tags object

var storageAccountName = take(replace(toLower('${namePrefix}${environmentName}stg'), '-', ''), 24)
var cosmosAccountName = toLower('${namePrefix}-${environmentName}-cosmos')
var cosmosDatabaseName = 'cip'
var cosmosEventsContainerName = 'events'
var cosmosOperationalContainerName = 'operational'
var cosmosLeasesContainerName = 'leases'
var keyVaultName = '${take(toLower('${namePrefix}-${environmentName}-kv'), 11)}${uniqueString(resourceGroup().id)}'
var aiAccountName = toLower('${namePrefix}-${environmentName}-aoai')
var rawArtifactsContainerName = 'artifacts-raw'
var renderedArtifactsContainerName = 'artifacts-rendered'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  name: 'default'
  parent: storageAccount
}

resource rawArtifactsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: rawArtifactsContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource renderedArtifactsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: renderedArtifactsContainerName
  parent: blobService
  properties: {
    publicAccess: 'None'
  }
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-11-15' = {
  name: cosmosAccountName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    locations: [
      {
        locationName: location
        failoverPriority: 0
      }
    ]
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    enableFreeTier: false
    capabilities: [
      {
        name: 'EnableServerless'
      }
    ]
  }
}

resource cosmosDatabase 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-11-15' = {
  name: cosmosDatabaseName
  parent: cosmosAccount
  properties: {
    resource: {
      id: cosmosDatabaseName
    }
  }
}

resource eventsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  name: cosmosEventsContainerName
  parent: cosmosDatabase
  properties: {
    resource: {
      id: cosmosEventsContainerName
      partitionKey: {
        paths: [
          '/tenantId'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource operationalContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  name: cosmosOperationalContainerName
  parent: cosmosDatabase
  properties: {
    resource: {
      id: cosmosOperationalContainerName
      partitionKey: {
        paths: [
          '/tenantId'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource leasesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-11-15' = {
  name: cosmosLeasesContainerName
  parent: cosmosDatabase
  properties: {
    resource: {
      id: cosmosLeasesContainerName
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
  }
}

resource aiAccount 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: aiAccountName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: aiAccountName
    publicNetworkAccess: 'Enabled'
  }
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
output rawArtifactsContainerName string = rawArtifactsContainer.name
output renderedArtifactsContainerName string = renderedArtifactsContainer.name
output cosmosAccountName string = cosmosAccount.name
output cosmosAccountId string = cosmosAccount.id
output cosmosAccountEndpoint string = cosmosAccount.properties.documentEndpoint
output cosmosDatabaseName string = cosmosDatabase.name
output cosmosEventsContainerName string = eventsContainer.name
output cosmosOperationalContainerName string = operationalContainer.name
output cosmosLeasesContainerName string = leasesContainer.name
output keyVaultName string = keyVault.name
output keyVaultId string = keyVault.id
output keyVaultUri string = keyVault.properties.vaultUri
output aiAccountName string = aiAccount.name
output aiAccountId string = aiAccount.id
output aiEndpoint string = aiAccount.properties.endpoint

// 1. Parameters
param location string = resourceGroup().location
param appName string = 'ca-bookcatalog-api-dev-002'

param sqlAdminUser string = 'islamadmin'
@secure()
param sqlAdminPassword string

// Microsoft Entra (Azure AD) Admin for Azure SQL - Enables GitHub Actions pipeline passwordless migrations
param entraAdminLogin string = 'GitHubActions-Workspace'
param entraAdminObjectId string = '442fdd1f-d6e8-41ad-a04b-33eb55b74b96'

// Container image - defaults to current deployed GHCR image
param containerImage string = 'ghcr.io/islamali-0/workspace-task-api:c3f64331af0cd15f1b00590c9e9b11a97d8cca52'

// Storage account name must be globally unique, 3-24 lowercase alphanumeric chars
param storageAccountName string = 'stbookcatalogdev001'

// 2. Container Apps Managed Environment (Standard Environment / Express)
resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-workspace-standard'
  location: location
  properties: {}
}

// 3. Azure SQL Logical Server
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: 'sqlserver-islam-dev-002'
  location: location
  properties: {
    administratorLogin: sqlAdminUser
    administratorLoginPassword: sqlAdminPassword
  }
}

// 3.1. Azure SQL Entra ID (Azure AD) Administrator for Pipeline Authentication
resource sqlEntraAdmin 'Microsoft.Sql/servers/administrators@2023-05-01-preview' = if (!empty(entraAdminObjectId)) {
  parent: sqlServer
  name: 'ActiveDirectory'
  properties: {
    administratorType: 'ActiveDirectory'
    login: entraAdminLogin
    sid: entraAdminObjectId
    tenantId: subscription().tenantId
  }
}

// 4. SQL Firewall Rule (Allow Azure Services)
resource sqlFirewall 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// 5. Azure SQL Database (Serverless)
resource sqlDb 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: 'sqldb-workspace-api-002'
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
  }
}

// 6. Azure Storage Account (for Blob Storage — book-assets container)
// Standard_LRS: locally-redundant storage, lowest cost, suitable for dev/staging.
// StorageV2 (general-purpose v2) supports all blob tiers and features.
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    // Disable anonymous (unauthenticated) blob access at account level.
    // All access requires either a SAS token or authenticated identity.
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// 6.1. Blob Container: book-assets
// Private access — all reads/writes require a SAS token or Managed Identity.
resource bookAssetsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storageAccountName}/default/book-assets'
  dependsOn: [storageAccount]
  properties: {
    publicAccess: 'None'
  }
}

// 6.2. Convenience: build the connection string from account name + key (used as env-var)
// Using listKeys() at deploy-time avoids a separate "go fetch the key" step.
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'

// 7. Azure Container App (Target deployed by deploy.yml)
// Express environments (Azure for Students) do not support System-Assigned Managed Identity.
// Configuration is injected via environment variables (connection strings).
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'ca-bookcatalog-api-dev-003'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Development'
            }
            {
              name: 'ConnectionStrings__DefaultConnection'
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDb.name};Encrypt=True;TrustServerCertificate=False;Authentication=Active Directory Default;'
            }
            {
              // Storage connection string injected as env-var; ASP.NET Core reads it as
              // IConfiguration["AzureBlobStorage:ConnectionString"] via double-underscore mapping.
              name: 'AzureBlobStorage__ConnectionString'
              value: storageConnectionString
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 3
      }
    }
  }
}

// 8. Azure Key Vault
resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: 'kv-bookcatalog-dev-003'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
  }
}

// 9. Outputs for CI/CD Pipeline & Orchestration
output containerAppName string = containerApp.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDb.name
output keyVaultName string = keyVault.name
output storageAccountName string = storageAccount.name
output storageBlobEndpoint string = storageAccount.properties.primaryEndpoints.blob

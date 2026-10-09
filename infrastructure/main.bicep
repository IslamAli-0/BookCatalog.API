// 1. Parameters
param location string = resourceGroup().location
param appName string = 'ca-bookcatalog-api-dev-002'

param sqlAdminUser string = 'islamadmin'
@secure()
param sqlAdminPassword string

// Temporary public bootstrap image until GitHub Actions pipeline pushes image to GHCR
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

// 2. Container Apps Managed Environment (Brand-new name = Standard Environment)
resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-std-bookcatalog-dev'
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

// 6. Azure Container App (Replaces App Service)
resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: containerAppEnv.id
    configuration: {
      // Note: activeRevisionsMode: 'Multiple' requires a Workload Profile environment,
      // which is not available on Azure for Students subscriptions.
      // Express environments (the only option here) use Single revision mode by default.
      ingress: {
        external: true
        targetPort: 8080
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'bookcatalog-api'
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
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDb.name};Authentication="Active Directory Default";'
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

// 7. Azure Key Vault
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

// 8. Grant the Container App permission to read secrets (Key Vault Secrets User)
resource keyVaultContainerAppRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, containerApp.id, '4633458b-17de-408a-b874-0445c86b69e6')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6'
    )
    principalId: containerApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// 1. Parameters
param location string = resourceGroup().location
param appName string = 'ca-bookcatalog-api-dev-003'

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

// Action Group Email for alerts
param alertEmail string = 'admin@example.com'

// 2. Log Analytics Workspace (30-day retention, 1 GB daily cap to stay within Azure free tier)
resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'law-bookcatalog-dev-001'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

// 2.1. Application Insights (workspace-based)
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-bookcatalog-dev-001'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalyticsWorkspace.id
  }
}

// 2.2. Container Apps Managed Environment (Standard Environment / Express)
resource containerAppEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-workspace-standard'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
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

// 5. Azure SQL Database (Serverless with Auto-Pause)
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
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// 6.1. Blob Container: book-assets
resource bookAssetsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  name: '${storageAccountName}/default/book-assets'
  dependsOn: [storageAccount]
  properties: {
    publicAccess: 'None'
  }
}

// 6.2. Convenience: build the connection string from account name + key (used as env-var)
var storageConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'

// 7. Azure Container App (Target deployed by deploy.yml)
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
              name: 'AzureBlobStorage__ConnectionString'
              value: storageConnectionString
            }
            {
              name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
              value: appInsights.properties.ConnectionString
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 30
              timeoutSeconds: 5
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 15
              periodSeconds: 60
              timeoutSeconds: 10
              failureThreshold: 3
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

// 9. Azure Monitor Action Group for Operational Alerts
resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-bookcatalog-alerts'
  location: 'global'
  properties: {
    groupShortName: 'bc-alerts'
    enabled: true
    emailReceivers: [
      {
        name: 'email-admin'
        emailAddress: alertEmail
        useCommonAlertSchema: true
      }
    ]
  }
}

// 10. Metric Alert 1 — High 5xx Server Error Rate (Symptom-based alert)
// Triggers when failed requests exceed threshold over a 5-minute evaluation window
resource alertHttp5xx 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-bookcatalog-high-5xx'
  location: 'global'
  properties: {
    description: 'Triggers when HTTP 5xx server errors exceed threshold over a 5-minute evaluation window'
    severity: 1
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'High5xxErrors'
          metricName: 'requests/failed'
          operator: 'GreaterThan'
          threshold: 5
          timeAggregation: 'Total'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// 11. Metric Alert 2 — High Latency / Degraded Performance
// Triggers when average response time exceeds 5000 ms (5 seconds) over a 5-minute evaluation window
resource alertHighLatency 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: 'alert-bookcatalog-high-latency'
  location: 'global'
  properties: {
    description: 'Triggers when average response time exceeds 5 seconds over a 5-minute evaluation window'
    severity: 2
    enabled: true
    scopes: [
      appInsights.id
    ]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          name: 'HighResponseTime'
          metricName: 'requests/duration'
          operator: 'GreaterThan'
          threshold: 5000
          timeAggregation: 'Average'
          criterionType: 'StaticThresholdCriterion'
        }
      ]
    }
    actions: [
      {
        actionGroupId: actionGroup.id
      }
    ]
  }
}

// 12. Outputs for CI/CD Pipeline & Orchestration
output containerAppName string = containerApp.name
output containerAppFqdn string = containerApp.properties.configuration.ingress.fqdn
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output sqlDatabaseName string = sqlDb.name
output keyVaultName string = keyVault.name
output storageAccountName string = storageAccount.name
output storageBlobEndpoint string = storageAccount.properties.primaryEndpoints.blob
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsId string = appInsights.id
output logAnalyticsWorkspaceId string = logAnalyticsWorkspace.id

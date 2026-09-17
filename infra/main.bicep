// GridGame - all-free-tier Azure infrastructure.
//
// Deployed by .github/workflows/deploy.yml on every push, so this file (not the portal) is the
// source of truth for the running environment. Everything here is on a free SKU:
//   - App Service plan F1    free forever, 60 CPU-minutes/day, no Always On
//   - Azure SQL GP_S_Gen5_2  free offer: 100k vCore-seconds + 32 GB storage per month
// Nothing secret lives in this file; the two sensitive values arrive as @secure() parameters fed
// from GitHub Actions secrets.

targetScope = 'resourceGroup'

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Short name used as the prefix for every resource. Lowercase letters and digits only.')
@minLength(3)
@maxLength(11)
param appName string = 'gridgame'

@description('SQL Server administrator login name. Not a secret, but not guessable either.')
@minLength(4)
param sqlAdminLogin string = 'gridgameadmin'

@description('SQL Server administrator password. Supplied from the SQL_ADMIN_PASSWORD GitHub secret.')
@secure()
@minLength(16)
param sqlAdminPassword string

@description('HMAC key used to sign JWTs. Supplied from the JWT_SIGNING_KEY GitHub secret.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Access token lifetime, in minutes.')
param accessTokenExpiryMinutes int = 60

@description('Refresh token lifetime, in days.')
param refreshTokenExpiryDays int = 30

@description('Claim the Azure SQL free offer (100,000 vCore-seconds and 32 GB per month, forever). Ten free databases are allowed per subscription; set this to false if that allowance is exhausted and you are willing to pay serverless rates for this one.')
param useSqlFreeLimit bool = true

@description('Request WebSocket support on the web app. SignalR prefers WebSockets and degrades to Server-Sent Events / long polling without them, which still works for a turn-based game. Set to false if the F1 plan rejects the setting in your region.')
param enableWebSockets bool = true

@description('Optional public IPv4 address allowed to reach the SQL server directly, so you can point SSMS or dotnet-ef at it. Leave empty to allow only Azure-hosted services (i.e. the web app).')
param clientIpAddress string = ''

// A deterministic suffix derived from the resource group id: re-running this template against the
// same resource group always lands on the same globally-unique names, so redeploys update in place
// instead of orphaning resources.
var suffix = take(uniqueString(resourceGroup().id), 8)
var planName = '${appName}-plan-${suffix}'
var siteName = '${appName}-${suffix}'
var sqlServerName = '${appName}-sql-${suffix}'
var sqlDatabaseName = 'GridGame'

// 32 GB, the free offer's storage ceiling.
var freeOfferMaxSizeBytes = 34359738368

// Merged into the database's properties rather than set inline, because the API rejects
// freeLimitExhaustionBehavior on a database that isn't claiming the offer - the properties have to be
// absent together, not present-but-null.
var freeOfferProperties = useSqlFreeLimit ? {
  useFreeLimit: true
  freeLimitExhaustionBehavior: 'AutoPause'
} : {}

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

// The all-zeroes range is Azure's sentinel for "any Azure service", which is how the web app reaches
// the database without a VNet - VNet integration is not available on the free App Service plan.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource allowDeveloperMachine 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (!empty(clientIpAddress)) {
  parent: sqlServer
  name: 'AllowDeveloperMachine'
  properties: {
    startIpAddress: clientIpAddress
    endIpAddress: clientIpAddress
  }
}

// The free offer is only available on General Purpose serverless at 2 vCores, so the SKU below is
// less a choice than the shape the offer requires. autoPauseDelay is the minimum 60 minutes, which
// is what keeps idle time from eating the monthly vCore-second allowance.
resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: union({
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    maxSizeBytes: freeOfferMaxSizeBytes
    autoPauseDelay: 60
    minCapacity: json('0.5')
    zoneRedundant: false
    requestedBackupStorageRedundancy: 'Local'
  }, freeOfferProperties)
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'F1'
    tier: 'Free'
  }
  properties: {
    // Linux plans are flagged by `reserved`, not by `kind`.
    reserved: true
  }
}

// A serverless database that has auto-paused takes up to a minute to resume, and the resume request
// itself surfaces as a connection failure. The generous timeout plus retry settings turn that into a
// slow first request rather than a 500.
var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;ConnectRetryCount=12;ConnectRetryInterval=10;'

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: siteName
  location: location
  kind: 'app,linux'
  identity: {
    // Not used yet, but it costs nothing and is the anchor for moving off the SQL password later.
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      // Always On is unavailable on F1; the app cold-starts after roughly 20 minutes idle.
      alwaysOn: false
      webSocketsEnabled: enableWebSockets
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // App Service terminates TLS at the front end and forwards to the worker over plain HTTP.
          // Without this, UseHttpsRedirection() sees a non-HTTPS request and redirect-loops.
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
        {
          // Double underscore is the environment-variable spelling of the ConnectionStrings:GridGame
          // configuration key; the dedicated App Service "connection strings" blade would prefix the
          // name with the provider and not match what Program.cs asks for.
          name: 'ConnectionStrings__GridGame'
          value: sqlConnectionString
        }
        {
          name: 'Jwt__Key'
          value: jwtSigningKey
        }
        {
          name: 'Jwt__AccessTokenExpiryMinutes'
          value: string(accessTokenExpiryMinutes)
        }
        {
          name: 'Jwt__RefreshTokenExpiryDays'
          value: string(refreshTokenExpiryDays)
        }
        {
          // CI publishes an already-built artifact; Oryx must not try to build it again on the worker.
          name: 'SCM_DO_BUILD_DURING_DEPLOYMENT'
          value: 'false'
        }
        {
          // The startup migration retries through a connection string with a 90-second timeout, so a
          // cold serverless database can keep the app from listening for longer than the default
          // 230-second container start budget allows - which would kill the container mid-migration.
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '600'
        }
      ]
    }
  }
  dependsOn: [
    allowAzureServices
  ]
}

@description('Name of the web app, consumed by the deploy job in the GitHub workflow.')
output webAppName string = site.name

@description('Public URL of the deployed game.')
output webAppUrl string = 'https://${site.properties.defaultHostName}'

@description('Fully qualified name of the SQL server, for pointing local tooling at it.')
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Name of the database within that server.')
output sqlDatabaseName string = sqlDatabase.name

@description('Principal id of the web app managed identity, for granting it access to other resources later.')
output webAppPrincipalId string = site.identity.principalId

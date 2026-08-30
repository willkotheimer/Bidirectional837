// PROVENANCE: ADR-031 - the deployment is declared here rather than performed by hand, so what is
// running can be read, reviewed and recreated. Governance Section 1's build order named a Bicep
// deployment from the start; this is it.
//
// One resource and one reference. A single Linux web app serves the React client and the API from
// the same origin, on an App Service plan that already exists and is already paid for - so this
// project adds no recurring cost. The plan is referenced, never created: deleting this deployment
// must not take somebody else's application down with it.

targetScope = 'resourceGroup'

@description('The site name, which becomes the hostname: <name>.azurewebsites.net.')
param siteName string = 'bidirectional837'

@description('Region for the site.')
param location string = resourceGroup().location

@description('Resource group holding the shared App Service plan.')
param sharedPlanResourceGroup string

@description('Name of the shared App Service plan this site runs on.')
param sharedPlanName string

@description('The .NET runtime, matched to Translator.Api.csproj by Governance.Traceability.Tests.')
param dotnetVersion string = '10.0'

// Referenced, not declared. This plan hosts other applications and its lifetime is not ours.
resource sharedPlan 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: sharedPlanName
  scope: resourceGroup(sharedPlanResourceGroup)
}

// PROVENANCE: ADR-032 - one site, serving the client and the API from the same origin.
//
// There is deliberately no CORS block here. ADR-028 withholds the application's grant in Production
// on the grounds that a deployed instance serves the client from its own origin; this topology is
// what makes that true. A Static Web App was the alternative and was rejected: its hostname is
// generated rather than chosen, and on the free plan it cannot proxy to a bring-your-own backend,
// which would have forced the cross-origin grant this shape does not need.
resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: siteName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: sharedPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|${dotnetVersion}'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      http20Enabled: true

      // PROVENANCE: ADR-015 - the claim store is an in-memory SQLite database held for the lifetime
      // of a singleton, so it exists only while the process does. Without this the platform idles
      // the app out and a generated batch disappears between two clicks. It does not make the store
      // durable; it moves the loss from twenty minutes of inactivity to a restart.
      alwaysOn: true

      appSettings: [
        {
          // PROVENANCE: ADR-028 - the application grants CORS only outside Production. That rule is
          // only exercised if the environment is actually set, so it is set here rather than left
          // to a default.
          name: 'ASPNETCORE_ENVIRONMENT'
          value: 'Production'
        }
        {
          // The platform terminates TLS ahead of the app, so the scheme and client address arrive
          // in forwarded headers. Without this the app would believe every request was plain HTTP.
          name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'
          value: 'true'
        }
      ]
    }
  }
}

output siteName string = site.name
output siteUrl string = 'https://${site.properties.defaultHostName}'

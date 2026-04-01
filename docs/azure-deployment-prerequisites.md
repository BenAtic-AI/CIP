# Azure Deployment Prerequisites

This repo is public-facing and secretless by design: checked-in configuration stays non-secret, while deployed workloads are expected to use managed identity, RBAC, and deployment-time resource references instead of committed secrets.

## Required baseline

- an Azure subscription and target resource group
- Azure CLI authenticated with an identity allowed to deploy resources and assign RBAC
- Bicep tooling available through current Azure CLI
- a naming prefix and environment name for the deployment
- Docker available anywhere you plan to build and publish the API and worker images, unless your pipeline builds them in ACR

## What the current baseline deploys

The Bicep baseline provisions the core Azure hosting resources for the MVP:

- Azure Container Registry (ACR)
- Azure Container Apps environment
- API and worker Container Apps
- system-assigned managed identity on both Container Apps
- Azure Storage, Cosmos DB, Key Vault, and Azure AI runtime account
- Azure AI Foundry hub and project workspaces
- Azure Static Web Apps, Application Insights, and Log Analytics

The Container Apps baseline also injects the main runtime environment values for Cosmos DB, Storage, environment name, and Azure AI deployment settings.

## What you still supply per environment

- published backend image references for `apiImage` and `workerImage`
- environment-specific naming values
- GitHub repository variables and secrets used by CI/CD
- Entra app registrations, client IDs, redirect URIs, and audiences if authentication is enabled

## AI platform direction

- Use Azure AI or Azure AI Foundry language in deployment planning and app documentation
- The underlying runtime resource can still be an Azure OpenAI account where Azure requires `kind: 'OpenAI'`
- App configuration should stay provider-neutral where practical, for example `AzureResources:AI`

## Auth baseline

- Entra authentication exists in both the API and frontend
- It stays feature-flagged off by default until configuration is supplied
- Authentication-related app registrations and OIDC configuration are environment-specific and are not fully provisioned by the current Bicep templates
- Keep client IDs, tenant IDs, audiences, and redirect URIs in Azure/GitHub/local configuration rather than in repo-tracked files

## Authentication and secrets policy

- Do not commit secrets, keys, connection strings, certificates, or exported portal files
- Prefer managed identity and `DefaultAzureCredential` for deployed workloads
- Use RBAC-based access to ACR, Storage, Cosmos DB, Key Vault, and Azure AI resources
- If a service still requires a secret, store it in Key Vault and inject it at deployment or runtime without committing it

## Managed identity and RBAC baseline

The current infrastructure assigns the API and worker Container App identities the access they need for the intended secretless runtime model:

- **AcrPull** on ACR so Container Apps can pull private images
- **Storage Blob Data Contributor** on the storage account
- **Cosmos DB built-in data contributor** on the Cosmos account
- **Key Vault Secrets User** on the Key Vault
- **Cognitive Services OpenAI User** on the Azure AI runtime account

This means the main deployment concern is wiring identities, endpoints, deployment names, and image references, rather than storing app secrets in repo-tracked files.

## Container build and publish workflow

The repo includes:

- `src/backend/Cip.Api/Dockerfile`
- `src/backend/Cip.Worker/Dockerfile`
- a root `.dockerignore` for repo-root builds

Recommended deployment flow:

1. deploy the Bicep baseline to create ACR, Container Apps, identities, RBAC, and supporting resources
2. build the API and worker images from the repo root, or let CI build them in ACR
3. tag the images for the ACR repositories provisioned by the baseline:
   - `cip/api`
   - `cip/worker`
4. push those images to ACR
5. set `apiImage` and `workerImage` to the published ACR image tags and deploy or update the environment

Example image tags:

```text
<acr-login-server>/cip/api:<tag>
<acr-login-server>/cip/worker:<tag>
```

Important current limitation:

- the example parameters file still uses placeholder image values
- publishing and referencing real app images is still required for any usable environment

## GitHub Actions deployment baseline

- `.github/workflows/ci.yml` validates backend and frontend changes
- `.github/workflows/deploy-dev.yml` handles a dev-style deployment flow for infra, backend images, and optional Static Web Apps publish
- `deploy-dev` requires GitHub repository variables for `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, and `AZURE_ACR_NAME`
- Static Web Apps deployment is optional and only runs when `AZURE_STATIC_WEB_APPS_API_TOKEN` is configured

## GitHub repository configuration

Required repository variables:

- `AZURE_CLIENT_ID` - client ID for the Azure deploy identity used by GitHub OIDC
- `AZURE_TENANT_ID` - Entra tenant ID
- `AZURE_SUBSCRIPTION_ID` - Azure subscription ID
- `AZURE_RESOURCE_GROUP` - target resource group
- `AZURE_ACR_NAME` - ACR name used by the deployment

Optional repository variables:

- `AZURE_LOCATION`
- `AZURE_NAME_PREFIX`
- `AZURE_API_ENTRA_AUTH_ENABLED` - keep `false` until auth config is ready
- `AZURE_API_ENTRA_AUTHORITY`
- `AZURE_API_ENTRA_AUTH_AUDIENCE` - audience or app ID URI for the API app registration
- `VITE_ENTRA_ENABLED` - keep empty or `false` until frontend auth is ready
- `VITE_ENTRA_TENANT_ID`
- `VITE_ENTRA_CLIENT_ID` - client ID for the SPA app registration
- `VITE_ENTRA_API_SCOPES`
- `VITE_ENTRA_AUTHORITY`
- `VITE_ENTRA_LOGIN_SCOPES`
- `VITE_ENTRA_REDIRECT_URI`

Optional repository secret:

- `AZURE_STATIC_WEB_APPS_API_TOKEN`

## Local-to-cloud configuration guidance

- Keep repo-tracked config limited to non-secret defaults and placeholders
- Use environment variables, `.NET` user secrets, or untracked local override files for developer-specific values
- Treat `.env`, `appsettings.Development.local.json`, and similar files as local only

## Known runtime follow-up risk

- Backend code should continue to prefer `System.Text.Json`
- The backend currently keeps an explicit `Newtonsoft.Json` `13.0.3` dependency as a Cosmos compatibility dependency
- That dependency currently triggers vulnerability warnings and should remain on the follow-up list until the Cosmos/runtime path can be updated

## Before first real deployment

- confirm RBAC assignments for app identities and deployment operators
- confirm AI endpoint, model deployment name, and any content safety dependencies
- confirm Key Vault access model and secret ownership
- confirm the API and worker images have been built and pushed to ACR
- confirm `apiImage` and `workerImage` no longer point at placeholder images
- confirm monitoring, diagnostics, and budget guardrails

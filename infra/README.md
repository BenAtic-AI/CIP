# Infrastructure Baseline

The `infra/bicep` folder contains the current CIP Azure hosting baseline.

## Included resource targets

- Azure Container Registry
- Azure Container Apps environment
- API and worker Container Apps
- Azure Static Web Apps
- Azure Cosmos DB for NoSQL
- Azure Blob Storage
- Azure AI runtime account
- Azure AI Foundry hub workspace
- Azure AI Foundry project workspace
- Azure Key Vault
- Application Insights and Log Analytics

## What the baseline supports today

The templates are intended to provision the main MVP hosting shape, including:

- private image hosting in ACR
- system-assigned managed identities on both Container Apps
- runtime environment values for Cosmos, Storage, environment name, and Azure AI settings
- RBAC assignments so apps can pull images and access Azure dependencies without checked-in secrets
- Azure AI Foundry hub/project resources in source-controlled Bicep
- a new vector-enabled `operational-vectors` Cosmos container for step 1 completion instead of mutating the legacy `operational` container

Applying that container change to an existing environment is a rollout task, not an in-place assumption: migrate or redeploy the environment to the new container shape before expecting native vector search there.

The example parameters still default to placeholder images, so a real deployment still depends on publishing backend images first.

Provisioned image repository paths are designed around:

- `cip/api`
- `cip/worker`

Expected image format:

```text
<acr-login-server>/cip/api:<tag>
<acr-login-server>/cip/worker:<tag>
```

## Auth and deployment notes

- Entra authentication exists in both the API and frontend, but stays feature-flagged off until configured
- App registrations, client IDs, redirect URIs, and OIDC federated credentials remain environment-specific and should be created/configured outside source control
- `.github/workflows/deploy-dev.yml` expects GitHub repository variables for Azure deployment and can optionally deploy Static Web Apps when `AZURE_STATIC_WEB_APPS_API_TOKEN` is available

## Managed identity and RBAC

The API and worker Container Apps receive system-assigned identities. The baseline assigns access for:

- ACR image pull
- Blob Storage data access
- Cosmos DB data access
- Key Vault secret reads
- Azure AI runtime access

This supports the intended secretless runtime model: identities + RBAC + non-secret environment values from infrastructure.

## Security posture

- Do not commit secrets, keys, connection strings, or copied portal values
- Prefer managed identity plus `DefaultAzureCredential` for deployed workloads
- Use Key Vault for deployed secret storage when a dependency still requires a secret
- For local development, use environment variables, `.NET` user secrets, or untracked local override files only

## Build and publish note

Backend containers are built from the repo root using:

- `src/backend/Cip.Api/Dockerfile`
- `src/backend/Cip.Worker/Dockerfile`

Use the root `.dockerignore`, publish the resulting images to ACR, then deploy or update the Container Apps to those tags.

## AI naming note

- App and documentation language should use Azure AI or Azure AI Foundry
- The underlying runtime resource may still be an Azure OpenAI account where Azure requires `kind: 'OpenAI'`
- Infra outputs use neutral naming such as `aiAccountName` to avoid binding the app contract to a provider-specific term

## Known follow-up risk

- The backend currently carries an explicit `Newtonsoft.Json` `13.0.3` dependency as a Cosmos compatibility dependency
- Application serialization still stays on `System.Text.Json`
- Treat the current package vulnerability warning as a follow-up item rather than infrastructure behavior

## See also

- `docs/azure-deployment-prerequisites.md`
- `docs/local-development.md`

# Local Development

## Defaults

- API: `http://localhost:5180`
- Web: `http://localhost:5173`
- Worker: local background process with no public HTTP endpoint

## Initial setup

```powershell
npm ci
dotnet restore CIP.slnx
```

## Backend

Run the API and worker in separate terminals:

```powershell
dotnet run --project src/backend/Cip.Api
dotnet run --project src/backend/Cip.Worker
```

For day-to-day development, `dotnet run` is still the fastest loop. The repo also includes Dockerfiles for both backend services so the same projects can be built into Azure-ready images when you are preparing a deployment.

Current vector-assisted search is exposed at `POST /api/profiles/search`. It uses stored profile synopsis vectors, with app-side ranking unless the Cosmos runtime and native vector search path are available.

Optional local image builds from the repo root:

```powershell
docker build -f src/backend/Cip.Api/Dockerfile -t cip-api:dev .
docker build -f src/backend/Cip.Worker/Dockerfile -t cip-worker:dev .
```

The root `.dockerignore` excludes local-only files and non-runtime folders such as `docs/`, `infra/`, `tests/`, and local secret files from the image build context.

## Configuration placeholders

The repo includes configuration sections for:

- Cosmos DB
- Blob Storage
- Azure AI
- Entra ID

No live Azure credentials or fixed environment IDs are checked in.

Deterministic fallback embeddings remain the default local behavior, so profile search and identity-resolution flows still work without external AI access.

Set `AzureResources:AI:UseLiveEmbeddings=true` to activate live Azure AI embeddings when the endpoint and embeddings deployment are configured.

## Auth baseline

- Entra authentication exists in both the API and frontend
- Local repo defaults keep it feature-flagged off, so local API requests continue without bearer tokens until you configure it
- Live dev is different: Entra auth is enabled, API health stays anonymous, and protected endpoints return `401 Unauthorized` without a token
- The current dev frontend deployment was published with Entra config enabled
- Use app registrations and client IDs for your own environment when enabling auth locally
- Do not commit tenant IDs, client IDs, audiences, or redirect URIs as fixed values unless they are intentionally public-safe placeholders

## No-secrets policy

- Never commit secrets, keys, connection strings, `.env` files, publish profiles, certificates, or portal exports
- Prefer `DefaultAzureCredential` so local development and deployed workloads can share the same authentication flow
- In Azure, the runtime direction is managed identity first
- If the current AI runtime uses an Azure OpenAI resource, treat it as the underlying Azure AI / Azure AI Foundry runtime and keep secrets out of source control

## Local configuration patterns

Use one of these local-only options for real values:

1. environment variables in your shell or an untracked local `.env` file
2. `.NET` user secrets for backend projects
3. untracked local override files such as `appsettings.Development.local.json`

Example user-secrets flow for the API:

```powershell
dotnet user-secrets init --project src/backend/Cip.Api
dotnet user-secrets set "AzureResources:AI:Endpoint" "https://<your-ai-resource>.openai.azure.com/" --project src/backend/Cip.Api
dotnet user-secrets set "AzureResources:AI:ChatDeployment" "<chat-deployment-name>" --project src/backend/Cip.Api
dotnet user-secrets set "AzureResources:AI:EmbeddingsDeployment" "<embeddings-deployment-name>" --project src/backend/Cip.Api
```

Environment variable examples:

```powershell
$env:AzureResources__AI__Endpoint = "https://<your-ai-resource>.openai.azure.com/"
$env:AzureResources__AI__ChatDeployment = "<chat-deployment-name>"
$env:AzureResources__AI__EmbeddingsDeployment = "<embeddings-deployment-name>"
```

Do not store API keys in repo-tracked config. If a local integration still needs a secret, keep it in user secrets, your shell environment, or Key Vault-backed development tooling.

## Frontend

Run the web app from the repo root:

```powershell
npm run dev:web
```

Useful validation commands:

```powershell
npm run typecheck:web
npm run build:web
dotnet test CIP.slnx
```

## CORS and browser calls

This setup is meant to avoid common local browser issues:

- the Vite dev server proxies `/api` to the backend
- the API explicitly allows `http://localhost:5173` in local development

If you switch to a direct frontend-to-API URL with `VITE_API_BASE_URL`, keep that origin in sync with API CORS settings.

## Azure access direction

- Local: developer login, environment variables, or user secrets feed `DefaultAzureCredential`
- Deployed: the API and worker Container Apps use system-assigned managed identities
- Deployed: Bicep assigns RBAC so those identities can pull from ACR and access Storage, Cosmos DB, Key Vault, and Azure AI without checked-in secrets
- Deployment config should reference resource names, endpoints, deployment names, and image tags, not committed secrets

## CI/CD note

- `.github/workflows/ci.yml` covers backend validation and frontend typecheck/build
- `.github/workflows/deploy-dev.yml` supports a dev-style deployment flow with GitHub OIDC variables
- Static Web Apps deployment from that workflow is optional and depends on `AZURE_STATIC_WEB_APPS_API_TOKEN`
- GitHub Actions repository variables and secrets still need manual setup in the GitHub UI

## Cloud build handoff

When you move from local development to Azure deployment:

1. build the API and worker images from the repo root
2. tag them for the provisioned ACR repositories (`cip/api` and `cip/worker`)
3. push them to ACR
4. deploy or update the Container Apps to use those real images instead of the placeholder defaults

The infrastructure baseline already provisions ACR, Container Apps, managed identities, and RBAC.

## Current limitation to remember

If you deploy the example parameters as-is, the API and worker image values still point at placeholder images. Replace them with published CIP images before treating the environment as usable.

## Known runtime follow-up risk

- The backend currently keeps an explicit `Newtonsoft.Json` `13.0.3` dependency as a Cosmos compatibility dependency
- Backend/application serialization still stays on `System.Text.Json`
- Treat the current vulnerability warning as a follow-up item when validating local or deployed package health

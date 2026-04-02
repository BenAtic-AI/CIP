# CIP

[![CI](https://github.com/BenAtic-AI/CIP/actions/workflows/ci.yml/badge.svg)](https://github.com/BenAtic-AI/CIP/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/BenAtic-AI/CIP/blob/main/LICENSE)

CIP is an Azure-first customer intelligence MVP monorepo. It includes a .NET API, a background worker, a React web app, and Bicep templates for deploying the current Azure hosting baseline.

The repository is intended to be public-facing and secretless: checked-in configuration stays non-secret, while environment-specific values are supplied through local configuration, GitHub variables/secrets, or Azure-managed identity and RBAC.

## Status

- Active MVP codebase with a working Azure deployment baseline
- Step-1 MVP slice is source-complete: profile-synopsis embeddings, the live Azure AI embeddings path with deterministic fallback, Cosmos-native vector search capability, and `POST /api/profiles/search` are in source
- Existing environments still need migration or redeployment to the vector-enabled Cosmos container before that native vector path is fully rolled out there
- Live dev now has Entra auth enabled for the API and frontend
- API health stays anonymous in dev, while protected endpoints return `401 Unauthorized` without a bearer token
- Repo-tracked defaults still keep Entra feature flags off until environment variables are supplied
- Infrastructure examples still require real container image references before a new environment is usable

## What is included

- **Backend**: modular monolith on **.NET 10** with separate API and worker services
- **Frontend**: **React + Vite + Tailwind** web app
- **Infrastructure**: Azure-ready **Bicep** for ACR, Container Apps, Storage, Cosmos DB, Key Vault, Azure AI runtime, Azure AI Foundry hub/project, Static Web Apps, and monitoring
- **Containers**: Dockerfiles for `Cip.Api` and `Cip.Worker`
- **CI/CD**: GitHub Actions workflows for validation and deployment
- **GitHub hygiene**: issue templates and a pull request template for public repo contributions
- **Docs**: local setup, deployment prerequisites, and architecture notes

## Repository layout

```text
.
├─ docs/
│  ├─ architecture/
│  │  ├─ azure-first-mvp.md
│  │  └─ solution-structure.md
│  ├─ azure-deployment-prerequisites.md
│  └─ local-development.md
├─ infra/
│  ├─ README.md
│  └─ bicep/
├─ src/
│  ├─ backend/
│  │  ├─ Cip.Api/
│  │  ├─ Cip.Worker/
│  │  ├─ Cip.Application/
│  │  ├─ Cip.Domain/
│  │  ├─ Cip.Contracts/
│  │  ├─ Cip.Infrastructure/
│  │  ├─ Integrations.Cosmos/
│  │  ├─ Integrations.Storage/
│  │  └─ Integrations.AzureAi/
│  └─ web/
│     └─ cip-web/
└─ tests/
   ├─ Cip.UnitTests/
   ├─ Cip.IntegrationTests/
   ├─ Cip.ApiTests/
   └─ cip-web-e2e/
```

## Getting started

### Prerequisites

- .NET SDK 10.0.x
- Node 23.x
- npm 10.x+

### Install and restore

```powershell
npm ci
dotnet restore CIP.slnx
```

### Run the services

```powershell
dotnet run --project src/backend/Cip.Api
dotnet run --project src/backend/Cip.Worker
npm run dev:web
```

Local defaults:

- API: `http://localhost:5180`
- Web: `http://localhost:5173`
- Worker: background process only

### Validate changes

```powershell
dotnet test CIP.slnx
npm run typecheck:web
npm run build:web
```

## Deployment overview

- `infra/bicep` provisions the current Azure baseline for container hosting, storage, Cosmos DB, Key Vault, Azure AI resources, Static Web Apps, and monitoring
- Backend images are built from the repo root using `src/backend/Cip.Api/Dockerfile` and `src/backend/Cip.Worker/Dockerfile`
- Example image paths follow:

```text
<acr-login-server>/cip/api:<tag>
<acr-login-server>/cip/worker:<tag>
```

- The example Bicep parameters file still uses placeholder `apiImage` and `workerImage` values, so you must replace them with real published image tags for any usable deployment
- Tenant-specific app registrations, client IDs, redirect URIs, and GitHub OIDC settings are intentionally not committed

## Security and configuration

- Do **not** commit secrets, keys, connection strings, `.env` files, or exported portal values
- Prefer **managed identity** and `DefaultAzureCredential` for deployed Azure access
- Use environment variables, `.NET` user secrets, or untracked local override files for local development
- GitHub Actions repository variables and secrets still need manual setup in the GitHub UI
- Backend JSON serialization should remain on `System.Text.Json`

## Known follow-up

- The backend currently carries an explicit `Newtonsoft.Json` `13.0.3` dependency for Cosmos compatibility
- Application serialization still stays on `System.Text.Json`
- Treat the package vulnerability warning as a follow-up item, not as documented behavior to build around

## More documentation

- `docs/local-development.md`
- `docs/azure-deployment-prerequisites.md`
- `docs/architecture/solution-structure.md`
- `infra/README.md`

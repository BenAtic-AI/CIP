# CIP MVP Solution Structure

This scaffold follows the Azure-first MVP direction and keeps the backend as a modular monolith.

## Backend projects

- `Cip.Api` - HTTP API surface for operators and integrations
- `Cip.Worker` - change-feed, enrichment, and scheduled processing host
- `Cip.Application` - application services and use-case orchestration
- `Cip.Domain` - core document and domain models
- `Cip.Contracts` - shared contracts and reusable constants
- `Cip.Infrastructure` - app-level composition over integration libraries
- `Integrations.Cosmos` - Cosmos-specific wiring placeholder
- `Integrations.Storage` - Blob Storage wiring placeholder
- `Integrations.AzureAi` - Azure AI wiring placeholder; runtime may still target an Azure OpenAI resource

## Test layout

- `Cip.UnitTests` - fast unit tests for application/domain logic
- `Cip.IntegrationTests` - composition and integration boundary tests
- `Cip.ApiTests` - API host and HTTP endpoint tests
- `cip-web-e2e` - reserved for future Playwright coverage

## Implementation assumptions used for the scaffold

- internal operator auth with Microsoft Entra ID
- `/tenantId` partitioning for MVP
- one React SPA, one API, one worker
- Azure-first infrastructure with no deployment executed from this task

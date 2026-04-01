# Contributing

Thanks for your interest in CIP.

## Before you open a PR

- Keep changes focused and implementation-true
- Update docs when behavior, commands, or configuration change
- Do not commit secrets, keys, connection strings, `.env` files, or exported portal values
- Keep public-facing docs generic; avoid hardcoding live environment names, hostnames, client IDs, or other tenant-specific values

## Local setup

Prerequisites:

- .NET SDK 10.0.x
- Node 23.x
- npm 10.x+

Install dependencies:

```powershell
npm ci
dotnet restore CIP.slnx
```

Run the app locally:

```powershell
dotnet run --project src/backend/Cip.Api
dotnet run --project src/backend/Cip.Worker
npm run dev:web
```

Default local URLs:

- API: `http://localhost:5180`
- Web: `http://localhost:5173`

## Validation

Run the checks that match your change:

```powershell
dotnet test CIP.slnx
npm run typecheck:web
npm run build:web
```

## Pull request guidance

- Explain the problem being solved and any user-visible impact
- Call out configuration, deployment, or migration implications
- Add or update tests when behavior changes
- Keep docs aligned with the current implementation

## Configuration and security

- Prefer managed identity and `DefaultAzureCredential` for deployed Azure access
- Use environment variables, `.NET` user secrets, or untracked local override files for local-only values
- If a dependency still needs a secret, keep it out of source control and use an appropriate secret store

## Questions or larger changes

If you plan to make a larger architectural or workflow change, open an issue or start a discussion first so the approach can be aligned before implementation.

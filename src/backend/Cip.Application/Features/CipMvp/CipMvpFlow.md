# CIP MVP runtime flow

This slice keeps the active runtime store in memory while preserving repository boundaries for a later Cosmos-backed implementation.

## Event ingestion

1. `POST /api/events` validates the event contract and enforces idempotency by `tenantId + eventId`.
2. The service resolves an existing profile by approved identity match or creates a new profile shell.
3. The event materializes into a pending ChangeSet that captures proposed identities, traits, and reviewer-facing operations.

## Approval flow

1. Reviewers approve or reject a ChangeSet through dedicated endpoints.
2. Approval applies proposed identities and traits to the profile and marks the source event as applied.
3. Rejection preserves the existing approved profile state and marks the source event as rejected.

## Trigger execution

1. Trigger definitions are stored in the same runtime repository.
2. Trigger runs evaluate approved profile identities and traits with simple exact or contains matching.
3. Worker heartbeats report repository counts so local demos can show current backend processing status.

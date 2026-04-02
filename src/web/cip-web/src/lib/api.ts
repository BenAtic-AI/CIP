import { acquireAccessToken } from '../auth/client'
import { entraAuth } from '../auth/config'

export type HealthResponse = {
  service: string
  environment: string
  utcTime: string
  modules: string[]
}

export type IdentityDto = {
  type: string
  value: string
  source: string
}

export type TraitDto = {
  name: string
  value: string
  confidence: number
}

export type IngestEventRequest = {
  tenantId: string
  eventId: string
  eventType: string
  source: string
  occurredAt: string
  identities: IdentityDto[]
  traits: TraitDto[]
  schemaVersion: number
}

export type IngestEventResponse = {
  tenantId: string
  eventId: string
  profileId: string
  changeSetId: string
  accepted: boolean
  duplicate: boolean
  processingState: string
}

export type ProfileResponse = {
  tenantId: string
  profileId: string
  status: string
  profileCard: string
  synopsis: string
  identities: IdentityDto[]
  traits: TraitDto[]
  pendingChangeSetCount: number
  createdAt: string
  updatedAt: string
}

export type ChangeSetResponse = {
  tenantId: string
  changeSetId: string
  targetProfileId: string
  type: string
  status: string
  proposedOperations: string[]
  proposedIdentities: IdentityDto[]
  proposedTraits: TraitDto[]
  evidenceReferences: string[]
  proposedAt: string
  reviewedAt: string | null
  reviewedBy: string | null
  reviewComment: string | null
  sourceEventId: string
}

export type ReviewChangeSetRequest = {
  tenantId: string
  reviewedBy: string
  comment: string | null
}

export type TriggerConditionRequest = {
  operator: string
  attribute: string
  value: string
}

export type TriggerConditionResponse = TriggerConditionRequest

export type TriggerDefinitionRequest = {
  tenantId: string
  name: string
  description: string | null
  conditions: TriggerConditionRequest[]
}

export type TriggerDefinitionResponse = {
  tenantId: string
  triggerId: string
  name: string
  description: string | null
  status: string
  conditions: TriggerConditionResponse[]
  createdAt: string
  lastRunAt: string | null
}

export type RunTriggerResponse = {
  tenantId: string
  triggerId: string
  matchedProfileCount: number
  matchedProfiles: ProfileResponse[]
  executedAt: string
}

type ProblemDetails = {
  title?: string
  detail?: string
}

type RequestOptions = {
  method?: 'GET' | 'POST'
  query?: Record<string, string | undefined>
  body?: unknown
}

const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, '')

export class ApiError extends Error {
  status?: number

  constructor(message: string, status?: number) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

function buildUrl(path: string, query?: Record<string, string | undefined>) {
  const searchParams = new URLSearchParams()

  Object.entries(query ?? {}).forEach(([key, value]) => {
    if (value) {
      searchParams.set(key, value)
    }
  })

  const queryString = searchParams.toString()
  const resolvedPath = queryString ? `${path}?${queryString}` : path

  return configuredBaseUrl ? `${configuredBaseUrl}${resolvedPath}` : resolvedPath
}

async function readErrorMessage(response: Response) {
  const contentType = response.headers.get('content-type') || ''

  if (contentType.includes('application/json')) {
    const problem = (await response.json().catch(() => null)) as ProblemDetails | null
    if (problem?.title || problem?.detail) {
      return problem.title ?? problem.detail ?? `Request failed with ${response.status}`
    }
  }

  const fallbackText = await response.text().catch(() => '')
  return fallbackText || `Request failed with ${response.status}`
}

function networkFailureMessage() {
  const apiTarget = configuredBaseUrl ?? 'the Vite /api proxy'
  const authNote = entraAuth.ready
    ? ' When auth is enabled, the Authorization header also requires the backend to allow CORS preflight (OPTIONS).'
    : ''

  return `Unable to reach ${apiTarget}. If the backend is running on another origin, make sure CORS and preflight requests are allowed for this frontend.${authNote}`
}

function authFailureMessage(error: unknown) {
  if (error instanceof Error && error.message.trim()) {
    return `Unable to acquire an API access token. ${error.message}`
  }

  return 'Unable to acquire an API access token. Sign in again and confirm API access.'
}

async function buildHeaders(options: RequestOptions) {
  const headers = new Headers()

  if (options.body) {
    headers.set('Content-Type', 'application/json')
  }

  if (entraAuth.ready) {
    const accessToken = await acquireAccessToken().catch((error: unknown) => {
      throw new ApiError(authFailureMessage(error), 401)
    })

    if (accessToken) {
      headers.set('Authorization', `Bearer ${accessToken}`)
    }
  }

  return Array.from(headers.keys()).length > 0 ? headers : undefined
}

async function requestJson<T>(path: string, options: RequestOptions = {}) {
  try {
    const response = await fetch(buildUrl(path, options.query), {
      method: options.method ?? 'GET',
      headers: await buildHeaders(options),
      body: options.body ? JSON.stringify(options.body) : undefined,
    })

    if (!response.ok) {
      throw new ApiError(await readErrorMessage(response), response.status)
    }

    return (await response.json()) as T
  } catch (error: unknown) {
    if (error instanceof ApiError) {
      throw error
    }

    if (error instanceof TypeError) {
      throw new ApiError(networkFailureMessage())
    }

    throw new ApiError('Unexpected API failure.')
  }
}

export function getHealth() {
  return requestJson<HealthResponse>('/api/health')
}

export function ingestEvent(body: IngestEventRequest) {
  return requestJson<IngestEventResponse>('/api/events', {
    method: 'POST',
    body,
  })
}

export function listProfiles(tenantId: string) {
  return requestJson<ProfileResponse[]>('/api/profiles', {
    query: { tenantId },
  })
}

export function getProfile(tenantId: string, profileId: string) {
  return requestJson<ProfileResponse>(`/api/profiles/${encodeURIComponent(profileId)}`, {
    query: { tenantId },
  })
}

export function listChangeSets(tenantId: string, status?: string) {
  return requestJson<ChangeSetResponse[]>('/api/change-sets', {
    query: { tenantId, status },
  })
}

export function getChangeSet(tenantId: string, changeSetId: string) {
  return requestJson<ChangeSetResponse>(`/api/change-sets/${encodeURIComponent(changeSetId)}`, {
    query: { tenantId },
  })
}

export function approveChangeSet(changeSetId: string, body: ReviewChangeSetRequest) {
  return requestJson<ChangeSetResponse>(`/api/change-sets/${encodeURIComponent(changeSetId)}/approve`, {
    method: 'POST',
    body,
  })
}

export function rejectChangeSet(changeSetId: string, body: ReviewChangeSetRequest) {
  return requestJson<ChangeSetResponse>(`/api/change-sets/${encodeURIComponent(changeSetId)}/reject`, {
    method: 'POST',
    body,
  })
}

export function listTriggers(tenantId: string) {
  return requestJson<TriggerDefinitionResponse[]>('/api/triggers', {
    query: { tenantId },
  })
}

export function createTrigger(body: TriggerDefinitionRequest) {
  return requestJson<TriggerDefinitionResponse>('/api/triggers', {
    method: 'POST',
    body,
  })
}

export function runTrigger(triggerId: string, tenantId: string) {
  return requestJson<RunTriggerResponse>(`/api/triggers/${encodeURIComponent(triggerId)}/run`, {
    method: 'POST',
    body: { tenantId },
  })
}

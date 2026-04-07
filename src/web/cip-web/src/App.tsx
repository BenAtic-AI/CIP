import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react'
import ReactMarkdown, { type Components } from 'react-markdown'
import {
  ApiError,
  approveChangeSet,
  createTrigger,
  type ChangeSetEvidenceItemResponse,
  getChangeSet,
  getHealth,
  getProfile,
  ingestEvent,
  listChangeSets,
  listProfiles,
  listTriggers,
  rejectChangeSet,
  runTrigger,
  type ChangeSetResponse,
  type HealthResponse,
  type IdentityDto,
  type IngestEventRequest,
  type IngestEventResponse,
  type ProfileResponse,
  type ReviewChangeSetRequest,
  type RunTriggerResponse,
  type TraitDto,
  type TriggerConditionRequest,
  type TriggerDefinitionRequest,
  type TriggerDefinitionResponse,
} from './lib/api'
import { useAuth } from './auth/provider'

const DEFAULT_TENANT_ID = 'demo-tenant'
const TENANT_STORAGE_KEY = 'cip-web.tenant-id'
const DEFAULT_REVIEWER = 'operator-ui'

const triggerOperators = [
  'TraitEquals',
  'IdentityEquals',
  'IdentityContains',
] as const

type LoadState<T> = {
  data: T
  loading: boolean
  error: string | null
}

type IdentityFormValue = {
  id: string
  type: string
  value: string
  source: string
}

type TraitFormValue = {
  id: string
  name: string
  value: string
  confidence: string
}

type EventFormState = {
  eventId: string
  eventType: string
  source: string
  occurredAt: string
  schemaVersion: string
  identities: IdentityFormValue[]
  traits: TraitFormValue[]
}

type TriggerConditionFormValue = {
  id: string
  operator: (typeof triggerOperators)[number]
  attribute: string
  value: string
}

type TriggerFormState = {
  name: string
  description: string
  conditions: TriggerConditionFormValue[]
}

type RequestState = {
  loading: boolean
  error: string | null
  success: string | null
}

function createRowId() {
  return typeof crypto !== 'undefined' && 'randomUUID' in crypto
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`
}

function createEventId() {
  return `evt_${createRowId().replace(/[^a-zA-Z0-9_-]/g, '')}`
}

function nowForDateTimeInput() {
  const now = new Date()
  const local = new Date(now.getTime() - now.getTimezoneOffset() * 60_000)
  return local.toISOString().slice(0, 16)
}

function createDefaultIdentity(): IdentityFormValue {
  return {
    id: createRowId(),
    type: 'email',
    value: '',
    source: 'crm',
  }
}

function createDefaultTrait(): TraitFormValue {
  return {
    id: createRowId(),
    name: 'loyaltyTier',
    value: '',
    confidence: '0.9',
  }
}

function createInitialEventForm(): EventFormState {
  return {
    eventId: createEventId(),
    eventType: 'ProfileObserved',
    source: 'operator-ui',
    occurredAt: nowForDateTimeInput(),
    schemaVersion: '1',
    identities: [createDefaultIdentity()],
    traits: [createDefaultTrait()],
  }
}

function createDefaultCondition(): TriggerConditionFormValue {
  return {
    id: createRowId(),
    operator: 'TraitEquals',
    attribute: 'loyaltyTier',
    value: '',
  }
}

function createInitialTriggerForm(): TriggerFormState {
  return {
    name: '',
    description: '',
    conditions: [createDefaultCondition()],
  }
}

function createLoadState<T>(data: T): LoadState<T> {
  return {
    data,
    loading: false,
    error: null,
  }
}

function createRequestState(): RequestState {
  return {
    loading: false,
    error: null,
    success: null,
  }
}

function getStoredTenantId() {
  if (typeof window === 'undefined') {
    return DEFAULT_TENANT_ID
  }

  return window.localStorage.getItem(TENANT_STORAGE_KEY) || DEFAULT_TENANT_ID
}

function normalizeError(error: unknown) {
  if (error instanceof ApiError || error instanceof Error) {
    return error.message
  }

  return 'Unexpected request failure.'
}

function formatDateTime(value: string | null | undefined) {
  if (!value) {
    return '—'
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(value))
}

function isBlank(value: string) {
  return value.trim().length === 0
}

function summarizePending(count: number) {
  return count === 1 ? '1 pending change set' : `${count} pending change sets`
}

function formatConfidence(value: number | null | undefined) {
  if (typeof value !== 'number' || Number.isNaN(value)) {
    return null
  }

  return value.toString()
}

export default function App() {
  const auth = useAuth()
  const [tenantDraft, setTenantDraft] = useState(getStoredTenantId)
  const [tenantId, setTenantId] = useState(getStoredTenantId)
  const [health, setHealth] = useState<HealthResponse | null>(null)
  const [healthError, setHealthError] = useState<string | null>(null)

  const [profilesState, setProfilesState] = useState<LoadState<ProfileResponse[]>>(() => createLoadState([]))
  const [selectedProfileId, setSelectedProfileId] = useState<string | null>(null)
  const [profileDetailState, setProfileDetailState] = useState<LoadState<ProfileResponse | null>>(() => createLoadState(null))

  const [changeSetStatusFilter, setChangeSetStatusFilter] = useState('Pending')
  const [changeSetsState, setChangeSetsState] = useState<LoadState<ChangeSetResponse[]>>(() => createLoadState([]))
  const [selectedChangeSetId, setSelectedChangeSetId] = useState<string | null>(null)
  const [changeSetDetailState, setChangeSetDetailState] = useState<LoadState<ChangeSetResponse | null>>(() => createLoadState(null))

  const [triggersState, setTriggersState] = useState<LoadState<TriggerDefinitionResponse[]>>(() => createLoadState([]))
  const [selectedTriggerId, setSelectedTriggerId] = useState<string | null>(null)
  const [runTriggerState, setRunTriggerState] = useState<LoadState<RunTriggerResponse | null>>(() => createLoadState(null))

  const [eventForm, setEventForm] = useState<EventFormState>(() => createInitialEventForm())
  const [eventRequestState, setEventRequestState] = useState<RequestState>(() => createRequestState())
  const [lastIngestResponse, setLastIngestResponse] = useState<IngestEventResponse | null>(null)

  const [reviewForm, setReviewForm] = useState<ReviewChangeSetRequest>({
    tenantId,
    reviewedBy: DEFAULT_REVIEWER,
    comment: '',
  })
  const [reviewRequestState, setReviewRequestState] = useState<RequestState>(() => createRequestState())

  const [triggerForm, setTriggerForm] = useState<TriggerFormState>(() => createInitialTriggerForm())
  const [triggerRequestState, setTriggerRequestState] = useState<RequestState>(() => createRequestState())
  const [runRequestState, setRunRequestState] = useState<RequestState>(() => createRequestState())

  useEffect(() => {
    getHealth()
      .then((response) => {
        setHealth(response)
        setHealthError(null)
      })
      .catch((error: unknown) => setHealthError(normalizeError(error)))
  }, [])

  useEffect(() => {
    setReviewForm((current) => ({
      ...current,
      tenantId,
    }))

    if (typeof window !== 'undefined') {
      window.localStorage.setItem(TENANT_STORAGE_KEY, tenantId)
    }
  }, [tenantId])

  const refreshProfiles = useCallback(async () => {
    setProfilesState((current) => ({ ...current, loading: true, error: null }))

    try {
      const profiles = await listProfiles(tenantId)
      setProfilesState({ data: profiles, loading: false, error: null })
      setSelectedProfileId((current) => {
        if (current && profiles.some((profile) => profile.profileId === current)) {
          return current
        }

        return profiles[0]?.profileId ?? null
      })
    } catch (error: unknown) {
      setProfilesState((current) => ({
        ...current,
        loading: false,
        error: normalizeError(error),
      }))
    }
  }, [tenantId])

  const refreshChangeSets = useCallback(async () => {
    setChangeSetsState((current) => ({ ...current, loading: true, error: null }))

    try {
      const changeSets = await listChangeSets(tenantId, changeSetStatusFilter || undefined)
      setChangeSetsState({ data: changeSets, loading: false, error: null })
      setSelectedChangeSetId((current) => {
        if (current && changeSets.some((changeSet) => changeSet.changeSetId === current)) {
          return current
        }

        return changeSets[0]?.changeSetId ?? null
      })
    } catch (error: unknown) {
      setChangeSetsState((current) => ({
        ...current,
        loading: false,
        error: normalizeError(error),
      }))
    }
  }, [changeSetStatusFilter, tenantId])

  const refreshTriggers = useCallback(async () => {
    setTriggersState((current) => ({ ...current, loading: true, error: null }))

    try {
      const triggers = await listTriggers(tenantId)
      setTriggersState({ data: triggers, loading: false, error: null })
      setSelectedTriggerId((current) => {
        if (current && triggers.some((trigger) => trigger.triggerId === current)) {
          return current
        }

        return triggers[0]?.triggerId ?? null
      })
    } catch (error: unknown) {
      setTriggersState((current) => ({
        ...current,
        loading: false,
        error: normalizeError(error),
      }))
    }
  }, [tenantId])

  useEffect(() => {
    void refreshProfiles()
  }, [refreshProfiles])

  useEffect(() => {
    void refreshChangeSets()
  }, [refreshChangeSets])

  useEffect(() => {
    void refreshTriggers()
  }, [refreshTriggers])

  useEffect(() => {
    if (!selectedProfileId) {
      setProfileDetailState(createLoadState(null))
      return
    }

    let active = true
    setProfileDetailState((current) => ({ ...current, loading: true, error: null }))

    getProfile(tenantId, selectedProfileId)
      .then((profile) => {
        if (!active) {
          return
        }

        setProfileDetailState({ data: profile, loading: false, error: null })
      })
      .catch((error: unknown) => {
        if (!active) {
          return
        }

        setProfileDetailState({ data: null, loading: false, error: normalizeError(error) })
      })

    return () => {
      active = false
    }
  }, [selectedProfileId, tenantId])

  useEffect(() => {
    if (!selectedChangeSetId) {
      setChangeSetDetailState(createLoadState(null))
      return
    }

    let active = true
    setChangeSetDetailState((current) => ({ ...current, loading: true, error: null }))

    getChangeSet(tenantId, selectedChangeSetId)
      .then((changeSet) => {
        if (!active) {
          return
        }

        setChangeSetDetailState({ data: changeSet, loading: false, error: null })
      })
      .catch((error: unknown) => {
        if (!active) {
          return
        }

        setChangeSetDetailState({ data: null, loading: false, error: normalizeError(error) })
      })

    return () => {
      active = false
    }
  }, [selectedChangeSetId, tenantId])

  const selectedTrigger = useMemo(
    () => triggersState.data.find((trigger) => trigger.triggerId === selectedTriggerId) ?? null,
    [selectedTriggerId, triggersState.data],
  )

  const handleTenantSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    const nextTenantId = tenantDraft.trim() || DEFAULT_TENANT_ID
    setTenantDraft(nextTenantId)
    setTenantId(nextTenantId)
    setRunTriggerState(createLoadState(null))
    setEventRequestState(createRequestState())
    setReviewRequestState(createRequestState())
    setTriggerRequestState(createRequestState())
    setRunRequestState(createRequestState())
  }

  const updateEventIdentity = (id: string, field: keyof Omit<IdentityFormValue, 'id'>, value: string) => {
    setEventForm((current) => ({
      ...current,
      identities: current.identities.map((identity) => (identity.id === id ? { ...identity, [field]: value } : identity)),
    }))
  }

  const updateEventTrait = (id: string, field: keyof Omit<TraitFormValue, 'id'>, value: string) => {
    setEventForm((current) => ({
      ...current,
      traits: current.traits.map((trait) => (trait.id === id ? { ...trait, [field]: value } : trait)),
    }))
  }

  const handleEventSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setEventRequestState({ loading: true, error: null, success: null })
    setLastIngestResponse(null)

    if (isBlank(eventForm.eventId) || isBlank(eventForm.eventType) || isBlank(eventForm.source) || isBlank(eventForm.occurredAt)) {
      setEventRequestState({ loading: false, error: 'Event ID, type, source, and occurred at are required.', success: null })
      return
    }

    const identities = eventForm.identities
      .filter((identity) => !isBlank(identity.type) || !isBlank(identity.value) || !isBlank(identity.source))
    const traits = eventForm.traits
      .filter((trait) => !isBlank(trait.name) || !isBlank(trait.value) || !isBlank(trait.confidence))

    if (identities.some((identity) => isBlank(identity.type) || isBlank(identity.value) || isBlank(identity.source))) {
      setEventRequestState({ loading: false, error: 'Each identity needs a type, value, and source.', success: null })
      return
    }

    if (traits.some((trait) => isBlank(trait.name) || isBlank(trait.value))) {
      setEventRequestState({ loading: false, error: 'Each trait needs a name and value.', success: null })
      return
    }

    if (identities.length === 0 && traits.length === 0) {
      setEventRequestState({ loading: false, error: 'Add at least one identity or trait to ingest an event.', success: null })
      return
    }

    const payload: IngestEventRequest = {
      tenantId,
      eventId: eventForm.eventId.trim(),
      eventType: eventForm.eventType.trim(),
      source: eventForm.source.trim(),
      occurredAt: new Date(eventForm.occurredAt).toISOString(),
      schemaVersion: Number(eventForm.schemaVersion) || 1,
      identities: identities.map<IdentityDto>((identity) => ({
        type: identity.type.trim(),
        value: identity.value.trim(),
        source: identity.source.trim(),
      })),
      traits: traits.map<TraitDto>((trait) => ({
        name: trait.name.trim(),
        value: trait.value.trim(),
        confidence: Number(trait.confidence) || 0,
      })),
    }

    try {
      const response = await ingestEvent(payload)
      setLastIngestResponse(response)
      setEventRequestState({
        loading: false,
        error: null,
        success: response.duplicate
          ? `Duplicate event detected. Existing change set ${response.changeSetId} was reused.`
          : `Event accepted and queued as ${response.changeSetId}.`,
      })
      setEventForm((current) => ({
        ...current,
        eventId: createEventId(),
        occurredAt: nowForDateTimeInput(),
      }))
      await Promise.all([refreshProfiles(), refreshChangeSets()])
      setSelectedProfileId(response.profileId)
      setSelectedChangeSetId(response.changeSetId)
    } catch (error: unknown) {
      setEventRequestState({ loading: false, error: normalizeError(error), success: null })
    }
  }

  const handleReview = async (decision: 'approve' | 'reject') => {
    if (!selectedChangeSetId) {
      return
    }

    setReviewRequestState({ loading: true, error: null, success: null })

    try {
      const request = {
        tenantId,
        reviewedBy: reviewForm.reviewedBy.trim(),
        comment: reviewForm.comment?.trim() || null,
      }

      const updated = decision === 'approve'
        ? await approveChangeSet(selectedChangeSetId, request)
        : await rejectChangeSet(selectedChangeSetId, request)

      setReviewRequestState({
        loading: false,
        error: null,
        success: `Change set ${updated.changeSetId} ${decision === 'approve' ? 'approved' : 'rejected'}.`,
      })
      setChangeSetDetailState({ data: updated, loading: false, error: null })
      setReviewForm((current) => ({ ...current, comment: '' }))
      await Promise.all([refreshChangeSets(), refreshProfiles()])
      setSelectedProfileId(updated.targetProfileId)
    } catch (error: unknown) {
      setReviewRequestState({ loading: false, error: normalizeError(error), success: null })
    }
  }

  const updateTriggerCondition = (id: string, field: keyof Omit<TriggerConditionFormValue, 'id'>, value: string) => {
    setTriggerForm((current) => ({
      ...current,
      conditions: current.conditions.map((condition) => (condition.id === id ? { ...condition, [field]: value } : condition)),
    }))
  }

  const handleTriggerSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setTriggerRequestState({ loading: true, error: null, success: null })

    if (isBlank(triggerForm.name)) {
      setTriggerRequestState({ loading: false, error: 'Trigger name is required.', success: null })
      return
    }

    const conditions = triggerForm.conditions.filter(
      (condition) => !isBlank(condition.attribute) || !isBlank(condition.value),
    )

    if (conditions.length === 0) {
      setTriggerRequestState({ loading: false, error: 'Add at least one trigger condition.', success: null })
      return
    }

    if (conditions.some((condition) => isBlank(condition.attribute) || isBlank(condition.value))) {
      setTriggerRequestState({ loading: false, error: 'Each trigger condition needs an attribute and value.', success: null })
      return
    }

    const payload: TriggerDefinitionRequest = {
      tenantId,
      name: triggerForm.name.trim(),
      description: triggerForm.description.trim() || null,
      conditions: conditions.map<TriggerConditionRequest>((condition) => ({
        operator: condition.operator,
        attribute: condition.attribute.trim(),
        value: condition.value.trim(),
      })),
    }

    try {
      const created = await createTrigger(payload)
      setTriggerRequestState({ loading: false, error: null, success: `Trigger ${created.name} created.` })
      setTriggerForm(createInitialTriggerForm())
      setRunTriggerState(createLoadState(null))
      await refreshTriggers()
      setSelectedTriggerId(created.triggerId)
    } catch (error: unknown) {
      setTriggerRequestState({ loading: false, error: normalizeError(error), success: null })
    }
  }

  const handleRunTrigger = async () => {
    if (!selectedTriggerId) {
      return
    }

    setRunRequestState({ loading: true, error: null, success: null })
    setRunTriggerState((current) => ({ ...current, loading: true, error: null }))

    try {
      const response = await runTrigger(selectedTriggerId, tenantId)
      setRunRequestState({ loading: false, error: null, success: `Trigger matched ${response.matchedProfileCount} profiles.` })
      setRunTriggerState({ data: response, loading: false, error: null })
      await refreshTriggers()
    } catch (error: unknown) {
      const message = normalizeError(error)
      setRunRequestState({ loading: false, error: message, success: null })
      setRunTriggerState({ data: null, loading: false, error: message })
    }
  }

  return (
    <main className="min-h-screen bg-slate-950 text-slate-100">
      <div className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-6 sm:px-6 lg:px-8">
        <header className="rounded-3xl border border-cyan-500/20 bg-gradient-to-br from-cyan-500/10 via-slate-900 to-slate-950 p-6 shadow-2xl shadow-cyan-950/30">
          <div className="flex flex-col gap-6 lg:flex-row lg:items-start lg:justify-between">
            <div className="max-w-3xl">
              <span className="inline-flex rounded-full border border-cyan-400/30 bg-cyan-400/10 px-3 py-1 text-xs font-medium uppercase tracking-[0.2em] text-cyan-200">
                CIP operator MVP
              </span>
              <h1 className="mt-4 text-3xl font-semibold tracking-tight text-white sm:text-4xl">
                Customer Intelligence Platform local operator console
              </h1>
              <p className="mt-3 text-sm text-slate-300 sm:text-base">
                Ingest events, inspect profiles, review change sets, and run demo triggers against the in-memory backend runtime.
              </p>
            </div>

            <div className="w-full max-w-xl space-y-4">
              <AuthShell auth={auth} />

              <form className="rounded-2xl border border-slate-800 bg-slate-950/70 p-4" onSubmit={handleTenantSubmit}>
                <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
                  <div className="flex-1">
                    <label className="text-xs font-medium uppercase tracking-[0.2em] text-slate-400" htmlFor="tenantId">
                      Active tenant
                    </label>
                    <input
                      id="tenantId"
                      className="mt-2 w-full rounded-xl border border-slate-700 bg-slate-900 px-3 py-2 text-sm text-white outline-none ring-0 transition focus:border-cyan-400"
                      value={tenantDraft}
                      onChange={(event) => setTenantDraft(event.target.value)}
                      placeholder={DEFAULT_TENANT_ID}
                    />
                  </div>
                  <button
                    type="submit"
                    className="rounded-xl bg-cyan-500 px-4 py-2 text-sm font-medium text-slate-950 transition hover:bg-cyan-400"
                  >
                    Apply tenant
                  </button>
                  <button
                    type="button"
                    className="rounded-xl border border-slate-700 px-4 py-2 text-sm font-medium text-slate-200 transition hover:border-slate-500 hover:text-white"
                    onClick={() => {
                      void Promise.all([refreshProfiles(), refreshChangeSets(), refreshTriggers()])
                    }}
                  >
                    Refresh data
                  </button>
                </div>
                <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-400">
                  <span className="rounded-full border border-slate-800 bg-slate-900 px-3 py-1">tenantId={tenantId}</span>
                  <span className="rounded-full border border-slate-800 bg-slate-900 px-3 py-1">{authStatusPillLabel(auth.status)}</span>
                  <span className="rounded-full border border-slate-800 bg-slate-900 px-3 py-1">/api endpoints</span>
                </div>
              </form>
            </div>
          </div>

          <div className="mt-6 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            <Metric label="Profiles" value={String(profilesState.data.length)} helper="Loaded for tenant" />
            <Metric label="Change sets" value={String(changeSetsState.data.length)} helper={changeSetStatusFilter || 'All statuses'} />
            <Metric label="Triggers" value={String(triggersState.data.length)} helper="Configured definitions" />
            <Metric
              label="API health"
              value={health ? health.environment : healthError ? 'Unavailable' : 'Checking'}
              helper={health ? health.service : healthError ?? 'Waiting for /api/health'}
            />
          </div>
        </header>

        <section className="grid gap-6 xl:grid-cols-[1.05fr_0.95fr]">
          <Panel
            title="Event ingestion"
            description="Create events with identities and traits, then push them into the backend runtime."
          >
            <form className="space-y-5" onSubmit={handleEventSubmit}>
              <div className="grid gap-4 md:grid-cols-2">
                <Field label="Event ID">
                  <input
                    className="input"
                    value={eventForm.eventId}
                    onChange={(event) => setEventForm((current) => ({ ...current, eventId: event.target.value }))}
                  />
                </Field>
                <Field label="Event type">
                  <input
                    className="input"
                    value={eventForm.eventType}
                    onChange={(event) => setEventForm((current) => ({ ...current, eventType: event.target.value }))}
                  />
                </Field>
                <Field label="Source">
                  <input
                    className="input"
                    value={eventForm.source}
                    onChange={(event) => setEventForm((current) => ({ ...current, source: event.target.value }))}
                  />
                </Field>
                <Field label="Occurred at">
                  <input
                    className="input"
                    type="datetime-local"
                    value={eventForm.occurredAt}
                    onChange={(event) => setEventForm((current) => ({ ...current, occurredAt: event.target.value }))}
                  />
                </Field>
              </div>

              <Field label="Schema version">
                <input
                  className="input max-w-40"
                  type="number"
                  min="1"
                  step="1"
                  value={eventForm.schemaVersion}
                  onChange={(event) => setEventForm((current) => ({ ...current, schemaVersion: event.target.value }))}
                />
              </Field>

              <div className="space-y-3">
                <SectionHeader
                  title="Identities"
                  actionLabel="Add identity"
                  onAction={() => setEventForm((current) => ({ ...current, identities: [...current.identities, createDefaultIdentity()] }))}
                />
                <div className="space-y-3">
                  {eventForm.identities.map((identity, index) => (
                    <div key={identity.id} className="rounded-2xl border border-slate-800 bg-slate-950/60 p-4">
                      <div className="grid gap-3 md:grid-cols-[1fr_1.4fr_1fr_auto]">
                        <Field label={`Type ${index + 1}`}>
                          <input className="input" value={identity.type} onChange={(event) => updateEventIdentity(identity.id, 'type', event.target.value)} />
                        </Field>
                        <Field label="Value">
                          <input className="input" value={identity.value} onChange={(event) => updateEventIdentity(identity.id, 'value', event.target.value)} />
                        </Field>
                        <Field label="Source">
                          <input className="input" value={identity.source} onChange={(event) => updateEventIdentity(identity.id, 'source', event.target.value)} />
                        </Field>
                        <div className="flex items-end">
                          <button
                            type="button"
                            className="w-full rounded-xl border border-slate-700 px-3 py-2 text-sm text-slate-200 transition hover:border-rose-500 hover:text-rose-200"
                            onClick={() => setEventForm((current) => ({
                              ...current,
                              identities: current.identities.length > 1
                                ? current.identities.filter((item) => item.id !== identity.id)
                                : [createDefaultIdentity()],
                            }))}
                          >
                            Remove
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="space-y-3">
                <SectionHeader
                  title="Traits"
                  actionLabel="Add trait"
                  onAction={() => setEventForm((current) => ({ ...current, traits: [...current.traits, createDefaultTrait()] }))}
                />
                <div className="space-y-3">
                  {eventForm.traits.map((trait, index) => (
                    <div key={trait.id} className="rounded-2xl border border-slate-800 bg-slate-950/60 p-4">
                      <div className="grid gap-3 md:grid-cols-[1fr_1fr_0.7fr_auto]">
                        <Field label={`Name ${index + 1}`}>
                          <input className="input" value={trait.name} onChange={(event) => updateEventTrait(trait.id, 'name', event.target.value)} />
                        </Field>
                        <Field label="Value">
                          <input className="input" value={trait.value} onChange={(event) => updateEventTrait(trait.id, 'value', event.target.value)} />
                        </Field>
                        <Field label="Confidence">
                          <input
                            className="input"
                            type="number"
                            min="0"
                            max="1"
                            step="0.01"
                            value={trait.confidence}
                            onChange={(event) => updateEventTrait(trait.id, 'confidence', event.target.value)}
                          />
                        </Field>
                        <div className="flex items-end">
                          <button
                            type="button"
                            className="w-full rounded-xl border border-slate-700 px-3 py-2 text-sm text-slate-200 transition hover:border-rose-500 hover:text-rose-200"
                            onClick={() => setEventForm((current) => ({
                              ...current,
                              traits: current.traits.length > 1
                                ? current.traits.filter((item) => item.id !== trait.id)
                                : [createDefaultTrait()],
                            }))}
                          >
                            Remove
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-3">
                <button
                  type="submit"
                  disabled={eventRequestState.loading}
                  className="rounded-xl bg-cyan-500 px-4 py-2 text-sm font-medium text-slate-950 transition hover:bg-cyan-400 disabled:cursor-not-allowed disabled:bg-cyan-900 disabled:text-cyan-200"
                >
                  {eventRequestState.loading ? 'Submitting...' : 'Submit event'}
                </button>
                <button
                  type="button"
                  className="rounded-xl border border-slate-700 px-4 py-2 text-sm font-medium text-slate-200 transition hover:border-slate-500 hover:text-white"
                  onClick={() => {
                    setEventForm(createInitialEventForm())
                    setEventRequestState(createRequestState())
                  }}
                >
                  Reset form
                </button>
              </div>

              <RequestFeedback state={eventRequestState} />

              {lastIngestResponse && (
                <div className="rounded-2xl border border-emerald-500/30 bg-emerald-500/10 p-4 text-sm text-emerald-100">
                  <p className="font-medium">Last response</p>
                  <div className="mt-2 grid gap-2 sm:grid-cols-2">
                    <InfoRow label="Profile ID" value={lastIngestResponse.profileId} />
                    <InfoRow label="Change set ID" value={lastIngestResponse.changeSetId} />
                    <InfoRow label="Processing state" value={lastIngestResponse.processingState} />
                    <InfoRow label="Duplicate" value={lastIngestResponse.duplicate ? 'Yes' : 'No'} />
                  </div>
                </div>
              )}
            </form>
          </Panel>

          <Panel
            title="Profiles"
            description="Browse profiles for the active tenant and inspect the current materialized state."
            action={
              <button
                type="button"
                className="rounded-xl border border-slate-700 px-3 py-2 text-sm text-slate-200 transition hover:border-slate-500 hover:text-white"
                onClick={() => void refreshProfiles()}
              >
                Refresh profiles
              </button>
            }
          >
            <div className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
              <div className="space-y-3">
                <ListState loading={profilesState.loading} error={profilesState.error} count={profilesState.data.length} emptyMessage="No profiles found for this tenant." />
                <div className="max-h-[32rem] space-y-3 overflow-auto pr-1">
                  {profilesState.data.map((profile) => {
                    const isSelected = profile.profileId === selectedProfileId

                    return (
                      <button
                        key={profile.profileId}
                        type="button"
                        onClick={() => setSelectedProfileId(profile.profileId)}
                        className={`w-full rounded-2xl border p-4 text-left transition ${
                          isSelected
                            ? 'border-cyan-400 bg-cyan-500/10 shadow-lg shadow-cyan-950/20'
                            : 'border-slate-800 bg-slate-950/60 hover:border-slate-700 hover:bg-slate-900'
                        }`}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0 flex-1">
                            <ProfileCardMarkdown content={profile.profileCard} variant="compact" />
                            <p className="mt-1 text-xs text-slate-400">{profile.profileId}</p>
                          </div>
                          <StatusPill value={profile.status} />
                        </div>
                        <p className="mt-3 text-sm text-slate-300">{profile.synopsis}</p>
                        <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-400">
                          <span>{profile.identities.length} identities</span>
                          <span>{profile.traits.length} traits</span>
                          <span>{summarizePending(profile.pendingChangeSetCount)}</span>
                        </div>
                      </button>
                    )
                  })}
                </div>
              </div>

              <div className="rounded-2xl border border-slate-800 bg-slate-950/60 p-5">
                {profileDetailState.loading && <p className="text-sm text-slate-400">Loading profile details...</p>}
                {profileDetailState.error && <p className="text-sm text-rose-300">{profileDetailState.error}</p>}
                {!profileDetailState.loading && !profileDetailState.error && !profileDetailState.data && (
                  <p className="text-sm text-slate-400">Select a profile to inspect details.</p>
                )}
                {profileDetailState.data && (
                    <div className="space-y-5">
                      <div>
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <div className="min-w-0 flex-1">
                            <ProfileCardMarkdown content={profileDetailState.data.profileCard} variant="detail" />
                            <p className="mt-1 text-xs text-slate-400">{profileDetailState.data.profileId}</p>
                          </div>
                          <StatusPill value={profileDetailState.data.status} />
                        </div>
                      <p className="mt-3 text-sm text-slate-300">{profileDetailState.data.synopsis}</p>
                    </div>

                    <div className="grid gap-3 sm:grid-cols-2">
                      <InfoRow label="Created" value={formatDateTime(profileDetailState.data.createdAt)} />
                      <InfoRow label="Updated" value={formatDateTime(profileDetailState.data.updatedAt)} />
                      <InfoRow label="Pending reviews" value={String(profileDetailState.data.pendingChangeSetCount)} />
                      <InfoRow label="Tenant" value={profileDetailState.data.tenantId} />
                    </div>

                    <DetailList title="Identities" emptyMessage="No identities on this profile yet.">
                      {profileDetailState.data.identities.map((identity, index) => (
                        <li key={`${identity.type}-${identity.value}-${index}`} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                          <span className="font-medium text-white">{identity.type}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span>{identity.value}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span className="text-slate-400">{identity.source}</span>
                        </li>
                      ))}
                    </DetailList>

                    <DetailList title="Traits" emptyMessage="No traits on this profile yet.">
                      {profileDetailState.data.traits.map((trait, index) => (
                        <li key={`${trait.name}-${trait.value}-${index}`} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                          <span className="font-medium text-white">{trait.name}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span>{trait.value}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span className="text-slate-400">confidence {trait.confidence}</span>
                        </li>
                      ))}
                    </DetailList>
                  </div>
                )}
              </div>
            </div>
          </Panel>
        </section>

        <section className="grid gap-6 xl:grid-cols-[1.05fr_0.95fr]">
          <Panel
            title="Change set queue"
            description="Review pending materialization proposals and apply or reject them."
            action={
              <div className="flex items-center gap-2">
                <select
                  className="rounded-xl border border-slate-700 bg-slate-950 px-3 py-2 text-sm text-white outline-none transition focus:border-cyan-400"
                  value={changeSetStatusFilter}
                  onChange={(event) => setChangeSetStatusFilter(event.target.value)}
                  aria-label="Filter change sets by status"
                >
                  <option value="">All statuses</option>
                  <option value="Pending">Pending</option>
                  <option value="Approved">Approved</option>
                  <option value="Rejected">Rejected</option>
                </select>
                <button
                  type="button"
                  className="rounded-xl border border-slate-700 px-3 py-2 text-sm text-slate-200 transition hover:border-slate-500 hover:text-white"
                  onClick={() => void refreshChangeSets()}
                >
                  Refresh queue
                </button>
              </div>
            }
          >
            <div className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
              <div className="space-y-3">
                <ListState loading={changeSetsState.loading} error={changeSetsState.error} count={changeSetsState.data.length} emptyMessage="No change sets found for this filter." />
                <div className="max-h-[34rem] space-y-3 overflow-auto pr-1">
                  {changeSetsState.data.map((changeSet) => {
                    const isSelected = changeSet.changeSetId === selectedChangeSetId

                    return (
                      <button
                        key={changeSet.changeSetId}
                        type="button"
                        onClick={() => setSelectedChangeSetId(changeSet.changeSetId)}
                        className={`w-full rounded-2xl border p-4 text-left transition ${
                          isSelected
                            ? 'border-cyan-400 bg-cyan-500/10 shadow-lg shadow-cyan-950/20'
                            : 'border-slate-800 bg-slate-950/60 hover:border-slate-700 hover:bg-slate-900'
                        }`}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <p className="text-sm font-medium text-white">{changeSet.changeSetId}</p>
                            <p className="mt-1 text-xs text-slate-400">Profile {changeSet.targetProfileId}</p>
                          </div>
                          <StatusPill value={changeSet.status} />
                        </div>
                        <div className="mt-3 flex flex-wrap gap-2 text-xs text-slate-400">
                          <span>{changeSet.proposedOperations.length} operations</span>
                          <span>{changeSet.proposedIdentities.length} identities</span>
                          <span>{changeSet.proposedTraits.length} traits</span>
                        </div>
                        <p className="mt-3 text-xs text-slate-500">Proposed {formatDateTime(changeSet.proposedAt)}</p>
                      </button>
                    )
                  })}
                </div>
              </div>

              <div className="rounded-2xl border border-slate-800 bg-slate-950/60 p-5">
                {changeSetDetailState.loading && <p className="text-sm text-slate-400">Loading change set details...</p>}
                {changeSetDetailState.error && <p className="text-sm text-rose-300">{changeSetDetailState.error}</p>}
                {!changeSetDetailState.loading && !changeSetDetailState.error && !changeSetDetailState.data && (
                  <p className="text-sm text-slate-400">Select a change set to inspect its review package.</p>
                )}
                {changeSetDetailState.data && (
                  <div className="space-y-5">
                    <div>
                      <div className="flex flex-wrap items-center justify-between gap-3">
                        <div>
                          <h3 className="text-lg font-semibold text-white">{changeSetDetailState.data.changeSetId}</h3>
                          <p className="mt-1 text-xs text-slate-400">Source event {changeSetDetailState.data.sourceEventId}</p>
                        </div>
                        <StatusPill value={changeSetDetailState.data.status} />
                      </div>
                      <div className="mt-3 grid gap-3 sm:grid-cols-2">
                        <InfoRow label="Profile" value={changeSetDetailState.data.targetProfileId} />
                        <InfoRow label="Type" value={changeSetDetailState.data.type} />
                        <InfoRow label="Proposed" value={formatDateTime(changeSetDetailState.data.proposedAt)} />
                        <InfoRow label="Reviewed" value={formatDateTime(changeSetDetailState.data.reviewedAt)} />
                        <InfoRow label="Reviewed by" value={changeSetDetailState.data.reviewedBy ?? '—'} />
                        <InfoRow label="Comment" value={changeSetDetailState.data.reviewComment ?? '—'} />
                      </div>
                    </div>

                    {changeSetDetailState.data.explanation && (
                      <div className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4">
                        <h4 className="text-sm font-semibold text-white">Rationale</h4>
                        <p className="mt-2 text-sm leading-6 text-slate-300">{changeSetDetailState.data.explanation}</p>
                      </div>
                    )}

                    <DetailList title="Proposed operations" emptyMessage="No operations were generated.">
                      {changeSetDetailState.data.proposedOperations.map((operation) => (
                        <li key={operation} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                          {operation}
                        </li>
                      ))}
                    </DetailList>

                    <DetailList title="Proposed identities" emptyMessage="No identity additions proposed.">
                      {changeSetDetailState.data.proposedIdentities.map((identity, index) => (
                        <li key={`${identity.type}-${identity.value}-${index}`} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                          <span className="font-medium text-white">{identity.type}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span>{identity.value}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span className="text-slate-400">{identity.source}</span>
                        </li>
                      ))}
                    </DetailList>

                    <DetailList title="Proposed traits" emptyMessage="No trait additions proposed.">
                      {changeSetDetailState.data.proposedTraits.map((trait, index) => (
                        <li key={`${trait.name}-${trait.value}-${index}`} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                          <span className="font-medium text-white">{trait.name}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span>{trait.value}</span>
                          <span className="mx-2 text-slate-500">•</span>
                          <span className="text-slate-400">confidence {trait.confidence}</span>
                        </li>
                      ))}
                    </DetailList>

                    <DetailList title="Structured evidence" emptyMessage="No structured evidence supplied.">
                      {changeSetDetailState.data.evidenceItems?.map((item, index) => (
                        <EvidenceItemCard key={`${item.reference}-${item.kind}-${index}`} item={item} />
                      ))}
                    </DetailList>

                    <DetailList title="Evidence references" emptyMessage="No evidence references supplied.">
                      {changeSetDetailState.data.evidenceReferences.map((reference) => (
                        <li key={reference} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                          {reference}
                        </li>
                      ))}
                    </DetailList>

                    <div className="rounded-2xl border border-slate-800 bg-slate-900/70 p-4">
                      <h4 className="text-sm font-semibold text-white">Review action</h4>
                      <div className="mt-4 grid gap-4 md:grid-cols-2">
                        <Field label="Reviewed by">
                          <input
                            className="input"
                            value={reviewForm.reviewedBy}
                            onChange={(event) => setReviewForm((current) => ({ ...current, reviewedBy: event.target.value }))}
                          />
                        </Field>
                        <Field label="Comment">
                          <input
                            className="input"
                            value={reviewForm.comment ?? ''}
                            onChange={(event) => setReviewForm((current) => ({ ...current, comment: event.target.value }))}
                          />
                        </Field>
                      </div>

                      <div className="mt-4 flex flex-wrap gap-3">
                        <button
                          type="button"
                          disabled={reviewRequestState.loading || changeSetDetailState.data.status !== 'Pending'}
                          className="rounded-xl bg-emerald-500 px-4 py-2 text-sm font-medium text-slate-950 transition hover:bg-emerald-400 disabled:cursor-not-allowed disabled:bg-emerald-900 disabled:text-emerald-200"
                          onClick={() => void handleReview('approve')}
                        >
                          {reviewRequestState.loading ? 'Submitting...' : 'Approve'}
                        </button>
                        <button
                          type="button"
                          disabled={reviewRequestState.loading || changeSetDetailState.data.status !== 'Pending'}
                          className="rounded-xl bg-rose-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-rose-400 disabled:cursor-not-allowed disabled:bg-rose-950 disabled:text-rose-200"
                          onClick={() => void handleReview('reject')}
                        >
                          {reviewRequestState.loading ? 'Submitting...' : 'Reject'}
                        </button>
                      </div>

                      {changeSetDetailState.data.status !== 'Pending' && (
                        <p className="mt-3 text-xs text-slate-500">Only pending change sets can be reviewed.</p>
                      )}

                      <div className="mt-4">
                        <RequestFeedback state={reviewRequestState} />
                      </div>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </Panel>

          <Panel
            title="Triggers"
            description="Create reusable trigger definitions and run them against the active tenant profile set."
            action={
              <button
                type="button"
                className="rounded-xl border border-slate-700 px-3 py-2 text-sm text-slate-200 transition hover:border-slate-500 hover:text-white"
                onClick={() => void refreshTriggers()}
              >
                Refresh triggers
              </button>
            }
          >
            <div className="space-y-4">
              <form className="space-y-4 rounded-2xl border border-slate-800 bg-slate-950/60 p-4" onSubmit={handleTriggerSubmit}>
                <div className="grid gap-4 md:grid-cols-2">
                  <Field label="Trigger name">
                    <input
                      className="input"
                      value={triggerForm.name}
                      onChange={(event) => setTriggerForm((current) => ({ ...current, name: event.target.value }))}
                    />
                  </Field>
                  <Field label="Description">
                    <input
                      className="input"
                      value={triggerForm.description}
                      onChange={(event) => setTriggerForm((current) => ({ ...current, description: event.target.value }))}
                    />
                  </Field>
                </div>

                <SectionHeader
                  title="Conditions"
                  actionLabel="Add condition"
                  onAction={() => setTriggerForm((current) => ({ ...current, conditions: [...current.conditions, createDefaultCondition()] }))}
                />

                <div className="space-y-3">
                  {triggerForm.conditions.map((condition, index) => (
                    <div key={condition.id} className="rounded-2xl border border-slate-800 bg-slate-900/60 p-4">
                      <div className="grid gap-3 md:grid-cols-[0.9fr_1fr_1fr_auto]">
                        <Field label={`Operator ${index + 1}`}>
                          <select
                            className="input"
                            value={condition.operator}
                            onChange={(event) => updateTriggerCondition(condition.id, 'operator', event.target.value as TriggerConditionFormValue['operator'])}
                          >
                            {triggerOperators.map((operator) => (
                              <option key={operator} value={operator}>
                                {operator}
                              </option>
                            ))}
                          </select>
                        </Field>
                        <Field label="Attribute">
                          <input
                            className="input"
                            value={condition.attribute}
                            onChange={(event) => updateTriggerCondition(condition.id, 'attribute', event.target.value)}
                          />
                        </Field>
                        <Field label="Value">
                          <input className="input" value={condition.value} onChange={(event) => updateTriggerCondition(condition.id, 'value', event.target.value)} />
                        </Field>
                        <div className="flex items-end">
                          <button
                            type="button"
                            className="w-full rounded-xl border border-slate-700 px-3 py-2 text-sm text-slate-200 transition hover:border-rose-500 hover:text-rose-200"
                            onClick={() => setTriggerForm((current) => ({
                              ...current,
                              conditions: current.conditions.length > 1
                                ? current.conditions.filter((item) => item.id !== condition.id)
                                : [createDefaultCondition()],
                            }))}
                          >
                            Remove
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>

                <div className="flex flex-wrap items-center gap-3">
                  <button
                    type="submit"
                    disabled={triggerRequestState.loading}
                    className="rounded-xl bg-cyan-500 px-4 py-2 text-sm font-medium text-slate-950 transition hover:bg-cyan-400 disabled:cursor-not-allowed disabled:bg-cyan-900 disabled:text-cyan-200"
                  >
                    {triggerRequestState.loading ? 'Creating...' : 'Create trigger'}
                  </button>
                  <button
                    type="button"
                    className="rounded-xl border border-slate-700 px-4 py-2 text-sm font-medium text-slate-200 transition hover:border-slate-500 hover:text-white"
                    onClick={() => {
                      setTriggerForm(createInitialTriggerForm())
                      setTriggerRequestState(createRequestState())
                    }}
                  >
                    Reset form
                  </button>
                </div>

                <RequestFeedback state={triggerRequestState} />
              </form>

              <div className="grid gap-4 xl:grid-cols-[0.95fr_1.05fr]">
                <div className="space-y-3">
                  <ListState loading={triggersState.loading} error={triggersState.error} count={triggersState.data.length} emptyMessage="No triggers defined for this tenant." />
                  <div className="max-h-[26rem] space-y-3 overflow-auto pr-1">
                    {triggersState.data.map((trigger) => {
                      const isSelected = trigger.triggerId === selectedTriggerId

                      return (
                        <button
                          key={trigger.triggerId}
                          type="button"
                          onClick={() => setSelectedTriggerId(trigger.triggerId)}
                          className={`w-full rounded-2xl border p-4 text-left transition ${
                            isSelected
                              ? 'border-cyan-400 bg-cyan-500/10 shadow-lg shadow-cyan-950/20'
                              : 'border-slate-800 bg-slate-950/60 hover:border-slate-700 hover:bg-slate-900'
                          }`}
                        >
                          <div className="flex items-start justify-between gap-3">
                            <div>
                              <p className="text-sm font-medium text-white">{trigger.name}</p>
                              <p className="mt-1 text-xs text-slate-400">{trigger.triggerId}</p>
                            </div>
                            <StatusPill value={trigger.status} />
                          </div>
                          {trigger.description && <p className="mt-3 text-sm text-slate-300">{trigger.description}</p>}
                          <p className="mt-3 text-xs text-slate-500">{trigger.conditions.length} conditions • last run {formatDateTime(trigger.lastRunAt)}</p>
                        </button>
                      )
                    })}
                  </div>
                </div>

                <div className="rounded-2xl border border-slate-800 bg-slate-950/60 p-5">
                  {!selectedTrigger && <p className="text-sm text-slate-400">Select a trigger to inspect or run it.</p>}
                  {selectedTrigger && (
                    <div className="space-y-5">
                      <div>
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <div>
                            <h3 className="text-lg font-semibold text-white">{selectedTrigger.name}</h3>
                            <p className="mt-1 text-xs text-slate-400">{selectedTrigger.triggerId}</p>
                          </div>
                          <StatusPill value={selectedTrigger.status} />
                        </div>
                        {selectedTrigger.description && <p className="mt-3 text-sm text-slate-300">{selectedTrigger.description}</p>}
                        <div className="mt-3 grid gap-3 sm:grid-cols-2">
                          <InfoRow label="Created" value={formatDateTime(selectedTrigger.createdAt)} />
                          <InfoRow label="Last run" value={formatDateTime(selectedTrigger.lastRunAt)} />
                        </div>
                      </div>

                      <DetailList title="Conditions" emptyMessage="No conditions configured.">
                        {selectedTrigger.conditions.map((condition, index) => (
                          <li key={`${condition.operator}-${condition.attribute}-${condition.value}-${index}`} className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
                            <span className="font-medium text-white">{condition.operator}</span>
                            <span className="mx-2 text-slate-500">•</span>
                            <span>{condition.attribute}</span>
                            <span className="mx-2 text-slate-500">=</span>
                            <span>{condition.value}</span>
                          </li>
                        ))}
                      </DetailList>

                      <div className="flex flex-wrap items-center gap-3">
                        <button
                          type="button"
                          disabled={runRequestState.loading}
                          className="rounded-xl bg-violet-500 px-4 py-2 text-sm font-medium text-white transition hover:bg-violet-400 disabled:cursor-not-allowed disabled:bg-violet-950 disabled:text-violet-200"
                          onClick={() => void handleRunTrigger()}
                        >
                          {runRequestState.loading ? 'Running...' : 'Run trigger'}
                        </button>
                      </div>

                      <RequestFeedback state={runRequestState} />

                      {runTriggerState.data && runTriggerState.data.triggerId === selectedTrigger.triggerId && (
                        <div className="rounded-2xl border border-violet-500/30 bg-violet-500/10 p-4">
                          <div className="grid gap-3 sm:grid-cols-2">
                            <InfoRow label="Executed at" value={formatDateTime(runTriggerState.data.executedAt)} />
                            <InfoRow label="Matched profiles" value={String(runTriggerState.data.matchedProfileCount)} />
                          </div>
                          <DetailList title="Matched profiles" emptyMessage="This run returned no profiles.">
                            {runTriggerState.data.matchedProfiles.map((profile) => (
                              <li key={profile.profileId} className="rounded-xl border border-violet-400/20 bg-slate-950/60 p-3 text-sm text-slate-200">
                                <button
                                  type="button"
                                  className="w-full text-left"
                                  onClick={() => setSelectedProfileId(profile.profileId)}
                                >
                                  <div className="flex items-start justify-between gap-3">
                                    <div className="min-w-0 flex-1">
                                      <ProfileCardMarkdown content={profile.profileCard} variant="compact" />
                                      <p className="mt-1 text-xs text-slate-400">{profile.profileId}</p>
                                    </div>
                                    <StatusPill value={profile.status} />
                                  </div>
                                </button>
                              </li>
                            ))}
                          </DetailList>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              </div>
            </div>
          </Panel>
        </section>
      </div>
    </main>
  )
}

function authStatusPillLabel(status: 'disabled' | 'incomplete' | 'signed_out' | 'signed_in') {
  switch (status) {
    case 'signed_in':
      return 'auth active'
    case 'signed_out':
      return 'auth ready'
    case 'incomplete':
      return 'auth incomplete'
    default:
      return 'no auth'
  }
}

function AuthShell({ auth }: { auth: ReturnType<typeof useAuth> }) {
  const signedIn = auth.status === 'signed_in'
  const canSignIn = auth.ready && !signedIn
  const canSignOut = auth.ready && signedIn

  return (
    <section className="rounded-2xl border border-slate-800 bg-slate-950/70 p-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-[0.2em] text-slate-400">Authentication</p>
          <h2 className="mt-2 text-lg font-semibold text-white">
            {signedIn ? 'Signed in with Entra ID' : auth.status === 'signed_out' ? 'Entra ID ready' : 'Unauthenticated mode'}
          </h2>
          <p className="mt-2 text-sm text-slate-300">{auth.message}</p>
          {auth.accountLabel && <p className="mt-2 text-xs text-cyan-200">{auth.accountLabel}</p>}
          {auth.error && <p className="mt-2 text-sm text-rose-300">{auth.error}</p>}
        </div>

        <div className="flex flex-wrap gap-2">
          {canSignIn && (
            <button
              type="button"
              disabled={auth.busy}
              className="rounded-xl bg-cyan-500 px-4 py-2 text-sm font-medium text-slate-950 transition hover:bg-cyan-400 disabled:cursor-not-allowed disabled:bg-cyan-900 disabled:text-cyan-200"
              onClick={() => {
                void auth.signIn()
              }}
            >
              {auth.busy ? 'Signing in...' : 'Sign in'}
            </button>
          )}
          {canSignOut && (
            <button
              type="button"
              disabled={auth.busy}
              className="rounded-xl border border-slate-700 px-4 py-2 text-sm font-medium text-slate-200 transition hover:border-slate-500 hover:text-white disabled:cursor-not-allowed disabled:border-slate-800 disabled:text-slate-500"
              onClick={() => {
                void auth.signOut()
              }}
            >
              {auth.busy ? 'Signing out...' : 'Sign out'}
            </button>
          )}
        </div>
      </div>
    </section>
  )
}

function Panel({
  title,
  description,
  action,
  children,
}: {
  title: string
  description: string
  action?: ReactNode
  children: ReactNode
}) {
  return (
    <section className="rounded-3xl border border-slate-800 bg-slate-900/70 p-5 shadow-xl shadow-slate-950/20 sm:p-6">
      <div className="flex flex-col gap-4 border-b border-slate-800 pb-4 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <h2 className="text-xl font-semibold text-white">{title}</h2>
          <p className="mt-2 text-sm text-slate-400">{description}</p>
        </div>
        {action}
      </div>
      <div className="mt-5">{children}</div>
    </section>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="block text-sm text-slate-300">
      <span className="mb-2 block text-xs font-medium uppercase tracking-[0.2em] text-slate-500">{label}</span>
      {children}
    </label>
  )
}

function SectionHeader({
  title,
  actionLabel,
  onAction,
}: {
  title: string
  actionLabel: string
  onAction: () => void
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <h3 className="text-sm font-semibold uppercase tracking-[0.2em] text-slate-400">{title}</h3>
      <button
        type="button"
        className="rounded-xl border border-slate-700 px-3 py-2 text-xs font-medium text-slate-200 transition hover:border-slate-500 hover:text-white"
        onClick={onAction}
      >
        {actionLabel}
      </button>
    </div>
  )
}

function Metric({ label, value, helper }: { label: string; value: string; helper: string }) {
  return (
    <div className="rounded-2xl border border-slate-800 bg-slate-950/60 p-4">
      <p className="text-xs uppercase tracking-[0.2em] text-slate-500">{label}</p>
      <p className="mt-2 text-2xl font-semibold text-white">{value}</p>
      <p className="mt-2 text-xs text-slate-400">{helper}</p>
    </div>
  )
}

function StatusPill({ value }: { value: string }) {
  const tone = value === 'Approved' || value === 'Ready' || value === 'Active'
    ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-200'
    : value === 'Rejected'
      ? 'border-rose-500/30 bg-rose-500/10 text-rose-200'
      : 'border-amber-500/30 bg-amber-500/10 text-amber-200'

  return <span className={`rounded-full border px-3 py-1 text-xs font-medium ${tone}`}>{value}</span>
}

function ProfileCardMarkdown({
  content,
  variant,
}: {
  content: string
  variant: 'compact' | 'detail'
}) {
  const paragraphClassName = variant === 'detail'
    ? 'text-base font-semibold leading-7 text-white'
    : 'text-sm font-medium leading-6 text-white'
  const listClassName = variant === 'detail'
    ? 'mt-2 list-disc space-y-1 pl-5 text-sm leading-6 text-slate-200'
    : 'mt-2 list-disc space-y-1 pl-5 text-sm leading-6 text-slate-200'
  const components: Components = {
    p: ({ children }) => <p className={paragraphClassName}>{children}</p>,
    ul: ({ children }) => <ul className={listClassName}>{children}</ul>,
    ol: ({ children }) => <ol className={`${listClassName} list-decimal`}>{children}</ol>,
    li: ({ children }) => <li>{children}</li>,
    strong: ({ children }) => <strong className="font-semibold text-white">{children}</strong>,
    em: ({ children }) => <em className="italic text-slate-100">{children}</em>,
    code: ({ children }) => <code className="rounded bg-slate-800 px-1 py-0.5 text-xs text-cyan-200">{children}</code>,
    a: ({ children, href }) => (
      <a className="text-cyan-300 underline underline-offset-2 hover:text-cyan-200" href={href} rel="noreferrer" target="_blank">
        {children}
      </a>
    ),
  }

  return (
    <div className="space-y-2">
      <ReactMarkdown components={components}>
        {content}
      </ReactMarkdown>
    </div>
  )
}

function EvidenceItemCard({ item }: { item: ChangeSetEvidenceItemResponse }) {
  const confidence = formatConfidence(item.confidence)

  return (
    <li className="rounded-xl border border-slate-800 bg-slate-900/60 p-3 text-sm text-slate-300">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-2">
          <div className="flex flex-wrap items-center gap-2">
            <span className="rounded-full border border-cyan-500/30 bg-cyan-500/10 px-2.5 py-1 text-[11px] font-medium uppercase tracking-[0.18em] text-cyan-200">
              {item.kind}
            </span>
            <span className="break-all font-medium text-white">{item.reference}</span>
          </div>
          <p className="leading-6 text-slate-300">{item.summary}</p>
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-x-4 gap-y-2 text-xs text-slate-400">
        {confidence && <span>confidence {confidence}</span>}
        {item.source && <span>source {item.source}</span>}
        {item.eventType && <span>event {item.eventType}</span>}
        {item.eventId && <span className="break-all">event ID {item.eventId}</span>}
        {item.occurredAt && <span>observed {formatDateTime(item.occurredAt)}</span>}
      </div>
    </li>
  )
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl border border-slate-800 bg-slate-900/60 p-3">
      <p className="text-xs uppercase tracking-[0.18em] text-slate-500">{label}</p>
      <p className="mt-2 text-sm text-slate-200 break-all">{value}</p>
    </div>
  )
}

function DetailList({
  title,
  emptyMessage,
  children,
}: {
  title: string
  emptyMessage: string
  children: ReactNode
}) {
  const hasChildren = Array.isArray(children) ? children.length > 0 : Boolean(children)

  return (
    <div>
      <h4 className="text-sm font-semibold text-white">{title}</h4>
      {hasChildren ? <ul className="mt-3 space-y-3">{children}</ul> : <p className="mt-2 text-sm text-slate-500">{emptyMessage}</p>}
    </div>
  )
}

function ListState({
  loading,
  error,
  count,
  emptyMessage,
}: {
  loading: boolean
  error: string | null
  count: number
  emptyMessage: string
}) {
  if (loading) {
    return <p className="text-sm text-slate-400">Loading...</p>
  }

  if (error) {
    return <p className="text-sm text-rose-300">{error}</p>
  }

  if (count > 0) {
    return null
  }

  return <p className="text-sm text-slate-500">{emptyMessage}</p>
}

function RequestFeedback({ state }: { state: RequestState }) {
  return (
    <div className="space-y-2">
      {state.error && <p className="text-sm text-rose-300">{state.error}</p>}
      {state.success && <p className="text-sm text-emerald-300">{state.success}</p>}
    </div>
  )
}

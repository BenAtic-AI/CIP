import { InteractionStatus } from '@azure/msal-browser'
import { MsalProvider, useIsAuthenticated, useMsal } from '@azure/msal-react'
import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { entraAuth } from './config'
import { loginWithPopup, logoutWithPopup, msalInstance } from './client'

type AuthViewState = 'disabled' | 'incomplete' | 'signed_out' | 'signed_in'

type AuthContextValue = {
  enabled: boolean
  ready: boolean
  status: AuthViewState
  busy: boolean
  accountLabel: string | null
  message: string
  error: string | null
  missing: string[]
  signIn: () => Promise<void>
  signOut: () => Promise<void>
}

function normalizeAuthError(error: unknown) {
  if (error instanceof Error) {
    return error.message
  }

  return 'Unexpected authentication failure.'
}

function createStaticAuthValue(): AuthContextValue {
  if (entraAuth.status === 'incomplete') {
    return {
      enabled: true,
      ready: false,
      status: 'incomplete',
      busy: false,
      accountLabel: null,
      message: `Entra auth is enabled but incomplete. Missing ${entraAuth.missing.join(', ')}.`,
      error: null,
      missing: [...entraAuth.missing],
      signIn: async () => {},
      signOut: async () => {},
    }
  }

  return {
    enabled: entraAuth.enabled,
    ready: false,
    status: 'disabled',
    busy: false,
    accountLabel: null,
    message: 'Entra auth is off. API requests continue without bearer tokens.',
    error: null,
    missing: [],
    signIn: async () => {},
    signOut: async () => {},
  }
}

const AuthContext = createContext<AuthContextValue>(createStaticAuthValue())

function ConfiguredAuthProvider({ children }: { children: ReactNode }) {
  const { accounts, inProgress, instance } = useMsal()
  const isAuthenticated = useIsAuthenticated()
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const activeAccount = instance.getActiveAccount()
    const nextAccount = activeAccount ?? accounts[0]

    if (nextAccount && activeAccount?.homeAccountId !== nextAccount.homeAccountId) {
      instance.setActiveAccount(nextAccount)
    }
  }, [accounts, instance])

  const activeAccount = instance.getActiveAccount() ?? accounts[0] ?? null

  const signIn = useCallback(async () => {
    setError(null)

    try {
      await loginWithPopup()
    } catch (nextError: unknown) {
      setError(normalizeAuthError(nextError))
    }
  }, [])

  const signOut = useCallback(async () => {
    setError(null)

    try {
      await logoutWithPopup()
    } catch (nextError: unknown) {
      setError(normalizeAuthError(nextError))
    }
  }, [])

  const value = useMemo<AuthContextValue>(() => ({
    enabled: true,
    ready: true,
    status: isAuthenticated ? 'signed_in' : 'signed_out',
    busy: inProgress !== InteractionStatus.None,
    accountLabel: activeAccount?.name ?? activeAccount?.username ?? null,
    message: isAuthenticated
      ? 'Signed in. API requests will try to attach a bearer token.'
      : 'Signed out. Sign in to attach bearer tokens to API requests.',
    error,
    missing: [],
    signIn,
    signOut,
  }), [activeAccount?.name, activeAccount?.username, error, inProgress, isAuthenticated, signIn, signOut])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function AuthProvider({ children }: { children: ReactNode }) {
  if (!msalInstance || !entraAuth.ready) {
    return <AuthContext.Provider value={createStaticAuthValue()}>{children}</AuthContext.Provider>
  }

  return (
    <MsalProvider instance={msalInstance}>
      <ConfiguredAuthProvider>{children}</ConfiguredAuthProvider>
    </MsalProvider>
  )
}

export function useAuth() {
  return useContext(AuthContext)
}

import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
  type AuthenticationResult,
} from '@azure/msal-browser'
import { entraAuth, msalConfiguration } from './config'

export const msalInstance = msalConfiguration ? new PublicClientApplication(msalConfiguration) : null

let initializationPromise: Promise<void> | null = null

function getSignInScopes() {
  return [...new Set([...entraAuth.loginScopes, ...entraAuth.apiScopes])]
}

function setActiveAccount(result: AuthenticationResult | null) {
  if (result?.account && msalInstance) {
    msalInstance.setActiveAccount(result.account)
  }
}

export function getActiveAccount(): AccountInfo | null {
  if (!msalInstance) {
    return null
  }

  return msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0] ?? null
}

export async function initializeAuthClient() {
  if (!msalInstance) {
    return
  }

  if (!initializationPromise) {
    initializationPromise = (async () => {
      await msalInstance.initialize()
      setActiveAccount(await msalInstance.handleRedirectPromise())

      const existingAccount = getActiveAccount()
      if (existingAccount) {
        msalInstance.setActiveAccount(existingAccount)
      }
    })()
  }

  await initializationPromise
}

export async function loginWithPopup() {
  if (!msalInstance || !entraAuth.ready) {
    return null
  }

  await initializeAuthClient()
  const result = await msalInstance.loginPopup({ scopes: getSignInScopes() })
  setActiveAccount(result)
  return result.account ?? null
}

export async function logoutWithPopup() {
  if (!msalInstance) {
    return
  }

  await initializeAuthClient()

  await msalInstance.logoutPopup({
    account: getActiveAccount() ?? undefined,
    postLogoutRedirectUri: entraAuth.redirectUri || (typeof window !== 'undefined' ? window.location.origin : undefined),
  })
}

export async function acquireAccessToken() {
  if (!msalInstance || !entraAuth.ready) {
    return null
  }

  await initializeAuthClient()

  const account = getActiveAccount()
  if (!account) {
    return null
  }

  try {
    const result = await msalInstance.acquireTokenSilent({
      account,
      scopes: entraAuth.apiScopes,
    })

    return result.accessToken || null
  } catch (error: unknown) {
    if (error instanceof InteractionRequiredAuthError) {
      const result = await msalInstance.acquireTokenPopup({
        account,
        scopes: entraAuth.apiScopes,
      })

      setActiveAccount(result)
      return result.accessToken || null
    }

    throw error
  }
}

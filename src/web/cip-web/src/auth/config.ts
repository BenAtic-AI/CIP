import { BrowserCacheLocation, LogLevel, type Configuration } from '@azure/msal-browser'

export type EntraAuthStatus = 'disabled' | 'incomplete' | 'ready'

function parseBooleanFlag(value: string | undefined) {
  return value?.trim().toLowerCase() === 'true'
}

function parseScopes(value: string | undefined) {
  return value
    ?.split(/[\s,]+/)
    .map((scope) => scope.trim())
    .filter(Boolean) ?? []
}

const enabled = parseBooleanFlag(import.meta.env.VITE_ENTRA_ENABLED)
const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID?.trim()
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID?.trim()
const redirectUri = import.meta.env.VITE_ENTRA_REDIRECT_URI?.trim()
const authority = import.meta.env.VITE_ENTRA_AUTHORITY?.trim() || (tenantId ? `https://login.microsoftonline.com/${tenantId}` : '')
const apiScopes = parseScopes(import.meta.env.VITE_ENTRA_API_SCOPES)
const loginScopes = parseScopes(import.meta.env.VITE_ENTRA_LOGIN_SCOPES)

const missing: string[] = []

if (enabled && !clientId) {
  missing.push('VITE_ENTRA_CLIENT_ID')
}

if (enabled && !authority) {
  missing.push('VITE_ENTRA_TENANT_ID or VITE_ENTRA_AUTHORITY')
}

if (enabled && apiScopes.length === 0) {
  missing.push('VITE_ENTRA_API_SCOPES')
}

const status: EntraAuthStatus = !enabled ? 'disabled' : missing.length > 0 ? 'incomplete' : 'ready'

export const entraAuth = {
  enabled,
  status,
  ready: status === 'ready',
  authority,
  clientId,
  tenantId,
  apiScopes,
  loginScopes: loginScopes.length > 0 ? loginScopes : ['openid', 'profile', 'email'],
  redirectUri,
  missing,
} as const

export const msalConfiguration: Configuration | null = !entraAuth.ready || !entraAuth.clientId
  ? null
  : {
      auth: {
        clientId: entraAuth.clientId,
        authority: entraAuth.authority,
        redirectUri: entraAuth.redirectUri || (typeof window !== 'undefined' ? window.location.origin : undefined),
        postLogoutRedirectUri: entraAuth.redirectUri || (typeof window !== 'undefined' ? window.location.origin : undefined),
      },
      cache: {
        cacheLocation: BrowserCacheLocation.LocalStorage,
      },
      system: {
        loggerOptions: {
          piiLoggingEnabled: false,
          loggerCallback: (_level: LogLevel, _message: string, containsPii: boolean) => {
            if (containsPii) {
              return
            }
          },
          logLevel: LogLevel.Warning,
        },
      },
    }

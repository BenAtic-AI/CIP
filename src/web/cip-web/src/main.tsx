import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App'
import { initializeAuthClient } from './auth/client'
import { AuthProvider } from './auth/provider'
import './index.css'

function renderApp() {
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <AuthProvider>
        <App />
      </AuthProvider>
    </React.StrictMode>,
  )
}

void initializeAuthClient()
  .catch((error: unknown) => {
    console.error('Failed to initialize Entra auth client.', error)
  })
  .finally(() => {
    renderApp()
  })

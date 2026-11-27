import { useState } from 'react'
import { login } from './api'

interface Props {
  passwordConfigured: boolean
  onLoggedIn: () => void
}

export function LoginScreen({ passwordConfigured, onLoggedIn }: Props) {
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    if (busy || !password) return

    setBusy(true)
    setError(null)

    try {
      await login(password)
      setPassword('')
      onLoggedIn()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="app login-screen">
      <div className="login-card">
        <div className="brand">
          <span className="brand-mark">XP</span>
          <div>
            <h1>XrmPocket</h1>
            <p>Inloggen</p>
          </div>
        </div>

        {!passwordConfigured ? (
          <p className="notice error">
            Er is nog geen wachtwoord ingesteld op de server. Zet de omgevingsvariabele XRMPOCKET_PASSWORD en start de server opnieuw.
          </p>
        ) : (
          <form onSubmit={(event) => void submit(event)}>
            <label className="field">
              <span>Wachtwoord</span>
              <input
                type="password"
                autoFocus
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </label>
            <button className="run" type="submit" disabled={busy || !password}>
              {busy ? 'Bezig…' : 'Inloggen'}
            </button>
            {error && <p className="notice error">{error}</p>}
          </form>
        )}
      </div>
    </div>
  )
}

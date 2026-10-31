import { useEffect, useState } from 'react'
import {
  deleteConnection,
  saveConnection,
  saveSecret,
  setDefaultConnection,
  signOut,
  startSignIn,
  testConnection,
  type ConnectionInput,
  type ConnectionTestResult,
  type EnvironmentTemplate,
  type Highlight,
  type SavedConnection,
} from './api'
import { ConnectionTestSummary } from './ConnectionTestSummary'

interface Props {
  open: boolean
  connections: SavedConnection[]
  templates: EnvironmentTemplate[]
  redirectUri: string
  onClose: () => void
  onChanged: () => void
}

const EMPTY_FORM: ConnectionInput = {
  name: '',
  kind: 'dataverse',
  authType: 'app-only',
  environment: 'test',
  url: 'https://operations-sav-test.crm4.dynamics.com/',
  tenantId: null,
  clientId: null,
  timeoutSeconds: 120,
  highlight: 'test',
}

/**
 * Verbindingen beheren — het equivalent van de Connection Manager in XrmToolBox:
 * lijst met opgeslagen verbindingen, nieuwe verbinding, standaard kiezen, testen en
 * verwijderen. Het client secret gaat als losse stap naar de backend (nooit in de
 * verbinding zelf).
 */
export function ConnectionManager({ open, connections, templates, redirectUri, onClose, onChanged }: Props) {
  const [form, setForm] = useState<ConnectionInput>(EMPTY_FORM)
  const [editing, setEditing] = useState<string | null>(null)
  const [secret, setSecret] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [tests, setTests] = useState<Record<string, ConnectionTestResult>>({})

  useEffect(() => {
    if (!open) {
      setForm(EMPTY_FORM)
      setEditing(null)
      setSecret('')
      setMessage(null)
      setError(null)
    }
  }, [open])

  if (!open) return null

  function startNew() {
    setEditing(null)
    setForm(EMPTY_FORM)
    setSecret('')
    setMessage(null)
    setError(null)
  }

  function startEdit(connection: SavedConnection) {
    setEditing(connection.id)
    setForm({
      id: connection.id,
      name: connection.name,
      kind: connection.kind,
      authType: connection.authType,
      environment: connection.environment,
      url: connection.url,
      tenantId: connection.tenantId,
      clientId: connection.clientId,
      timeoutSeconds: connection.timeoutSeconds,
      highlight: connection.highlight,
    })
    setSecret('')
    setMessage(null)
    setError(null)
  }

  function applyTemplate(templateId: string) {
    const template = templates.find((item) => item.id === templateId)
    if (!template) return

    setForm((current) => ({
      ...current,
      environment: template.id,
      url: template.url,
      name: current.name || template.label,
      highlight: (template.production ? 'production' : (template.id as Highlight)) ?? 'none',
    }))
  }

  async function handleSave() {
    setBusy(true)
    setError(null)
    setMessage(null)

    try {
      const saved = await saveConnection(form)

      if (saved.authType === 'interactive') {
        setMessage(`'${saved.name}' bewaard. Klik nu op 'Aanmelden met Microsoft'.`)
      }

      if (secret.trim() && form.kind === 'dataverse') {
        await saveSecret(saved.id, secret.trim())
        setSecret('')

        // Meteen testen, zoals XrmToolBox na het invullen van een wachtwoord:
        // zo zie je direct of het secret en de application user kloppen.
        const testResult = await testConnection(saved.id)
        setTests((current) => ({ ...current, [saved.id]: testResult }))
      }

      setMessage(`'${saved.name}' bewaard.`)
      setEditing(saved.id)
      setForm((current) => ({ ...current, id: saved.id }))
      onChanged()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setBusy(false)
    }
  }

  async function handleTest(connection: SavedConnection) {
    setBusy(true)
    setError(null)

    try {
      const result = await testConnection(connection.id)
      setTests((current) => ({ ...current, [connection.id]: result }))
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setBusy(false)
    }
  }

  async function handleDefault(connection: SavedConnection) {
    setBusy(true)

    try {
      await setDefaultConnection(connection.id)
      onChanged()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setBusy(false)
    }
  }

  async function handleDelete(connection: SavedConnection) {
    if (!window.confirm(`Verbinding '${connection.name}' verwijderen?`)) return

    setBusy(true)

    try {
      await deleteConnection(connection.id)
      if (editing === connection.id) startNew()
      onChanged()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setBusy(false)
    }
  }

  async function handleSignIn(connection: SavedConnection) {
    setBusy(true)
    setError(null)

    try {
      const { authorizationUrl } = await startSignIn(connection.id)
      // De browser gaat naar Microsoft en komt terug op de callback-URL.
      window.location.href = authorizationUrl
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
      setBusy(false)
    }
  }

  async function handleSignOut(connection: SavedConnection) {
    setBusy(true)

    try {
      await signOut(connection.id)
      setTests((current) => {
        const next = { ...current }
        delete next[connection.id]
        return next
      })
      onChanged()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Verbindingen beheren">
      <div className="modal">
        <header className="modal-head">
          <h2>Verbindingen</h2>
          <button className="ghost" onClick={onClose} aria-label="Sluiten">
            ✕
          </button>
        </header>

        <div className="modal-body">
          <ul className="connection-list">
            {connections.map((connection) => {
              const test = tests[connection.id]

              return (
                <li key={connection.id} className={editing === connection.id ? 'selected' : undefined}>
                  <div className="connection-main">
                    <span className={`badge badge-${connection.highlight}`}>{connection.environment}</span>
                    <div>
                      <strong>
                        {connection.name}
                        {connection.isDefault && <span className="default-mark" title="Standaardverbinding">★</span>}
                      </strong>
                      <p className="meta">
                        {connection.kind === 'mock' ? 'Synthetische data' : connection.url}
                        {connection.configured ? '' : ` — ${connection.note}`}
                      </p>
                      {test && <ConnectionTestSummary test={test} compact />}
                    </div>
                  </div>

                  {connection.authType === 'interactive' && (
                    <p className={connection.signedIn ? 'test-ok' : 'hint'}>
                      {connection.signedIn
                        ? `Aangemeld als ${connection.account ?? 'onbekend account'}`
                        : 'Nog niet aangemeld met een Microsoft-account.'}
                    </p>
                  )}

                  <div className="connection-actions">
                    {connection.authType === 'interactive' && !connection.signedIn && (
                      <button className="run small" disabled={busy} onClick={() => void handleSignIn(connection)}>
                        Aanmelden met Microsoft
                      </button>
                    )}
                    {connection.authType === 'interactive' && connection.signedIn && (
                      <button className="ghost" disabled={busy} onClick={() => void handleSignOut(connection)}>
                        Afmelden
                      </button>
                    )}
                    <button className="ghost" disabled={busy} onClick={() => void handleTest(connection)}>
                      Testen
                    </button>
                    <button className="ghost" disabled={busy || connection.isDefault} onClick={() => void handleDefault(connection)}>
                      Standaard
                    </button>
                    <button className="ghost" disabled={busy} onClick={() => startEdit(connection)}>
                      Bewerken
                    </button>
                    <button className="ghost danger" disabled={busy} onClick={() => void handleDelete(connection)}>
                      Verwijderen
                    </button>
                  </div>
                </li>
              )
            })}
          </ul>

          <form
            className="connection-form"
            onSubmit={(event) => {
              event.preventDefault()
              void handleSave()
            }}
          >
            <h3>{editing ? 'Verbinding bewerken' : 'Nieuwe verbinding'}</h3>

            <div className="form-grid">
              <label className="field">
                <span>Naam</span>
                <input
                  value={form.name}
                  onChange={(event) => setForm({ ...form, name: event.target.value })}
                  placeholder="bv. SAV-TEST"
                  required
                />
              </label>

              <label className="field">
                <span>Soort</span>
                <select
                  value={form.kind}
                  onChange={(event) =>
                    setForm({ ...form, kind: event.target.value as ConnectionInput['kind'] })
                  }
                >
                  <option value="dataverse">Dataverse</option>
                  <option value="mock">Mock (synthetisch)</option>
                </select>
              </label>

              {form.kind === 'dataverse' && (
                <>
                  <label className="field">
                    <span>Omgeving (sjabloon)</span>
                    <select value={form.environment} onChange={(event) => applyTemplate(event.target.value)}>
                      {templates.map((template) => (
                        <option key={template.id} value={template.id}>
                          {template.label}
                        </option>
                      ))}
                      <option value="custom">Eigen URL</option>
                    </select>
                  </label>

                  <label className="field wide">
                    <span>URL</span>
                    <input
                      value={form.url ?? ''}
                      onChange={(event) => setForm({ ...form, url: event.target.value })}
                      placeholder="https://operations-sav-test.crm4.dynamics.com/"
                      required
                    />
                  </label>

                  <label className="field">
                    <span>Authenticatie</span>
                    <select value={form.authType} onChange={(event) => setForm({ ...form, authType: event.target.value })}>
                      <option value="interactive">Interactief (aanmelden met je account)</option>
                      <option value="app-only">App-only (client credentials)</option>
                    </select>
                  </label>

                  <label className="field">
                    <span>Time-out (seconden)</span>
                    <input
                      type="number"
                      min={5}
                      max={600}
                      value={form.timeoutSeconds}
                      onChange={(event) => setForm({ ...form, timeoutSeconds: Number(event.target.value) })}
                    />
                  </label>

                  <label className="field">
                    <span>Client-ID (leeg = standaard)</span>
                    <input
                      value={form.clientId ?? ''}
                      onChange={(event) => setForm({ ...form, clientId: event.target.value || null })}
                      placeholder="app registration id"
                    />
                  </label>

                  <label className="field">
                    <span>Tenant-ID (leeg = standaard)</span>
                    <input
                      value={form.tenantId ?? ''}
                      onChange={(event) => setForm({ ...form, tenantId: event.target.value || null })}
                      placeholder="tenant id"
                    />
                  </label>

                  {form.authType === 'app-only' ? (
                    <label className="field wide">
                      <span>Client secret {editing ? '(leeg = ongewijzigd)' : ''}</span>
                      <input
                        type="password"
                        value={secret}
                        onChange={(event) => setSecret(event.target.value)}
                        placeholder="Value uit de app registration"
                        autoComplete="off"
                      />
                    </label>
                  ) : (
                    <p className="hint wide">
                      Bij interactief meld je je aan met je eigen Microsoft-account; er is geen client
                      secret nodig. Zet in de app registration (platform <strong>Web</strong>) deze
                      redirect-URI:{' '}
                      <code>{redirectUri}</code> en geef de gedelegeerde toestemming{' '}
                      <code>Dataverse &gt; user_impersonation</code> (met admin consent).
                    </p>
                  )}
                </>
              )}
            </div>

            {message && <p className="test-ok">{message}</p>}
            {error && <p className="test-error">{error}</p>}

            <div className="form-actions">
              <button type="submit" className="run" disabled={busy}>
                {busy ? 'Bezig…' : 'Bewaren'}
              </button>
              {editing && (
                <button type="button" className="ghost" onClick={startNew}>
                  Nieuwe verbinding
                </button>
              )}
            </div>
          </form>
        </div>
      </div>
    </div>
  )
}

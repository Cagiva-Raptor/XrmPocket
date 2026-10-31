import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  fetchEnvironmentTemplates,
  formatCell,
  listConnections,
  runQuery,
  testConnection,
  type ConnectionTestResult,
  type EnvironmentTemplate,
  type QueryResponse,
  type SavedConnection,
} from './api'
import { ConnectionManager } from './ConnectionManager'
import { ConnectionTestSummary } from './ConnectionTestSummary'
import './App.css'

const SQL_STORAGE_KEY = 'xrmpocket.sql'
const CONNECTION_STORAGE_KEY = 'xrmpocket.connection'

const EXAMPLES: { label: string; sql: string }[] = [
  {
    label: 'Accounts met omzet',
    sql: 'SELECT name, revenue, numberofemployees, createdon\nFROM account\nWHERE revenue > 500000\nORDER BY revenue DESC',
  },
  {
    label: 'Contacten met hun bedrijf (join)',
    sql: 'SELECT c.fullname, c.emailaddress1, a.name AS company\nFROM contact c\nJOIN account a ON c.parentcustomerid = a.accountid\nORDER BY c.fullname',
  },
  {
    label: 'Aantal accounts per status',
    sql: 'SELECT statecode, COUNT(*) AS aantal, SUM(revenue) AS omzet\nFROM account\nGROUP BY statecode\nORDER BY aantal DESC',
  },
  {
    label: 'Openstaande cases',
    sql: "SELECT title, prioritycode, createdon\nFROM incident\nWHERE statecode = 0\nORDER BY prioritycode",
  },
]

function App() {
  const [connections, setConnections] = useState<SavedConnection[]>([])
  const [templates, setTemplates] = useState<EnvironmentTemplate[]>([])
  const [connectionId, setConnectionId] = useState(() => localStorage.getItem(CONNECTION_STORAGE_KEY) ?? '')
  const [redirectUri, setRedirectUri] = useState('')
  const [notice, setNotice] = useState<string | null>(null)
  const [managerOpen, setManagerOpen] = useState(false)
  const [test, setTest] = useState<ConnectionTestResult | null>(null)
  const [sql, setSql] = useState(() => localStorage.getItem(SQL_STORAGE_KEY) ?? EXAMPLES[0].sql)
  const [maxRows, setMaxRows] = useState(200)
  const [response, setResponse] = useState<QueryResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [fatal, setFatal] = useState<string | null>(null)
  const editorRef = useRef<HTMLTextAreaElement>(null)

  const reload = useCallback(async () => {
    try {
      const [list, environmentTemplates] = await Promise.all([listConnections(), fetchEnvironmentTemplates()])
      setConnections(list.connections)
      setTemplates(environmentTemplates)
      setRedirectUri(list.interactiveRedirectUri)

      const stored = localStorage.getItem(CONNECTION_STORAGE_KEY)
      const known = list.connections.find((connection) => connection.id === stored)

      if (!known) {
        const fallback =
          list.connections.find((connection) => connection.id === list.defaultConnectionId) ??
          list.connections.find((connection) => connection.configured) ??
          list.connections[0]

        if (fallback) setConnectionId(fallback.id)
      }
    } catch (error) {
      setFatal(error instanceof Error ? error.message : String(error))
    }
  }, [])

  useEffect(() => {
    void reload()
  }, [reload])

  // Terugkeer van de Microsoft-aanmelding: ?auth=ok of ?auth=error&code&message.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const auth = params.get('auth')

    if (!auth) return

    const connection = params.get('connection')
    const code = params.get('code')
    const message = params.get('message')

    if (auth === 'ok') {
      setNotice(`Aangemeld${connection ? '' : ''}. Test de verbinding om je gebruiker en rollen te zien.`)
      if (connection) setConnectionId(connection)
      setManagerOpen(true)
    } else {
      setFatal(`${code ?? 'auth_error'}: ${message ?? 'Aanmelden mislukt.'}`)
      setManagerOpen(true)
    }

    window.history.replaceState({}, '', window.location.pathname)
    void reload()
  }, [reload])

  useEffect(() => {
    localStorage.setItem(SQL_STORAGE_KEY, sql)
  }, [sql])

  useEffect(() => {
    if (connectionId) localStorage.setItem(CONNECTION_STORAGE_KEY, connectionId)
    setTest(null)
  }, [connectionId])

  const connection = useMemo(
    () => connections.find((item) => item.id === connectionId),
    [connections, connectionId],
  )

  async function handleRun() {
    if (loading) return

    setLoading(true)
    setFatal(null)

    try {
      setResponse(await runQuery(sql, connectionId || null, maxRows))
    } catch (error) {
      setFatal(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  async function handleTest() {
    if (!connection) return

    setLoading(true)

    try {
      setTest(await testConnection(connection.id))
    } catch (error) {
      setFatal(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  function handleEditorKeyDown(event: React.KeyboardEvent<HTMLTextAreaElement>) {
    if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
      event.preventDefault()
      void handleRun()
      return
    }

    if (event.key === 'Tab') {
      event.preventDefault()
      const target = event.currentTarget
      const start = target.selectionStart
      const end = target.selectionEnd
      setSql(`${sql.slice(0, start)}  ${sql.slice(end)}`)
      requestAnimationFrame(() => {
        target.selectionStart = target.selectionEnd = start + 2
      })
    }
  }

  const error = response?.error ?? null

  return (
    <div className="app">
      <header className="topbar">
        <div className="brand">
          <span className="brand-mark">XP</span>
          <div>
            <h1>XrmPocket</h1>
            <p>SQL 4 CDS — Dataverse vanaf je telefoon</p>
          </div>
        </div>

        <div className="toolbar">
          <label className="field">
            <span>Verbinding</span>
            <select
              value={connectionId}
              onChange={(event) => setConnectionId(event.target.value)}
              disabled={loading}
            >
              {connections.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                  {item.configured ? '' : ' (nog niet bruikbaar)'}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Max. rijen</span>
            <select value={maxRows} onChange={(event) => setMaxRows(Number(event.target.value))} disabled={loading}>
              {[50, 200, 500, 1000].map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <button className="ghost" onClick={() => setManagerOpen(true)} disabled={loading}>
            Verbindingen…
          </button>

          <button className="run" onClick={() => void handleRun()} disabled={loading}>
            {loading ? 'Bezig…' : 'Uitvoeren'}
            <kbd>⌘↵</kbd>
          </button>
        </div>
      </header>

      {connection && (
        <div className="statusbar">
          <span className={`badge badge-${connection.highlight}`}>{connection.environment}</span>
          <strong>{connection.name}</strong>
          <span className="meta">{connection.kind === 'mock' ? 'synthetische data' : connection.url}</span>

          <button className="ghost small" onClick={() => void handleTest()} disabled={loading}>
            Testen
          </button>

          {test && <ConnectionTestSummary test={test} />}

          {!connection.configured && !test && connection.note && <span className="hint">{connection.note}</span>}
        </div>
      )}

      {fatal && <p className="notice error">{fatal}</p>}
      {notice && <p className="notice">{notice}</p>}

      <section className="editor">
        <div className="editor-head">
          <label className="field field-inline">
            <span>Voorbeeld</span>
            <select
              value=""
              onChange={(event) => {
                const example = EXAMPLES.find((item) => item.label === event.target.value)
                if (example) {
                  setSql(example.sql)
                  editorRef.current?.focus()
                }
              }}
            >
              <option value="">Kies een query…</option>
              {EXAMPLES.map((example) => (
                <option key={example.label} value={example.label}>
                  {example.label}
                </option>
              ))}
            </select>
          </label>
          <span className="hint">⌘/Ctrl + Enter voert de query uit</span>
        </div>

        <textarea
          ref={editorRef}
          className="sql-editor"
          value={sql}
          onChange={(event) => setSql(event.target.value)}
          onKeyDown={handleEditorKeyDown}
          spellCheck={false}
          rows={9}
        />
      </section>

      <section className="results">
        <div className="results-head">
          <h2>Resultaat</h2>
          {response && (
            <p className="meta">
              {response.rowCount} {response.rowCount === 1 ? 'rij' : 'rijen'}
              {response.truncated ? ' (afgekapt op de limiet)' : ''} · {response.durationMs.toFixed(0)} ms ·{' '}
              {response.connectionName}
            </p>
          )}
        </div>

        {error && (
          <div className="notice error">
            <strong>{error.code}</strong>
            <p>{error.message}</p>
            {error.hint && <p className="hint">{error.hint}</p>}
          </div>
        )}

        {response?.ok && response.columns.length > 0 && (
          <div className="grid-wrap">
            <table>
              <thead>
                <tr>
                  <th className="row-number">#</th>
                  {response.columns.map((column) => (
                    <th key={column.name} title={column.type}>
                      {column.name}
                      <span className="type">{column.type}</span>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {response.rows.map((row, rowIndex) => (
                  <tr key={rowIndex}>
                    <td className="row-number">{rowIndex + 1}</td>
                    {row.map((cell, cellIndex) => (
                      <td
                        key={cellIndex}
                        className={
                          cell === null ? 'cell-null' : typeof cell === 'number' ? 'cell-number' : undefined
                        }
                      >
                        {formatCell(cell)}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {response?.ok && response.rows.length === 0 && <p className="empty">De query gaf geen rijen terug.</p>}

        {response && response.fetchXml.length > 0 && (
          <details className="fetchxml">
            <summary>FetchXML ({response.fetchXml.length})</summary>
            {response.fetchXml.map((xml, index) => (
              <pre key={index}>{xml}</pre>
            ))}
          </details>
        )}
      </section>

      <ConnectionManager
        open={managerOpen}
        connections={connections}
        templates={templates}
        redirectUri={redirectUri}
        onClose={() => setManagerOpen(false)}
        onChanged={() => void reload()}
      />
    </div>
  )
}

export default App

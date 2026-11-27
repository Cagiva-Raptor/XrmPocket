import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  deleteStatement,
  fetchEnvironmentTemplates,
  formatCell,
  getSession,
  listConnections,
  listStatements,
  logout,
  runQuery,
  saveStatement,
  updateStatement,
  type SavedStatement,
  testConnection,
  type ConnectionTestResult,
  type EnvironmentTemplate,
  type QueryResponse,
  type SavedConnection,
} from './api'
import { SqlEditor, type SqlEditorHandle } from './SqlEditor'
import { Tools, TOOL_TABS, type ToolId } from './Tools'
import { LoginScreen } from './Login'
import { ConnectionManager } from './ConnectionManager'
import { ConnectionTestSummary } from './ConnectionTestSummary'
import { loadGlobalSettings, saveGlobalSettings, type GlobalSettings } from './settings'
import { formatIdList } from './idList'
import './App.css'

const SQL_STORAGE_KEY = 'xrmpocket.sql'
const CONNECTION_STORAGE_KEY = 'xrmpocket.connection'
const HIDE_DEMO_STORAGE_KEY = 'xrmpocket.hideDemo'
const VIM_STORAGE_KEY = 'xrmpocket.vim'
const THEME_STORAGE_KEY = 'xrmpocket.theme'
const VIEW_STORAGE_KEY = 'xrmpocket.view'
const SHOW_EXAMPLES_KEY = 'xrmpocket.showExamples'

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
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [toolSettingsOpen, setToolSettingsOpen] = useState(false)
  const [xlsOpen, setXlsOpen] = useState(false)
  const [xlsText, setXlsText] = useState('')
  const [xlsInvalid, setXlsInvalid] = useState<string[]>([])
  const [view, setView] = useState<'sql' | ToolId>(() => {
    const saved = localStorage.getItem(VIEW_STORAGE_KEY)
    return saved === 'sql' || TOOL_TABS.some((item) => item.id === saved) ? (saved as 'sql' | ToolId) : 'sql'
  })

  useEffect(() => {
    localStorage.setItem(VIEW_STORAGE_KEY, view)
    const label = view === 'sql' ? 'SQL 4 CDS' : TOOL_TABS.find((item) => item.id === view)?.label ?? 'SQL 4 CDS'
    document.title = `XrmPocket — ${label}`
  }, [view])
  const [auth, setAuth] = useState<'checking' | 'in' | 'out'>('checking')
  const [passwordConfigured, setPasswordConfigured] = useState(true)
  const [hideDemo, setHideDemo] = useState(() => localStorage.getItem(HIDE_DEMO_STORAGE_KEY) === '1')
  const [test, setTest] = useState<ConnectionTestResult | null>(null)
  const [sql, setSql] = useState(() => localStorage.getItem(SQL_STORAGE_KEY) ?? EXAMPLES[0].sql)
  const [globalSettings, setGlobalSettings] = useState<GlobalSettings>(loadGlobalSettings)
  const maxRows = globalSettings.maxRows

  function setMaxRows(value: number) {
    const next = { ...globalSettings, maxRows: value }
    setGlobalSettings(next)
    saveGlobalSettings(next)
  }
  const [response, setResponse] = useState<QueryResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [running, setRunning] = useState(false)
  const runController = useRef<AbortController | null>(null)
  const [fatal, setFatal] = useState<string | null>(null)
  const editorRef = useRef<SqlEditorHandle>(null)
  const sqlFileRef = useRef<HTMLInputElement>(null)
  const [vimMode, setVimMode] = useState(() => localStorage.getItem(VIM_STORAGE_KEY) === '1')
  const [theme, setTheme] = useState<'light' | 'dark'>(() => (localStorage.getItem(THEME_STORAGE_KEY) === 'dark' ? 'dark' : 'light'))

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  }, [theme])
  const [statements, setStatements] = useState<SavedStatement[]>([])
  const [selectedStatement, setSelectedStatement] = useState('')
  const [showExamples, setShowExamples] = useState(() => localStorage.getItem(SHOW_EXAMPLES_KEY) !== '0')

  const refreshStatements = useCallback(async () => {
    try {
      setStatements((await listStatements()).statements)
    } catch {
      // Zonder opgeslagen statements werkt de editor gewoon door.
    }
  }, [])

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
    void getSession()
      .then((session) => {
        setPasswordConfigured(session.passwordConfigured)
        setAuth(session.authenticated ? 'in' : 'out')
      })
      .catch(() => setAuth('out'))
  }, [])

  useEffect(() => {
    const handler = () => setAuth('out')
    window.addEventListener('xp-auth-required', handler)
    return () => window.removeEventListener('xp-auth-required', handler)
  }, [])

  useEffect(() => {
    if (auth === 'in') {
      void reload()
      void refreshStatements()
    }
  }, [auth, reload, refreshStatements])

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

  const hasRealConnections = connections.some((item) => item.kind !== 'mock')

  const visibleConnections = useMemo(
    () => (hideDemo && hasRealConnections ? connections.filter((item) => item.kind !== 'mock') : connections),
    [connections, hideDemo, hasRealConnections],
  )

  useEffect(() => {
    if (visibleConnections.length === 0) return
    if (!visibleConnections.some((item) => item.id === connectionId)) {
      setConnectionId(visibleConnections[0].id)
    }
  }, [visibleConnections, connectionId])

  function handleHideDemoChange(checked: boolean) {
    setHideDemo(checked)
    localStorage.setItem(HIDE_DEMO_STORAGE_KEY, checked ? '1' : '0')
  }

  async function handleSaveStatement() {
    const selectedSaved = statements.find((item) => item.id === selectedStatement)
    const selected = editorRef.current?.getSelectedText().trim() ?? ''
    const text = selected || sql

    try {
      if (selectedSaved) {
        if (!window.confirm(`Statement '${selectedSaved.name}' overschrijven?`)) return
        const saved = await updateStatement(selectedSaved.id, text)
        setSelectedStatement(saved.id)
        setNotice(`Statement '${saved.name}' overschreven (versleuteld).`)
      } else {
        const name = window.prompt('Naam voor dit statement')?.trim()
        if (!name) return
        const saved = await saveStatement(name, text)
        setSelectedStatement(saved.id)
        setNotice(`Statement '${name}' opgeslagen (versleuteld).`)
      }
      await refreshStatements()
    } catch (err) {
      setFatal(err instanceof Error ? err.message : String(err))
    }
  }

  async function handleDeleteStatement() {
    const saved = statements.find((item) => item.id === selectedStatement)
    if (!saved || !window.confirm(`Statement '${saved.name}' verwijderen?`)) return

    try {
      await deleteStatement(saved.id)
      setSelectedStatement('')
      await refreshStatements()
    } catch (err) {
      setFatal(err instanceof Error ? err.message : String(err))
    }
  }

  function stopRun() {
    runController.current?.abort()
  }

  async function handleRun(dryRun = false) {
    if (loading) return

    const selected = editorRef.current?.getSelectedText().trim() ?? ''
    const statement = selected || sql
    const controller = new AbortController()

    runController.current = controller
    setRunning(true)
    setLoading(true)
    setFatal(null)

    try {
      setResponse(await runQuery(statement, connectionId || null, maxRows, dryRun, controller.signal))
    } catch (error) {
      if (controller.signal.aborted) setNotice('Uitvoering gestopt.')
      else setFatal(error instanceof Error ? error.message : String(error))
    } finally {
      runController.current = null
      setRunning(false)
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

  const error = response?.error ?? null

  if (auth !== 'in') {
    return auth === 'checking' ? (
      <p className="empty">Laden…</p>
    ) : (
      <LoginScreen
        passwordConfigured={passwordConfigured}
        onLoggedIn={() => {
          setFatal(null)
          setAuth('in')
        }}
      />
    )
  }

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
              {visibleConnections.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                  {item.configured ? '' : ' (nog niet bruikbaar)'}
                </option>
              ))}
            </select>
          </label>

          <button
            className="ghost icon-button"
            onClick={() => setTheme((current) => (current === 'dark' ? 'light' : 'dark'))}
            aria-label={theme === 'dark' ? 'Lichte modus' : 'Donkere modus'}
            title={theme === 'dark' ? 'Lichte modus' : 'Donkere modus'}
          >
            {theme === 'dark' ? (
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
                <circle cx="12" cy="12" r="4" />
                <path d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M4.93 19.07l1.41-1.41M17.66 6.34l1.41-1.41" />
              </svg>
            ) : (
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
              </svg>
            )}
          </button>

          <button
            className="ghost icon-button"
            onClick={() => setSettingsOpen(true)}
            disabled={loading}
            aria-label="Globale instellingen"
            title="Globale instellingen"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <circle cx="12" cy="12" r="3" />
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
            </svg>
          </button>

          <button className="ghost" onClick={() => setManagerOpen(true)} disabled={loading}>
            Verbindingen…
          </button>

          <button
            className="ghost"
            onClick={() => {
              void logout().finally(() => setAuth('out'))
            }}
          >
            Uitloggen
          </button>
        </div>
      </header>

      {connection && (
        <div className={`statusbar hl-${connection.highlight}`}>
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

      <nav className="view-tabs" role="tablist">
        <button className={view === 'sql' ? 'tool-tab active' : 'tool-tab'} onClick={() => setView('sql')}>
          SQL
        </button>
        {TOOL_TABS.map((item) => (
          <button
            key={item.id}
            className={view === item.id ? 'tool-tab active' : 'tool-tab'}
            onClick={() => setView(item.id)}
          >
            {item.label}
          </button>
        ))}

        {(view === 'sql' || view === 'fetchxml') && (
        <button
          className="ghost icon-button settings-tab"
          onClick={() => (view === 'sql' ? setSettingsOpen(true) : setToolSettingsOpen(true))}
          aria-label="Instellingen"
          title={view === 'sql' ? 'Instellingen SQL' : 'Instellingen van dit gereedschap'}
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z" />
          </svg>
        </button>
        )}
      </nav>

      {view !== 'sql' && connection && (
        <Tools
          connectionId={connection.id}
          isMock={connection.kind === 'mock'}
          orgUrl={connection.url}
          maxRows={maxRows}
          tool={view}
          settingsOpen={toolSettingsOpen}
          onCloseSettings={() => setToolSettingsOpen(false)}
        />
      )}

      {view === 'sql' && (
        <>
      <section className="editor">
        <div className="editor-head">
          {showExamples && (
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
          )}

          <label className="field field-inline">
            <span>Opgeslagen</span>
            <select
              value={selectedStatement}
              onChange={(event) => {
                const saved = statements.find((item) => item.id === event.target.value)
                setSelectedStatement(event.target.value)
                if (saved) {
                  setSql(saved.sql)
                  editorRef.current?.focus()
                }
              }}
            >
              <option value="">{statements.length ? 'Kies…' : 'Nog niets opgeslagen'}</option>
              {statements.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>
          <div className="icon-group">
            <input
              ref={sqlFileRef}
              type="file"
              accept=".sql,text/plain"
              hidden
              onChange={async (event) => {
                const file = event.target.files?.[0]
                if (!file) return
                const text = await file.text()
                setSql(text)
                setSelectedStatement('')
                setNotice(`SQL-bestand '${file.name}' geladen (${text.split(/\r?\n/).length} regels, ${text.length.toLocaleString('nl-BE')} tekens).`)
                event.target.value = ''
                editorRef.current?.focus()
              }}
            />
            <button
              className="ghost icon-button"
              onClick={() => sqlFileRef.current?.click()}
              disabled={loading}
              aria-label="SQL-bestand openen"
              title="SQL-bestand openen"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M3 7v12h18V7M3 7l3-3h5l2 3M8 13h8" />
              </svg>
            </button>
            <button
              className="ghost icon-button"
              onClick={() => void handleSaveStatement()}
              disabled={loading}
              title="Opslaan"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" />
                <path d="M17 21v-8H7v8M7 3v5h8" />
              </svg>
            </button>
            <button
              className="ghost icon-button danger"
              onClick={() => void handleDeleteStatement()}
              disabled={loading || !selectedStatement}
              aria-label="Verwijderen"
              title="Verwijderen"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6" />
              </svg>
            </button>
          </div>


          <div className="icon-group end">
            <button className="ghost xls-button" onClick={() => setXlsOpen(true)} title="Excel-ID's omzetten naar SQL">
              XLS ID 2 SQL
            </button>
            <button
              className="ghost icon-button"
              onClick={() => void handleRun(true)}
              disabled={loading}
              aria-label="Controleren"
              title="Controleren (dry run)"
            >
              <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                <path d="M9 12l2 2 4-4" />
              </svg>
            </button>
            <button
              className={running ? 'run icon-button stop' : 'run icon-button'}
              onClick={() => (running ? stopRun() : void handleRun())}
              aria-label={running ? 'Stoppen' : 'Uitvoeren'}
              title={running ? 'Stoppen' : '⌘/Ctrl + Enter'}
            >
              {running ? (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                  <rect x="5" y="5" width="14" height="14" rx="2" />
                </svg>
              ) : (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                  <path d="M7 4v16l13-8z" />
                </svg>
              )}
            </button>
          </div>
        </div>

        <SqlEditor
          ref={editorRef}
          value={sql}
          onChange={setSql}
          onRun={() => void handleRun()}
          vimMode={vimMode}
          dark={theme === 'dark'}
        />
        <p className="meta editor-stats">{sql.split(/\r?\n/).length} regels · {sql.length.toLocaleString('nl-BE')} tekens</p>
      </section>

      <section className="results">
        <div className="results-head">
          <h2>Resultaat</h2>
          {response && (
            <p className="meta">
              {response.rowsAffected != null
                ? `${response.rowsAffected} ${response.rowsAffected === 1 ? 'rij' : 'rijen'} gewijzigd`
                : `${response.rowCount} ${response.rowCount === 1 ? 'rij' : 'rijen'}`}
              {response.truncated ? ' (afgekapt op de limiet)' : ''} · {response.durationMs.toFixed(0)} ms ·{' '}
              {response.connectionName}
            </p>
          )}
        </div>

        {response?.messages?.length ? <pre className="script-messages">{response.messages.join('\n')}</pre> : null}

        {error && (
          <div className="notice error">
            <strong>{error.code}</strong>
            {error.line != null && (
              <p className="error-location">
                Regel {error.line}
                {error.column != null ? `, kolom ${error.column}` : ''}
              </p>
            )}
            <p>{error.message}</p>
            {error.hint && <p className="hint">{error.hint}</p>}
          </div>
        )}

        {response?.ok && response.resultSets.map((result, resultIndex) => (
          <div className="result-set" key={resultIndex}>
            {response.resultSets.length > 1 && <h3 className="result-label">Resultaat {resultIndex + 1}</h3>}
            <div className="grid-wrap">
              <table>
                <thead>
                  <tr>
                    <th className="row-number">#</th>
                    {result.columns.map((column) => (
                      <th key={column.name} title={column.type}>
                        {column.name}
                        <span className="type">{column.type}</span>
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {result.rows.map((row, rowIndex) => (
                    <tr key={rowIndex}>
                      <td className="row-number">{rowIndex + 1}</td>
                      {row.map((cell, cellIndex) => (
                        <td
                          key={cellIndex}
                          className={cell === null ? 'cell-null' : typeof cell === 'number' ? 'cell-number' : undefined}
                        >
                          {formatCell(cell)}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        ))}

        {response?.ok && response.resultSets.length === 0 && response.rowsAffected == null && (
          <p className="empty">De query gaf geen rijen terug.</p>
        )}

        {response && response.fetchXml.length > 0 && (
          <details className="fetchxml">
            <summary>FetchXML ({response.fetchXml.length})</summary>
            {response.fetchXml.map((xml, index) => (
              <pre key={index}>{xml}</pre>
            ))}
          </details>
        )}
      </section>
        </>
      )}

      <ConnectionManager
        open={managerOpen}
        connections={connections}
        templates={templates}
        redirectUri={redirectUri}
        onClose={() => setManagerOpen(false)}
        onChanged={() => void reload()}
      />

      {xlsOpen && (
        <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Excel-ID's naar SQL">
          <div className="modal">
            <div className="modal-head">
              <h2>Excel-ID's naar SQL</h2>
              <button className="ghost small" onClick={() => setXlsOpen(false)}>
                Sluiten
              </button>
            </div>
            <div className="modal-body">
              <div className="icon-group">
                <button
                  className="run"
                  disabled={!xlsText.trim()}
                  onClick={() => {
                    const result = formatIdList(xlsText)
                    setXlsInvalid(result.invalid)
                    if (result.invalid.length > 0) return
                    editorRef.current?.insertText(result.text)
                    setXlsText('')
                    setXlsOpen(false)
                  }}
                >
                  Invoegen in SQL
                </button>
              </div>
              <textarea
                className="sql-editor plain-editor"
                value={xlsText}
                onChange={(event) => setXlsText(event.target.value)}
                onPaste={() => setXlsInvalid([])}
                placeholder="Plak hier de ID's uit Excel"
                rows={20}
                spellCheck={false}
              />
              <p className="meta">
                {formatIdList(xlsText).count} ID's · {xlsText.length.toLocaleString('nl-BE')} tekens
                {xlsText.length > 200000 ? ' · te lang voor één SQL-query (max. 200.000 tekens)' : ''}
              </p>
              {xlsInvalid.length > 0 && (
                <p className="notice error">Deze waarden zijn geen GUID en zijn overgeslagen: {xlsInvalid.join(', ')}</p>
              )}
            </div>
          </div>
        </div>
      )}

      {settingsOpen && (
        <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label="Instellingen">
          <div className="modal">
            <div className="modal-head">
              <h2>Instellingen</h2>
              <button className="ghost small" onClick={() => setSettingsOpen(false)}>
                Sluiten
              </button>
            </div>
            <div className="modal-body">
              <label className="field field-inline">
                <input
                  type="checkbox"
                  checked={hideDemo}
                  disabled={!hasRealConnections}
                  onChange={(event) => handleHideDemoChange(event.target.checked)}
                />
                <span>Demo-verbinding (synthetische data) uitzetten</span>
              </label>
              {!hasRealConnections && (
                <p className="hint">Voeg eerst een echte verbinding toe om de demo uit te zetten.</p>
              )}

              <label className="field field-inline">
                <input
                  type="checkbox"
                  checked={vimMode}
                  onChange={(event) => {
                    setVimMode(event.target.checked)
                    localStorage.setItem(VIM_STORAGE_KEY, event.target.checked ? '1' : '0')
                  }}
                />
                <span>VIM-modus in de SQL-editor</span>
              </label>

              <label className="field field-inline">
                <input
                  type="checkbox"
                  checked={showExamples}
                  onChange={(event) => {
                    setShowExamples(event.target.checked)
                    localStorage.setItem(SHOW_EXAMPLES_KEY, event.target.checked ? '1' : '0')
                  }}
                />
                <span>Voorbeeldqueries tonen</span>
              </label>

              <label className="field field-inline">
                <span>Max. rijen</span>
                <select value={maxRows} onChange={(event) => setMaxRows(Number(event.target.value))} disabled={loading}>
                  {[50, 200, 500, 1000].map((value) => (
                    <option key={value} value={value}>
                      {value}
                    </option>
                  ))}
                </select>
              </label>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export default App

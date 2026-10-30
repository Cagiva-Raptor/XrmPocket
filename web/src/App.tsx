import { useEffect, useMemo, useRef, useState } from 'react'
import {
  fetchEnvironments,
  formatCell,
  runQuery,
  type EnvironmentInfo,
  type QueryResponse,
} from './api'
import './App.css'

const SQL_STORAGE_KEY = 'xrmpocket.sql'
const ENV_STORAGE_KEY = 'xrmpocket.environment'

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
  const [environments, setEnvironments] = useState<EnvironmentInfo[]>([])
  const [environment, setEnvironment] = useState(() => localStorage.getItem(ENV_STORAGE_KEY) ?? 'mock')
  const [sql, setSql] = useState(
    () => localStorage.getItem(SQL_STORAGE_KEY) ?? EXAMPLES[0].sql,
  )
  const [maxRows, setMaxRows] = useState(200)
  const [response, setResponse] = useState<QueryResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [fatal, setFatal] = useState<string | null>(null)
  const editorRef = useRef<HTMLTextAreaElement>(null)

  useEffect(() => {
    fetchEnvironments()
      .then((list) => {
        setEnvironments(list)
        const stored = localStorage.getItem(ENV_STORAGE_KEY)
        const current = list.find((env) => env.id === (stored ?? environment))

        if (!current || !current.configured) {
          const fallback = list.find((env) => env.configured)
          if (fallback) setEnvironment(fallback.id)
        }
      })
      .catch((error: unknown) => setFatal(String(error)))
  }, [])

  useEffect(() => {
    localStorage.setItem(SQL_STORAGE_KEY, sql)
  }, [sql])

  useEffect(() => {
    localStorage.setItem(ENV_STORAGE_KEY, environment)
  }, [environment])

  const activeEnvironment = useMemo(
    () => environments.find((env) => env.id === environment),
    [environments, environment],
  )

  async function handleRun() {
    if (loading) return

    setLoading(true)
    setFatal(null)

    try {
      setResponse(await runQuery(sql, environment, maxRows))
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
      const next = `${sql.slice(0, start)}  ${sql.slice(end)}`
      setSql(next)
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
            <span>Omgeving</span>
            <select
              value={environment}
              onChange={(event) => setEnvironment(event.target.value)}
              disabled={loading}
            >
              {environments.map((env) => (
                <option key={env.id} value={env.id} disabled={!env.configured}>
                  {env.label}
                  {env.configured ? '' : ' (niet geconfigureerd)'}
                </option>
              ))}
            </select>
          </label>

          <label className="field">
            <span>Max. rijen</span>
            <select
              value={maxRows}
              onChange={(event) => setMaxRows(Number(event.target.value))}
              disabled={loading}
            >
              {[50, 200, 500, 1000].map((value) => (
                <option key={value} value={value}>
                  {value}
                </option>
              ))}
            </select>
          </label>

          <button className="run" onClick={() => void handleRun()} disabled={loading}>
            {loading ? 'Bezig…' : 'Uitvoeren'}
            <kbd>⌘↵</kbd>
          </button>
        </div>
      </header>

      {activeEnvironment && !activeEnvironment.configured && (
        <p className="notice">
          {activeEnvironment.label} is nog niet bruikbaar: {activeEnvironment.note}. Gebruik
          voorlopig de mock-omgeving.
        </p>
      )}

      {fatal && <p className="notice error">{fatal}</p>}

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
              {response.truncated ? ' (afgekapt op de limiet)' : ''} ·{' '}
              {response.durationMs.toFixed(0)} ms · omgeving {response.environment}
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
                          cell === null
                            ? 'cell-null'
                            : typeof cell === 'number'
                              ? 'cell-number'
                              : undefined
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

        {response?.ok && response.rows.length === 0 && (
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
    </div>
  )
}

export default App

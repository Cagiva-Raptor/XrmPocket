import { useMemo, useState } from 'react'
import {
  fetchRibbonXml,
  fetchRoles,
  fetchUserRoles,
  fetchUsers,
  importRows,
  runFetchXml,
  setUserRole,
  type CellValue,
  type ToolRole,
  type ToolUser,
} from './api'

export type ToolId = 'fetchxml' | 'ribbon' | 'transporter' | 'security'

export const TOOL_TABS: { id: ToolId; label: string }[] = [
  { id: 'fetchxml', label: 'FetchXML Builder' },
  { id: 'ribbon', label: 'Ribbon Workbench' },
  { id: 'transporter', label: 'Data Transporter' },
  { id: 'security', label: 'User Security Manager' },
]

const FETCH_EXAMPLE = `<fetch top="50">
  <entity name="account">
    <attribute name="name" />
    <attribute name="revenue" />
    <order attribute="revenue" descending="true" />
  </entity>
</fetch>`

interface Props {
  connectionId: string
  isMock: boolean
  tool: ToolId
}

export function Tools({ connectionId, isMock, tool }: Props) {
  if (isMock) {
    return (
      <section className="editor">
        <p className="empty">De gereedschappen werken alleen op een Dataverse-verbinding. Kies SAV-TEST, SAV-ACC of SAV-PRD.</p>
      </section>
    )
  }

  return (
    <section className="tools">
      {tool === 'fetchxml' && <FetchXmlBuilder connectionId={connectionId} />}
      {tool === 'ribbon' && <RibbonViewer connectionId={connectionId} />}
      {tool === 'transporter' && <DataTransporter connectionId={connectionId} />}
      {tool === 'security' && <UserSecurity connectionId={connectionId} />}
    </section>
  )
}

function ErrorLine({ message }: { message: string | null }) {
  return message ? <p className="notice error">{message}</p> : null
}

function FetchXmlBuilder({ connectionId }: { connectionId: string }) {
  const [xml, setXml] = useState(FETCH_EXAMPLE)
  const [result, setResult] = useState<{ columns: string[]; rows: CellValue[][]; truncated: boolean } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function run() {
    setBusy(true)
    setError(null)
    try {
      setResult(await runFetchXml(connectionId, xml, 200))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setResult(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tool-body">
      <textarea className="sql-editor" value={xml} onChange={(event) => setXml(event.target.value)} rows={12} spellCheck={false} />
      <div className="tool-actions">
        <button className="run" onClick={() => void run()} disabled={busy}>
          {busy ? 'Bezig…' : 'FetchXML uitvoeren'}
        </button>
        {result && (
          <span className="meta">
            {result.rows.length} rijen{result.truncated ? ' (afgekapt)' : ''}
          </span>
        )}
      </div>
      <ErrorLine message={error} />
      {result && <SimpleGrid columns={result.columns} rows={result.rows} />}
    </div>
  )
}

function RibbonViewer({ connectionId }: { connectionId: string }) {
  const [entity, setEntity] = useState('account')
  const [xml, setXml] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function load() {
    setBusy(true)
    setError(null)
    try {
      const response = await fetchRibbonXml(connectionId, entity.trim())
      setXml(response.xml)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setXml(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tool-body">
      <div className="tool-actions">
        <label className="field field-inline">
          <span>Tabel (logische naam)</span>
          <input value={entity} onChange={(event) => setEntity(event.target.value)} />
        </label>
        <button className="run" onClick={() => void load()} disabled={busy || !entity.trim()}>
          {busy ? 'Bezig…' : 'Ribbon ophalen'}
        </button>
      </div>
      <ErrorLine message={error} />
      {xml !== null && (
        <>
          <p className="meta">Alleen-lezen weergave van de ribbon-XML. Wijzigen gebeurt nog niet in deze versie.</p>
          <pre className="sql-highlight tool-pre">{xml || 'Geen ribbon-XML gevonden voor deze tabel.'}</pre>
        </>
      )}
    </div>
  )
}

function DataTransporter({ connectionId }: { connectionId: string }) {
  const [entity, setEntity] = useState('')
  const [rows, setRows] = useState<Record<string, string | null>[]>([])
  const [fileName, setFileName] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function handleFile(file: File) {
    setError(null)
    setMessage(null)
    setFileName(file.name)
    setRows(parseCsv(await file.text()))
  }

  async function dryRun() {
    setBusy(true)
    setError(null)
    try {
      const preview = await importRows(connectionId, entity.trim(), rows, true)
      setMessage(`Controle: ${preview.rows} rijen voor ${entity.trim()} (kolommen: ${(preview.columns ?? []).join(', ')}).`)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function importNow() {
    if (!window.confirm(`${rows.length} rijen aanmaken in ${entity.trim()} op deze omgeving?`)) return
    setBusy(true)
    setError(null)
    try {
      const result = await importRows(connectionId, entity.trim(), rows, false)
      setMessage(`${result.created ?? 0} aangemaakt.${result.errors?.length ? ` Fouten: ${result.errors.length}` : ''}`)
      if (result.errors?.length) setError(result.errors.slice(0, 5).join('\n'))
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tool-body">
      <p className="meta">
        Importeert een CSV-bestand als nieuwe records. Eerste rij = kolomnamen (logische namen). Alleen tekstwaarden; lookups en
        keuzelijsten nog niet.
      </p>
      <div className="tool-actions">
        <label className="field field-inline">
          <span>Tabel (logische naam)</span>
          <input value={entity} onChange={(event) => setEntity(event.target.value)} />
        </label>
        <input
          type="file"
          accept=".csv,text/csv"
          onChange={(event) => {
            const file = event.target.files?.[0]
            if (file) void handleFile(file)
          }}
        />
      </div>
      {fileName && (
        <p className="meta">
          {fileName}: {rows.length} rijen
        </p>
      )}
      <div className="tool-actions">
        <button className="ghost" onClick={() => void dryRun()} disabled={busy || rows.length === 0 || !entity.trim()}>
          Controleren
        </button>
        <button className="run" onClick={() => void importNow()} disabled={busy || rows.length === 0 || !entity.trim()}>
          {busy ? 'Bezig…' : 'Importeren'}
        </button>
      </div>
      {message && <p className="notice">{message}</p>}
      <ErrorLine message={error} />
      {rows.length > 0 && <SimpleGrid columns={Object.keys(rows[0])} rows={rows.slice(0, 20).map((row) => Object.values(row))} />}
    </div>
  )
}

function UserSecurity({ connectionId }: { connectionId: string }) {
  const [users, setUsers] = useState<ToolUser[]>([])
  const [roles, setRoles] = useState<ToolRole[]>([])
  const [userRoles, setUserRoles] = useState<ToolRole[]>([])
  const [selectedUser, setSelectedUser] = useState('')
  const [search, setSearch] = useState('')
  const [onlyUserBusinessUnit, setOnlyUserBusinessUnit] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const user = users.find((item) => item.id === selectedUser)

  const assignedIds = useMemo(() => new Set(userRoles.map((role) => role.id)), [userRoles])

  const available = useMemo(() => {
    const term = search.trim().toLowerCase()

    return roles
      .filter((role) => !assignedIds.has(role.id))
      .filter((role) => !onlyUserBusinessUnit || !user?.businessUnitId || role.businessUnitId === user.businessUnitId)
      .filter((role) => !term || role.name.toLowerCase().includes(term) || (role.businessUnit ?? '').toLowerCase().includes(term))
  }, [roles, assignedIds, onlyUserBusinessUnit, user, search])

  async function loadUsers() {
    setBusy(true)
    setError(null)
    try {
      const [userList, roleList] = await Promise.all([fetchUsers(connectionId), fetchRoles(connectionId)])
      setUsers(userList.users)
      setRoles(roleList.roles)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function selectUser(userId: string) {
    setSelectedUser(userId)
    setUserRoles([])
    if (!userId) return
    setBusy(true)
    setError(null)
    try {
      setUserRoles((await fetchUserRoles(connectionId, userId)).roles)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function changeRole(roleId: string, assign: boolean) {
    if (!selectedUser) return
    setBusy(true)
    setError(null)
    try {
      await setUserRole(connectionId, selectedUser, roleId, assign)
      setUserRoles((await fetchUserRoles(connectionId, selectedUser)).roles)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tool-body">
      <div className="tool-actions">
        <button className="ghost" onClick={() => void loadUsers()} disabled={busy}>
          {users.length ? 'Opnieuw laden' : 'Gebruikers laden'}
        </button>
        {users.length > 0 && (
          <label className="field field-inline">
            <span>Gebruiker</span>
            <select value={selectedUser} onChange={(event) => void selectUser(event.target.value)} disabled={busy}>
              <option value="">Kies een gebruiker…</option>
              {users.map((item) => (
                <option key={item.id} value={item.id}>
                  {item.fullName ?? item.domainName ?? item.id}
                  {item.isDisabled ? ' (uitgeschakeld)' : ''}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      <ErrorLine message={error} />

      {user && (
        <>
          <h3 className="tool-heading">
            {user.fullName ?? user.domainName}
            <span className="meta"> · {userRoles.length} rollen</span>
          </h3>

          <h4 className="tool-heading">Huidige rollen</h4>
          {userRoles.length === 0 ? (
            <p className="empty">Geen rollen toegewezen.</p>
          ) : (
            <RoleTable
              roles={userRoles}
              actionLabel="Verwijderen"
              danger
              disabled={busy}
              onAction={(role) => void changeRole(role.id, false)}
            />
          )}

          <h4 className="tool-heading">Rol toevoegen</h4>
          <div className="tool-actions">
            <input
              className="search-input"
              placeholder="Zoek op rol of business unit…"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
            />
            <label className="check">
              <input
                type="checkbox"
                checked={onlyUserBusinessUnit}
                onChange={(event) => setOnlyUserBusinessUnit(event.target.checked)}
              />
              <span>Alleen business unit van gebruiker</span>
            </label>
          </div>
          <p className="meta">{available.length} rollen beschikbaar</p>
          <RoleTable roles={available.slice(0, 200)} actionLabel="Toevoegen" disabled={busy} onAction={(role) => void changeRole(role.id, true)} />
          {available.length > 200 && <p className="meta">Eerste 200 getoond; verfijn met zoeken.</p>}
        </>
      )}
    </div>
  )
}

function RoleTable({
  roles,
  actionLabel,
  danger = false,
  disabled,
  onAction,
}: {
  roles: ToolRole[]
  actionLabel: string
  danger?: boolean
  disabled: boolean
  onAction: (role: ToolRole) => void
}) {
  return (
    <div className="grid-wrap">
      <table className="grid role-table">
        <thead>
          <tr>
            <th>Rol</th>
            <th>Business unit</th>
            <th aria-label="actie" />
          </tr>
        </thead>
        <tbody>
          {roles.map((role) => (
            <tr key={role.id}>
              <td>{role.name}</td>
              <td className="meta">{role.businessUnit ?? '—'}</td>
              <td>
                <button className={danger ? 'ghost small danger' : 'ghost small'} disabled={disabled} onClick={() => onAction(role)}>
                  {actionLabel}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function SimpleGrid({ columns, rows }: { columns: string[]; rows: CellValue[][] }) {
  return (
    <div className="grid-wrap">
      <table className="grid">
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column}>{column}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, rowIndex) => (
            <tr key={rowIndex}>
              {row.map((cell, cellIndex) => (
                <td key={cellIndex}>{cell === null ? '' : typeof cell === 'object' ? JSON.stringify(cell) : String(cell)}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function parseCsv(text: string): Record<string, string | null>[] {
  const lines = text.split(/\r?\n/).filter((line) => line.trim() !== '')
  if (lines.length < 2) return []

  const headers = splitCsvLine(lines[0])
  return lines.slice(1).map((line) => {
    const values = splitCsvLine(line)
    const record: Record<string, string | null> = {}
    headers.forEach((header, index) => {
      const value = values[index] ?? ''
      record[header] = value === '' ? null : value
    })
    return record
  })
}

function splitCsvLine(line: string): string[] {
  const values: string[] = []
  let current = ''
  let quoted = false

  for (let index = 0; index < line.length; index++) {
    const char = line[index]
    if (quoted) {
      if (char === '"' && line[index + 1] === '"') {
        current += '"'
        index++
      } else if (char === '"') {
        quoted = false
      } else {
        current += char
      }
    } else if (char === '"') {
      quoted = true
    } else if (char === ',') {
      values.push(current)
      current = ''
    } else {
      current += char
    }
  }

  values.push(current)
  return values.map((value) => value.trim())
}

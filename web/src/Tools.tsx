import { useEffect, useMemo, useState } from 'react'
import {
  deleteStatement,
  fetchRibbonXml,
  fetchRoles,
  fetchUserRoles,
  fetchUsers,
  importRows,
  listStatements,
  runFetchXml,
  saveStatement,
  setUserRole,
  type CellValue,
  type SavedStatement,
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
  orgUrl: string | null
  maxRows: number
  tool: ToolId
}

interface FetchSettings {
  autoFormat: boolean
  idLinks: boolean
}

const FETCH_SETTINGS_KEY = 'xrmpocket.tool.fetchxml'
const DEFAULT_FETCH_SETTINGS: FetchSettings = { autoFormat: true, idLinks: true }

function loadFetchSettings(): FetchSettings {
  try {
    return { ...DEFAULT_FETCH_SETTINGS, ...(JSON.parse(localStorage.getItem(FETCH_SETTINGS_KEY) ?? '{}') as Partial<FetchSettings>) }
  } catch {
    return DEFAULT_FETCH_SETTINGS
  }
}

export function Tools({
  connectionId,
  isMock,
  orgUrl,
  maxRows,
  tool,
  settingsOpen,
  onCloseSettings,
}: Props & { settingsOpen: boolean; onCloseSettings: () => void }) {
  const [fetchSettings, setFetchSettings] = useState<FetchSettings>(loadFetchSettings)

  function updateFetchSettings(next: FetchSettings) {
    setFetchSettings(next)
    localStorage.setItem(FETCH_SETTINGS_KEY, JSON.stringify(next))
  }

  if (isMock) {
    return (
      <section className="editor">
        <p className="empty">De gereedschappen werken alleen op een Dataverse-verbinding. Kies SAV-TEST, SAV-ACC of SAV-PRD.</p>
      </section>
    )
  }

  const toolLabel = TOOL_TABS.find((item) => item.id === tool)?.label ?? ''

  return (
    <section className="tools">
      {tool === 'fetchxml' && (
        <FetchXmlBuilder
          connectionId={connectionId}
          orgUrl={fetchSettings.idLinks ? orgUrl : null}
          maxRows={maxRows}
          autoFormat={fetchSettings.autoFormat}
        />
      )}
      {tool === 'ribbon' && <RibbonViewer connectionId={connectionId} />}
      {tool === 'transporter' && <DataTransporter connectionId={connectionId} />}
      {tool === 'security' && <UserSecurity connectionId={connectionId} maxRows={maxRows} />}

      {settingsOpen && (
        <div className="modal-backdrop" role="dialog" aria-modal="true" aria-label={`Instellingen ${toolLabel}`}>
          <div className="modal">
            <div className="modal-head">
              <h2>{toolLabel}: instellingen</h2>
              <button className="ghost small" onClick={onCloseSettings}>
                Sluiten
              </button>
            </div>
            <div className="modal-body">
              {tool === 'fetchxml' ? (
                <>
                  <label className="field field-inline">
                    <input
                      type="checkbox"
                      checked={fetchSettings.autoFormat}
                      onChange={(event) => updateFetchSettings({ ...fetchSettings, autoFormat: event.target.checked })}
                    />
                    <span>XML automatisch formatteren bij plakken</span>
                  </label>
                  <label className="field field-inline">
                    <input
                      type="checkbox"
                      checked={fetchSettings.idLinks}
                      onChange={(event) => updateFetchSettings({ ...fetchSettings, idLinks: event.target.checked })}
                    />
                    <span>ID-kolommen als link naar het record tonen</span>
                  </label>
                  <p className="hint">Het aantal rijen komt uit de algemene instelling "Max. rijen".</p>
                </>
              ) : (
                <p className="hint">Dit gereedschap heeft nog geen eigen instellingen.</p>
              )}
            </div>
          </div>
        </div>
      )}
    </section>
  )
}

function ErrorLine({ message }: { message: string | null }) {
  return message ? <p className="notice error">{message}</p> : null
}

function escapeXml(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}

function serializeXml(el: Element, depth: number): string {
  const pad = '  '.repeat(depth)
  const attrs = Array.from(el.attributes)
    .map((attr) => ` ${attr.name}="${escapeXml(attr.value)}"`)
    .join('')
  const children = Array.from(el.childNodes).filter(
    (node) => node.nodeType === Node.ELEMENT_NODE || (node.nodeType === Node.TEXT_NODE && node.textContent?.trim()),
  )
  if (children.length === 0) return `${pad}<${el.nodeName}${attrs}/>`
  if (children.length === 1 && children[0].nodeType === Node.TEXT_NODE) {
    return `${pad}<${el.nodeName}${attrs}>${escapeXml(children[0].textContent?.trim() ?? '')}</${el.nodeName}>`
  }
  const inner = children
    .map((node) =>
      node.nodeType === Node.ELEMENT_NODE
        ? serializeXml(node as Element, depth + 1)
        : `${'  '.repeat(depth + 1)}${escapeXml(node.textContent?.trim() ?? '')}`,
    )
    .join('\n')
  return `${pad}<${el.nodeName}${attrs}>\n${inner}\n${pad}</${el.nodeName}>`
}

function checkFetchXml(input: string): { text: string; error: string | null } {
  if (!input.trim()) return { text: input, error: 'Plak of typ een FetchXML-query.' }
  const doc = new DOMParser().parseFromString(input, 'application/xml')
  const parseError = doc.querySelector('parsererror')
  if (parseError) {
    return { text: input, error: `Ongeldige XML: ${parseError.textContent?.split('\n')[0] ?? 'onbekende fout'}` }
  }
  const root = doc.documentElement
  if (root.nodeName !== 'fetch') return { text: input, error: 'Het hoofdelement moet <fetch> zijn.' }
  if (!root.querySelector(':scope > entity')) return { text: input, error: 'Er is geen <entity> binnen <fetch>.' }
  return { text: serializeXml(root, 0), error: null }
}

function FetchXmlBuilder({
  connectionId,
  orgUrl,
  maxRows,
  autoFormat,
}: {
  connectionId: string
  orgUrl: string | null
  maxRows: number
  autoFormat: boolean
}) {
  const [xml, setXml] = useState(FETCH_EXAMPLE)
  const [result, setResult] = useState<{ columns: string[]; rows: CellValue[][]; truncated: boolean; entity: string | null } | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const check = useMemo(() => checkFetchXml(xml), [xml])
  const [saved, setSaved] = useState<SavedStatement[]>([])
  const [selectedSaved, setSelectedSaved] = useState('')

  useEffect(() => {
    void refreshSaved()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  async function refreshSaved() {
    try {
      setSaved((await listStatements('fetchxml')).statements)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  function selectSaved(id: string) {
    setSelectedSaved(id)
    const item = saved.find((entry) => entry.id === id)
    if (item) setXml(item.sql)
  }

  async function saveCurrent() {
    if (check.error) {
      setError('Corrigeer eerst de XML voordat je opslaat.')
      return
    }
    const name = window.prompt('Naam voor deze FetchXML')?.trim()
    if (!name) return

    try {
      const item = await saveStatement(name, check.text, 'fetchxml')
      await refreshSaved()
      setSelectedSaved(item.id)
      setError(null)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  async function removeSaved() {
    const item = saved.find((entry) => entry.id === selectedSaved)
    if (!item || !window.confirm(`FetchXML '${item.name}' verwijderen?`)) return

    try {
      await deleteStatement(item.id)
      setSelectedSaved('')
      await refreshSaved()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    }
  }

  function handlePaste(event: React.ClipboardEvent<HTMLTextAreaElement>) {
    const pasted = event.clipboardData.getData('text')
    if (!autoFormat) return
    const formatted = checkFetchXml(pasted)
    if (formatted.error) return
    event.preventDefault()
    setXml(formatted.text)
  }

  async function run() {
    if (check.error) return
    setBusy(true)
    setError(null)
    try {
      const response = await runFetchXml(connectionId, check.text, maxRows)
      setResult({ ...response, entity: new DOMParser().parseFromString(check.text, 'application/xml').querySelector('entity')?.getAttribute('name') ?? null })
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setResult(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="tool-body">
      <div className="editor-head start">
        <label className="field field-inline">
          <span>Opgeslagen</span>
          <select value={selectedSaved} onChange={(event) => selectSaved(event.target.value)}>
            <option value="">{saved.length ? 'Kies…' : 'Nog niets opgeslagen'}</option>
            {saved.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
        </label>
        <div className="icon-group">
          <button className="ghost icon-button" onClick={() => void saveCurrent()} disabled={busy} aria-label="Opslaan" title="Opslaan">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z" />
              <path d="M17 21v-8H7v8M7 3v5h8" />
            </svg>
          </button>
          <button
            className="ghost icon-button danger"
            onClick={() => void removeSaved()}
            disabled={busy || !selectedSaved}
            aria-label="Verwijderen"
            title="Verwijderen"
          >
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6" />
            </svg>
          </button>
        </div>
        <div className="icon-group end">
          <button className="ghost" onClick={() => setXml(check.text)} disabled={busy || check.error !== null}>
            Formatteer
          </button>
          <button className="run" onClick={() => void run()} disabled={busy || check.error !== null}>
            {busy ? 'Bezig…' : 'FetchXML uitvoeren'}
          </button>
        </div>
      </div>
      <textarea
        className="sql-editor plain-editor"
        value={xml}
        onChange={(event) => setXml(event.target.value)}
        onPaste={handlePaste}
        rows={12}
        spellCheck={false}
      />
      <div className="tool-actions">
        {result && (
          <span className="meta">
            {result.rows.length} rijen{result.truncated ? ' (afgekapt)' : ''}
          </span>
        )}
      </div>
      {check.error ? <ErrorLine message={check.error} /> : <p className="meta">XML is geldig.</p>}
      <ErrorLine message={error} />
      {result && <SimpleGrid columns={result.columns} rows={result.rows} orgUrl={orgUrl} entity={result.entity} />}
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

function UserSecurity({ connectionId, maxRows }: { connectionId: string; maxRows: number }) {
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
          <RoleTable roles={available.slice(0, maxRows)} actionLabel="Toevoegen" disabled={busy} onAction={(role) => void changeRole(role.id, true)} />
          {available.length > maxRows && <p className="meta">Eerste {maxRows} getoond; verfijn met zoeken.</p>}
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

const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

function recordLink(orgUrl: string | null, entity: string | null, column: string, cell: CellValue): string | null {
  if (!orgUrl || !entity || column !== `${entity}id` || typeof cell !== 'string' || !GUID_PATTERN.test(cell)) return null
  return `${orgUrl.replace(/\/+$/, '')}/main.aspx?etn=${encodeURIComponent(entity)}&id=${cell}&pagetype=entityrecord`
}

function SimpleGrid({
  columns,
  rows,
  orgUrl = null,
  entity = null,
}: {
  columns: string[]
  rows: CellValue[][]
  orgUrl?: string | null
  entity?: string | null
}) {
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
              {row.map((cell, cellIndex) => {
                const link = recordLink(orgUrl, entity, columns[cellIndex], cell)
                const text = cell === null ? '' : typeof cell === 'object' ? JSON.stringify(cell) : String(cell)
                return <td key={cellIndex}>{link ? <a href={link} target="_blank" rel="noreferrer">{text}</a> : text}</td>
              })}
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

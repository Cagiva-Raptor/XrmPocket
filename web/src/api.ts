/**
 * Dun laagje om de eigen API. De frontend praat nooit rechtstreeks met Dataverse:
 * alle verbindingen en secrets blijven in de backend.
 */

export type ConnectionKind = 'mock' | 'dataverse'
export type Highlight = 'none' | 'dev' | 'test' | 'acc' | 'production'

/** Eén opgeslagen verbinding (het equivalent van een ConnectionDetail in XrmToolBox). */
export interface SavedConnection {
  id: string
  name: string
  kind: ConnectionKind
  authType: string
  environment: string
  url: string | null
  tenantId: string | null
  clientId: string | null
  highlight: Highlight
  timeoutSeconds: number
  isDefault: boolean
  configured: boolean
  secretPresent: boolean
  note: string | null
  lastUsedOn: string | null
  signedIn: boolean
  account: string | null
}

export interface ConnectionList {
  defaultConnectionId: string | null
  interactiveRedirectUri: string
  connections: SavedConnection[]
}

export interface ConnectionTestResult {
  ok: boolean
  organizationName: string | null
  organizationUniqueName: string | null
  organizationVersion: string | null
  userId: string | null
  userFullName: string | null
  userDomainName: string | null
  applicationId: string | null
  userDisabled: boolean | null
  roles: string[]
  isSystemAdministrator: boolean
  errorCode: string | null
  message: string | null
  hint: string | null
}

/** Sjabloon om snel een verbinding te maken (dev/test/acc/productie). */
export interface EnvironmentTemplate {
  id: string
  label: string
  url: string | null
  production: boolean
}

export interface ConnectionInput {
  id?: string
  name: string
  kind: ConnectionKind
  authType: string
  environment: string
  url: string | null
  tenantId: string | null
  clientId: string | null
  secretFile?: string | null
  timeoutSeconds: number
  highlight: Highlight
}

export interface ResultColumn {
  name: string
  type: string
}

export type CellValue = string | number | boolean | null | Record<string, unknown>

export interface QueryError {
  code: string
  message: string
  hint: string | null
  line?: number | null
  column?: number | null
}

export interface QueryResponse {
  ok: boolean
  connectionId: string | null
  connectionName: string | null
  columns: ResultColumn[]
  rows: CellValue[][]
  rowCount: number
  truncated: boolean
  resultSets: { columns: ResultColumn[]; rows: CellValue[][]; truncated: boolean }[]
  rowsAffected: number | null
  messages: string[]
  fetchXml: string[]
  durationMs: number
  error: QueryError | null
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)

  if (response.status === 401 && !url.startsWith('/api/login')) {
    window.dispatchEvent(new Event('xp-auth-required'))
  }

  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`

    try {
      const body = (await response.json()) as { error?: string }
      if (body.error) message = body.error
    } catch {
      // geen JSON-body: statusmelding volstaat
    }

    throw new Error(message)
  }

  return (await response.json()) as T
}

export function listConnections(): Promise<ConnectionList> {
  return request<ConnectionList>('/api/connections')
}

export function fetchEnvironmentTemplates(): Promise<EnvironmentTemplate[]> {
  return request<{ environments: EnvironmentTemplate[] }>('/api/environments').then((data) => data.environments)
}

export function saveConnection(input: ConnectionInput): Promise<SavedConnection> {
  return request<SavedConnection>('/api/connections', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(input),
  })
}

export function deleteConnection(id: string): Promise<{ deleted: string }> {
  return request<{ deleted: string }>(`/api/connections/${id}`, { method: 'DELETE' })
}

export function setDefaultConnection(id: string): Promise<SavedConnection> {
  return request<SavedConnection>(`/api/connections/${id}/default`, { method: 'POST' })
}

export function testConnection(id: string): Promise<ConnectionTestResult> {
  return request<ConnectionTestResult>(`/api/connections/${id}/test`, { method: 'POST' })
}

/** Bewaart het client secret server-side (chmod 600); de waarde komt nooit terug. */
export function saveSecret(id: string, secret: string): Promise<{ ok: boolean; message: string }> {
  return request<{ ok: boolean; message: string }>(`/api/connections/${id}/secret`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ secret }),
  })
}

/**
 * Start interactief aanmelden; de UI opent de teruggegeven URL.
 * De origin gaat mee zodat de callback-URL klopt met het adres waarop je werkt
 * (localhost, LAN of Tailscale).
 */
export function startSignIn(id: string): Promise<{ authorizationUrl: string; redirectUri: string }> {
  return request<{ authorizationUrl: string; redirectUri: string }>(`/api/connections/${id}/signin`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ origin: window.location.origin }),
  })
}

export function signOut(id: string): Promise<{ signedOut: string }> {
  return request<{ signedOut: string }>(`/api/connections/${id}/signout`, { method: 'POST' })
}

export function runQuery(
  sql: string,
  connectionId: string | null,
  maxRows: number,
  dryRun = false,
  signal?: AbortSignal,
): Promise<QueryResponse> {
  return request<QueryResponse>('/api/query', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ sql, connectionId, maxRows, dryRun }),
    signal,
  })
}

/** Weergave van één cel: null blijft zichtbaar als NULL, objecten als JSON. */
export function formatCell(value: CellValue): string {
  if (value === null || value === undefined) return 'NULL'
  if (typeof value === 'object') {
    const reference = value as { name?: string | null; id?: string }
    if (reference.name) return reference.name
    return JSON.stringify(value)
  }
  return String(value)
}

export interface ToolRole {
  id: string
  name: string
  businessUnitId: string | null
  businessUnit: string | null
}

export interface ToolUser {
  id: string
  fullName: string | null
  domainName: string | null
  isDisabled: boolean
  applicationId: string | null
  businessUnitId: string | null
}

export interface ToolFetchResult {
  columns: string[]
  rows: CellValue[][]
  truncated: boolean
}

function jsonPost<T>(url: string, body: unknown): Promise<T> {
  return request<T>(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export function fetchRoles(connectionId: string): Promise<{ roles: ToolRole[] }> {
  return request(`/api/tools/roles?connectionId=${encodeURIComponent(connectionId)}`)
}

export function fetchUsers(connectionId: string): Promise<{ users: ToolUser[] }> {
  return request(`/api/tools/users?connectionId=${encodeURIComponent(connectionId)}`)
}

export function fetchUserRoles(connectionId: string, userId: string): Promise<{ roles: ToolRole[] }> {
  return request(`/api/tools/users/${encodeURIComponent(userId)}/roles?connectionId=${encodeURIComponent(connectionId)}`)
}

export function setUserRole(connectionId: string, userId: string, roleId: string, assign: boolean): Promise<unknown> {
  return jsonPost(`/api/tools/users/${encodeURIComponent(userId)}/roles`, { connectionId, roleId, assign })
}

export interface ToolEntity {
  logicalName: string
  displayName: string
  primaryId: string
  primaryName: string
}

export interface ToolAttribute {
  logicalName: string
  displayName: string
  type: string
  targets: string[]
  options: { value: number; label: string | null }[] | null
  required: string
}

export function fetchEntities(connectionId: string): Promise<{ entities: ToolEntity[] }> {
  return request(`/api/tools/entities?connectionId=${encodeURIComponent(connectionId)}`)
}

export function fetchEntityMetadata(connectionId: string, entity: string): Promise<{ attributes: ToolAttribute[] }> {
  return request(`/api/tools/metadata/${encodeURIComponent(entity)}?connectionId=${encodeURIComponent(connectionId)}`)
}

export function runFetchXml(connectionId: string, fetchXml: string, maxRows: number): Promise<ToolFetchResult> {
  return jsonPost('/api/tools/fetchxml', { connectionId, fetchXml, maxRows })
}

export function fetchRibbonXml(connectionId: string, entity: string): Promise<{ entity: string; xml: string }> {
  return request(`/api/tools/ribbon?entity=${encodeURIComponent(entity)}&connectionId=${encodeURIComponent(connectionId)}`)
}

export function importRows(
  connectionId: string,
  entity: string,
  rows: Record<string, string | null>[],
  dryRun: boolean,
): Promise<{ dryRun: boolean; rows?: number; columns?: string[]; created?: number; errors?: string[] }> {
  return jsonPost('/api/tools/import', { connectionId, entity, rows, dryRun })
}

export interface SessionState {
  authenticated: boolean
  passwordConfigured: boolean
}

export function getSession(): Promise<SessionState> {
  return request<SessionState>('/api/session')
}

export async function login(password: string): Promise<void> {
  await request('/api/login', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ password }),
  })
}

export function logout(): Promise<unknown> {
  return request('/api/logout', { method: 'POST' })
}

export interface SavedStatement {
  id: string
  name: string
  sql: string
  createdOn: string
}

export function listStatements(kind: 'sql' | 'fetchxml' = 'sql'): Promise<{ statements: SavedStatement[] }> {
  return request(`/api/statements?kind=${kind}`)
}

export function saveStatement(name: string, sql: string, kind: 'sql' | 'fetchxml' = 'sql'): Promise<SavedStatement> {
  return jsonPost<SavedStatement>('/api/statements', { name, sql, kind })
}

export function updateStatement(id: string, sql: string): Promise<SavedStatement> {
  return request<SavedStatement>(`/api/statements/${encodeURIComponent(id)}`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ name: null, sql }),
  })
}

export function deleteStatement(id: string): Promise<unknown> {
  return request(`/api/statements/${encodeURIComponent(id)}`, { method: 'DELETE' })
}

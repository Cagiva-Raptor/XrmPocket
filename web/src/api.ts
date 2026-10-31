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
}

export interface ConnectionList {
  defaultConnectionId: string | null
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
}

export interface QueryResponse {
  ok: boolean
  connectionId: string | null
  connectionName: string | null
  columns: ResultColumn[]
  rows: CellValue[][]
  rowCount: number
  truncated: boolean
  fetchXml: string[]
  durationMs: number
  error: QueryError | null
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  const response = await fetch(url, init)

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

export function runQuery(sql: string, connectionId: string | null, maxRows: number): Promise<QueryResponse> {
  return request<QueryResponse>('/api/query', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ sql, connectionId, maxRows }),
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

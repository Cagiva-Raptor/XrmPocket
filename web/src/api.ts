/**
 * Dun laagje om de eigen API. De frontend praat nooit rechtstreeks met Dataverse:
 * alle verbindingen en secrets blijven in de backend.
 */

export interface EnvironmentInfo {
  id: string
  label: string
  kind: 'mock' | 'dataverse'
  url: string | null
  production: boolean
  configured: boolean
  note: string | null
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
  environment: string | null
  columns: ResultColumn[]
  rows: CellValue[][]
  rowCount: number
  truncated: boolean
  fetchXml: string[]
  durationMs: number
  error: QueryError | null
}

export async function fetchEnvironments(): Promise<EnvironmentInfo[]> {
  const response = await fetch('/api/environments')

  if (!response.ok) {
    throw new Error(`Omgevingen ophalen mislukt (${response.status})`)
  }

  const data = (await response.json()) as { environments: EnvironmentInfo[] }
  return data.environments
}

export async function runQuery(sql: string, environment: string, maxRows: number): Promise<QueryResponse> {
  const response = await fetch('/api/query', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ sql, environment, maxRows }),
  })

  if (!response.ok) {
    throw new Error(`Query mislukt (${response.status})`)
  }

  return (await response.json()) as QueryResponse
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

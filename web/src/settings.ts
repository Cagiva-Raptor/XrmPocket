/**
 * Globale instellingen. Alle waarden staan hier al klaar en worden bewaard,
 * ook als ze nog niet in het instellingenvenster staan. Alleen de instellingen
 * die de app nu daadwerkelijk toepast, worden getoond.
 */
export interface GlobalSettings {
  maxRows: number
  stopAfterRetrievals: number
  warnInsertAbove: number
  warnUpdateAbove: number
  warnDeleteAbove: number
  preventUpdateWithoutWhere: boolean
  preventDeleteWithoutWhere: boolean
  quotedIdentifiers: boolean
  batchSize: number
  workerThreads: number
  useBulkDelete: boolean
  bypassCustomPlugins: boolean
  useTdsEndpoint: boolean
  utcTimes: boolean
  useSchemaColumnOrdering: boolean
  showTooltips: boolean
  rememberSession: boolean
  reopenSessions: boolean
  editorFontSize: number
  autoSizeColumns: boolean
  localDateFormat: boolean
  resultsFontSize: number
  conversionSimplified: boolean
  copilotProvider: string
  copilotSelectIndependent: boolean
  copilotAutocomplete: boolean
}

export const DEFAULT_GLOBAL_SETTINGS: GlobalSettings = {
  maxRows: 200,
  stopAfterRetrievals: 10000,
  warnInsertAbove: 1,
  warnUpdateAbove: 0,
  warnDeleteAbove: 0,
  preventUpdateWithoutWhere: true,
  preventDeleteWithoutWhere: true,
  quotedIdentifiers: true,
  batchSize: 10,
  workerThreads: 0,
  useBulkDelete: false,
  bypassCustomPlugins: false,
  useTdsEndpoint: false,
  utcTimes: true,
  useSchemaColumnOrdering: false,
  showTooltips: true,
  rememberSession: true,
  reopenSessions: true,
  editorFontSize: 14,
  autoSizeColumns: true,
  localDateFormat: false,
  resultsFontSize: 13,
  conversionSimplified: true,
  copilotProvider: 'none',
  copilotSelectIndependent: false,
  copilotAutocomplete: true,
}

const STORAGE_KEY = 'xrmpocket.settings'

export function loadGlobalSettings(): GlobalSettings {
  try {
    return { ...DEFAULT_GLOBAL_SETTINGS, ...(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '{}') as Partial<GlobalSettings>) }
  } catch {
    return DEFAULT_GLOBAL_SETTINGS
  }
}

export function saveGlobalSettings(settings: GlobalSettings): void {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(settings))
}

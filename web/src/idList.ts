const GUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i

/** Zet een lijst GUID's (bijv. uit Excel) om naar 'guid', per regel. */
export function formatIdList(input: string): { text: string; count: number; invalid: string[] } {
  const tokens = input
    .split(/[\s,;]+/)
    .map((token) => token.trim().replace(/^['"]+|['"]+$/g, ''))
    .filter((token) => token.length > 0)

  const valid = tokens.filter((token) => GUID_PATTERN.test(token))
  const invalid = tokens.filter((token) => !GUID_PATTERN.test(token))

  const text = valid.map((id, index) => `'${id}'${index < valid.length - 1 ? ',' : ''}`).join('\n')

  return { text, count: valid.length, invalid }
}

import type { ConnectionTestResult } from './api'

interface Props {
  test: ConnectionTestResult
  compact?: boolean
}

/**
 * Weergave van een verbindingstest: omgeving + versie + gebruiker + rollen.
 * Dit is de web-versie van de statusbalk in XrmToolBox, aangevuld met de
 * application-user-check.
 */
export function ConnectionTestSummary({ test, compact = false }: Props) {
  if (!test.ok) {
    return (
      <p className="test-error">
        ✘ {test.errorCode}: {test.message}
        {test.hint && <span className="hint"> {test.hint}</span>}
      </p>
    )
  }

  const organization = test.organizationName ?? 'verbonden'
  const version = test.organizationVersion ? ` (${test.organizationVersion})` : ''
  const user = test.userFullName ?? test.userDomainName

  return (
    <p className="test-ok">
      ✔ {organization}
      {test.organizationUniqueName && !compact ? ` [${test.organizationUniqueName}]` : ''}
      {version}

      {user && (
        <>
          {' · '}
          {user}
          {test.userDomainName && test.userFullName ? ` (${test.userDomainName})` : ''}
        </>
      )}

      {test.roles.length > 0 && (
        <>
          {' · '}
          <span title={test.roles.join(', ')}>
            {test.roles.length} {test.roles.length === 1 ? 'rol' : 'rollen'}
          </span>
        </>
      )}

      {test.isSystemAdministrator && <span className="pill">systeembeheerder</span>}

      {test.userDisabled && <span className="pill danger">gedeactiveerd</span>}

      {test.message && <span className="hint"> {test.message}</span>}
      {test.hint && <span className="hint"> {test.hint}</span>}
    </p>
  )
}

import type { CSSProperties } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import type { Me } from '../api/types'
import { ModuleIcon } from '../modules/icons'
import './Shell.css'

interface ShellProps {
  me: Me
  onSignOut: () => void
}

/**
 * The frame around every screen. Its navigation is built from the modules the signed-in company
 * has been given, so two companies on the same installation see two different applications.
 */
export function Shell({ me, onSignOut }: ShellProps) {
  return (
    <div className="shell">
      <aside className="shell__side">
        <div className="shell__company">
          <strong>{me.company?.name ?? 'Tezgâh'}</strong>
          <span>{me.company?.sector ?? 'Platform yönetimi'}</span>
        </div>

        <nav className="shell__nav" aria-label="Modüller">
          {me.company ? (
            <>
              <NavLink to="/" end className="shell__link">
                <ModuleIcon name="home" />
                Genel bakış
              </NavLink>
              {me.modules.map((module) => (
                <NavLink
                  key={module.key}
                  to={`/${module.key}`}
                  className="shell__link"
                  style={{ '--accent': `var(--${module.key})` } as CSSProperties}
                >
                  <ModuleIcon name={module.key} />
                  {module.name}
                </NavLink>
              ))}
            </>
          ) : (
            <NavLink to="/" end className="shell__link">
              <ModuleIcon name="home" />
              Firmalar
            </NavLink>
          )}
        </nav>

        <div className="shell__user">
          <span>{me.user.fullName}</span>
          <button type="button" onClick={onSignOut}>
            Çıkış yap
          </button>
        </div>
      </aside>

      <main className="shell__main">
        <Outlet />
      </main>
    </div>
  )
}

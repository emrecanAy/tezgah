import type { ReactElement } from 'react'
import type { ModuleKey } from '../api/types'

const paths: Record<ModuleKey | 'home' | 'team', ReactElement> = {
  home: <path d="M4 11.5 12 5l8 6.5V20h-5v-5H9v5H4z" />,
  customers: (
    <>
      <circle cx="12" cy="8.5" r="3.5" />
      <path d="M5 20c.6-3.6 3.3-5.5 7-5.5s6.4 1.9 7 5.5" />
    </>
  ),
  loyalty: (
    <>
      <rect x="3.5" y="6" width="17" height="12" rx="2" />
      <circle cx="8.5" cy="12" r="1.6" />
      <circle cx="12.75" cy="12" r="1.6" />
      <circle cx="17" cy="12" r="1.6" />
    </>
  ),
  appointments: (
    <>
      <rect x="4" y="5.5" width="16" height="14.5" rx="2" />
      <path d="M4 10h16M8.5 3.5v4M15.5 3.5v4" />
    </>
  ),
  fleet: (
    <>
      <path d="M3 16V8.5a1.5 1.5 0 0 1 1.5-1.5H14v9M14 10h3.6L21 13.2V16h-7" />
      <circle cx="7.5" cy="17" r="1.8" />
      <circle cx="17" cy="17" r="1.8" />
    </>
  ),
  inventory: (
    <>
      <path d="m12 3.5 8 4v9l-8 4-8-4v-9z" />
      <path d="m4 7.5 8 4 8-4M12 11.5v9" />
    </>
  ),
  team: (
    <>
      <circle cx="9" cy="8.5" r="3" />
      <path d="M3.5 19.5c.6-3 2.8-4.8 5.5-4.8s4.9 1.8 5.5 4.8" />
      <path d="M15.5 6a3 3 0 0 1 0 5.6M17.5 15c1.7.6 2.7 2.2 3 4.5" />
    </>
  ),
  orders: (
    <>
      <path d="M6 3.5h12v17l-2-1.4-2 1.4-2-1.4-2 1.4-2-1.4-2 1.4z" />
      <path d="M9 8.5h6M9 12h6M9 15.5h3.5" />
    </>
  ),
}

export function ModuleIcon({ name }: { name: ModuleKey | 'home' | 'team' }) {
  return (
    <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="1.7"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {paths[name]}
    </svg>
  )
}

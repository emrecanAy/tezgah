import type { CSSProperties, ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { Me, ModuleKey } from '../api/types'
import { formatLongDay, formatMoney, startOfDay } from '../lib/format'
import { ModuleIcon } from '../modules/icons'
import './HomePage.css'

/** What each module reports on the overview, written as the sentence a shop owner would say. */
const SUMMARIES: Record<ModuleKey, { path: string; render: (data: Record<string, number>) => ReactNode }> = {
  customers: {
    path: '/customers/summary',
    render: (d) => (
      <>
        <b>{d.total}</b> kayıtlı müşteri, bu ay <b>{d.newThisMonth}</b> yeni.
      </>
    ),
  },
  loyalty: {
    path: '/loyalty/summary',
    render: (d) => (
      <>
        Son 7 günde <b>{d.stampsThisWeek}</b> damga basıldı. <b>{d.rewardsWaiting}</b> ödül kullanılmayı bekliyor.
      </>
    ),
  },
  appointments: {
    path: `/appointments/summary?dayStart=${startOfDay(new Date()).toISOString()}`,
    render: (d) => (
      <>
        Bugün <b>{d.today}</b> randevu var, önümüzdeki 7 günde <b>{d.nextSevenDays}</b>.
      </>
    ),
  },
  fleet: {
    path: '/fleet/summary',
    render: (d) => (
      <>
        <b>{d.vehicles}</b> araç. {d.alerts === 0 ? 'Yaklaşan tarih yok.' : <><b>{d.alerts}</b> konu ilgi bekliyor.</>} Bu ay
        yakıt: <b>{formatMoney(d.fuelCostThisMonth ?? 0)}</b>.
      </>
    ),
  },
  inventory: {
    path: '/inventory/summary',
    render: (d) => (
      <>
        <b>{d.products}</b> ürün takipte. {d.runningLow === 0 ? 'Azalan ürün yok.' : <><b>{d.runningLow}</b> ürün azaldı.</>}
      </>
    ),
  },
}

export function HomePage({ me }: { me: Me }) {
  return (
    <>
      <header className="page-header">
        <div>
          <h1>Merhaba, {me.user.fullName.split(' ')[0]}</h1>
          <p>{formatLongDay(new Date())}</p>
        </div>
      </header>

      {me.modules.length === 0 ? (
        <p className="panel empty">
          <strong>Firmanız için henüz bir modül açılmamış</strong>
          Platform yöneticinizle iletişime geçin.
        </p>
      ) : (
        <ul className="overview">
          {me.modules.map((module) => (
            <li key={module.key} style={{ '--accent': `var(--${module.key})` } as CSSProperties}>
              <Link to={`/${module.key}`} className="overview__row">
                <span className="overview__icon">
                  <ModuleIcon name={module.key} />
                </span>
                <span className="overview__name">{module.name}</span>
                <span className="overview__summary">
                  <Summary module={module.key} />
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </>
  )
}

function Summary({ module }: { module: ModuleKey }) {
  const { path, render } = SUMMARIES[module]
  const { data } = useQuery({ queryKey: [module, 'summary'], queryFn: () => api<Record<string, number>>(path) })
  return data ? render(data) : null
}

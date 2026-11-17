import { type CSSProperties, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api, send } from '../../api/client'
import type { FleetAlert, Vehicle } from '../../api/types'
import { FormDialog } from '../../components/Dialog'
import { describeDaysLeft, formatDate, formatNumber } from '../../lib/format'
import { VehicleFields, readVehicle } from './VehicleFields'
import './Fleet.css'

export const ALERT_LABEL: Record<FleetAlert['kind'], string> = {
  inspection: 'Muayene',
  insurance: 'Sigorta',
  maintenance: 'Bakım',
}

export function FleetPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState(false)
  const { data: vehicles } = useQuery({ queryKey: ['fleet', 'vehicles'], queryFn: () => api<Vehicle[]>('/fleet/vehicles') })

  const alerts = (vehicles ?? []).flatMap((vehicle) => vehicle.alerts).sort((a, b) => a.daysLeft - b.daysLeft)

  return (
    <div style={{ '--accent': 'var(--fleet)' } as CSSProperties}>
      <header className="page-header">
        <div>
          <h1>Filo</h1>
          <p>Araçlar, yakıt ve bakım kayıtları; yaklaşan muayene ve sigorta tarihleri.</p>
        </div>
        <button type="button" className="button" onClick={() => setAdding(true)}>
          Araç ekle
        </button>
      </header>

      {alerts.length > 0 && (
        <section className="alerts" aria-labelledby="alerts-title">
          <h2 id="alerts-title">İlgilenilmesi gerekenler</h2>
          <ul>
            {alerts.map((alert) => (
              <li key={`${alert.vehicleId}-${alert.kind}`}>
                <AlertTag alert={alert} />
                <strong>{alert.plate}</strong>
                <span className="muted">{formatDate(alert.dueOn)}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      <div className="panel table-scroll">
        {vehicles?.length === 0 ? (
          <p className="empty">
            <strong>Henüz araç yok</strong>
            İlk aracınızı ekleyin; muayene ve sigorta tarihlerini biz takip edelim.
          </p>
        ) : (
          <table className="table table--clickable">
            <thead>
              <tr>
                <th scope="col">Plaka</th>
                <th scope="col">Araç</th>
                <th scope="col">Sürücü</th>
                <th scope="col" className="right">
                  Kilometre
                </th>
                <th scope="col">Durum</th>
              </tr>
            </thead>
            <tbody>
              {vehicles?.map((vehicle) => (
                <tr key={vehicle.id} onClick={() => navigate(`/fleet/${vehicle.id}`)}>
                  <td>
                    {/* The link gives keyboard users the same destination the row click gives mouse users. */}
                    <a className="plate" href={`/fleet/${vehicle.id}`} onClick={(event) => event.preventDefault()}>
                      {vehicle.plate}
                    </a>
                  </td>
                  <td>
                    {vehicle.brand} {vehicle.model} <span className="muted">{vehicle.year}</span>
                  </td>
                  <td>{vehicle.driver ?? <span className="muted">Atanmadı</span>}</td>
                  <td className="right num">{formatNumber(vehicle.odometerKm)} km</td>
                  <td>
                    {vehicle.alerts.length === 0 ? (
                      <span className="tag tag--ok">Sorun yok</span>
                    ) : (
                      vehicle.alerts.map((alert) => <AlertTag key={alert.kind} alert={alert} />)
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {adding && (
        <FormDialog
          title="Araç ekle"
          submitLabel="Aracı kaydet"
          onSubmit={(fields) => send('POST', '/fleet/vehicles', readVehicle(fields))}
          onSaved={() => {
            setAdding(false)
            void queryClient.invalidateQueries({ queryKey: ['fleet'] })
          }}
          onClose={() => setAdding(false)}
        >
          <VehicleFields />
        </FormDialog>
      )}
    </div>
  )
}

export function AlertTag({ alert }: { alert: FleetAlert }) {
  return (
    <span className={alert.daysLeft < 0 ? 'tag tag--danger' : 'tag tag--warn'}>
      {ALERT_LABEL[alert.kind]}: {describeDaysLeft(alert.daysLeft)}
    </span>
  )
}

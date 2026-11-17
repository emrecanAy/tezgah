import { type CSSProperties, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api, send } from '../../api/client'
import type { VehicleLogbook } from '../../api/types'
import { FormDialog, num, text } from '../../components/Dialog'
import { formatDate, formatMoney, formatNumber, toDateInput } from '../../lib/format'
import { AlertTag } from './FleetPage'
import { VehicleFields, readVehicle } from './VehicleFields'
import { useCan } from '../../lib/access'
import './Fleet.css'

export function VehiclePage() {
  const canManage = useCan('fleet.vehicles')
  const canLog = useCan('fleet.log')
  const { vehicleId = '' } = useParams()
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<'fuel' | 'maintenance' | 'edit'>()
  const { data: logbook, error } = useQuery({
    queryKey: ['fleet', 'vehicles', vehicleId],
    queryFn: () => api<VehicleLogbook>(`/fleet/vehicles/${vehicleId}`),
  })

  if (error) {
    return (
      <p className="panel empty">
        <strong>Araç bulunamadı</strong>
        <Link to="/fleet">Araç listesine dön</Link>
      </p>
    )
  }

  if (!logbook) return null
  const { vehicle } = logbook
  const today = toDateInput(new Date())

  const saved = () => {
    setDialog(undefined)
    void queryClient.invalidateQueries({ queryKey: ['fleet'] })
  }

  return (
    <div style={{ '--accent': 'var(--fleet)' } as CSSProperties}>
      <Link to="/fleet" className="back-link">
        Tüm araçlar
      </Link>
      <header className="page-header">
        <div>
          <h1 className="plate plate--large">{vehicle.plate}</h1>
          <p>
            {vehicle.brand} {vehicle.model}, {vehicle.year}
            {vehicle.driver ? `. Sürücü: ${vehicle.driver}` : ''}
          </p>
        </div>
        <div className="page-actions">
          {canManage && (
            <button type="button" className="button button--quiet" onClick={() => setDialog('edit')}>
              Düzenle
            </button>
          )}
          {canLog && (
            <>
              <button type="button" className="button button--quiet" onClick={() => setDialog('maintenance')}>
                Bakım ekle
              </button>
              <button type="button" className="button" onClick={() => setDialog('fuel')}>
                Yakıt ekle
              </button>
            </>
          )}
        </div>
      </header>

      {vehicle.alerts.length > 0 && (
        <p className="vehicle__alerts">
          {vehicle.alerts.map((alert) => (
            <AlertTag key={alert.kind} alert={alert} />
          ))}
        </p>
      )}

      <dl className="stats">
        <div>
          <dt>Kilometre</dt>
          <dd className="num">{formatNumber(vehicle.odometerKm)} km</dd>
        </div>
        <div>
          <dt>Ortalama tüketim</dt>
          <dd className="num">
            {logbook.averageConsumption === null ? 'En az iki dolum gerekli' : `${formatNumber(logbook.averageConsumption)} lt / 100 km`}
          </dd>
        </div>
        <div>
          <dt>Yakıt gideri</dt>
          <dd className="num">{formatMoney(logbook.fuelCost)}</dd>
        </div>
        <div>
          <dt>Bakım gideri</dt>
          <dd className="num">{formatMoney(logbook.maintenanceCost)}</dd>
        </div>
        <div>
          <dt>Muayene bitişi</dt>
          <dd>{vehicle.inspectionDueOn ? formatDate(vehicle.inspectionDueOn) : 'Girilmedi'}</dd>
        </div>
        <div>
          <dt>Sigorta bitişi</dt>
          <dd>{vehicle.insuranceDueOn ? formatDate(vehicle.insuranceDueOn) : 'Girilmedi'}</dd>
        </div>
      </dl>

      <div className="logs">
        <section className="panel">
          <div className="panel__title">
            <h2>Yakıt</h2>
          </div>
          {logbook.fuel.length === 0 ? (
            <p className="empty">Henüz yakıt kaydı yok.</p>
          ) : (
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Tarih</th>
                    <th scope="col" className="right">
                      Litre
                    </th>
                    <th scope="col" className="right">
                      Tutar
                    </th>
                    <th scope="col" className="right">
                      Kilometre
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {logbook.fuel.map((entry) => (
                    <tr key={entry.id}>
                      <td>{formatDate(entry.filledOn)}</td>
                      <td className="right num">{formatNumber(entry.liters)}</td>
                      <td className="right num">{formatMoney(entry.totalCost)}</td>
                      <td className="right num">{formatNumber(entry.odometerKm)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="panel">
          <div className="panel__title">
            <h2>Bakım</h2>
          </div>
          {logbook.maintenance.length === 0 ? (
            <p className="empty">Henüz bakım kaydı yok.</p>
          ) : (
            <div className="table-scroll">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">Tarih</th>
                    <th scope="col">Yapılan iş</th>
                    <th scope="col" className="right">
                      Tutar
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {logbook.maintenance.map((record) => (
                    <tr key={record.id}>
                      <td>{formatDate(record.performedOn)}</td>
                      <td>
                        {record.description}
                        {record.nextDueOn && <span className="muted"> Sonraki: {formatDate(record.nextDueOn)}</span>}
                      </td>
                      <td className="right num">{formatMoney(record.cost)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>

      {dialog === 'fuel' && (
        <FormDialog
          title="Yakıt ekle"
          submitLabel="Kaydet"
          onSubmit={(fields) => send('POST', `/fleet/vehicles/${vehicle.id}/fuel`, {
            filledOn: text(fields, 'filledOn'), liters: num(fields, 'liters'), totalCost: num(fields, 'totalCost'),
            odometerKm: num(fields, 'odometerKm'),
          })}
          onSaved={saved}
          onClose={() => setDialog(undefined)}
        >
          <div className="field-row">
            <label className="field">
              Tarih
              <input name="filledOn" type="date" required defaultValue={today} max={today} />
            </label>
            <label className="field">
              Kilometre
              <input name="odometerKm" type="number" required min={vehicle.odometerKm} defaultValue={vehicle.odometerKm} />
            </label>
          </div>
          <div className="field-row">
            <label className="field">
              Litre
              <input name="liters" inputMode="decimal" required />
            </label>
            <label className="field">
              Tutar (₺)
              <input name="totalCost" inputMode="decimal" required />
            </label>
          </div>
        </FormDialog>
      )}

      {dialog === 'maintenance' && (
        <FormDialog
          title="Bakım ekle"
          submitLabel="Kaydet"
          onSubmit={(fields) => send('POST', `/fleet/vehicles/${vehicle.id}/maintenance`, {
            performedOn: text(fields, 'performedOn'), description: text(fields, 'description'), cost: num(fields, 'cost'),
            odometerKm: num(fields, 'odometerKm'), nextDueOn: text(fields, 'nextDueOn'),
          })}
          onSaved={saved}
          onClose={() => setDialog(undefined)}
        >
          <label className="field">
            Yapılan iş
            <input name="description" required placeholder="Periyodik bakım, lastik değişimi..." />
          </label>
          <div className="field-row">
            <label className="field">
              Tarih
              <input name="performedOn" type="date" required defaultValue={today} max={today} />
            </label>
            <label className="field">
              Tutar (₺)
              <input name="cost" inputMode="decimal" required defaultValue={0} />
            </label>
          </div>
          <div className="field-row">
            <label className="field">
              Kilometre
              <input name="odometerKm" type="number" required defaultValue={vehicle.odometerKm} />
            </label>
            <label className="field">
              Sonraki bakım <small>İsteğe bağlı</small>
              <input name="nextDueOn" type="date" />
            </label>
          </div>
        </FormDialog>
      )}

      {dialog === 'edit' && (
        <FormDialog
          title="Aracı düzenle"
          submitLabel="Kaydet"
          onSubmit={(fields) => send('PUT', `/fleet/vehicles/${vehicle.id}`, readVehicle(fields))}
          onSaved={saved}
          onClose={() => setDialog(undefined)}
        >
          <VehicleFields vehicle={vehicle} />
        </FormDialog>
      )}
    </div>
  )
}

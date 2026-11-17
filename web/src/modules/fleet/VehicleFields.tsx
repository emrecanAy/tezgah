import type { Vehicle } from '../../api/types'
import { num, text } from '../../components/Dialog'

export function VehicleFields({ vehicle }: { vehicle?: Vehicle }) {
  return (
    <>
      <div className="field-row">
        <label className="field">
          Plaka
          <input name="plate" required placeholder="16 ABC 123" defaultValue={vehicle?.plate} />
        </label>
        <label className="field">
          Model yılı
          <input name="year" type="number" min={1980} max={2100} required defaultValue={vehicle?.year ?? new Date().getFullYear()} />
        </label>
      </div>
      <div className="field-row">
        <label className="field">
          Marka
          <input name="brand" required defaultValue={vehicle?.brand} />
        </label>
        <label className="field">
          Model
          <input name="model" required defaultValue={vehicle?.model} />
        </label>
      </div>
      <div className="field-row">
        <label className="field">
          Kilometre
          <input name="odometerKm" type="number" min={vehicle?.odometerKm ?? 0} required defaultValue={vehicle?.odometerKm ?? 0} />
        </label>
        <label className="field">
          Sürücü <small>İsteğe bağlı</small>
          <input name="driver" defaultValue={vehicle?.driver ?? ''} />
        </label>
      </div>
      <div className="field-row">
        <label className="field">
          Muayene bitişi
          <input name="inspectionDueOn" type="date" defaultValue={vehicle?.inspectionDueOn ?? ''} />
        </label>
        <label className="field">
          Sigorta bitişi
          <input name="insuranceDueOn" type="date" defaultValue={vehicle?.insuranceDueOn ?? ''} />
        </label>
      </div>
    </>
  )
}

export function readVehicle(fields: FormData) {
  return {
    plate: text(fields, 'plate'),
    brand: text(fields, 'brand'),
    model: text(fields, 'model'),
    year: num(fields, 'year'),
    odometerKm: num(fields, 'odometerKm'),
    driver: text(fields, 'driver'),
    inspectionDueOn: text(fields, 'inspectionDueOn'),
    insuranceDueOn: text(fields, 'insuranceDueOn'),
  }
}

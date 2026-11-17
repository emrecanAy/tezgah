import { type CSSProperties, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { Appointment, Customer, Service, StaffMember } from '../../api/types'
import { Dialog, FormDialog, num, text } from '../../components/Dialog'
import { layoutDay } from '../../lib/agenda'
import { addDays, formatLongDay, formatMoney, formatTime, startOfDay, toDateInput } from '../../lib/format'
import './AppointmentsPage.css'

const OPEN_HOUR = 9
const CLOSE_HOUR = 20
const HOURS = Array.from({ length: CLOSE_HOUR - OPEN_HOUR }, (_, index) => OPEN_HOUR + index)

const STATUS_LABEL: Record<Appointment['status'], string> = {
  scheduled: 'Bekliyor',
  completed: 'Tamamlandı',
  cancelled: 'İptal edildi',
  noshow: 'Gelmedi',
}

export function AppointmentsPage() {
  const queryClient = useQueryClient()
  const [day, setDay] = useState(() => startOfDay(new Date()))
  const [view, setView] = useState<'agenda' | 'setup'>('agenda')
  const [dialog, setDialog] = useState<'book' | 'service' | 'staff'>()
  const [opened, setOpened] = useState<Appointment>()

  const { data: staff } = useQuery({ queryKey: ['appointments', 'staff'], queryFn: () => api<StaffMember[]>('/appointments/staff') })
  const { data: services } = useQuery({ queryKey: ['appointments', 'services'], queryFn: () => api<Service[]>('/appointments/services') })
  const { data: appointments } = useQuery({
    queryKey: ['appointments', 'day', day.toISOString()],
    queryFn: () => api<Appointment[]>(`/appointments?from=${day.toISOString()}&to=${addDays(day, 1).toISOString()}`),
  })

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['appointments'] })
  const closeAndRefresh = () => {
    setDialog(undefined)
    setOpened(undefined)
    void refresh()
  }

  const close = useMutation({
    mutationFn: ({ id, action }: { id: string; action: 'complete' | 'cancel' | 'no-show' }) =>
      send('POST', `/appointments/${id}/${action}`),
    onSuccess: () => {
      closeAndRefresh()
      // A completed visit may have stamped a loyalty card.
      void queryClient.invalidateQueries({ queryKey: ['loyalty'] })
    },
  })

  const activeStaff = staff?.filter((member) => member.isActive) ?? []
  const ready = activeStaff.length > 0 && (services?.some((service) => service.isActive) ?? false)

  return (
    <div style={{ '--accent': 'var(--appointments)' } as CSSProperties}>
      <header className="page-header">
        <div>
          <h1>Randevu</h1>
          <p>Aynı personele aynı saatte iki randevu verilemez; bunu sistem garanti eder.</p>
        </div>
        <div className="page-actions">
          <button type="button" className="button button--quiet" onClick={() => setView(view === 'agenda' ? 'setup' : 'agenda')}>
            {view === 'agenda' ? 'Hizmetler ve personel' : 'Takvime dön'}
          </button>
          <button type="button" className="button" disabled={!ready} onClick={() => setDialog('book')}>
            Randevu ver
          </button>
        </div>
      </header>

      {view === 'setup' || !ready ? (
        <div className="setup">
          {!ready && view === 'agenda' && (
            <p className="setup__hint">Randevu verebilmek için en az bir hizmet ve bir personel tanımlayın.</p>
          )}
          <section className="panel">
            <div className="panel__title">
              <h2>Hizmetler</h2>
              <button type="button" className="button button--quiet button--small" onClick={() => setDialog('service')}>
                Hizmet ekle
              </button>
            </div>
            {services?.length ? (
              <table className="table">
                <tbody>
                  {services.map((service) => (
                    <tr key={service.id}>
                      <td>
                        <strong>{service.name}</strong>
                      </td>
                      <td className="muted">{service.durationMinutes} dk</td>
                      <td className="right num">{formatMoney(service.price)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <p className="empty">Henüz hizmet yok.</p>
            )}
          </section>

          <section className="panel">
            <div className="panel__title">
              <h2>Personel</h2>
              <button type="button" className="button button--quiet button--small" onClick={() => setDialog('staff')}>
                Personel ekle
              </button>
            </div>
            {staff?.length ? (
              <table className="table">
                <tbody>
                  {staff.map((member) => (
                    <tr key={member.id}>
                      <td>
                        <strong>{member.name}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <p className="empty">Henüz personel yok.</p>
            )}
          </section>
        </div>
      ) : (
        <>
          <div className="day-nav">
            <button type="button" className="button button--quiet button--small" onClick={() => setDay(addDays(day, -1))} aria-label="Önceki gün">
              ‹
            </button>
            <button type="button" className="button button--quiet button--small" onClick={() => setDay(startOfDay(new Date()))}>
              Bugün
            </button>
            <button type="button" className="button button--quiet button--small" onClick={() => setDay(addDays(day, 1))} aria-label="Sonraki gün">
              ›
            </button>
            <h2>{formatLongDay(day)}</h2>
          </div>

          <div className="agenda panel">
            <div className="agenda__scroll">
              <div className="agenda__grid" style={{ '--columns': activeStaff.length } as CSSProperties}>
                <div className="agenda__corner" />
                {activeStaff.map((member) => (
                  <div key={member.id} className="agenda__staff">
                    {member.name}
                  </div>
                ))}

                <div className="agenda__hours" aria-hidden="true">
                  {HOURS.map((hour) => (
                    <span key={hour}>{hour.toString().padStart(2, '0')}:00</span>
                  ))}
                </div>
                {activeStaff.map((member) => {
                  const mine = (appointments ?? []).filter((appointment) => appointment.staffId === member.id)
                  const blocks = layoutDay(mine, OPEN_HOUR, CLOSE_HOUR)
                  return (
                    <div key={member.id} className="agenda__column">
                      {mine.map((appointment, index) => (
                        <button
                          key={appointment.id}
                          type="button"
                          className={`booking booking--${appointment.status}${isShort(appointment) ? ' booking--short' : ''}`}
                          title={`${appointment.customerName}, ${appointment.serviceName}`}
                          style={{ top: `${blocks[index]!.topPercent}%`, height: `${blocks[index]!.heightPercent}%` }}
                          onClick={() => setOpened(appointment)}
                        >
                          <strong>
                            {formatTime(appointment.startsAt)} {appointment.customerName}
                          </strong>
                          <span>{appointment.serviceName}</span>
                        </button>
                      ))}
                    </div>
                  )
                })}
              </div>
            </div>
          </div>
        </>
      )}

      {dialog === 'book' && (
        <BookDialog day={day} staff={activeStaff} services={services?.filter((service) => service.isActive) ?? []}
          onSaved={closeAndRefresh} onClose={() => setDialog(undefined)} />
      )}

      {dialog === 'service' && (
        <FormDialog
          title="Hizmet ekle"
          submitLabel="Kaydet"
          onSubmit={(fields) => send('POST', '/appointments/services', {
            name: text(fields, 'name'), durationMinutes: num(fields, 'durationMinutes'), price: num(fields, 'price'),
          })}
          onSaved={closeAndRefresh}
          onClose={() => setDialog(undefined)}
        >
          <label className="field">
            Hizmet adı
            <input name="name" required placeholder="Saç kesimi" />
          </label>
          <div className="field-row">
            <label className="field">
              Süre (dakika)
              <input name="durationMinutes" type="number" min={5} max={480} step={5} required defaultValue={30} />
            </label>
            <label className="field">
              Fiyat (₺)
              <input name="price" inputMode="decimal" required defaultValue={0} />
            </label>
          </div>
        </FormDialog>
      )}

      {dialog === 'staff' && (
        <FormDialog
          title="Personel ekle"
          submitLabel="Kaydet"
          onSubmit={(fields) => send('POST', '/appointments/staff', { name: text(fields, 'name') })}
          onSaved={closeAndRefresh}
          onClose={() => setDialog(undefined)}
        >
          <label className="field">
            Ad
            <input name="name" required />
          </label>
        </FormDialog>
      )}

      {opened && (
        <Dialog title={opened.customerName} onClose={() => setOpened(undefined)}>
          <dl className="facts">
            <div>
              <dt>Hizmet</dt>
              <dd>{opened.serviceName}</dd>
            </div>
            <div>
              <dt>Personel</dt>
              <dd>{opened.staffName}</dd>
            </div>
            <div>
              <dt>Saat</dt>
              <dd className="num">
                {formatTime(opened.startsAt)} - {formatTime(opened.endsAt)}
              </dd>
            </div>
            <div>
              <dt>Durum</dt>
              <dd>{STATUS_LABEL[opened.status]}</dd>
            </div>
          </dl>
          {close.error && (
            <p className="form-error" role="alert">
              {close.error.message}
            </p>
          )}
          {opened.status === 'scheduled' && (
            <div className="form-actions">
              <button type="button" className="button button--danger" disabled={close.isPending}
                onClick={() => close.mutate({ id: opened.id, action: 'cancel' })}>
                İptal et
              </button>
              <button type="button" className="button button--quiet" disabled={close.isPending}
                onClick={() => close.mutate({ id: opened.id, action: 'no-show' })}>
                Gelmedi
              </button>
              <button type="button" className="button" disabled={close.isPending}
                onClick={() => close.mutate({ id: opened.id, action: 'complete' })}>
                Tamamlandı
              </button>
            </div>
          )}
        </Dialog>
      )}
    </div>
  )
}

/** Too short to fit two lines of text at the agenda's scale. */
function isShort(appointment: Appointment): boolean {
  return new Date(appointment.endsAt).getTime() - new Date(appointment.startsAt).getTime() < 40 * 60_000
}

interface BookDialogProps {
  day: Date
  staff: StaffMember[]
  services: Service[]
  onSaved: () => void
  onClose: () => void
}

function BookDialog({ day, staff, services, onSaved, onClose }: BookDialogProps) {
  const { data: customers } = useQuery({ queryKey: ['customers', ''], queryFn: () => api<Customer[]>('/customers?search=') })

  function book(fields: FormData) {
    return send('POST', '/appointments', {
      customerId: text(fields, 'customerId'),
      serviceId: text(fields, 'serviceId'),
      staffId: text(fields, 'staffId'),
      // The browser's local time; the API stores it in UTC.
      startsAt: new Date(`${text(fields, 'date')}T${text(fields, 'time')}`).toISOString(),
      notes: text(fields, 'notes'),
    })
  }

  return (
    <FormDialog title="Randevu ver" submitLabel="Randevuyu kaydet" onSubmit={book} onSaved={onSaved} onClose={onClose}>
      <label className="field">
        Müşteri
        <select name="customerId" required defaultValue="">
          <option value="" disabled>
            Seçin
          </option>
          {customers?.map((customer) => (
            <option key={customer.id} value={customer.id}>
              {customer.fullName}
            </option>
          ))}
        </select>
      </label>
      <div className="field-row">
        <label className="field">
          Hizmet
          <select name="serviceId" required>
            {services.map((service) => (
              <option key={service.id} value={service.id}>
                {service.name} ({service.durationMinutes} dk)
              </option>
            ))}
          </select>
        </label>
        <label className="field">
          Personel
          <select name="staffId" required>
            {staff.map((member) => (
              <option key={member.id} value={member.id}>
                {member.name}
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="field-row">
        <label className="field">
          Gün
          <input name="date" type="date" required defaultValue={toDateInput(day)} />
        </label>
        <label className="field">
          Saat
          <input name="time" type="time" step={300} required defaultValue="14:00" />
        </label>
      </div>
      <label className="field">
        Not <small>İsteğe bağlı</small>
        <input name="notes" />
      </label>
    </FormDialog>
  )
}

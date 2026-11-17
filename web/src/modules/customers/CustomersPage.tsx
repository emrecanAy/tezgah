import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { Customer } from '../../api/types'
import { FormDialog, text } from '../../components/Dialog'
import { formatPhone } from '../../lib/format'

export function CustomersPage() {
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  /** `'new'` while adding, a customer while editing. */
  const [editing, setEditing] = useState<Customer | 'new'>()

  const { data: customers } = useQuery({
    queryKey: ['customers', search],
    queryFn: () => api<Customer[]>(`/customers?search=${encodeURIComponent(search)}`),
    placeholderData: (previous) => previous,
  })

  function save(fields: FormData) {
    const body = {
      fullName: text(fields, 'fullName'),
      phone: text(fields, 'phone'),
      email: text(fields, 'email'),
      notes: text(fields, 'notes'),
    }
    return editing === 'new' ? send('POST', '/customers', body) : send('PUT', `/customers/${editing!.id}`, body)
  }

  const current = editing === 'new' ? undefined : editing

  return (
    <>
      <header className="page-header">
        <div>
          <h1>Müşteriler</h1>
          <p>Sadakat kartları ve randevular bu kayıtları kullanır.</p>
        </div>
        <div className="page-actions">
          <input
            type="search"
            className="search"
            placeholder="Ad veya telefon ara"
            aria-label="Müşteri ara"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <button type="button" className="button" onClick={() => setEditing('new')}>
            Müşteri ekle
          </button>
        </div>
      </header>

      <div className="panel table-scroll">
        {customers?.length === 0 ? (
          <p className="empty">
            <strong>{search ? 'Aramayla eşleşen müşteri yok' : 'Henüz müşteri yok'}</strong>
            {search ? 'Farklı bir ad veya numara deneyin.' : 'İlk müşterinizi ekleyerek başlayın.'}
          </p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Ad soyad</th>
                <th scope="col">Telefon</th>
                <th scope="col">E-posta</th>
                <th scope="col">Not</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {customers?.map((customer) => (
                <tr key={customer.id}>
                  <td>
                    <strong>{customer.fullName}</strong>
                  </td>
                  <td className="num">{formatPhone(customer.phone)}</td>
                  <td className="muted">{customer.email ?? ''}</td>
                  <td className="muted">{customer.notes ?? ''}</td>
                  <td className="right">
                    <button type="button" className="button button--quiet button--small" onClick={() => setEditing(customer)}>
                      Düzenle
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {editing && (
        <FormDialog
          title={current ? 'Müşteriyi düzenle' : 'Müşteri ekle'}
          submitLabel="Kaydet"
          onSubmit={save}
          onSaved={() => {
            setEditing(undefined)
            void queryClient.invalidateQueries({ queryKey: ['customers'] })
          }}
          onClose={() => setEditing(undefined)}
        >
          <label className="field">
            Ad soyad
            <input name="fullName" required defaultValue={current?.fullName} />
          </label>
          <label className="field">
            Telefon
            <input name="phone" type="tel" required placeholder="05xx xxx xx xx"
              defaultValue={current ? formatPhone(current.phone) : ''} />
          </label>
          <label className="field">
            E-posta <small>İsteğe bağlı</small>
            <input name="email" type="email" defaultValue={current?.email ?? ''} />
          </label>
          <label className="field">
            Not <small>İsteğe bağlı</small>
            <textarea name="notes" rows={2} defaultValue={current?.notes ?? ''} />
          </label>
        </FormDialog>
      )}
    </>
  )
}

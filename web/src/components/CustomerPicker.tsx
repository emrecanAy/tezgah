import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Customer } from '../api/types'
import { formatPhone } from '../lib/format'
import { Dialog } from './Dialog'
import './CustomerPicker.css'

/** Finds the customer at the counter: type a few letters or digits, pick. */
export function CustomerPicker({ onPick, onClose }: { onPick: (customer: Customer) => void; onClose: () => void }) {
  const [search, setSearch] = useState('')
  const { data: customers } = useQuery({
    queryKey: ['customers', search],
    queryFn: () => api<Customer[]>(`/customers?search=${encodeURIComponent(search)}`),
    placeholderData: (previous) => previous,
  })

  return (
    <Dialog title="Müşteri seç" onClose={onClose}>
      <label className="field">
        Müşteri
        <input type="search" autoFocus placeholder="Ad veya telefon" value={search} onChange={(event) => setSearch(event.target.value)} />
      </label>
      <ul className="pick-list">
        {customers?.slice(0, 8).map((customer) => (
          <li key={customer.id}>
            <button type="button" onClick={() => onPick(customer)}>
              <strong>{customer.fullName}</strong>
              <span className="muted num">{formatPhone(customer.phone)}</span>
            </button>
          </li>
        ))}
        {customers?.length === 0 && <li className="muted">Eşleşen müşteri yok. Önce Müşteriler bölümünden ekleyin.</li>}
      </ul>
    </Dialog>
  )
}

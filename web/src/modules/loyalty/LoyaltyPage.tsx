import { type CSSProperties, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { Customer, LoyaltyCard, LoyaltyProgram } from '../../api/types'
import { Dialog, FormDialog, num, text } from '../../components/Dialog'
import { formatPhone } from '../../lib/format'
import { StampCard } from './StampCard'
import './LoyaltyPage.css'

export function LoyaltyPage() {
  const queryClient = useQueryClient()
  const [selectedId, setSelectedId] = useState<string>()
  const [dialog, setDialog] = useState<'program' | 'edit-program' | 'stamp'>()
  const [notice, setNotice] = useState<string>()

  const { data: programs } = useQuery({ queryKey: ['loyalty', 'programs'], queryFn: () => api<LoyaltyProgram[]>('/loyalty/programs') })
  const program = programs?.find((candidate) => candidate.id === selectedId) ?? programs?.[0]

  const { data: cards } = useQuery({
    queryKey: ['loyalty', 'cards', program?.id],
    queryFn: () => api<LoyaltyCard[]>(`/loyalty/programs/${program!.id}/cards`),
    enabled: program !== undefined,
  })

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['loyalty'] })

  const stamp = useMutation({
    mutationFn: (customerId: string) =>
      send<{ card: LoyaltyCard; rewardEarned: boolean }>('POST', `/loyalty/programs/${program!.id}/stamps`, { customerId }),
    onSuccess: ({ card, rewardEarned }) => {
      setNotice(rewardEarned ? `${card.customerName} kartını doldurdu: ${program!.reward}.` : undefined)
      return refresh()
    },
    onError: (error) => setNotice(error.message),
  })

  const redeem = useMutation({
    mutationFn: (cardId: string) => send('POST', `/loyalty/cards/${cardId}/redemptions`),
    onSuccess: () => {
      setNotice(undefined)
      return refresh()
    },
    onError: (error) => setNotice(error.message),
  })

  function saveProgram(fields: FormData) {
    const body = {
      name: text(fields, 'name'),
      stampsRequired: num(fields, 'stampsRequired'),
      reward: text(fields, 'reward'),
      stampOnAppointment: fields.get('stampOnAppointment') === 'on',
      isActive: dialog === 'edit-program' ? fields.get('isActive') === 'on' : true,
    }
    return dialog === 'edit-program'
      ? send('PUT', `/loyalty/programs/${program!.id}`, body)
      : send<LoyaltyProgram>('POST', '/loyalty/programs', body).then((created) => setSelectedId(created.id))
  }

  const closeAndRefresh = () => {
    setDialog(undefined)
    void refresh()
  }

  return (
    <div style={{ '--accent': 'var(--loyalty)' } as CSSProperties}>
      <header className="page-header">
        <div>
          <h1>Sadakat</h1>
          <p>Kuralı siz koyarsınız: kaç alışverişe bir ödül.</p>
        </div>
        <div className="page-actions">
          <button type="button" className="button button--quiet" onClick={() => setDialog('program')}>
            Program ekle
          </button>
          {program?.isActive && (
            <button type="button" className="button" onClick={() => setDialog('stamp')}>
              Müşteriye damga bas
            </button>
          )}
        </div>
      </header>

      {programs?.length === 0 && (
        <p className="panel empty">
          <strong>Henüz bir sadakat programınız yok</strong>
          Örneğin "4 kahveye 1 kahve bizden" gibi bir kural tanımlayın.
        </p>
      )}

      {program && (
        <>
          <ul className="programs" aria-label="Programlar">
            {programs!.map((candidate) => (
              <li key={candidate.id}>
                <button
                  type="button"
                  className={candidate.id === program.id ? 'program program--selected' : 'program'}
                  aria-pressed={candidate.id === program.id}
                  onClick={() => setSelectedId(candidate.id)}
                >
                  <strong>{candidate.name}</strong>
                  <span>
                    {candidate.stampsRequired} damgaya {candidate.reward}
                  </span>
                  <span className="muted">
                    {candidate.cardCount} kart{candidate.isActive ? '' : ', duraklatıldı'}
                  </span>
                </button>
              </li>
            ))}
          </ul>

          <div className="loyalty__bar">
            <p className="muted">
              {program.stampOnAppointment ? 'Tamamlanan her randevu bu karta otomatik damga basar.' : 'Damgalar kasada elle basılır.'}
            </p>
            <button type="button" className="button button--quiet button--small" onClick={() => setDialog('edit-program')}>
              Programı düzenle
            </button>
          </div>

          {notice && (
            <p className="loyalty__notice" role="status">
              {notice}
            </p>
          )}

          {cards?.length === 0 ? (
            <p className="panel empty">
              <strong>Bu programda henüz kart yok</strong>
              İlk damgayı bastığınız müşterinin kartı kendiliğinden açılır.
            </p>
          ) : (
            <div className="cards">
              {cards?.map((card) => (
                <StampCard
                  key={card.id}
                  card={card}
                  reward={program.reward}
                  busy={stamp.isPending || redeem.isPending}
                  onStamp={() => stamp.mutate(card.customerId)}
                  onRedeem={() => redeem.mutate(card.id)}
                />
              ))}
            </div>
          )}
        </>
      )}

      {(dialog === 'program' || dialog === 'edit-program') && (
        <FormDialog
          title={dialog === 'program' ? 'Program ekle' : 'Programı düzenle'}
          submitLabel="Kaydet"
          onSubmit={saveProgram}
          onSaved={closeAndRefresh}
          onClose={() => setDialog(undefined)}
        >
          <label className="field">
            Program adı
            <input name="name" required placeholder="Kahve Kartı" defaultValue={dialog === 'edit-program' ? program?.name : ''} />
          </label>
          <div className="field-row">
            <label className="field">
              Kaç damgada dolar?
              <input name="stampsRequired" type="number" min={2} max={20} required
                defaultValue={dialog === 'edit-program' ? program?.stampsRequired : 4} />
            </label>
            <label className="field">
              Ödül
              <input name="reward" required placeholder="1 kahve bizden" defaultValue={dialog === 'edit-program' ? program?.reward : ''} />
            </label>
          </div>
          <label className="check">
            <input type="checkbox" name="stampOnAppointment"
              defaultChecked={dialog === 'edit-program' && program?.stampOnAppointment} />
            <span>Randevu tamamlandığında otomatik damga bas (Randevu modülü açıksa)</span>
          </label>
          {dialog === 'edit-program' && (
            <label className="check">
              <input type="checkbox" name="isActive" defaultChecked={program?.isActive} />
              <span>Program açık. Kapatırsanız yeni damga basılamaz, mevcut kartlar durur.</span>
            </label>
          )}
        </FormDialog>
      )}

      {dialog === 'stamp' && program && (
        <StampDialog
          onClose={() => setDialog(undefined)}
          onPick={(customerId) => {
            setDialog(undefined)
            stamp.mutate(customerId)
          }}
        />
      )}
    </div>
  )
}

/** Finds the customer at the till: type a few letters or digits, pick, stamped. */
function StampDialog({ onPick, onClose }: { onPick: (customerId: string) => void; onClose: () => void }) {
  const [search, setSearch] = useState('')
  const { data: customers } = useQuery({
    queryKey: ['customers', search],
    queryFn: () => api<Customer[]>(`/customers?search=${encodeURIComponent(search)}`),
    placeholderData: (previous) => previous,
  })

  return (
    <Dialog title="Müşteriye damga bas" onClose={onClose}>
      <label className="field">
        Müşteri
        <input type="search" autoFocus placeholder="Ad veya telefon" value={search} onChange={(event) => setSearch(event.target.value)} />
      </label>
      <ul className="pick-list" style={{ marginTop: '0.75rem' }}>
        {customers?.slice(0, 8).map((customer) => (
          <li key={customer.id}>
            <button type="button" onClick={() => onPick(customer.id)}>
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

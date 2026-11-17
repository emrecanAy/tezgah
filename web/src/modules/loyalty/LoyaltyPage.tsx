import { type CSSProperties, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { EarnRule, LoyaltyCard, LoyaltyProgram, Me, MenuItem } from '../../api/types'
import { FormDialog, num, text } from '../../components/Dialog'
import { StampCard } from './StampCard'
import { useCan } from '../../lib/access'
import './LoyaltyPage.css'

const EARN_RULES: { rule: EarnRule; label: string }[] = [
  { rule: 'item', label: 'Satılan her ürün 1 damga (iki latte, iki damga)' },
  { rule: 'order', label: 'Her sipariş 1 damga, içinde kaç ürün olursa olsun' },
  { rule: 'appointment', label: 'Tamamlanan her randevu 1 damga' },
]

/** The program's rule as a shop owner would say it. */
function describeRule(program: LoyaltyProgram): string {
  const items = program.category ? `${program.category} kategorisindeki her ürün` : 'Siparişteki her ürün'
  switch (program.earnRule) {
    case 'appointment':
      return 'Tamamlanan her randevu bu karta 1 damga basar.'
    case 'order':
      return program.category
        ? `İçinde ${program.category} kategorisinden ürün olan her sipariş 1 damga basar.`
        : 'Müşteriye kaydedilen her sipariş 1 damga basar.'
    case 'item':
      return `${items} 1 damga basar; ödül olarak verilen ürün damga kazandırmaz.`
  }
}

export function LoyaltyPage() {
  const canManage = useCan('loyalty.programs')
  const canRedeem = useCan('loyalty.redeem')
  const queryClient = useQueryClient()
  const [selectedId, setSelectedId] = useState<string>()
  const [dialog, setDialog] = useState<'program' | 'edit-program'>()
  const [notice, setNotice] = useState<string>()

  const { data: programs } = useQuery({ queryKey: ['loyalty', 'programs'], queryFn: () => api<LoyaltyProgram[]>('/loyalty/programs') })
  const program = programs?.find((candidate) => candidate.id === selectedId) ?? programs?.[0]

  const { data: cards } = useQuery({
    queryKey: ['loyalty', 'cards', program?.id],
    queryFn: () => api<LoyaltyCard[]>(`/loyalty/programs/${program!.id}/cards`),
    enabled: program !== undefined,
  })

  const refresh = () => queryClient.invalidateQueries({ queryKey: ['loyalty'] })

  const redeem = useMutation({
    mutationFn: (cardId: string) => send('POST', `/loyalty/cards/${cardId}/redemptions`),
    onSuccess: () => {
      setNotice(undefined)
      return refresh()
    },
    onError: (error) => setNotice(error.message),
  })

  function saveProgram(fields: FormData) {
    const earnRule = fields.get('earnRule') as EarnRule
    const body = {
      name: text(fields, 'name'),
      stampsRequired: num(fields, 'stampsRequired'),
      reward: text(fields, 'reward'),
      earnRule,
      category: earnRule === 'appointment' ? null : text(fields, 'category'),
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
          <p>Damgalar sipariş ve randevulardan kendiliğinden işlenir; elle damga basılmaz.</p>
        </div>
        {canManage && (
          <button type="button" className="button" onClick={() => setDialog('program')}>
            Program ekle
          </button>
        )}
      </header>

      {programs?.length === 0 && (
        <p className="panel empty">
          <strong>Henüz bir sadakat programınız yok</strong>
          Örneğin "her 4 kahveye 1 kahve bizden" gibi bir kural tanımlayın.
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
              {describeRule(program)} İptal edilen siparişin damgası geri alınır.
            </p>
            {canManage && (
              <button type="button" className="button button--quiet button--small" onClick={() => setDialog('edit-program')}>
                Programı düzenle
              </button>
            )}
          </div>

          {notice && (
            <p className="loyalty__notice" role="status">
              {notice}
            </p>
          )}

          {cards?.length === 0 ? (
            <p className="panel empty">
              <strong>Bu programda henüz kart yok</strong>
              {program.earnRule === 'appointment'
                ? 'Bir müşterinin randevusu tamamlandığında kartı kendiliğinden açılır.'
                : 'Kasada bir müşteriye sipariş kaydedildiğinde kartı kendiliğinden açılır.'}
            </p>
          ) : (
            <div className="cards">
              {cards?.map((card) => (
                <StampCard
                  key={card.id}
                  card={card}
                  reward={program.reward}
                  busy={redeem.isPending}
                  onRedeem={program.earnRule === 'appointment' && canRedeem ? () => redeem.mutate(card.id) : undefined}
                />
              ))}
            </div>
          )}
        </>
      )}

      {dialog && (
        <ProgramDialog
          program={dialog === 'edit-program' ? program : undefined}
          onSubmit={saveProgram}
          onSaved={closeAndRefresh}
          onClose={() => setDialog(undefined)}
        />
      )}
    </div>
  )
}

interface ProgramDialogProps {
  program: LoyaltyProgram | undefined
  onSubmit: (fields: FormData) => Promise<unknown>
  onSaved: () => void
  onClose: () => void
}

function ProgramDialog({ program, onSubmit, onSaved, onClose }: ProgramDialogProps) {
  const [rule, setRule] = useState<EarnRule>(program?.earnRule ?? 'item')
  // Offers the till's own categories, so "Kahve" here is spelled the way the menu spells it.
  const me = useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/me') }).data
  const hasOrders = me?.modules.some((module) => module.key === 'orders') ?? false
  const { data: menu } = useQuery({ queryKey: ['orders', 'menu'], queryFn: () => api<MenuItem[]>('/orders/menu'), enabled: hasOrders })
  const categories = [...new Set(menu?.map((item) => item.category))]

  return (
    <FormDialog
      title={program ? 'Programı düzenle' : 'Program ekle'}
      submitLabel="Kaydet"
      onSubmit={onSubmit}
      onSaved={onSaved}
      onClose={onClose}
    >
      <label className="field">
        Program adı
        <input name="name" required placeholder="Kahve Kartı" defaultValue={program?.name} />
      </label>
      <div className="field-row">
        <label className="field">
          Kaç damgada dolar?
          <input name="stampsRequired" type="number" min={2} max={20} required defaultValue={program?.stampsRequired ?? 4} />
        </label>
        <label className="field">
          Ödül
          <input name="reward" required placeholder="1 kahve bizden" defaultValue={program?.reward} />
        </label>
      </div>
      <fieldset className="earn-rules">
        <legend>Damga nasıl kazanılır?</legend>
        {EARN_RULES.map(({ rule: option, label }) => (
          <label key={option} className="check">
            <input type="radio" name="earnRule" value={option} checked={rule === option} onChange={() => setRule(option)} />
            <span>{label}</span>
          </label>
        ))}
      </fieldset>
      {rule !== 'appointment' && (
        <label className="field">
          Sadece bu kategori <small>Boş bırakırsanız her ürün sayılır. Ödül de bu kategoriden bir ürünü bedava yapar.</small>
          <input name="category" list="program-categories" placeholder="Kahve" defaultValue={program?.category ?? ''} />
          <datalist id="program-categories">
            {categories.map((category) => (
              <option key={category} value={category} />
            ))}
          </datalist>
        </label>
      )}
      {program && (
        <label className="check">
          <input type="checkbox" name="isActive" defaultChecked={program.isActive} />
          <span>Program açık. Kapatırsanız yeni damga işlenmez, mevcut kartlar durur.</span>
        </label>
      )}
    </FormDialog>
  )
}

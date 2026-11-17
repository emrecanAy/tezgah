import { type CSSProperties, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { Customer, MenuItem, Order, RewardOffer } from '../../api/types'
import { CustomerPicker } from '../../components/CustomerPicker'
import { Dialog, FormDialog, num, text } from '../../components/Dialog'
import { addDays, formatMoney, formatPhone, formatTime, startOfDay } from '../../lib/format'
import { type TicketLine, byCategory, changeQuantity, ticketTotals } from '../../lib/ticket'
import { mayCancelOrder, useCan, useMe } from '../../lib/access'
import './OrdersPage.css'

type MenuAction = { kind: 'menu' } | { kind: 'add' } | { kind: 'edit'; item: MenuItem }

export function OrdersPage() {
  const me = useMe()
  const canEditMenu = useCan('orders.menu')
  const queryClient = useQueryClient()
  const [menuAction, setMenuAction] = useState<MenuAction>()
  const [cancelling, setCancelling] = useState<Order>()

  const { data: menu } = useQuery({ queryKey: ['orders', 'menu'], queryFn: () => api<MenuItem[]>('/orders/menu') })
  const today = startOfDay(new Date())
  const { data: orders } = useQuery({
    queryKey: ['orders', 'list', today.toISOString()],
    queryFn: () =>
      api<Order[]>(`/orders?from=${encodeURIComponent(today.toISOString())}&to=${encodeURIComponent(addDays(today, 1).toISOString())}`),
  })

  const placed = orders?.filter((order) => order.status === 'placed') ?? []
  const takings = placed.reduce((sum, order) => sum + order.total, 0)
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['orders'] })

  return (
    <div style={{ '--accent': 'var(--orders)' } as CSSProperties}>
      <header className="page-header">
        <div>
          <h1>Sipariş</h1>
          <p>
            Bugün {placed.length} sipariş, {formatMoney(takings)} ciro.
          </p>
        </div>
        {canEditMenu && (
          <button type="button" className="button button--quiet" onClick={() => setMenuAction({ kind: 'menu' })}>
            Menüyü düzenle
          </button>
        )}
      </header>

      {menu?.length === 0 ? (
        <p className="panel empty">
          <strong>Menünüz boş</strong>
          {canEditMenu ? 'Sattığınız ilk ürünü ekleyin; kasa tutarı menüdeki fiyattan hesaplar.' : 'Menüyü bir yönetici hazırlamalı.'}
          {canEditMenu && (
            <button type="button" className="button" style={{ marginTop: '1rem' }} onClick={() => setMenuAction({ kind: 'add' })}>
              Menüye ürün ekle
            </button>
          )}
        </p>
      ) : (
        menu && <Till menu={menu} onPlaced={refresh} />
      )}

      <section className="today">
        <h2>Bugünün siparişleri</h2>
        {orders?.length === 0 ? (
          <p className="muted">Bugün henüz sipariş yok.</p>
        ) : (
          <div className="panel table-scroll">
            <table className="table">
              <thead>
                <tr>
                  <th scope="col">Saat</th>
                  <th scope="col">Müşteri</th>
                  <th scope="col">Ürünler</th>
                  <th scope="col" className="right">
                    Tutar
                  </th>
                  <th scope="col" />
                </tr>
              </thead>
              <tbody>
                {orders?.map((order) => (
                  <tr key={order.id} className={order.status === 'cancelled' ? 'order--cancelled' : undefined}>
                    <td className="num">{formatTime(order.placedAt)}</td>
                    <td>{order.customerName ?? <span className="muted">Kayıtsız</span>}</td>
                    <td>
                      {order.lines.map((line) => `${line.quantity} × ${line.name}`).join(', ')}
                      {order.rewardNote && <small className="order__reward">Ödül: {order.rewardNote}</small>}
                    </td>
                    <td className="right num">{formatMoney(order.total)}</td>
                    <td className="right">
                      {order.status === 'cancelled' ? (
                        <span className="tag">İptal edildi</span>
                      ) : (
                        mayCancelOrder(me, order) && (
                          <button type="button" className="button button--danger button--small" onClick={() => setCancelling(order)}>
                            İptal et
                          </button>
                        )
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      {cancelling && (
        <CancelDialog
          order={cancelling}
          onClose={() => setCancelling(undefined)}
          onCancelled={() => {
            setCancelling(undefined)
            void refresh()
          }}
        />
      )}

      {menuAction && menu && (
        <MenuDialogs
          action={menuAction}
          menu={menu}
          onAction={setMenuAction}
          onSaved={() => {
            setMenuAction({ kind: 'menu' })
            void refresh()
          }}
          onClose={() => setMenuAction(undefined)}
        />
      )}
    </div>
  )
}

/** The order being rung up: menu on one side, the ticket on the other. */
function Till({ menu, onPlaced }: { menu: MenuItem[]; onPlaced: () => Promise<unknown> }) {
  const queryClient = useQueryClient()
  const [lines, setLines] = useState<TicketLine[]>([])
  const [customer, setCustomer] = useState<Customer>()
  const [rewardCardId, setRewardCardId] = useState<string>()
  const [picking, setPicking] = useState(false)
  const [notice, setNotice] = useState<string>()
  // Chosen here and kept until the order is saved: if the request times out and is sent again, the
  // API recognises the same order instead of recording (and stamping) it twice.
  const [orderId, setOrderId] = useState(() => crypto.randomUUID())

  const { data: rewards } = useQuery({
    queryKey: ['orders', 'rewards', customer?.id],
    queryFn: () => api<RewardOffer[]>(`/orders/rewards?customerId=${customer!.id}`),
    enabled: customer !== undefined,
  })
  const reward = rewards?.find((offer) => offer.cardId === rewardCardId) ?? null
  const totals = ticketTotals(lines, reward)
  const rewardFits = reward === null || totals.freeItemId !== null

  const place = useMutation({
    mutationFn: () =>
      send<Order>('POST', '/orders', {
        id: orderId,
        customerId: customer?.id ?? null,
        items: lines.map((line) => ({ menuItemId: line.item.id, quantity: line.quantity })),
        rewardCardId: reward?.cardId ?? null,
      }),
    onSuccess: (order) => {
      setNotice(
        order.customerName
          ? `${formatMoney(order.total)} kaydedildi. ${order.customerName} müşterisinin sadakat kartı bu siparişe göre işlenir.`
          : `${formatMoney(order.total)} kaydedildi.`,
      )
      setLines([])
      setCustomer(undefined)
      setRewardCardId(undefined)
      setOrderId(crypto.randomUUID())
      void queryClient.invalidateQueries({ queryKey: ['loyalty'] })
      return onPlaced()
    },
  })

  const add = (item: MenuItem, change: 1 | -1) => {
    setNotice(undefined)
    place.reset()
    setLines((current) => changeQuantity(current, item, change))
  }

  return (
    <div className="till">
      <section className="till__menu" aria-label="Menü">
        {byCategory(menu.filter((item) => item.isAvailable)).map(([category, items]) => (
          <div key={category} className="till__category">
            <h2>{category}</h2>
            <div className="till__items">
              {items.map((item) => (
                <button key={item.id} type="button" className="menu-item" onClick={() => add(item, 1)}>
                  <span>{item.name}</span>
                  <span className="num">{formatMoney(item.price)}</span>
                </button>
              ))}
            </div>
          </div>
        ))}
      </section>

      <aside className="ticket" aria-label="Sipariş fişi">
        <div className="ticket__customer">
          {customer ? (
            <>
              <div>
                <strong>{customer.fullName}</strong>
                <span className="muted num">{formatPhone(customer.phone)}</span>
              </div>
              <button
                type="button"
                className="button button--quiet button--small"
                onClick={() => {
                  setCustomer(undefined)
                  setRewardCardId(undefined)
                }}
              >
                Kaldır
              </button>
            </>
          ) : (
            <>
              <span className="muted">Kayıtsız müşteri: sadakat damgası işlenmez.</span>
              <button type="button" className="button button--quiet button--small" onClick={() => setPicking(true)}>
                Müşteri seç
              </button>
            </>
          )}
        </div>

        {lines.length === 0 ? (
          <p className="ticket__empty muted">Menüden ürün seçin.</p>
        ) : (
          <ul className="ticket__lines">
            {lines.map((line) => (
              <li key={line.item.id}>
                <span className="ticket__name">
                  {line.item.name}
                  {totals.freeItemId === line.item.id && <span className="tag tag--ok">1 tanesi ödül</span>}
                </span>
                <span className="stepper">
                  <button type="button" aria-label={`${line.item.name} bir azalt`} onClick={() => add(line.item, -1)}>
                    −
                  </button>
                  <span className="num" aria-live="polite">
                    {line.quantity}
                  </span>
                  <button type="button" aria-label={`${line.item.name} bir artır`} onClick={() => add(line.item, 1)}>
                    +
                  </button>
                </span>
                <span className="num ticket__amount">{formatMoney(line.item.price * line.quantity)}</span>
              </li>
            ))}
          </ul>
        )}

        {customer && rewards && rewards.length > 0 && (
          <fieldset className="ticket__rewards">
            <legend>Kullanılabilir ödül</legend>
            {rewards.map((offer) => (
              <label key={offer.cardId} className="check">
                <input
                  type="checkbox"
                  checked={offer.cardId === rewardCardId}
                  onChange={(event) => setRewardCardId(event.target.checked ? offer.cardId : undefined)}
                />
                <span>
                  {offer.programName}: {offer.reward}
                  {offer.available > 1 && <span className="muted"> ({offer.available} hazır)</span>}
                </span>
              </label>
            ))}
            {!rewardFits && <p className="ticket__hint">Bu ödül için siparişte bir {reward?.category} olmalı.</p>}
          </fieldset>
        )}

        <dl className="ticket__totals">
          {totals.discount > 0 && (
            <>
              <dt>Ara toplam</dt>
              <dd className="num">{formatMoney(totals.subtotal)}</dd>
              <dt>Ödül</dt>
              <dd className="num">−{formatMoney(totals.discount)}</dd>
            </>
          )}
          <dt className="ticket__total">Toplam</dt>
          <dd className="ticket__total num">{formatMoney(totals.total)}</dd>
        </dl>

        {place.error && (
          <p className="form-error" role="alert">
            {place.error.message}
          </p>
        )}
        {notice && (
          <p className="ticket__notice" role="status">
            {notice}
          </p>
        )}

        <button
          type="button"
          className="button ticket__submit"
          disabled={lines.length === 0 || !rewardFits || place.isPending}
          onClick={() => place.mutate()}
        >
          {place.isPending ? 'Kaydediliyor' : 'Siparişi kaydet'}
        </button>
      </aside>

      {picking && (
        <CustomerPicker
          onClose={() => setPicking(false)}
          onPick={(picked) => {
            setPicking(false)
            setCustomer(picked)
            setRewardCardId(undefined)
          }}
        />
      )}
    </div>
  )
}

function CancelDialog({ order, onClose, onCancelled }: { order: Order; onClose: () => void; onCancelled: () => void }) {
  const queryClient = useQueryClient()
  const cancel = useMutation({
    mutationFn: () => send('POST', `/orders/${order.id}/cancel`),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['loyalty'] })
      onCancelled()
    },
  })

  return (
    <Dialog title="Siparişi iptal et" onClose={onClose}>
      <p>
        {formatTime(order.placedAt)} siparişi ({formatMoney(order.total)}) iptal edilecek.
        {order.customerName && ' Müşterinin bu siparişten kazandığı damgalar geri alınır, kullandığı ödül kartına iade edilir.'}
      </p>
      {cancel.error && (
        <p className="form-error" role="alert">
          {cancel.error.message}
        </p>
      )}
      <div className="form-actions">
        <button type="button" className="button button--quiet" onClick={onClose}>
          Vazgeç
        </button>
        <button type="button" className="button button--danger" disabled={cancel.isPending} onClick={() => cancel.mutate()}>
          Siparişi iptal et
        </button>
      </div>
    </Dialog>
  )
}

interface MenuDialogsProps {
  action: MenuAction
  menu: MenuItem[]
  onAction: (action: MenuAction) => void
  onSaved: () => void
  onClose: () => void
}

function MenuDialogs({ action, menu, onAction, onSaved, onClose }: MenuDialogsProps) {
  if (action.kind === 'menu') {
    return (
      <Dialog title="Menü" onClose={onClose}>
        <ul className="menu-list">
          {menu.map((item) => (
            <li key={item.id}>
              <span>
                <strong>{item.name}</strong>
                <small className="muted">
                  {item.category}
                  {item.isAvailable ? '' : ', satışta değil'}
                </small>
              </span>
              <span className="num">{formatMoney(item.price)}</span>
              <button type="button" className="button button--quiet button--small" onClick={() => onAction({ kind: 'edit', item })}>
                Düzenle
              </button>
            </li>
          ))}
        </ul>
        <div className="form-actions">
          <button type="button" className="button" onClick={() => onAction({ kind: 'add' })}>
            Menüye ürün ekle
          </button>
        </div>
      </Dialog>
    )
  }

  const item = action.kind === 'edit' ? action.item : undefined
  const categories = [...new Set(menu.map((entry) => entry.category))]

  return (
    <FormDialog
      title={item ? `${item.name}: düzenle` : 'Menüye ürün ekle'}
      submitLabel="Kaydet"
      onSubmit={(fields) => {
        const body = {
          name: text(fields, 'name'),
          category: text(fields, 'category'),
          price: num(fields, 'price'),
          isAvailable: item ? fields.get('isAvailable') === 'on' : true,
        }
        return item ? send('PUT', `/orders/menu/${item.id}`, body) : send('POST', '/orders/menu', body)
      }}
      onSaved={onSaved}
      onClose={onClose}
    >
      <label className="field">
        Ürün adı
        <input name="name" required autoFocus defaultValue={item?.name} placeholder="Latte" />
      </label>
      <div className="field-row">
        <label className="field">
          Kategori <small>Sadakat programı kategoriye göre sayar</small>
          <input name="category" required list="menu-categories" defaultValue={item?.category} placeholder="Kahve" />
          <datalist id="menu-categories">
            {categories.map((category) => (
              <option key={category} value={category} />
            ))}
          </datalist>
        </label>
        <label className="field">
          Fiyat (₺)
          <input name="price" inputMode="decimal" required defaultValue={item?.price} />
        </label>
      </div>
      {item && (
        <label className="check">
          <input type="checkbox" name="isAvailable" defaultChecked={item.isAvailable} />
          <span>Satışta. Kapatırsanız kasada görünmez; geçmiş siparişler etkilenmez.</span>
        </label>
      )}
    </FormDialog>
  )
}

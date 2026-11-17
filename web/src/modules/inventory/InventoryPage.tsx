import { type CSSProperties, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { Product, StockMovement } from '../../api/types'
import { Dialog, FormDialog, num, text } from '../../components/Dialog'
import { formatDateTime, formatNumber } from '../../lib/format'
import { UNITS, isCounted, parseQuantity } from '../../lib/units'
import { useCan } from '../../lib/access'
import './InventoryPage.css'

type Action = { kind: 'add' } | { kind: 'in' | 'out' | 'history'; product: Product }

export function InventoryPage() {
  const canManageProducts = useCan('inventory.products')
  const canMove = useCan('inventory.move')
  const queryClient = useQueryClient()
  const [action, setAction] = useState<Action>()
  const { data: products } = useQuery({ queryKey: ['inventory', 'products'], queryFn: () => api<Product[]>('/inventory/products') })

  const saved = () => {
    setAction(undefined)
    void queryClient.invalidateQueries({ queryKey: ['inventory'] })
  }

  const low = products?.filter((product) => product.runningLow) ?? []

  return (
    <div style={{ '--accent': 'var(--inventory)' } as CSSProperties}>
      <header className="page-header">
        <div>
          <h1>Stok</h1>
          <p>
            {low.length === 0
              ? 'Tüm ürünler yeterli seviyede.'
              : `${low.length} ürün azaldı: ${low.map((product) => product.name).join(', ')}.`}
          </p>
        </div>
        {canManageProducts && (
          <button type="button" className="button" onClick={() => setAction({ kind: 'add' })}>
            Ürün ekle
          </button>
        )}
      </header>

      <div className="panel table-scroll">
        {products?.length === 0 ? (
          <p className="empty">
            <strong>Henüz ürün yok</strong>
            Takip etmek istediğiniz ilk ürünü ekleyin.
          </p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th scope="col">Ürün</th>
                <th scope="col" className="right">
                  Stok
                </th>
                <th scope="col">Seviye</th>
                <th scope="col" />
              </tr>
            </thead>
            <tbody>
              {products?.map((product) => (
                <tr key={product.id}>
                  <td>
                    <strong>{product.name}</strong>
                  </td>
                  <td className="right num">
                    {formatNumber(product.stock)} {product.unit}
                  </td>
                  <td>
                    <StockLevel product={product} />
                  </td>
                  <td className="right">
                    <div className="row-actions">
                      {canMove && (
                        <>
                          <button type="button" className="button button--quiet button--small" onClick={() => setAction({ kind: 'in', product })}>
                            Giriş
                          </button>
                          <button type="button" className="button button--quiet button--small" onClick={() => setAction({ kind: 'out', product })}>
                            Çıkış
                          </button>
                        </>
                      )}
                      <button type="button" className="button button--quiet button--small" onClick={() => setAction({ kind: 'history', product })}>
                        Hareketler
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {action?.kind === 'add' && <AddProductDialog onSaved={saved} onClose={() => setAction(undefined)} />}

      {(action?.kind === 'in' || action?.kind === 'out') && (
        <MovementDialog kind={action.kind} product={action.product} onSaved={saved} onClose={() => setAction(undefined)} />
      )}

      {action?.kind === 'history' && <History product={action.product} onClose={() => setAction(undefined)} />}
    </div>
  )
}

/**
 * Two things that are easy to mix up are kept apart here: what the product is (its name and the
 * unit it is counted in) and how much of it is on the shelf right now. The latter is optional and
 * is recorded as the product's first stock entry, so it shows up in its history.
 */
function AddProductDialog({ onSaved, onClose }: { onSaved: () => void; onClose: () => void }) {
  const [unit, setUnit] = useState('adet')
  const quantityMode = isCounted(unit) ? 'numeric' : 'decimal'

  return (
    <FormDialog
      title="Ürün ekle"
      submitLabel="Ürünü kaydet"
      onSubmit={(fields) =>
        send('POST', '/inventory/products', {
          name: text(fields, 'name'),
          unit,
          reorderLevel: num(fields, 'reorderLevel'),
          openingStock: num(fields, 'openingStock'),
        })
      }
      onSaved={onSaved}
      onClose={onClose}
    >
      <fieldset className="form-section">
        <legend>Ürün</legend>
        <div className="field-row">
          <label className="field">
            Ürün adı
            <input name="name" required autoFocus placeholder="Süt" />
          </label>
          <label className="field">
            Ölçü birimi <small>Ürün neyle sayılıyor?</small>
            <select name="unit" value={unit} onChange={(event) => setUnit(event.target.value)}>
              {UNITS.map((entry) => (
                <option key={entry.unit} value={entry.unit}>
                  {entry.label}
                </option>
              ))}
            </select>
          </label>
        </div>
      </fieldset>

      <fieldset className="form-section">
        <legend>Miktarlar ({unit} cinsinden)</legend>
        <div className="field-row">
          <label className="field">
            Şu an rafta olan <small>Boş bırakırsanız stok 0 başlar. Hareket geçmişine “Açılış stoğu” olarak yazılır.</small>
            <span className="unit-input">
              <input name="openingStock" inputMode={quantityMode} placeholder="0" />
              <span>{unit}</span>
            </span>
          </label>
          <label className="field">
            Uyarı seviyesi <small>Stok bu miktara inince “Azaldı” olarak işaretlenir.</small>
            <span className="unit-input">
              <input name="reorderLevel" inputMode={quantityMode} placeholder="0" />
              <span>{unit}</span>
            </span>
          </label>
        </div>
        {isCounted(unit) && <p className="muted form-note">{unit} ile sayılan ürünlerde miktarlar tam sayı olur.</p>}
      </fieldset>
    </FormDialog>
  )
}

interface MovementDialogProps {
  kind: 'in' | 'out'
  product: Product
  onSaved: () => void
  onClose: () => void
}

/** A delivery or a use: one quantity, shown against what the stock will be afterwards. */
function MovementDialog({ kind, product, onSaved, onClose }: MovementDialogProps) {
  const [typed, setTyped] = useState('')
  const quantity = parseQuantity(typed)
  const after = quantity === null ? null : product.stock + (kind === 'in' ? quantity : -quantity)

  return (
    <FormDialog
      title={`${product.name}: ${kind === 'in' ? 'stok girişi' : 'stok çıkışı'}`}
      submitLabel={kind === 'in' ? 'Girişi kaydet' : 'Çıkışı kaydet'}
      onSubmit={(fields) =>
        send('POST', `/inventory/products/${product.id}/movements`, {
          kind,
          quantity: num(fields, 'quantity'),
          note: text(fields, 'note'),
        })
      }
      onSaved={onSaved}
      onClose={onClose}
    >
      <dl className="stock-change">
        <dt>Şu anki stok</dt>
        <dd className="num">
          {formatNumber(product.stock)} {product.unit}
        </dd>
        <dt>Kayıttan sonra</dt>
        <dd className={after !== null && after < 0 ? 'num stock-change--short' : 'num'}>
          {after === null ? '–' : after < 0 ? 'Stokta bu kadar yok' : `${formatNumber(after)} ${product.unit}`}
        </dd>
      </dl>
      <label className="field">
        {kind === 'in' ? 'Gelen miktar' : 'Çıkan miktar'}
        <span className="unit-input">
          <input
            name="quantity"
            inputMode={isCounted(product.unit) ? 'numeric' : 'decimal'}
            required
            autoFocus
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
          />
          <span>{product.unit}</span>
        </span>
      </label>
      <label className="field">
        Not <small>İsteğe bağlı</small>
        <input name="note" placeholder={kind === 'in' ? 'Tedarikçi, fatura no...' : 'Kullanım, fire...'} />
      </label>
    </FormDialog>
  )
}

/** A bar that fills to twice the reorder level, so "running low" is the left half. */
function StockLevel({ product }: { product: Product }) {
  const full = Math.max(product.reorderLevel * 2, 1)
  const percent = Math.min((product.stock / full) * 100, 100)
  return (
    <div className="level">
      <div className="level__track" aria-hidden="true">
        <div className={product.runningLow ? 'level__fill level__fill--low' : 'level__fill'} style={{ width: `${percent}%` }} />
      </div>
      {product.runningLow && <span className="tag tag--warn">Azaldı</span>}
    </div>
  )
}

function History({ product, onClose }: { product: Product; onClose: () => void }) {
  const { data: movements } = useQuery({
    queryKey: ['inventory', 'movements', product.id],
    queryFn: () => api<StockMovement[]>(`/inventory/products/${product.id}/movements`),
  })

  return (
    <Dialog title={`${product.name}: hareketler`} onClose={onClose}>
      {movements?.length === 0 ? (
        <p className="muted">Bu ürün için henüz giriş veya çıkış yapılmadı.</p>
      ) : (
        <ul className="movements">
          {movements?.map((movement, index) => (
            <li key={index}>
              <strong className={movement.kind === 'in' ? 'movement--in num' : 'movement--out num'}>
                {movement.kind === 'in' ? '+' : '-'}
                {formatNumber(movement.quantity)} {product.unit}
              </strong>
              <span>
                {movement.note ?? (movement.kind === 'in' ? 'Giriş' : 'Çıkış')}
                <small className="muted">
                  {formatDateTime(movement.createdAt)}, {movement.createdBy}
                </small>
              </span>
            </li>
          ))}
        </ul>
      )}
    </Dialog>
  )
}

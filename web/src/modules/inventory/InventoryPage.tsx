import { type CSSProperties, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../../api/client'
import type { Product, StockMovement } from '../../api/types'
import { Dialog, FormDialog, num, text } from '../../components/Dialog'
import { formatDateTime, formatNumber } from '../../lib/format'
import './InventoryPage.css'

type Action = { kind: 'add' } | { kind: 'in' | 'out' | 'history'; product: Product }

export function InventoryPage() {
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
        <button type="button" className="button" onClick={() => setAction({ kind: 'add' })}>
          Ürün ekle
        </button>
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
                      <button type="button" className="button button--quiet button--small" onClick={() => setAction({ kind: 'in', product })}>
                        Giriş
                      </button>
                      <button type="button" className="button button--quiet button--small" onClick={() => setAction({ kind: 'out', product })}>
                        Çıkış
                      </button>
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

      {action?.kind === 'add' && (
        <FormDialog
          title="Ürün ekle"
          submitLabel="Ürünü kaydet"
          onSubmit={(fields) => send('POST', '/inventory/products', {
            name: text(fields, 'name'), unit: text(fields, 'unit'), reorderLevel: num(fields, 'reorderLevel'),
            initialStock: num(fields, 'initialStock'),
          })}
          onSaved={saved}
          onClose={() => setAction(undefined)}
        >
          <div className="field-row">
            <label className="field">
              Ürün adı
              <input name="name" required />
            </label>
            <label className="field">
              Birim
              <input name="unit" required placeholder="adet, kg, lt" defaultValue="adet" />
            </label>
          </div>
          <div className="field-row">
            <label className="field">
              Mevcut stok
              <input name="initialStock" inputMode="decimal" required defaultValue={0} />
            </label>
            <label className="field">
              Uyarı seviyesi <small>Bu miktara düşünce azaldı sayılır</small>
              <input name="reorderLevel" inputMode="decimal" required defaultValue={0} />
            </label>
          </div>
        </FormDialog>
      )}

      {(action?.kind === 'in' || action?.kind === 'out') && (
        <FormDialog
          title={`${action.product.name}: ${action.kind === 'in' ? 'stok girişi' : 'stok çıkışı'}`}
          submitLabel={action.kind === 'in' ? 'Girişi kaydet' : 'Çıkışı kaydet'}
          onSubmit={(fields) => send('POST', `/inventory/products/${action.product.id}/movements`, {
            kind: action.kind, quantity: num(fields, 'quantity'), note: text(fields, 'note'),
          })}
          onSaved={saved}
          onClose={() => setAction(undefined)}
        >
          <p className="muted">
            Şu an stokta {formatNumber(action.product.stock)} {action.product.unit} var.
          </p>
          <label className="field">
            Miktar ({action.product.unit})
            <input name="quantity" inputMode="decimal" required autoFocus />
          </label>
          <label className="field">
            Not <small>İsteğe bağlı</small>
            <input name="note" placeholder={action.kind === 'in' ? 'Tedarikçi, fatura no...' : 'Kullanım, fire...'} />
          </label>
        </FormDialog>
      )}

      {action?.kind === 'history' && <History product={action.product} onClose={() => setAction(undefined)} />}
    </div>
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

import type { LoyaltyCard } from '../../api/types'
import { formatPhone } from '../../lib/format'
import './StampCard.css'

interface StampCardProps {
  card: LoyaltyCard
  reward: string
  /** Hand the reward over here; only for programs whose rewards are not spent on an order. */
  onRedeem?: () => void
  busy: boolean
}

/**
 * A customer's card as it would look on paper: one circle per purchase, inked as they add up.
 * It is read-only: stamps come from orders and appointments, never from a button.
 */
export function StampCard({ card, reward, onRedeem, busy }: StampCardProps) {
  const slots = Array.from({ length: card.stampsRequired }, (_, index) => index < card.stamps)
  const remaining = card.stampsRequired - card.stamps

  return (
    <article className={card.rewardsAvailable > 0 ? 'stamp-card stamp-card--reward' : 'stamp-card'}>
      <header>
        <h3>{card.customerName}</h3>
        <span className="muted num">{formatPhone(card.customerPhone)}</span>
      </header>

      <ol className="stamp-card__slots" aria-label={`${card.stampsRequired} damgadan ${card.stamps} tanesi dolu`}>
        {slots.map((filled, index) => (
          <li key={index} className={filled ? 'stamp stamp--inked' : 'stamp'} aria-hidden="true">
            {filled ? '✓' : index + 1}
          </li>
        ))}
      </ol>

      <p className="stamp-card__status">
        {card.rewardsAvailable > 0 ? (
          <strong>
            {card.rewardsAvailable > 1 ? `${card.rewardsAvailable} ödül bekliyor: ` : 'Ödül hazır: '}
            {reward}
          </strong>
        ) : (
          <span className="muted">Ödüle {remaining} damga kaldı</span>
        )}
      </p>

      {card.rewardsAvailable > 0 &&
        (onRedeem ? (
          <div className="stamp-card__actions">
            <button type="button" className="button button--quiet button--small" disabled={busy} onClick={onRedeem}>
              Ödülü teslim et
            </button>
          </div>
        ) : (
          <p className="muted stamp-card__hint">Kasada bu müşteriye sipariş girilirken kullanılır.</p>
        ))}
    </article>
  )
}

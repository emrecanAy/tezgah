import type { LoyaltyCard } from '../../api/types'
import { formatPhone } from '../../lib/format'
import './StampCard.css'

interface StampCardProps {
  card: LoyaltyCard
  reward: string
  busy: boolean
  onStamp: () => void
  onRedeem: () => void
}

/** A customer's card as it would look on paper: one circle per purchase, inked as they add up. */
export function StampCard({ card, reward, busy, onStamp, onRedeem }: StampCardProps) {
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

      <div className="stamp-card__actions">
        <button type="button" className="button button--small" disabled={busy} onClick={onStamp}>
          Damga bas
        </button>
        {card.rewardsAvailable > 0 && (
          <button type="button" className="button button--quiet button--small" disabled={busy} onClick={onRedeem}>
            Ödülü kullan
          </button>
        )}
      </div>
    </article>
  )
}

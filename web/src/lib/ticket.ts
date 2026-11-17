import type { MenuItem } from '../api/types'

export interface TicketLine {
  item: MenuItem
  quantity: number
}

export interface TicketTotals {
  subtotal: number
  discount: number
  total: number
  /** The menu item the reward makes free, or null when no reward applies. */
  freeItemId: string | null
}

/**
 * What the till shows while an order is being rung up. The API works the amounts out again from
 * its own menu and that answer is the one recorded; this only has to agree with it, which is why it
 * follows the same rule: the cheapest item of the reward's category goes free.
 */
export function ticketTotals(lines: TicketLine[], reward: { category: string | null } | null): TicketTotals {
  const subtotal = round(lines.reduce((sum, line) => sum + line.item.price * line.quantity, 0))
  const qualifying = reward
    ? lines.filter((line) => reward.category === null || sameCategory(line.item.category, reward.category))
    : []
  const free = qualifying.reduce<TicketLine | null>(
    (cheapest, line) => (cheapest === null || line.item.price < cheapest.item.price ? line : cheapest),
    null,
  )
  const discount = free?.item.price ?? 0
  return { subtotal, discount, total: round(subtotal - discount), freeItemId: free?.item.id ?? null }
}

/** Adds one of an item, or removes one when `change` is -1; a line that reaches zero disappears. */
export function changeQuantity(lines: TicketLine[], item: MenuItem, change: 1 | -1): TicketLine[] {
  const existing = lines.find((line) => line.item.id === item.id)
  if (!existing) return change === 1 ? [...lines, { item, quantity: 1 }] : lines
  return lines
    .map((line) => (line === existing ? { ...line, quantity: Math.min(line.quantity + change, 99) } : line))
    .filter((line) => line.quantity > 0)
}

/** Groups the menu for the till, categories and items in alphabetical order. */
export function byCategory(menu: MenuItem[]): [string, MenuItem[]][] {
  const groups = new Map<string, MenuItem[]>()
  for (const item of [...menu].sort((a, b) => a.name.localeCompare(b.name, 'tr'))) {
    groups.set(item.category, [...(groups.get(item.category) ?? []), item])
  }
  return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b, 'tr'))
}

// The API compares categories ignoring case, character by character; upper-casing both sides matches it.
const sameCategory = (a: string, b: string) => a.toUpperCase() === b.toUpperCase()

const round = (amount: number) => Math.round(amount * 100) / 100

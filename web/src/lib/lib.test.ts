import { describe, expect, it } from 'vitest'
import { describeDaysLeft, formatDate, formatPhone, toDateInput } from './format'
import type { CatalogModule } from '../api/types'
import { layoutDay } from './agenda'
import { toggleModule } from './modules'
import { byCategory, changeQuantity, ticketTotals } from './ticket'
import { isCounted, parseQuantity } from './units'
import { mayCancelOrder } from './access'
import { describeDevice } from './devices'

describe('format', () => {
  it('groups a Turkish mobile number however many leading zeros it was stored with', () => {
    expect(formatPhone('05324102231')).toBe('0532 410 22 31')
    expect(formatPhone('5324102231')).toBe('0532 410 22 31')
    expect(formatPhone('123')).toBe('123')
  })

  it('keeps calendar dates on their own day in every time zone', () => {
    expect(formatDate('2026-11-16')).toBe('16 Kasım 2026')
  })

  it('writes dates the way a date input wants them', () => {
    expect(toDateInput(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05')
  })

  it('says how far away a due date is', () => {
    expect(describeDaysLeft(-6)).toBe('6 gün gecikti')
    expect(describeDaysLeft(0)).toBe('Bugün')
    expect(describeDaysLeft(12)).toBe('12 gün kaldı')
  })
})

describe('agenda layout', () => {
  const at = (hour: number, minute = 0) => new Date(2026, 10, 16, hour, minute).toISOString()

  it('places an appointment by its start and length within the visible hours', () => {
    const [block] = layoutDay([{ startsAt: at(10, 30), endsAt: at(11, 15) }], 9, 20)

    expect(block).toMatchObject({ topPercent: (90 / 660) * 100, heightPercent: (45 / 660) * 100 })
  })

  it('clips appointments that start before opening or run past closing', () => {
    const [early, late] = layoutDay(
      [
        { startsAt: at(8), endsAt: at(10) },
        { startsAt: at(19, 30), endsAt: at(21) },
      ],
      9,
      20,
    )

    expect(early).toMatchObject({ topPercent: 0, heightPercent: (60 / 660) * 100 })
    expect(late!.topPercent + late!.heightPercent).toBeCloseTo(100)
  })
})

describe('module selection', () => {
  const catalog: CatalogModule[] = [
    { key: 'customers', name: 'Müşteriler', description: '', dependsOn: [] },
    { key: 'appointments', name: 'Randevu', description: '', dependsOn: ['customers'] },
    { key: 'fleet', name: 'Filo', description: '', dependsOn: [] },
    { key: 'loyalty', name: 'Sadakat', description: '', dependsOn: ['customers'] },
  ]

  it('switching a module on brings what it depends on', () => {
    expect(toggleModule(catalog, ['fleet'], 'loyalty', true)).toEqual(['customers', 'fleet', 'loyalty'])
  })

  it('switching a module off takes what depends on it', () => {
    expect(toggleModule(catalog, ['customers', 'appointments', 'fleet', 'loyalty'], 'customers', false)).toEqual(['fleet'])
  })

  it('leaves independent modules alone', () => {
    expect(toggleModule(catalog, ['customers', 'loyalty'], 'fleet', true)).toEqual(['customers', 'fleet', 'loyalty'])
  })
})

describe('till', () => {
  const item = (id: string, category: string, price: number) => ({ id, name: id, category, price, isAvailable: true })
  const latte = item('Latte', 'Kahve', 130)
  const turkish = item('Türk Kahvesi', 'Kahve', 90)
  const brownie = item('Brownie', 'Tatlı', 140)

  it('adds up the ticket and makes the cheapest item of the reward category free', () => {
    const lines = [{ item: latte, quantity: 2 }, { item: turkish, quantity: 1 }, { item: brownie, quantity: 1 }]

    expect(ticketTotals(lines, null)).toEqual({ subtotal: 490, discount: 0, total: 490, freeItemId: null })
    expect(ticketTotals(lines, { category: 'kahve' })).toEqual({ subtotal: 490, discount: 90, total: 400, freeItemId: 'Türk Kahvesi' })
    expect(ticketTotals(lines, { category: null }).freeItemId).toBe('Türk Kahvesi')
  })

  it('gives nothing away when the order has no item of the reward category', () => {
    expect(ticketTotals([{ item: brownie, quantity: 1 }], { category: 'Kahve' }).discount).toBe(0)
  })

  it('adds and removes one at a time and drops a line at zero', () => {
    let lines = changeQuantity([], latte, 1)
    lines = changeQuantity(lines, latte, 1)
    expect(lines).toEqual([{ item: latte, quantity: 2 }])
    expect(changeQuantity(changeQuantity(lines, latte, -1), latte, -1)).toEqual([])
  })

  it('groups the menu by category in Turkish alphabetical order', () => {
    expect(byCategory([brownie, turkish, latte, item('Çay', 'Çay', 30)]).map(([category]) => category)).toEqual(['Çay', 'Kahve', 'Tatlı'])
  })
})

describe('stock quantities', () => {
  it('reads a comma as the decimal separator and an empty box as nothing', () => {
    expect(parseQuantity('2,5')).toBe(2.5)
    expect(parseQuantity(' 12 ')).toBe(12)
    expect(parseQuantity('')).toBeNull()
    expect(parseQuantity('iki')).toBeNull()
  })

  it('knows which units are counted in whole numbers', () => {
    expect(isCounted('adet')).toBe(true)
    expect(isCounted('kg')).toBe(false)
  })
})

describe('access', () => {
  const me = (permissions: string[]) => ({
    user: { id: 'u1', fullName: 'Deniz', email: 'd@x', role: 'staff' as const, twoStepEnabled: false, mustChangePassword: false },
    company: null,
    modules: [],
    permissions,
  })
  const placedAt = new Date('2026-11-18T10:00:00Z')
  const at = (minutes: number) => placedAt.getTime() + minutes * 60_000

  it('lets staff cancel their own order for 15 minutes and a manager any order', () => {
    const own = { placedById: 'u1', placedAt: placedAt.toISOString() }
    const colleagues = { placedById: 'u2', placedAt: placedAt.toISOString() }

    expect(mayCancelOrder(me(['orders.sell']), own, at(15))).toBe(true)
    expect(mayCancelOrder(me(['orders.sell']), own, at(16))).toBe(false)
    expect(mayCancelOrder(me(['orders.sell']), colleagues, at(1))).toBe(false)
    expect(mayCancelOrder(me(['orders.cancel']), colleagues, at(600))).toBe(true)
  })

  it('names the browser and the system of a session', () => {
    const chromeOnMac = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0 Safari/537.36'
    expect(describeDevice(chromeOnMac)).toBe('Chrome, Mac')
    expect(describeDevice(null)).toBe('Bilinmeyen cihaz')
  })
})

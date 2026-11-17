import { describe, expect, it } from 'vitest'
import { describeDaysLeft, formatDate, formatPhone, toDateInput } from './format'
import type { CatalogModule } from '../api/types'
import { layoutDay } from './agenda'
import { toggleModule } from './modules'

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

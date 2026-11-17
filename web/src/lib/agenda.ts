export interface AgendaBlock {
  topPercent: number
  heightPercent: number
}

/**
 * Where each appointment sits in a day column that shows `openHour` to `closeHour`, as
 * percentages of the column height. Anything outside the visible hours is clipped to the edge.
 */
export function layoutDay(
  appointments: { startsAt: string; endsAt: string }[],
  openHour: number,
  closeHour: number,
): AgendaBlock[] {
  const visibleMinutes = (closeHour - openHour) * 60

  return appointments.map(({ startsAt, endsAt }) => {
    const start = clamp(minutesIntoDay(startsAt) - openHour * 60, 0, visibleMinutes)
    const end = clamp(minutesIntoDay(endsAt) - openHour * 60, 0, visibleMinutes)
    return { topPercent: (start / visibleMinutes) * 100, heightPercent: ((end - start) / visibleMinutes) * 100 }
  })
}

function minutesIntoDay(iso: string): number {
  const time = new Date(iso)
  return time.getHours() * 60 + time.getMinutes()
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

const money = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY', maximumFractionDigits: 2 })
const number = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 3 })
const date = new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'long', year: 'numeric' })
const longDay = new Intl.DateTimeFormat('tr-TR', { weekday: 'long', day: 'numeric', month: 'long' })
const clock = new Intl.DateTimeFormat('tr-TR', { hour: '2-digit', minute: '2-digit' })
const dateTime = new Intl.DateTimeFormat('tr-TR', { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' })

export const formatMoney = (amount: number) => money.format(amount).replace(/,00$/, '')
export const formatNumber = (value: number) => number.format(value)
export const formatTime = (iso: string) => clock.format(new Date(iso))
export const formatDateTime = (iso: string) => dateTime.format(new Date(iso))
export const formatLongDay = (day: Date) => longDay.format(day)

/** Formats a calendar date ("2026-11-16") without letting the time zone shift it by a day. */
export function formatDate(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  return date.format(new Date(year!, month! - 1, day))
}

/** "0532 410 22 31" from the digits the API stores. */
export function formatPhone(digits: string): string {
  const match = /^(0?)(\d{3})(\d{3})(\d{2})(\d{2})$/.exec(digits)
  return match ? `0${match[2]} ${match[3]} ${match[4]} ${match[5]}` : digits
}

/** The value an `<input type="date">` expects, in local time. */
export function toDateInput(day: Date): string {
  const pad = (value: number) => value.toString().padStart(2, '0')
  return `${day.getFullYear()}-${pad(day.getMonth() + 1)}-${pad(day.getDate())}`
}

export function startOfDay(day: Date): Date {
  return new Date(day.getFullYear(), day.getMonth(), day.getDate())
}

export function addDays(day: Date, days: number): Date {
  return new Date(day.getFullYear(), day.getMonth(), day.getDate() + days)
}

/** How a due date reads in a list: overdue, today, or in so many days. */
export function describeDaysLeft(daysLeft: number): string {
  if (daysLeft < 0) return `${-daysLeft} gün gecikti`
  if (daysLeft === 0) return 'Bugün'
  return `${daysLeft} gün kaldı`
}

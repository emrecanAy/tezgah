/** The units the API accepts. Counted units take whole numbers, measured ones decimals. */
export const UNITS: { unit: string; label: string; counted: boolean }[] = [
  { unit: 'adet', label: 'adet', counted: true },
  { unit: 'paket', label: 'paket', counted: true },
  { unit: 'koli', label: 'koli', counted: true },
  { unit: 'şişe', label: 'şişe', counted: true },
  { unit: 'kg', label: 'kilogram (kg)', counted: false },
  { unit: 'g', label: 'gram (g)', counted: false },
  { unit: 'lt', label: 'litre (lt)', counted: false },
  { unit: 'ml', label: 'mililitre (ml)', counted: false },
]

export const isCounted = (unit: string) => UNITS.find((entry) => entry.unit === unit)?.counted ?? true

/** Reads what was typed into a quantity field; Turkish keyboards type decimals with a comma. Empty is null. */
export function parseQuantity(value: string): number | null {
  const trimmed = value.trim()
  if (trimmed === '') return null
  const parsed = Number(trimmed.replace(',', '.'))
  return Number.isFinite(parsed) ? parsed : null
}

/**
 * Calendar dates. The API sends and expects date-only strings ("2026-11-10"). They are calendar days, not moments,
 * so they are never turned into a local-time Date: that is how a booking ends up a day off in another time zone.
 * All arithmetic and formatting goes through UTC midnight of the same calendar day.
 */

const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})$/

function toUtcMs(iso: string): number | null {
  const match = ISO_DATE.exec(iso)
  if (!match) return null
  const [, y, m, d] = match
  const ms = Date.UTC(Number(y), Number(m) - 1, Number(d))
  const check = new Date(ms)
  // Rejects dates that roll over, such as 2026-02-31.
  if (check.getUTCFullYear() !== Number(y) || check.getUTCMonth() !== Number(m) - 1 || check.getUTCDate() !== Number(d)) {
    return null
  }
  return ms
}

export function isIsoDate(value: string): boolean {
  return toUtcMs(value) !== null
}

function fromUtcMs(ms: number): string {
  const d = new Date(ms)
  const y = String(d.getUTCFullYear()).padStart(4, '0')
  const m = String(d.getUTCMonth() + 1).padStart(2, '0')
  const day = String(d.getUTCDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/** The visitor's current calendar date, in their own time zone. */
export function todayIso(now: Date = new Date()): string {
  const y = String(now.getFullYear()).padStart(4, '0')
  const m = String(now.getMonth() + 1).padStart(2, '0')
  const d = String(now.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export function addDays(iso: string, days: number): string {
  const ms = toUtcMs(iso)
  if (ms === null) throw new RangeError(`Not a date: ${iso}`)
  return fromUtcMs(ms + days * 86_400_000)
}

/** Whole days from start to end. The pickup day counts, the return day does not. */
export function daysBetween(startIso: string, endIso: string): number {
  const start = toUtcMs(startIso)
  const end = toUtcMs(endIso)
  if (start === null || end === null) throw new RangeError('Not a date')
  return Math.round((end - start) / 86_400_000)
}

/** A readable calendar day such as "9 Oct 2026" (day, month name, year: no US-style numeric order). */
export function formatDate(iso: string): string {
  const ms = toUtcMs(iso)
  if (ms === null) return iso
  return new Intl.DateTimeFormat('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
    timeZone: 'UTC',
  }).format(new Date(ms))
}

/** The day of the week for a calendar day, such as "Fri". */
export function formatWeekday(iso: string): string {
  const ms = toUtcMs(iso)
  if (ms === null) return ''
  return new Intl.DateTimeFormat('en-GB', { weekday: 'short', timeZone: 'UTC' }).format(new Date(ms))
}

export function formatDateTime(isoDateTime: string): string {
  const date = new Date(isoDateTime)
  if (Number.isNaN(date.getTime())) return isoDateTime
  return new Intl.DateTimeFormat('en-GB', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export interface RangeErrors {
  startDate?: string
  endDate?: string
}

/**
 * Quick feedback while the customer fills in the dates. These checks only catch obvious mistakes; the API applies
 * the real booking rules (maximum length, how far ahead, availability) and its messages are shown as they come.
 */
export function validateRange(startDate: string, endDate: string, today: string): RangeErrors {
  const errors: RangeErrors = {}

  if (!startDate) {
    errors.startDate = 'Choose a pickup date.'
  } else if (!isIsoDate(startDate)) {
    errors.startDate = 'Enter the pickup date as a valid date.'
  } else if (startDate < today) {
    errors.startDate = 'The pickup date cannot be in the past.'
  }

  if (!endDate) {
    errors.endDate = 'Choose a return date.'
  } else if (!isIsoDate(endDate)) {
    errors.endDate = 'Enter the return date as a valid date.'
  } else if (!errors.startDate && startDate && endDate <= startDate) {
    errors.endDate = 'The return date must be after the pickup date.'
  }

  return errors
}

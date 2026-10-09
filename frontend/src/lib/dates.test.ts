import { describe, expect, it } from 'vitest'
import { addDays, daysBetween, formatDate, isIsoDate, todayIso, validateRange } from './dates'

describe('calendar dates', () => {
  it('reads today from the local calendar, not from UTC', () => {
    // 23:30 on 9 Oct in the visitor's own time zone is still 9 Oct, whatever UTC says.
    expect(todayIso(new Date(2026, 9, 9, 23, 30))).toBe('2026-10-09')
    expect(todayIso(new Date(2026, 0, 1, 0, 5))).toBe('2026-01-01')
  })

  it('recognises only real dates', () => {
    expect(isIsoDate('2026-02-28')).toBe(true)
    expect(isIsoDate('2028-02-29')).toBe(true)
    expect(isIsoDate('2026-02-29')).toBe(false)
    expect(isIsoDate('2026-02-31')).toBe(false)
    expect(isIsoDate('2026-13-01')).toBe(false)
    expect(isIsoDate('10/11/2026')).toBe(false)
    expect(isIsoDate('')).toBe(false)
  })

  it('adds days across months, years and leap days without drifting', () => {
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01')
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01')
    expect(addDays('2028-02-28', 1)).toBe('2028-02-29')
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28')
    // Days where clocks change are still exactly one calendar day apart.
    expect(addDays('2026-03-28', 1)).toBe('2026-03-29')
    expect(addDays('2026-10-24', 2)).toBe('2026-10-26')
  })

  it('counts the days between pickup and return', () => {
    expect(daysBetween('2026-10-01', '2026-10-04')).toBe(3)
    expect(daysBetween('2026-10-31', '2026-11-02')).toBe(2)
    expect(daysBetween('2026-03-28', '2026-03-30')).toBe(2)
  })

  it('formats a date as the same calendar day it names', () => {
    expect(formatDate('2026-11-10')).toMatch(/10 Nov 2026/)
    expect(formatDate('2026-01-01')).toMatch(/1 Jan 2026/)
    expect(formatDate('not a date')).toBe('not a date')
  })

  it('throws for dates that do not exist instead of guessing', () => {
    expect(() => addDays('2026-02-31', 1)).toThrow(RangeError)
  })
})

describe('validateRange', () => {
  const today = '2026-10-09'

  it('accepts a normal period, and a pickup today', () => {
    expect(validateRange('2026-10-20', '2026-10-23', today)).toEqual({})
    expect(validateRange(today, '2026-10-10', today)).toEqual({})
  })

  it('asks for both dates', () => {
    expect(validateRange('', '', today)).toEqual({ startDate: 'Choose a pickup date.', endDate: 'Choose a return date.' })
  })

  it('rejects a pickup in the past', () => {
    expect(validateRange('2026-10-08', '2026-10-12', today).startDate).toMatch(/past/)
  })

  it('requires the return to be after the pickup (a one-day booking needs two different dates)', () => {
    expect(validateRange('2026-10-20', '2026-10-20', today).endDate).toMatch(/after the pickup/)
    expect(validateRange('2026-10-20', '2026-10-19', today).endDate).toMatch(/after the pickup/)
  })

  it('rejects text that is not a date', () => {
    expect(validateRange('soon', '2026-10-12', today).startDate).toMatch(/valid date/)
    expect(validateRange('2026-10-20', '2026-02-31', today).endDate).toMatch(/valid date/)
  })
})

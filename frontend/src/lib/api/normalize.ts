import type { components } from './schema'
import type { Page, Quote, Rental, Reservation, Vehicle } from './types'

type Schemas = components['schemas']

/** The API sends JSON numbers; this also guards against a stray numeric string. */
function num(value: number | string, field: string): number {
  const n = typeof value === 'number' ? value : Number(value)
  if (!Number.isFinite(n)) {
    throw new TypeError(`Unexpected non-numeric value for ${field}`)
  }
  return n
}

export function toVehicle(dto: Schemas['VehicleDto']): Vehicle {
  return { ...dto, year: num(dto.year, 'year'), dailyRate: num(dto.dailyRate, 'dailyRate') }
}

export function toQuote(dto: Schemas['QuoteDto']): Quote {
  return {
    ...dto,
    billableDays: num(dto.billableDays, 'billableDays'),
    dailyRate: num(dto.dailyRate, 'dailyRate'),
    totalCost: num(dto.totalCost, 'totalCost'),
  }
}

export function toReservation(dto: Schemas['ReservationDto']): Reservation {
  return {
    ...dto,
    billableDays: num(dto.billableDays, 'billableDays'),
    dailyRateAtReservation: num(dto.dailyRateAtReservation, 'dailyRateAtReservation'),
    totalCost: num(dto.totalCost, 'totalCost'),
  }
}

export function toRental(dto: Schemas['RentalDto']): Rental {
  return {
    ...dto,
    billableDays: num(dto.billableDays, 'billableDays'),
    dailyRateAtRental: num(dto.dailyRateAtRental, 'dailyRateAtRental'),
    totalCost: num(dto.totalCost, 'totalCost'),
  }
}

interface RawPage<T> {
  items: T[]
  page: number | string
  pageSize: number | string
  totalCount: number | string
  totalPages?: number | string
}

export function toPage<TRaw, T>(raw: RawPage<TRaw>, map: (item: TRaw) => T): Page<T> {
  const pageSize = num(raw.pageSize, 'pageSize')
  const totalCount = num(raw.totalCount, 'totalCount')
  return {
    items: raw.items.map(map),
    page: num(raw.page, 'page'),
    pageSize,
    totalCount,
    totalPages: raw.totalPages !== undefined ? num(raw.totalPages, 'totalPages') : Math.ceil(totalCount / pageSize),
  }
}

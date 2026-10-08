import type { components } from './schema'

type Schemas = components['schemas']

/** The numeric fields arrive as JSON numbers; the generated schema also allows strings, so they are normalised once. */
type Numeric<T, K extends keyof T> = Omit<T, K> & { [P in K]: number }

export type VehicleType = Schemas['VehicleType']
export type ReservationStatus = Schemas['ReservationStatus']
export type VehicleAvailabilityStatus = Schemas['VehicleAvailabilityStatus']
export type RentalStatus = Schemas['RentalStatus']

export type Vehicle = Numeric<Schemas['VehicleDto'], 'year' | 'dailyRate'>
export type Quote = Numeric<Schemas['QuoteDto'], 'billableDays' | 'dailyRate' | 'totalCost'>
export type Reservation = Numeric<
  Schemas['ReservationDto'],
  'billableDays' | 'dailyRateAtReservation' | 'totalCost'
>
export type Rental = Numeric<Schemas['RentalDto'], 'billableDays' | 'dailyRateAtRental' | 'totalCost'>
export type User = Schemas['UserDto']
export type Customer = Schemas['CustomerDto']

export interface Page<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface Session {
  expiresAtUtc: string
  user: User
}

export interface Credentials {
  email: string
  password: string
}

export interface Registration extends Credentials {
  name: string
}

export const VEHICLE_TYPES: readonly VehicleType[] = ['Car', 'Motorcycle', 'Van']

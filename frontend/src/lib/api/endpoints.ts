import type { components } from './schema'
import { request, requestNoContent } from './client'
import { toPage, toQuote, toRental, toReservation, toVehicle } from './normalize'
import type {
  Credentials,
  Customer,
  Page,
  Quote,
  Registration,
  Rental,
  Reservation,
  Session,
  User,
  Vehicle,
  VehicleType,
} from './types'

type Schemas = components['schemas']

export interface VehicleFilters {
  vehicleType?: VehicleType | ''
  maxDailyRate?: number | null
  page?: number
  pageSize?: number
}

export interface DateRange {
  startDate: string
  endDate: string
}

// ----- Vehicles (public) -----

export async function listVehicles(filters: VehicleFilters, signal?: AbortSignal): Promise<Page<Vehicle>> {
  const raw = await request<Schemas['PagedResultOfVehicleDto']>('GET', '/vehicles', {
    query: { ...filters },
    ...(signal && { signal }),
  })
  return toPage(raw, toVehicle)
}

export async function listAvailableVehicles(
  range: DateRange,
  filters: VehicleFilters,
  signal?: AbortSignal,
): Promise<Page<Vehicle>> {
  const raw = await request<Schemas['PagedResultOfVehicleDto']>('GET', '/vehicles/availability', {
    query: { ...range, ...filters },
    ...(signal && { signal }),
  })
  return toPage(raw, toVehicle)
}

export async function getVehicle(id: string, signal?: AbortSignal): Promise<Vehicle> {
  return toVehicle(await request<Schemas['VehicleDto']>('GET', `/vehicles/${id}`, signal ? { signal } : {}))
}

/** The price the backend would quote for these dates. Nothing is reserved. */
export async function getQuote(id: string, range: DateRange, signal?: AbortSignal): Promise<Quote> {
  const raw = await request<Schemas['QuoteDto']>('GET', `/vehicles/${id}/quote`, {
    query: { ...range },
    ...(signal && { signal }),
  })
  return toQuote(raw)
}

// ----- Session -----

export async function register(details: Registration): Promise<User> {
  return request<Schemas['UserDto']>('POST', '/auth/register', { body: details })
}

/** Signs in. The access token is set as an HttpOnly cookie by the server; it is not in the response. */
export async function startSession(credentials: Credentials): Promise<Session> {
  return request<Schemas['SessionResponse']>('POST', '/auth/session', { body: credentials })
}

export async function endSession(): Promise<void> {
  await requestNoContent('DELETE', '/auth/session')
}

export async function getMe(signal?: AbortSignal): Promise<User> {
  return request<Schemas['UserDto']>('GET', '/me', signal ? { signal } : {})
}

// ----- The signed-in customer's own data -----

export async function getMyCustomer(signal?: AbortSignal): Promise<Customer> {
  return request<Customer>('GET', '/me/customer', signal ? { signal } : {})
}

export async function listMyReservations(
  paging: { page?: number; pageSize?: number },
  signal?: AbortSignal,
): Promise<Page<Reservation>> {
  const raw = await request<Schemas['PagedResultOfReservationDto']>('GET', '/me/reservations', {
    query: { ...paging },
    ...(signal && { signal }),
  })
  return toPage(raw, toReservation)
}

export async function getMyReservation(id: string, signal?: AbortSignal): Promise<Reservation> {
  return toReservation(
    await request<Schemas['ReservationDto']>('GET', `/me/reservations/${id}`, signal ? { signal } : {}),
  )
}

/** Reserves a vehicle for the signed-in customer. There is deliberately no customer field: the server knows who is signed in. */
export async function createReservation(vehicleId: string, range: DateRange): Promise<Reservation> {
  return toReservation(
    await request<Schemas['ReservationDto']>('POST', '/me/reservations', {
      body: { vehicleId, startDate: range.startDate, endDate: range.endDate },
    }),
  )
}

export async function cancelReservation(id: string): Promise<Reservation> {
  return toReservation(await request<Schemas['ReservationDto']>('POST', `/me/reservations/${id}/cancel`))
}

export async function listMyRentals(
  paging: { page?: number; pageSize?: number },
  signal?: AbortSignal,
): Promise<Page<Rental>> {
  const raw = await request<Schemas['PagedResultOfRentalDto']>('GET', '/me/rentals', {
    query: { ...paging },
    ...(signal && { signal }),
  })
  return toPage(raw, toRental)
}

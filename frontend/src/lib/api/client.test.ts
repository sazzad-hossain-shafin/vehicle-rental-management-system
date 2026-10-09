import { describe, expect, it, vi } from 'vitest'
import { json, mockApi, noContent, problem } from '../../test/utils'
import { onUnauthorized, request, requestNoContent } from './client'
import { ApiError, errorFromResponse } from './errors'
import { toPage, toQuote, toReservation, toVehicle } from './normalize'

describe('api client', () => {
  it('sends same-origin requests with no Authorization header, and no CSRF header on reads', async () => {
    const api = mockApi({ 'GET /api/v1/vehicles': json({ items: [] }) })

    await request('GET', '/vehicles', { query: { page: 2, vehicleType: '', maxDailyRate: undefined, type: 'Car' } })

    const [call] = api.calls
    expect(call?.path).toBe('/api/v1/vehicles')
    expect(call?.query.get('page')).toBe('2')
    expect(call?.query.get('type')).toBe('Car')
    expect(call?.query.has('vehicleType')).toBe(false) // empty values are left out
    expect(call?.query.has('maxDailyRate')).toBe(false)
    expect(call?.credentials).toBe('same-origin')
    expect(call?.headers).not.toHaveProperty('Authorization')
    expect(call?.headers).not.toHaveProperty('X-Requested-With')
  })

  it('adds the anti-CSRF header, and a JSON body, on state-changing requests', async () => {
    const api = mockApi({ 'POST /api/v1/me/reservations': json({}, 201) })

    await request('POST', '/me/reservations', { body: { vehicleId: 'v', startDate: 'a', endDate: 'b' } })

    expect(api.calls[0]?.headers['X-Requested-With']).toBe('VehicleRentalWeb')
    expect(api.calls[0]?.headers['Content-Type']).toBe('application/json')
    expect(api.calls[0]?.body).toEqual({ vehicleId: 'v', startDate: 'a', endDate: 'b' })
  })

  it('handles an empty 204 response', async () => {
    mockApi({ 'DELETE /api/v1/auth/session': noContent() })

    await expect(requestNoContent('DELETE', '/auth/session')).resolves.toBeUndefined()
  })

  it('turns Problem Details into an ApiError with the title, detail, trace id and field errors', async () => {
    mockApi({
      'POST /api/v1/auth/register': problem(400, 'One or more validation errors occurred.', {
        errors: { email: ['Enter a valid email.'], password: ['Too short.', 'Needs a digit.'] },
      }),
    })

    const error = await request('POST', '/auth/register', { body: {} }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(400)
    expect(apiError.title).toBe('One or more validation errors occurred.')
    expect(apiError.traceId).toBe('trace-123')
    expect(apiError.fieldErrors.password).toEqual(['Too short.', 'Needs a digit.'])
  })

  it('classifies 401, 403, 404 and 409', () => {
    const make = (status: number) => new ApiError({ status, title: 't' })

    expect(make(401).isUnauthorized).toBe(true)
    expect(make(403).isForbidden).toBe(true)
    expect(make(404).isNotFound).toBe(true)
    expect(make(409).isConflict).toBe(true)
    expect(make(404).isConflict).toBe(false)
  })

  it('shows the server detail for client errors, but a generic message for server failures', () => {
    expect(new ApiError({ status: 409, title: 'Conflict', detail: 'Vehicle is taken.' }).userMessage).toBe('Vehicle is taken.')
    const failure = new ApiError({ status: 500, title: 'x', detail: 'stack trace at Foo.Bar' })
    expect(failure.userMessage).not.toContain('stack trace')
    expect(failure.userMessage).toMatch(/our side/)
  })

  it('copes with an error body that is not JSON (for example a proxy page)', async () => {
    const response = new Response('<html>Bad gateway</html>', { status: 502, statusText: 'Bad Gateway' })

    const error = await errorFromResponse(response)

    expect(error.status).toBe(502)
    expect(error.title).toBe('Bad Gateway')
    expect(error.detail).toBeNull()
  })

  it('reports an unreachable server as a friendly network error', async () => {
    vi.stubGlobal('fetch', () => Promise.reject(new TypeError('Failed to fetch')))

    const error = (await request('GET', '/vehicles').catch((e: unknown) => e)) as ApiError

    expect(error.isNetworkError).toBe(true)
    expect(error.userMessage).toMatch(/could not reach the server/i)
  })

  it('lets a cancelled request stay a cancellation rather than an error message', async () => {
    vi.stubGlobal('fetch', () => Promise.reject(new DOMException('Aborted', 'AbortError')))

    const error = await request('GET', '/vehicles').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(DOMException)
  })

  it('tells listeners about a 401 so the session can end', async () => {
    mockApi({ 'GET /api/v1/me/reservations': problem(401, 'Unauthorized') })
    const listener = vi.fn()
    const stop = onUnauthorized(listener)

    await request('GET', '/me/reservations').catch(() => undefined)
    stop()
    await request('GET', '/me/reservations').catch(() => undefined)

    expect(listener).toHaveBeenCalledTimes(1)
  })
})

describe('response normalisation', () => {
  it('turns numeric strings into numbers and rejects nonsense', () => {
    const vehicle = toVehicle({
      id: 'v', registrationNumber: 'R', make: 'M', model: 'X', displayName: 'M X', year: '2022',
      vehicleType: 'Car', dailyRate: '60.5', availabilityStatus: 'Available',
    })
    expect(vehicle.year).toBe(2022)
    expect(vehicle.dailyRate).toBe(60.5)

    expect(() => toVehicle({ ...vehicle, dailyRate: 'free' })).toThrow(TypeError)
  })

  it('normalises quotes, reservations and pages', () => {
    expect(toQuote({ vehicleId: 'v', startDate: 'a', endDate: 'b', billableDays: '3', dailyRate: 60, pricingDescription: 'p', totalCost: '180', isAvailable: true }).totalCost).toBe(180)
    expect(toReservation({
      id: 'r', customerId: 'c', customerNumber: 'n', customerName: 'N', vehicleId: 'v', vehicleRegistrationNumber: 'R',
      vehicleDisplayName: 'D', vehicleType: 'Car', startDate: 'a', endDate: 'b', status: 'Active', createdAt: 't',
      cancelledAt: null, fulfilledAt: null, rentalId: null, dailyRateAtReservation: 60, billableDays: 3,
      pricingDescription: 'p', totalCost: 180, isExpired: false,
    }).billableDays).toBe(3)

    const result = toPage({ items: [1, 2], page: 1, pageSize: '2', totalCount: '5' }, (n: number) => n * 2)
    expect(result.items).toEqual([2, 4])
    expect(result.totalPages).toBe(3) // worked out when the server omits it
  })
})

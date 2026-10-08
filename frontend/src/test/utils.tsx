import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { vi } from 'vitest'
import { createQueryClient } from '../app/queryClient'
import { AuthProvider } from '../features/auth/AuthProvider'

export interface RecordedCall {
  method: string
  path: string
  query: URLSearchParams
  headers: Record<string, string>
  body: unknown
  credentials: RequestCredentials | undefined
}

export type Handler = (call: RecordedCall) => Response | Promise<Response>

/** A JSON response, the way the API sends one. */
export function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** A Problem Details response (RFC 9457), like the API's errors. */
export function problem(status: number, title: string, extra: Record<string, unknown> = {}): Response {
  return new Response(JSON.stringify({ status, title, traceId: 'trace-123', ...extra }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

export function noContent(): Response {
  return new Response(null, { status: 204 })
}

/**
 * Replaces fetch with a fake API. Routes are keyed "METHOD /api/v1/path" (no query string). A request nobody
 * planned for fails the test loudly instead of silently returning nothing.
 */
export function mockApi(routes: Record<string, Handler | Response>) {
  const calls: RecordedCall[] = []

  const fake = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const href = typeof input === 'string' ? input : input instanceof URL ? input.href : input.url
    const url = new URL(href, 'http://localhost')
    const headers = (init?.headers ?? {}) as Record<string, string>
    const call: RecordedCall = {
      method: init?.method ?? 'GET',
      path: url.pathname,
      query: url.searchParams,
      headers,
      body: typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined,
      credentials: init?.credentials,
    }
    calls.push(call)

    const route = routes[`${call.method} ${call.path}`]
    if (!route) {
      return Promise.reject(new Error(`Unplanned request: ${call.method} ${call.path}`))
    }

    return Promise.resolve(typeof route === 'function' ? route(call) : route.clone())
  })

  vi.stubGlobal('fetch', fake)

  return {
    calls,
    called: (method: string, path: string) => calls.filter((c) => c.method === method && c.path === path),
  }
}

export function renderWithProviders(
  ui: ReactElement,
  options: { route?: string; queryClient?: QueryClient } = {},
) {
  const queryClient = options.queryClient ?? createQueryClient()
  // No retries or delays in tests.
  queryClient.setDefaultOptions({ queries: { retry: false, staleTime: 30_000, refetchOnWindowFocus: false } })

  const result = render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[options.route ?? '/']}>
        <AuthProvider>{ui}</AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )

  return { ...result, queryClient }
}

// ---- Sample data in the shape the API returns ----

export interface TestUser {
  id: string
  email: string
  roles: string[]
  customerId: string | null
  customerNumber: string | null
}

export const customerUser: TestUser = {
  id: '11111111-1111-4111-8111-111111111111',
  email: 'casey@example.test',
  roles: ['Customer'],
  customerId: '22222222-2222-4222-8222-222222222222',
  customerNumber: 'WEB-1',
}

export const staffUser: TestUser = { ...customerUser, email: 'desk@example.test', roles: ['Staff'], customerId: null, customerNumber: null }

export function vehicle(overrides: Record<string, unknown> = {}) {
  return {
    id: '33333333-3333-4333-8333-333333333333',
    registrationNumber: 'ABC-123',
    make: 'Toyota',
    model: 'Corolla',
    displayName: 'Toyota Corolla',
    year: 2022,
    vehicleType: 'Car',
    dailyRate: 60,
    availabilityStatus: 'Available',
    ...overrides,
  }
}

export function page<T>(items: T[], overrides: Record<string, unknown> = {}) {
  return { items, page: 1, pageSize: 9, totalCount: items.length, totalPages: 1, ...overrides }
}

export function quote(overrides: Record<string, unknown> = {}) {
  return {
    vehicleId: '33333333-3333-4333-8333-333333333333',
    startDate: '2030-01-10',
    endDate: '2030-01-13',
    billableDays: 3,
    dailyRate: 60,
    pricingDescription: 'Normal pricing',
    totalCost: 180,
    isAvailable: true,
    ...overrides,
  }
}

export function reservation(overrides: Record<string, unknown> = {}) {
  return {
    id: '44444444-4444-4444-8444-444444444444',
    customerId: customerUser.customerId,
    customerNumber: 'WEB-1',
    customerName: 'Casey',
    vehicleId: '33333333-3333-4333-8333-333333333333',
    vehicleRegistrationNumber: 'ABC-123',
    vehicleDisplayName: 'Toyota Corolla',
    vehicleType: 'Car',
    startDate: '2030-01-10',
    endDate: '2030-01-13',
    status: 'Active',
    createdAt: '2029-12-01T10:00:00+00:00',
    cancelledAt: null,
    fulfilledAt: null,
    rentalId: null,
    dailyRateAtReservation: 60,
    billableDays: 3,
    pricingDescription: 'Normal pricing',
    totalCost: 180,
    isExpired: false,
    ...overrides,
  }
}

/** The /me call every signed-in test starts with. */
export const signedIn = (user: TestUser = customerUser) => ({ 'GET /api/v1/me': json(user) })
export const signedOut = { 'GET /api/v1/me': problem(401, 'Unauthorized') }

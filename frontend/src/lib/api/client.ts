import { ApiError, errorFromResponse, networkError } from './errors'

/**
 * The one place that talks HTTP. Everything is relative to this origin ("/api/v1/..."), so the browser sends the
 * HttpOnly session cookie by itself. No token is ever read, stored or logged by this code.
 */
const BASE_PATH = import.meta.env.VITE_API_BASE_PATH ?? '/api/v1'

/** The header the API requires on state-changing, cookie-authenticated requests (anti-CSRF). */
const CSRF_HEADER = { 'X-Requested-With': 'VehicleRentalWeb' } as const

type Query = Record<string, string | number | boolean | null | undefined>

export interface RequestOptions {
  query?: Query
  body?: unknown
  signal?: AbortSignal
}

type UnauthorizedListener = () => void
const unauthorizedListeners = new Set<UnauthorizedListener>()

/**
 * Lets the session layer hear that a request came back 401, so it can end the session and clear cached customer
 * data. Returns an unsubscribe function.
 */
export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener)
  return () => unauthorizedListeners.delete(listener)
}

function buildUrl(path: string, query?: Query): string {
  const url = `${BASE_PATH}${path}`
  if (!query) return url
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') {
      params.set(key, String(value))
    }
  }
  const text = params.toString()
  return text ? `${url}?${text}` : url
}

async function send(method: string, path: string, options: RequestOptions = {}): Promise<Response> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  const init: RequestInit = { method, headers, credentials: 'same-origin' }

  if (options.signal) init.signal = options.signal
  if (method !== 'GET' && method !== 'HEAD') Object.assign(headers, CSRF_HEADER)
  if (options.body !== undefined) {
    headers['Content-Type'] = 'application/json'
    init.body = JSON.stringify(options.body)
  }

  let response: Response
  try {
    response = await fetch(buildUrl(path, options.query), init)
  } catch (error) {
    // A cancelled request is not a failure: let the caller (TanStack Query) see the abort.
    if (error instanceof DOMException && error.name === 'AbortError') throw error
    throw networkError()
  }

  if (!response.ok) {
    const error = await errorFromResponse(response)
    if (error.isUnauthorized) {
      for (const listener of unauthorizedListeners) listener()
    }
    throw error
  }

  return response
}

/** Sends a request and returns the parsed JSON body. */
export async function request<T>(method: string, path: string, options?: RequestOptions): Promise<T> {
  const response = await send(method, path, options)
  return (await response.json()) as T
}

/** Sends a request whose successful response has no body (204). */
export async function requestNoContent(method: string, path: string, options?: RequestOptions): Promise<void> {
  await send(method, path, options)
}

export { ApiError }

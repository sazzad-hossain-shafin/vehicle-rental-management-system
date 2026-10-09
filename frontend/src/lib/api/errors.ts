/**
 * An error response from the API (RFC 9457 Problem Details) or a failure to reach it. Nothing sensitive is kept:
 * only what the server chose to tell a client.
 */
export class ApiError extends Error {
  readonly status: number
  readonly title: string
  readonly detail: string | null
  readonly traceId: string | null
  /** Field name (camelCase, as sent by the API) to its messages. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>

  constructor(init: {
    status: number
    title: string
    detail?: string | null
    traceId?: string | null
    fieldErrors?: Record<string, readonly string[]>
  }) {
    super(init.detail ?? init.title)
    this.name = 'ApiError'
    this.status = init.status
    this.title = init.title
    this.detail = init.detail ?? null
    this.traceId = init.traceId ?? null
    this.fieldErrors = init.fieldErrors ?? {}
  }

  get isNetworkError(): boolean {
    return this.status === 0
  }

  /** The server could not be reached, or a gateway in front of it could not (502, 503, 504). */
  get isUnavailable(): boolean {
    return this.status === 0 || this.status === 502 || this.status === 503 || this.status === 504
  }

  get isUnauthorized(): boolean {
    return this.status === 401
  }

  get isForbidden(): boolean {
    return this.status === 403
  }

  get isNotFound(): boolean {
    return this.status === 404
  }

  get isConflict(): boolean {
    return this.status === 409
  }

  /** A sentence that is safe and useful to show to a person. */
  get userMessage(): string {
    if (this.isUnavailable) {
      return 'We could not reach the server. Check your connection and try again.'
    }
    if (this.status >= 500) {
      return 'Something went wrong on our side. Please try again in a moment.'
    }
    return this.detail ?? this.title
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function readFieldErrors(value: unknown): Record<string, string[]> {
  if (!isRecord(value)) return {}
  const result: Record<string, string[]> = {}
  for (const [field, messages] of Object.entries(value)) {
    if (Array.isArray(messages)) {
      result[field] = messages.filter((m): m is string => typeof m === 'string')
    }
  }
  return result
}

/** Builds an ApiError from a failed HTTP response, whatever the body looks like. */
export async function errorFromResponse(response: Response): Promise<ApiError> {
  let body: unknown = null
  try {
    body = await response.json()
  } catch {
    // Not JSON (for example a proxy error page): fall back to the status.
  }

  if (isRecord(body)) {
    return new ApiError({
      status: response.status,
      title: typeof body.title === 'string' ? body.title : response.statusText || 'Request failed',
      detail: typeof body.detail === 'string' ? body.detail : null,
      traceId: typeof body.traceId === 'string' ? body.traceId : null,
      fieldErrors: readFieldErrors(body.errors),
    })
  }

  return new ApiError({ status: response.status, title: response.statusText || 'Request failed' })
}

export function networkError(): ApiError {
  return new ApiError({ status: 0, title: 'Network error' })
}

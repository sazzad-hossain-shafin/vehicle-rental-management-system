import { ApiError } from './api/errors'

/** The first server-side validation message for a field, if the error carries one. */
export function fieldError(error: unknown, field: string): string | undefined {
  if (!(error instanceof ApiError)) return undefined
  const wanted = field.toLowerCase()
  for (const [name, messages] of Object.entries(error.fieldErrors)) {
    if (name.toLowerCase() === wanted && messages[0]) return messages[0]
  }
  return undefined
}

/** True if the error is a validation error whose messages are all about fields the form shows. */
export function hasUnmappedError(error: unknown, shownFields: readonly string[]): boolean {
  if (!(error instanceof ApiError)) return true
  const names = Object.keys(error.fieldErrors)
  if (names.length === 0) return true
  const shown = shownFields.map((f) => f.toLowerCase())
  return names.some((n) => !shown.includes(n.toLowerCase()))
}

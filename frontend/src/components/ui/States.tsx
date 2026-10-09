import type { ReactNode } from 'react'
import { ApiError } from '../../lib/api/errors'
import { Alert } from './Alert'
import { Button } from './Button'

/** Placeholder shapes while data loads. Announced once to screen readers instead of every shape. */
export function LoadingGrid({ count = 6, label = 'Loading' }: { count?: number; label?: string }) {
  return (
    <div role="status" aria-live="polite">
      <span className="visually-hidden">{label}…</span>
      <ul className="vehicle-grid" aria-hidden="true">
        {Array.from({ length: count }, (_, index) => (
          <li key={index} className="card">
            <div className="skeleton skeleton-art" />
            <div className="vehicle-card-body">
              <div className="skeleton skeleton-line" />
              <div className="skeleton skeleton-line" />
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}

/** Placeholder rows while a list of reservations loads. */
export function LoadingList({ count = 3, label = 'Loading' }: { count?: number; label?: string }) {
  return (
    <div role="status" aria-live="polite">
      <span className="visually-hidden">{label}…</span>
      <ul className="reservation-list" aria-hidden="true">
        {Array.from({ length: count }, (_, index) => (
          <li key={index} className="card reservation-card">
            <div className="skeleton skeleton-icon" />
            <div className="reservation-main">
              <div className="skeleton skeleton-line" style={{ width: '45%' }} />
              <div className="skeleton skeleton-line" style={{ width: '70%' }} />
            </div>
          </li>
        ))}
      </ul>
    </div>
  )
}

export function LoadingBlock({ label = 'Loading' }: { label?: string }) {
  return (
    <div role="status" aria-live="polite" className="stack">
      <span className="visually-hidden">{label}…</span>
      <div aria-hidden="true">
        <div className="skeleton skeleton-line" />
        <div className="skeleton skeleton-line" />
        <div className="skeleton skeleton-line" style={{ width: '60%' }} />
      </div>
    </div>
  )
}

interface EmptyStateProps {
  icon?: ReactNode
  title: string
  children?: ReactNode
  action?: ReactNode
}

export function EmptyState({ icon, title, children, action }: EmptyStateProps) {
  return (
    <div className="empty-state">
      {icon && <span className="state-icon">{icon}</span>}
      <h2>{title}</h2>
      {children && <p className="muted">{children}</p>}
      {action}
    </div>
  )
}

/** An error from the API or the network, with a retry when it makes sense. */
export function ErrorState({ error, onRetry, title = 'Something went wrong' }: { error: unknown; onRetry?: () => void; title?: string }) {
  const message =
    error instanceof ApiError ? error.userMessage : 'An unexpected problem occurred. Please try again.'
  // The booking service itself could not be reached: say so plainly instead of implying the request was wrong.
  const unavailable = error instanceof ApiError && error.isUnavailable

  return (
    <Alert
      tone={unavailable ? 'warning' : 'error'}
      title={title}
      actions={
        onRetry && (
          <Button variant="secondary" onClick={onRetry}>
            Try again
          </Button>
        )
      }
    >
      <p>{message}</p>
      {unavailable && <p>The booking service may be starting up or briefly offline. Nothing you entered has been lost.</p>}
      {error instanceof ApiError && error.traceId && (
        <p className="hint">Reference: {error.traceId}</p>
      )}
    </Alert>
  )
}

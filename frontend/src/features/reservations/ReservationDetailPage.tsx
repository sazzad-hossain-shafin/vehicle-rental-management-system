import { useState } from 'react'
import { Link, useLocation, useParams } from 'react-router-dom'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { Dialog } from '../../components/ui/Dialog'
import { ErrorState, LoadingBlock } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { ApiError } from '../../lib/api/errors'
import type { Reservation } from '../../lib/api/types'
import { formatDate, formatDateTime, todayIso } from '../../lib/dates'
import { formatMoney, pluralize } from '../../lib/format'
import { useCancelReservation, useMyReservation } from './queries'
import { ReservationStatusBadge } from './ReservationStatusBadge'

interface LocationState {
  justBooked?: boolean
}

export function ReservationDetailPage() {
  const { id } = useParams()
  const location = useLocation()
  const justBooked = (location.state as LocationState | null)?.justBooked === true
  const reservation = useMyReservation(id)

  useDocumentTitle(justBooked ? 'Reservation confirmed' : 'Reservation')

  if (reservation.isPending) {
    return (
      <div className="container page">
        <LoadingBlock label="Loading the reservation" />
      </div>
    )
  }

  if (reservation.isError) {
    // Another customer's reservation looks exactly like one that does not exist, so both get the same message.
    const missing = reservation.error instanceof ApiError && reservation.error.isNotFound
    return (
      <div className="container page stack">
        {missing ? (
          <Alert tone="warning" title="We could not find that reservation">
            <p>Check the link, or look for it in your list of reservations.</p>
          </Alert>
        ) : (
          <ErrorState error={reservation.error} onRetry={() => void reservation.refetch()} title="We could not load the reservation" />
        )}
        <p>
          <Link to="/reservations">Back to my reservations</Link>
        </p>
      </div>
    )
  }

  return <ReservationView reservation={reservation.data} justBooked={justBooked} />
}

function ReservationView({ reservation, justBooked }: { reservation: Reservation; justBooked: boolean }) {
  const cancel = useCancelReservation()
  const [confirming, setConfirming] = useState(false)
  const [cancelled, setCancelled] = useState(false)

  // The server makes the final call; this only decides whether to offer the button.
  const canCancel = reservation.status === 'Active' && reservation.startDate > todayIso()
  const startedButActive = reservation.status === 'Active' && !canCancel

  function closeDialog() {
    cancel.reset()
    setConfirming(false)
  }

  function confirmCancel() {
    cancel.mutate(reservation.id, {
      onSuccess: () => {
        setConfirming(false)
        setCancelled(true)
      },
    })
  }

  const cancelError = cancel.error

  return (
    <div className="container page stack">
      <p>
        <Link to="/reservations">&larr; My reservations</Link>
      </p>

      {justBooked && (
        <Alert tone="success" title="Reservation confirmed">
          <p>
            Your {reservation.vehicleDisplayName} is reserved from {formatDate(reservation.startDate)} to{' '}
            {formatDate(reservation.endDate)}. Keep your reference below. Staff will hand over the vehicle at the rental desk
            on your pickup day.
          </p>
        </Alert>
      )}
      {cancelled && (
        <Alert tone="success" title="Reservation cancelled">
          <p>The dates are free again. You can reserve another vehicle at any time.</p>
        </Alert>
      )}

      <section className="card card-body" aria-labelledby="reservation-title">
        <h1 id="reservation-title">{reservation.vehicleDisplayName}</h1>
        <p>
          <ReservationStatusBadge reservation={reservation} />
        </p>

        <dl className="detail-list">
          <dt>Reference</dt>
          <dd>{reservation.id}</dd>
          <dt>Pickup</dt>
          <dd>{formatDate(reservation.startDate)}</dd>
          <dt>Return</dt>
          <dd>{formatDate(reservation.endDate)}</dd>
          <dt>Days charged</dt>
          <dd>{pluralize(reservation.billableDays, 'day')}</dd>
          <dt>Price per day</dt>
          <dd>{formatMoney(reservation.dailyRateAtReservation)}</dd>
          <dt>Pricing</dt>
          <dd>{reservation.pricingDescription}</dd>
          <dt>Total</dt>
          <dd>{formatMoney(reservation.totalCost)}</dd>
          <dt>Reserved on</dt>
          <dd>{formatDateTime(reservation.createdAt)}</dd>
          {reservation.cancelledAt && (
            <>
              <dt>Cancelled on</dt>
              <dd>{formatDateTime(reservation.cancelledAt)}</dd>
            </>
          )}
          {reservation.fulfilledAt && (
            <>
              <dt>Picked up on</dt>
              <dd>{formatDateTime(reservation.fulfilledAt)}</dd>
            </>
          )}
        </dl>

        {reservation.isExpired && (
          <Alert tone="warning" title="This reservation was not collected">
            <p>Its dates have passed. The rental desk can cancel it for you.</p>
          </Alert>
        )}
        {startedButActive && !reservation.isExpired && (
          <p className="muted">
            This reservation has started, so it can only be cancelled by the rental desk.
          </p>
        )}

        {canCancel && (
          <p>
            <Button variant="danger" onClick={() => setConfirming(true)}>
              Cancel this reservation
            </Button>
          </p>
        )}
      </section>

      <Dialog open={confirming} onClose={closeDialog} title="Cancel this reservation?">
        <p>
          Your reservation of the {reservation.vehicleDisplayName} for {formatDate(reservation.startDate)} to{' '}
          {formatDate(reservation.endDate)} will be cancelled and the dates released. This cannot be undone.
        </p>
        {cancelError && (
          <Alert tone="error" title="We could not cancel the reservation">
            <p>{cancelError instanceof ApiError ? cancelError.userMessage : 'Please try again.'}</p>
          </Alert>
        )}
        <div className="dialog-actions">
          <Button variant="secondary" onClick={closeDialog}>
            Keep reservation
          </Button>
          <Button variant="danger" onClick={confirmCancel} loading={cancel.isPending}>
            Yes, cancel it
          </Button>
        </div>
      </Dialog>
    </div>
  )
}

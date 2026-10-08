import { useNavigate } from 'react-router-dom'
import { Alert } from '../../components/ui/Alert'
import { Button, LinkButton } from '../../components/ui/Button'
import { Dialog } from '../../components/ui/Dialog'
import { ApiError } from '../../lib/api/errors'
import type { Quote, Vehicle } from '../../lib/api/types'
import { formatDate } from '../../lib/dates'
import { formatMoney, pluralize } from '../../lib/format'
import { useCreateReservation } from './queries'

interface ReserveDialogProps {
  open: boolean
  onClose: () => void
  vehicle: Vehicle
  quote: Quote
}

/**
 * The last step before booking. The price and dates shown are the ones the server quoted a moment ago; the server
 * decides again when the customer confirms, so this can still be refused (for example if someone else booked first).
 */
export function ReserveDialog({ open, onClose, vehicle, quote }: ReserveDialogProps) {
  const navigate = useNavigate()
  const create = useCreateReservation()
  const error = create.error
  const conflict = error instanceof ApiError && error.isConflict

  function close() {
    create.reset()
    onClose()
  }

  function confirm() {
    create.mutate(
      { vehicleId: vehicle.id, range: { startDate: quote.startDate, endDate: quote.endDate } },
      {
        onSuccess: (reservation) => {
          void navigate(`/reservations/${reservation.id}`, { state: { justBooked: true } })
        },
      },
    )
  }

  return (
    <Dialog open={open} onClose={close} title="Confirm your reservation">
      <dl className="detail-list">
        <dt>Vehicle</dt>
        <dd>{vehicle.displayName}</dd>
        <dt>Pickup</dt>
        <dd>{formatDate(quote.startDate)}</dd>
        <dt>Return</dt>
        <dd>{formatDate(quote.endDate)}</dd>
        <dt>Charged for</dt>
        <dd>{pluralize(quote.billableDays, 'day')}</dd>
        <dt>Total</dt>
        <dd>
          {formatMoney(quote.totalCost)} <span className="muted">({quote.pricingDescription})</span>
        </dd>
      </dl>
      <p className="muted">
        No payment is taken on this website. Staff hand the vehicle over at the rental desk on your pickup day.
      </p>

      {conflict && (
        <Alert
          tone="warning"
          title="This vehicle is no longer free for those dates"
          actions={
            <>
              <Button variant="secondary" onClick={close}>
                Choose other dates
              </Button>
              <LinkButton
                variant="secondary"
                to={`/vehicles?start=${quote.startDate}&end=${quote.endDate}`}
                onClick={close}
              >
                See other vehicles for these dates
              </LinkButton>
            </>
          }
        >
          <p>Someone else may have reserved it a moment ago. {error.detail ?? ''}</p>
        </Alert>
      )}
      {error && !conflict && (
        <Alert tone="error" title="We could not make the reservation">
          <p>{error instanceof ApiError ? error.userMessage : 'Please try again.'}</p>
        </Alert>
      )}

      {!conflict && (
        <div className="dialog-actions">
          <Button variant="secondary" onClick={close}>
            Cancel
          </Button>
          <Button onClick={confirm} loading={create.isPending}>
            Confirm reservation
          </Button>
        </div>
      )}
    </Dialog>
  )
}

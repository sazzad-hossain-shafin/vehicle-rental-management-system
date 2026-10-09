import { CalendarCheck, ShieldCheck } from 'lucide-react'
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
 * The last step before booking. The price and dates shown are the ones the server quoted a moment ago and are the
 * same values sent when the customer confirms; the server decides again, so this can still be refused (for example
 * if someone else booked first).
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
      <p className="muted dialog-lead">Check the details below. No payment is taken on this website.</p>

      <div className="summary-panel">
        <p className="summary-vehicle">
          <CalendarCheck size={18} aria-hidden="true" /> {vehicle.displayName}
        </p>
        <dl className="detail-list">
          <dt>Pickup</dt>
          <dd>{formatDate(quote.startDate)}</dd>
          <dt>Return</dt>
          <dd>{formatDate(quote.endDate)}</dd>
          <dt>Charged for</dt>
          <dd>{pluralize(quote.billableDays, 'day')}</dd>
          <dt>Price per day</dt>
          <dd>{formatMoney(quote.dailyRate)}</dd>
          <dt>Pricing</dt>
          <dd>{quote.pricingDescription}</dd>
        </dl>
        <p className="summary-total">
          <span>Total</span>
          <strong>{formatMoney(quote.totalCost)}</strong>
        </p>
      </div>

      <p className="muted fine-print dialog-note">
        <ShieldCheck size={16} aria-hidden="true" /> Staff hand the vehicle over at the rental desk on your pickup day. You
        can cancel until the day before pickup.
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

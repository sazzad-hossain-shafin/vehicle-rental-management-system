import { ArrowRight, Bike, CalendarX2, Car, Truck } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { LinkButton } from '../../components/ui/Button'
import { Pagination } from '../../components/ui/Pagination'
import { EmptyState, ErrorState, LoadingList } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import type { Reservation, VehicleType } from '../../lib/api/types'
import { formatDate, formatWeekday } from '../../lib/dates'
import { formatMoney, pluralize } from '../../lib/format'
import { useMyReservations } from './queries'
import { ReservationStatusBadge } from './ReservationStatusBadge'

const TYPE_ICON: Record<VehicleType, typeof Car> = { Car, Motorcycle: Bike, Van: Truck }

export function ReservationsPage() {
  useDocumentTitle('My reservations')
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const reservations = useMyReservations(page)

  return (
    <div className="container page">
      <div className="page-intro">
        <h1 className="page-heading">My reservations</h1>
        <p className="muted">Your reservations, listed by pickup date. Open one to see the full price or to cancel it.</p>
      </div>

      {reservations.isPending ? (
        <LoadingList label="Loading your reservations" />
      ) : reservations.isError ? (
        <ErrorState
          error={reservations.error}
          onRetry={() => void reservations.refetch()}
          title="We could not load your reservations"
        />
      ) : reservations.data.items.length === 0 ? (
        <EmptyState
          icon={<CalendarX2 aria-hidden="true" />}
          title="You have no reservations yet"
          action={<LinkButton to="/vehicles">Find a vehicle</LinkButton>}
        >
          When you reserve a vehicle, it will appear here.
        </EmptyState>
      ) : (
        <>
          <ul className="reservation-list" aria-label="Your reservations">
            {reservations.data.items.map((reservation) => (
              <ReservationCard key={reservation.id} reservation={reservation} />
            ))}
          </ul>
          <p className="muted fine-print">
            Showing {reservations.data.items.length} of {pluralize(reservations.data.totalCount, 'reservation')}.
          </p>
          <Pagination
            page={reservations.data.page}
            totalPages={reservations.data.totalPages}
            onPageChange={(next) => setParams(next === 1 ? {} : { page: String(next) })}
          />
        </>
      )}
    </div>
  )
}

function ReservationCard({ reservation }: { reservation: Reservation }) {
  const Icon = TYPE_ICON[reservation.vehicleType]
  const inactive = reservation.status === 'Cancelled'

  return (
    <li className={`card card-lift reservation-card${inactive ? ' is-inactive' : ''}`}>
      <span className="reservation-icon" data-type={reservation.vehicleType} aria-hidden="true">
        <Icon size={26} />
      </span>
      <div className="reservation-main">
        <div className="reservation-head">
          <h2 className="reservation-title">{reservation.vehicleDisplayName}</h2>
          <ReservationStatusBadge reservation={reservation} />
        </div>
        <dl className="date-pair">
          <div>
            <dt>Pickup</dt>
            <dd>
              <span className="weekday">{formatWeekday(reservation.startDate)}</span> {formatDate(reservation.startDate)}
            </dd>
          </div>
          <ArrowRight size={16} aria-hidden="true" className="date-arrow" />
          <div>
            <dt>Return</dt>
            <dd>
              <span className="weekday">{formatWeekday(reservation.endDate)}</span> {formatDate(reservation.endDate)}
            </dd>
          </div>
        </dl>
      </div>
      <div className="reservation-side">
        <p className="reservation-total">
          <span className="muted">Total</span>
          <strong>{formatMoney(reservation.totalCost)}</strong>
        </p>
        <Link
          className="btn btn-secondary"
          to={`/reservations/${reservation.id}`}
          aria-label={`View details of the ${reservation.vehicleDisplayName} reservation`}
        >
          View details
        </Link>
      </div>
    </li>
  )
}

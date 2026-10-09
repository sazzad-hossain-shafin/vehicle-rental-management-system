import { CalendarX2 } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { LinkButton } from '../../components/ui/Button'
import { Pagination } from '../../components/ui/Pagination'
import { EmptyState, ErrorState, LoadingBlock } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { formatDate } from '../../lib/dates'
import { formatMoney } from '../../lib/format'
import { useMyReservations } from './queries'
import { ReservationStatusBadge } from './ReservationStatusBadge'

export function ReservationsPage() {
  useDocumentTitle('My reservations')
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const reservations = useMyReservations(page)

  return (
    <div className="container page">
      <h1 className="page-heading">My reservations</h1>

      {reservations.isPending ? (
        <LoadingBlock label="Loading your reservations" />
      ) : reservations.isError ? (
        <ErrorState
          error={reservations.error}
          onRetry={() => void reservations.refetch()}
          title="We could not load your reservations"
        />
      ) : reservations.data.items.length === 0 ? (
        <EmptyState
          icon={<CalendarX2 size={40} aria-hidden="true" />}
          title="You have no reservations yet"
          action={<LinkButton to="/vehicles">Find a vehicle</LinkButton>}
        >
          When you reserve a vehicle, it will appear here.
        </EmptyState>
      ) : (
        <>
          <ul className="reservation-list" aria-label="Your reservations">
            {reservations.data.items.map((reservation) => (
              <li key={reservation.id} className="card reservation-item">
                <div>
                  <h2 className="reservation-title">{reservation.vehicleDisplayName}</h2>
                  <p className="muted">
                    Pickup {formatDate(reservation.startDate)} &middot; Return {formatDate(reservation.endDate)}
                  </p>
                  <p>
                    <ReservationStatusBadge reservation={reservation} /> &nbsp;
                    <strong>{formatMoney(reservation.totalCost)}</strong>
                  </p>
                </div>
                <Link
                  className="btn btn-secondary"
                  to={`/reservations/${reservation.id}`}
                  aria-label={`View details of the ${reservation.vehicleDisplayName} reservation`}
                >
                  View details
                </Link>
              </li>
            ))}
          </ul>
          <p className="muted">
            Reservations are listed by pickup date. Showing {reservations.data.items.length} of {reservations.data.totalCount}.
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

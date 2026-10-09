import { ArrowRight, CalendarCheck, CarFront, KeyRound } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Pagination } from '../../components/ui/Pagination'
import { ErrorState, LoadingBlock } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { formatDate } from '../../lib/dates'
import { formatMoney } from '../../lib/format'
import { useMyCustomer, useMyRentals, useMyReservations } from '../reservations/queries'
import { isCustomer, useAuth } from '../auth/useAuth'

/** Up to two capital letters from a name, for the avatar. */
function initials(name: string): string {
  const letters = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
  return letters.join('') || '?'
}

export function AccountPage() {
  useDocumentTitle('My account')
  const { user } = useAuth()
  const customerAccount = isCustomer(user)

  return (
    <div className="container page">
      <div className="page-intro">
        <h1 className="page-heading">My account</h1>
        <p className="muted">Your details and rental history.</p>
      </div>

      <div className="account-grid">
        <section className="card card-body profile-card" aria-labelledby="profile-title">
          {customerAccount ? <Profile email={user?.email ?? ''} /> : <StaffProfile email={user?.email ?? ''} />}
        </section>

        <nav className="card card-body quick-links" aria-label="Account shortcuts">
          <h2 className="section-title">Shortcuts</h2>
          <ul>
            <li>
              <Link to="/reservations">
                <CalendarCheck size={18} aria-hidden="true" /> My reservations <ArrowRight size={16} aria-hidden="true" />
              </Link>
            </li>
            <li>
              <Link to="/vehicles">
                <CarFront size={18} aria-hidden="true" /> Browse vehicles <ArrowRight size={16} aria-hidden="true" />
              </Link>
            </li>
          </ul>
        </nav>
      </div>

      {customerAccount ? (
        <Rentals />
      ) : (
        <p className="muted account-note">This is a staff account. Rental history is available to customer accounts.</p>
      )}
    </div>
  )
}

function StaffProfile({ email }: { email: string }) {
  return (
    <>
    <div className="profile-header">
      <span className="avatar" aria-hidden="true">
        S
      </span>
      <h2 id="profile-title">Staff account</h2>
    </div>
    <dl className="detail-list">
      <dt>Email</dt>
      <dd>{email}</dd>
    </dl>
    </>
  )
}

function Profile({ email }: { email: string }) {
  const customer = useMyCustomer()

  return (
    <>
      <div className="profile-header">
        <span className="avatar" aria-hidden="true">
          {customer.data ? initials(customer.data.name) : '…'}
        </span>
        <h2 id="profile-title">Your details</h2>
      </div>
      <dl className="detail-list">
        <dt>Email</dt>
        <dd>{email}</dd>
        <dt>Name</dt>
        <dd>{customer.isPending ? 'Loading…' : customer.isError ? 'Unavailable' : customer.data.name}</dd>
        <dt>Customer number</dt>
        <dd>{customer.isPending ? 'Loading…' : customer.isError ? 'Unavailable' : customer.data.customerNumber}</dd>
      </dl>
    </>
  )
}

function Rentals() {
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const rentals = useMyRentals(page)
  const reservations = useMyReservations(1)

  return (
    <section className="card card-body rentals-card" aria-labelledby="rentals-title">
      <div className="section-head">
        <div>
          <h2 id="rentals-title">Your rentals</h2>
          <p className="muted">
            A rental starts when the vehicle is handed over at the rental desk. Your <Link to="/reservations">reservations</Link>{' '}
            are listed separately.
          </p>
        </div>
      </div>

      {/* Counts come straight from the API's totals: nothing is estimated or derived by this page. */}
      {rentals.data && reservations.data && (
        <dl className="stat-row" aria-label="Summary">
          <div>
            <dt>Rentals</dt>
            <dd>{rentals.data.totalCount}</dd>
          </div>
          <div>
            <dt>Reservations</dt>
            <dd>{reservations.data.totalCount}</dd>
          </div>
        </dl>
      )}

      {rentals.isPending ? (
        <LoadingBlock label="Loading your rentals" />
      ) : rentals.isError ? (
        <ErrorState error={rentals.error} onRetry={() => void rentals.refetch()} title="We could not load your rentals" />
      ) : rentals.data.items.length === 0 ? (
        <div className="inline-empty">
          <KeyRound size={22} aria-hidden="true" />
          <p className="muted">You have no rentals yet. When staff hand you a vehicle, the rental will appear here.</p>
        </div>
      ) : (
        <>
          <ul className="reservation-list" aria-label="Your rentals">
            {rentals.data.items.map((rental) => (
              <li key={rental.id} className="reservation-item card">
                <div>
                  <h3>{rental.vehicleDisplayName}</h3>
                  <p className="muted">
                    From {formatDate(rental.startDate)} to {formatDate(rental.expectedReturnDate)}
                    {rental.actualReturnDate && <> &middot; returned {formatDate(rental.actualReturnDate)}</>}
                  </p>
                  <p className="meta-row">
                    <Badge tone={rental.status === 'Active' ? 'info' : 'success'}>
                      {rental.status === 'Active' ? 'Out now' : 'Returned'}
                    </Badge>
                    <strong>{formatMoney(rental.totalCost)}</strong>
                  </p>
                </div>
              </li>
            ))}
          </ul>
          <Pagination
            page={rentals.data.page}
            totalPages={rentals.data.totalPages}
            onPageChange={(next) => setParams(next === 1 ? {} : { page: String(next) })}
          />
        </>
      )}
    </section>
  )
}

import { UserRound } from 'lucide-react'
import { Link, useSearchParams } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { Pagination } from '../../components/ui/Pagination'
import { ErrorState, LoadingBlock } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { formatDate } from '../../lib/dates'
import { formatMoney } from '../../lib/format'
import { useMyCustomer, useMyRentals } from '../reservations/queries'
import { isCustomer, useAuth } from '../auth/useAuth'

export function AccountPage() {
  useDocumentTitle('My account')
  const { user } = useAuth()
  const customerAccount = isCustomer(user)

  return (
    <div className="container page stack">
      <div className="page-intro">
        <h1 className="page-heading">My account</h1>
        <p className="muted">Your details and rental history.</p>
      </div>

      <section className="card card-body" aria-labelledby="profile-title">
        <div className="profile-header">
          <span className="avatar">
            <UserRound size={28} aria-hidden="true" />
          </span>
          <h2 id="profile-title">Your details</h2>
        </div>
        <dl className="detail-list">
          <dt>Email</dt>
          <dd>{user?.email}</dd>
          {customerAccount && <Profile />}
        </dl>
      </section>

      {customerAccount ? (
        <Rentals />
      ) : (
        <p className="muted">This is a staff account. Rental history is available to customer accounts.</p>
      )}
    </div>
  )
}

function Profile() {
  const customer = useMyCustomer()

  if (customer.isPending) {
    return (
      <>
        <dt>Name</dt>
        <dd>Loading…</dd>
      </>
    )
  }

  if (customer.isError) {
    return (
      <>
        <dt>Name</dt>
        <dd>Unavailable</dd>
      </>
    )
  }

  return (
    <>
      <dt>Name</dt>
      <dd>{customer.data.name}</dd>
      <dt>Customer number</dt>
      <dd>{customer.data.customerNumber}</dd>
    </>
  )
}

function Rentals() {
  const [params, setParams] = useSearchParams()
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const rentals = useMyRentals(page)

  return (
    <section className="card card-body" aria-labelledby="rentals-title">
      <h2 id="rentals-title">Your rentals</h2>
      <p className="muted">
        A rental starts when the vehicle is handed over at the rental desk. Your <Link to="/reservations">reservations</Link>{' '}
        are listed separately.
      </p>

      {rentals.isPending ? (
        <LoadingBlock label="Loading your rentals" />
      ) : rentals.isError ? (
        <ErrorState error={rentals.error} onRetry={() => void rentals.refetch()} title="We could not load your rentals" />
      ) : rentals.data.items.length === 0 ? (
        <p className="muted">You have no rentals yet. When staff hand you a vehicle, the rental will appear here.</p>
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

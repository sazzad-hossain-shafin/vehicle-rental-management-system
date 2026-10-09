import { ArrowLeft } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation, useParams, useSearchParams } from 'react-router-dom'
import { Alert } from '../../components/ui/Alert'
import { Badge } from '../../components/ui/Badge'
import { Button, LinkButton } from '../../components/ui/Button'
import { ErrorState, LoadingBlock } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { isIsoDate, formatDate, todayIso, validateRange } from '../../lib/dates'
import { formatMoney, pluralize } from '../../lib/format'
import { ApiError } from '../../lib/api/errors'
import type { Quote, Vehicle } from '../../lib/api/types'
import { useAuth, isCustomer } from '../auth/useAuth'
import { ReserveDialog } from '../reservations/ReserveDialog'
import { DateRangeForm } from './DateRangeForm'
import { useQuote, useVehicle } from './queries'
import { VehicleArt } from './VehicleArt'

export function VehicleDetailPage() {
  const { id } = useParams()
  const [params, setParams] = useSearchParams()
  const vehicle = useVehicle(id)

  useDocumentTitle(vehicle.data?.displayName ?? 'Vehicle')

  const start = params.get('start') ?? ''
  const end = params.get('end') ?? ''
  const range =
    isIsoDate(start) && isIsoDate(end) && Object.keys(validateRange(start, end, todayIso())).length === 0
      ? { startDate: start, endDate: end }
      : null

  if (vehicle.isPending) {
    return (
      <div className="container page">
        <LoadingBlock label="Loading the vehicle" />
      </div>
    )
  }

  if (vehicle.isError) {
    const notFound = vehicle.error instanceof ApiError && vehicle.error.isNotFound
    return (
      <div className="container page stack">
        {notFound ? (
          <Alert tone="warning" title="We could not find that vehicle">
            <p>It may have been removed.</p>
          </Alert>
        ) : (
          <ErrorState error={vehicle.error} onRetry={() => void vehicle.refetch()} title="We could not load the vehicle" />
        )}
        <p>
          <Link to="/vehicles">Back to all vehicles</Link>
        </p>
      </div>
    )
  }

  const data = vehicle.data

  return (
    <div className="container page">
      <Link className="back-link" to="/vehicles">
        <ArrowLeft size={18} aria-hidden="true" /> All vehicles
      </Link>
      <div className="split">
        <section className="card" aria-labelledby="vehicle-title">
          <VehicleArt type={data.vehicleType} />
          <div className="card-body">
            <div className="meta-row">
              <Badge tone="neutral">{data.vehicleType}</Badge>
              <Badge tone={data.availabilityStatus === 'Available' ? 'success' : 'warning'}>
                {data.availabilityStatus === 'Available' ? 'Available now' : 'Out right now'}
              </Badge>
            </div>
            <h1 id="vehicle-title">{data.displayName}</h1>
            <p className="muted">Model year {data.year}</p>
            <p className="price">
              {formatMoney(data.dailyRate)} <small>per day</small>
            </p>
            <p className="muted">
              The total for your trip is calculated by our booking system for the dates you choose, so any longer-stay
              pricing is applied for you. You are charged for each day from pickup up to, but not including, the return
              day.
            </p>
          </div>
        </section>

        <section className="card card-body stack sticky" aria-labelledby="book-title">
          <h2 id="book-title">Check dates and price</h2>
          <DateRangeForm
            key={`${start}|${end}`}
            idPrefix="detail"
            initial={{ startDate: start, endDate: end }}
            submitLabel="Get price"
            onSubmit={(value) => setParams({ start: value.startDate, end: value.endDate })}
          />
          {range ? <QuotePanel vehicle={data} range={range} /> : <p className="muted">Choose your dates to see the price and availability.</p>}
        </section>
      </div>
    </div>
  )
}

function QuotePanel({ vehicle, range }: { vehicle: Vehicle; range: { startDate: string; endDate: string } }) {
  const quote = useQuote(vehicle.id, range)

  if (quote.isPending) return <LoadingBlock label="Getting the price" />

  if (quote.isError) {
    return <ErrorState error={quote.error} onRetry={() => void quote.refetch()} title="We could not get a price for those dates" />
  }

  return <QuoteResult vehicle={vehicle} quote={quote.data} />
}

function QuoteResult({ vehicle, quote }: { vehicle: Vehicle; quote: Quote }) {
  const { status, user } = useAuth()
  const location = useLocation()
  const [confirming, setConfirming] = useState(false)
  const from = `${location.pathname}${location.search}`

  return (
    <div className="stack" aria-live="polite">
      <p>
        Pickup <strong>{formatDate(quote.startDate)}</strong>, return <strong>{formatDate(quote.endDate)}</strong>.
      </p>

      <table className="price-table">
        <caption className="visually-hidden">Price for your dates</caption>
        <tbody>
          <tr>
            <th scope="row">Price per day</th>
            <td>{formatMoney(quote.dailyRate)}</td>
          </tr>
          <tr>
            <th scope="row">Days charged</th>
            <td>{pluralize(quote.billableDays, 'day')}</td>
          </tr>
          <tr>
            <th scope="row">Pricing</th>
            <td>{quote.pricingDescription}</td>
          </tr>
          <tr className="total">
            <th scope="row">Total</th>
            <td>{formatMoney(quote.totalCost)}</td>
          </tr>
        </tbody>
      </table>

      {quote.isAvailable ? (
        <Alert tone="success">
          <p>This vehicle is free for those dates right now. It is only held for you once you reserve it.</p>
        </Alert>
      ) : (
        <Alert
          tone="warning"
          title="Not available for those dates"
          actions={
            <LinkButton
              variant="secondary"
              to={`/vehicles?start=${quote.startDate}&end=${quote.endDate}`}
            >
              See vehicles free for these dates
            </LinkButton>
          }
        >
          <p>Someone else has it reserved or rented for part of that period.</p>
        </Alert>
      )}

      {status === 'authenticated' && isCustomer(user) ? (
        <>
          <Button block onClick={() => setConfirming(true)} disabled={!quote.isAvailable}>
            Reserve these dates
          </Button>
          <ReserveDialog open={confirming} onClose={() => setConfirming(false)} vehicle={vehicle} quote={quote} />
        </>
      ) : status === 'authenticated' ? (
        <Alert tone="info">
          <p>Staff accounts take bookings at the rental desk. Sign in with a customer account to reserve online.</p>
        </Alert>
      ) : status === 'anonymous' ? (
        <div className="stack">
          <LinkButton to="/login" state={{ from }} block>
            Sign in to reserve
          </LinkButton>
          <p className="muted">
            No account yet? <Link to="/register" state={{ from }}>Create one</Link>
          </p>
        </div>
      ) : null}
    </div>
  )
}

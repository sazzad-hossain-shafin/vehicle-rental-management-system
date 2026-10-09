import { ArrowRight, CalendarCheck, CarFront, CircleCheck, KeyRound, Search } from 'lucide-react'
import { Link, useNavigate } from 'react-router-dom'
import { ErrorState, LoadingGrid } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { DateRangeForm } from './DateRangeForm'
import { useVehicles } from './queries'
import { VehicleCard } from './VehicleCard'

const STEPS = [
  { icon: Search, title: 'Choose your dates', text: 'Pick the day you collect the vehicle and the day you bring it back.' },
  { icon: CarFront, title: 'Pick a vehicle', text: 'See which vehicles are free for those dates, with the exact price.' },
  { icon: CalendarCheck, title: 'Reserve online', text: 'Sign in and confirm. No payment is taken on this website.' },
  { icon: KeyRound, title: 'Collect at the desk', text: 'Our rental desk hands the vehicle over on your pickup day.' },
] as const

const PROMISES = ['Exact price before you reserve', 'Cancel any time before pickup day', 'No payment taken online'] as const

export function HomePage() {
  useDocumentTitle('Reserve a vehicle for your dates')
  const navigate = useNavigate()
  const fleet = useVehicles({ page: 1, pageSize: 3 })

  return (
    <>
      <section className="hero" aria-labelledby="hero-title">
        <div className="container">
          <span className="eyebrow">Cars, motorcycles and vans</span>
          <h1 id="hero-title">Reserve the right vehicle for your dates</h1>
          <p className="lead">
            Check which vehicles are free, see the exact price for your trip, and reserve in a few clicks.
          </p>
          <ul className="trust-list">
            {PROMISES.map((promise) => (
              <li key={promise}>
                <CircleCheck size={16} aria-hidden="true" /> {promise}
              </li>
            ))}
          </ul>
        </div>
      </section>

      <div className="container search-overlap">
        <div className="search-panel">
          <DateRangeForm
            wide
            idPrefix="home"
            submitLabel="Find vehicles"
            onSubmit={({ startDate, endDate }) => navigate(`/vehicles?start=${startDate}&end=${endDate}`)}
          />
        </div>
      </div>

      <section className="container section" aria-labelledby="how-title">
        <div className="section-head">
          <div>
            <h2 id="how-title">How it works</h2>
            <p className="muted">From search to keys in four steps.</p>
          </div>
        </div>
        <ol className="steps">
          {STEPS.map(({ icon: Icon, title, text }) => (
            <li key={title}>
              <span className="step-icon">
                <Icon size={24} aria-hidden="true" />
              </span>
              <h3>{title}</h3>
              <p className="muted">{text}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="container section fleet-section" aria-labelledby="fleet-title">
        <div className="section-head">
          <div>
            <h2 id="fleet-title">From the fleet</h2>
            <p className="muted">A few of the vehicles you can reserve.</p>
          </div>
          <Link className="more-link" to="/vehicles">
            Browse all vehicles <ArrowRight size={18} aria-hidden="true" />
          </Link>
        </div>
        {fleet.isPending ? (
          <LoadingGrid count={3} label="Loading vehicles" />
        ) : fleet.isError ? (
          <ErrorState error={fleet.error} onRetry={() => void fleet.refetch()} title="We could not load the vehicles" />
        ) : fleet.data.items.length === 0 ? (
          <p className="muted">There are no vehicles to show yet.</p>
        ) : (
          <ul className="vehicle-grid">
            {fleet.data.items.map((vehicle) => (
              <li key={vehicle.id}>
                <VehicleCard vehicle={vehicle} />
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  )
}

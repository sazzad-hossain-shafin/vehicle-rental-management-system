import { CalendarCheck, CarFront, KeyRound, Search } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { LinkButton } from '../../components/ui/Button'
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

export function HomePage() {
  useDocumentTitle('Reserve a vehicle for your dates')
  const navigate = useNavigate()
  const fleet = useVehicles({ page: 1, pageSize: 3 })

  return (
    <>
      <section className="hero" aria-labelledby="hero-title">
        <div className="container">
          <h1 id="hero-title">Reserve the right vehicle for your dates</h1>
          <p className="lead">
            Check which cars, motorcycles and vans are free, see the exact price for your trip, and reserve in a few
            clicks.
          </p>
          <div className="search-panel">
            <DateRangeForm
              idPrefix="home"
              submitLabel="Find vehicles"
              onSubmit={({ startDate, endDate }) => navigate(`/vehicles?start=${startDate}&end=${endDate}`)}
            />
          </div>
        </div>
      </section>

      <section className="container page" aria-labelledby="how-title">
        <h2 id="how-title">How it works</h2>
        <ol className="steps">
          {STEPS.map(({ icon: Icon, title, text }) => (
            <li key={title}>
              <Icon size={22} aria-hidden="true" />
              <h3>{title}</h3>
              <p className="muted">{text}</p>
            </li>
          ))}
        </ol>
      </section>

      <section className="container page" aria-labelledby="fleet-title">
        <h2 id="fleet-title">From the fleet</h2>
        {fleet.isPending ? (
          <LoadingGrid count={3} label="Loading vehicles" />
        ) : fleet.isError ? (
          <ErrorState error={fleet.error} onRetry={() => void fleet.refetch()} title="We could not load the vehicles" />
        ) : fleet.data.items.length === 0 ? (
          <p className="muted">There are no vehicles to show yet.</p>
        ) : (
          <>
            <ul className="vehicle-grid">
              {fleet.data.items.map((vehicle) => (
                <li key={vehicle.id}>
                  <VehicleCard vehicle={vehicle} />
                </li>
              ))}
            </ul>
            <p>
              <LinkButton to="/vehicles" variant="secondary">
                Browse all vehicles
              </LinkButton>
            </p>
          </>
        )}
      </section>
    </>
  )
}

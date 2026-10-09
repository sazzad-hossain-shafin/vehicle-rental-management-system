import { Link } from 'react-router-dom'
import { Badge } from '../../components/ui/Badge'
import { formatMoney } from '../../lib/format'
import type { Vehicle } from '../../lib/api/types'
import { VehicleArt } from './VehicleArt'

interface VehicleCardProps {
  vehicle: Vehicle
  /** Set when the list was searched by dates: every vehicle shown is free for them. */
  dates?: { startDate: string; endDate: string } | undefined
}

export function VehicleCard({ vehicle, dates }: VehicleCardProps) {
  const search = dates ? `?start=${dates.startDate}&end=${dates.endDate}` : ''

  return (
    <article className="card card-lift vehicle-card" aria-labelledby={`vehicle-${vehicle.id}`}>
      <div className="art-frame">
        <VehicleArt type={vehicle.vehicleType} />
        <span className="vehicle-art-label">{vehicle.vehicleType}</span>
      </div>
      <div className="vehicle-card-body">
        <h3 id={`vehicle-${vehicle.id}`}>{vehicle.displayName}</h3>
        <p className="muted">{vehicle.year}</p>
        <div className="vehicle-meta">
          <p className="price">
            {formatMoney(vehicle.dailyRate)} <small>per day</small>
          </p>
          {dates ? (
            <Badge tone="success">Free for your dates</Badge>
          ) : vehicle.availabilityStatus === 'Available' ? (
            <Badge tone="success">Available now</Badge>
          ) : (
            <Badge tone="warning">Out right now</Badge>
          )}
        </div>
        <div className="card-actions">
          <Link
            className="btn btn-secondary btn-block"
            to={`/vehicles/${vehicle.id}${search}`}
            aria-label={`View details for ${vehicle.displayName}`}
          >
            View details
          </Link>
        </div>
      </div>
    </article>
  )
}

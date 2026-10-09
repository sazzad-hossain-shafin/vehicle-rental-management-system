import { Bike, Car, Truck } from 'lucide-react'
import type { VehicleType } from '../../lib/api/types'

/**
 * The vehicles have no photographs, so each card shows an original icon for its type. Nothing here pretends to be
 * a picture of the actual vehicle.
 */
export function VehicleArt({ type }: { type: VehicleType }) {
  const Icon = type === 'Motorcycle' ? Bike : type === 'Van' ? Truck : Car

  return (
    <div className="vehicle-art" data-type={type} aria-hidden="true">
      <Icon size={72} strokeWidth={1.3} />
    </div>
  )
}

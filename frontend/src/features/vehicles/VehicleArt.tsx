import { Bike, Car, Truck } from 'lucide-react'
import type { VehicleType } from '../../lib/api/types'

/**
 * The vehicles have no photographs, so each one shows an original icon for its type. Nothing here pretends to be
 * a picture of the actual vehicle. Real photographs and specifications would need backend support (future work).
 */
export function VehicleArt({ type, hero = false }: { type: VehicleType; hero?: boolean }) {
  const Icon = type === 'Motorcycle' ? Bike : type === 'Van' ? Truck : Car

  if (hero) {
    return (
      <div className="vehicle-art vehicle-art-hero" data-type={type} aria-hidden="true">
        <span className="vehicle-art-disc">
          <Icon size={64} strokeWidth={1.2} />
        </span>
      </div>
    )
  }

  return (
    <div className="vehicle-art" data-type={type} aria-hidden="true">
      <Icon size={72} strokeWidth={1.3} />
    </div>
  )
}

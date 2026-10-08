import { Badge } from '../../components/ui/Badge'
import type { Reservation } from '../../lib/api/types'

/** The status as the server reports it, plus the server's own "expired" flag for a booking that was never collected. */
export function ReservationStatusBadge({ reservation }: { reservation: Pick<Reservation, 'status' | 'isExpired'> }) {
  if (reservation.status === 'Cancelled') return <Badge tone="neutral">Cancelled</Badge>
  if (reservation.status === 'Fulfilled') return <Badge tone="success">Picked up</Badge>
  if (reservation.isExpired) return <Badge tone="warning">Expired, not collected</Badge>
  return <Badge tone="info">Active</Badge>
}

import { keepPreviousData, useQuery } from '@tanstack/react-query'
import {
  getQuote,
  getVehicle,
  listAvailableVehicles,
  listVehicles,
  type DateRange,
  type VehicleFilters,
} from '../../lib/api/endpoints'

export const vehicleKeys = {
  all: ['vehicles'] as const,
  list: (filters: VehicleFilters) => ['vehicles', 'list', filters] as const,
  available: (range: DateRange, filters: VehicleFilters) => ['vehicles', 'available', range, filters] as const,
  detail: (id: string) => ['vehicles', 'detail', id] as const,
  quote: (id: string, range: DateRange) => ['vehicles', 'quote', id, range] as const,
}

/** Vehicles, one server page at a time. Filtering and paging happen on the server. */
export function useVehicles(filters: VehicleFilters, enabled = true) {
  return useQuery({
    queryKey: vehicleKeys.list(filters),
    queryFn: ({ signal }) => listVehicles(filters, signal),
    placeholderData: keepPreviousData,
    enabled,
  })
}

/** Vehicles that are free for the whole period, as the server sees it right now. */
export function useAvailableVehicles(range: DateRange | null, filters: VehicleFilters) {
  return useQuery({
    queryKey: vehicleKeys.available(range ?? { startDate: '', endDate: '' }, filters),
    queryFn: ({ signal }) => listAvailableVehicles(range!, filters, signal),
    enabled: range !== null,
    placeholderData: keepPreviousData,
  })
}

export function useVehicle(id: string | undefined) {
  return useQuery({
    queryKey: vehicleKeys.detail(id ?? ''),
    queryFn: ({ signal }) => getVehicle(id!, signal),
    enabled: Boolean(id),
  })
}

/** The price the server would quote. Re-checked whenever the dates change; a snapshot, never a guarantee. */
export function useQuote(id: string | undefined, range: DateRange | null) {
  return useQuery({
    queryKey: vehicleKeys.quote(id ?? '', range ?? { startDate: '', endDate: '' }),
    queryFn: ({ signal }) => getQuote(id!, range!, signal),
    enabled: Boolean(id) && range !== null,
    staleTime: 0,
  })
}

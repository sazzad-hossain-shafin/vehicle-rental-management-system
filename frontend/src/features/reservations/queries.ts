import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  cancelReservation,
  createReservation,
  getMyCustomer,
  getMyReservation,
  listMyRentals,
  listMyReservations,
  type DateRange,
} from '../../lib/api/endpoints'
import type { Reservation } from '../../lib/api/types'

export const reservationKeys = {
  all: ['reservations'] as const,
  list: (page: number) => ['reservations', 'list', page] as const,
  detail: (id: string) => ['reservations', 'detail', id] as const,
  rentals: (page: number) => ['rentals', page] as const,
  customer: ['customer'] as const,
}

const PAGE_SIZE = 10

export function useMyReservations(page: number) {
  return useQuery({
    queryKey: reservationKeys.list(page),
    queryFn: ({ signal }) => listMyReservations({ page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  })
}

export function useMyReservation(id: string | undefined) {
  return useQuery({
    queryKey: reservationKeys.detail(id ?? ''),
    queryFn: ({ signal }) => getMyReservation(id!, signal),
    enabled: Boolean(id),
  })
}

export function useMyRentals(page: number) {
  return useQuery({
    queryKey: reservationKeys.rentals(page),
    queryFn: ({ signal }) => listMyRentals({ page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  })
}

export function useMyCustomer() {
  return useQuery({
    queryKey: reservationKeys.customer,
    queryFn: ({ signal }) => getMyCustomer(signal),
  })
}

/** Reserves a vehicle for the signed-in customer. After success, availability and the reservation lists are refetched. */
export function useCreateReservation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ vehicleId, range }: { vehicleId: string; range: DateRange }) =>
      createReservation(vehicleId, range),
    onSuccess: async (reservation) => {
      queryClient.setQueryData(reservationKeys.detail(reservation.id), reservation)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: reservationKeys.all }),
        // The vehicle is no longer free for those dates: availability and quotes must not be served from the cache.
        queryClient.invalidateQueries({ queryKey: ['vehicles'] }),
      ])
    },
  })
}

export function useCancelReservation() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => cancelReservation(id),
    onSuccess: async (reservation: Reservation) => {
      queryClient.setQueryData(reservationKeys.detail(reservation.id), reservation)
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: reservationKeys.all }),
        // Cancelling frees the dates again.
        queryClient.invalidateQueries({ queryKey: ['vehicles'] }),
      ])
    },
  })
}

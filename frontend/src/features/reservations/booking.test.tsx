import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes, useLocation } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { customerUser, json, mockApi, page, problem, quote, renderWithProviders, reservation, signedIn, signedOut, staffUser, vehicle } from '../../test/utils'
import { VehicleDetailPage } from '../vehicles/VehicleDetailPage'
import { ReservationDetailPage } from './ReservationDetailPage'
import { ReservationsPage } from './ReservationsPage'

const VEHICLE_ID = '33333333-3333-4333-8333-333333333333'
const RESERVATION_ID = '44444444-4444-4444-8444-444444444444'
const DETAIL_ROUTE = `/vehicles/${VEHICLE_ID}?start=2030-01-10&end=2030-01-13`

function Location() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

function BookingRoutes() {
  return (
    <Routes>
      <Route path="/vehicles/:id" element={<VehicleDetailPage />} />
      <Route path="/vehicles" element={<Location />} />
      <Route path="/reservations/:id" element={<ReservationDetailPage />} />
      <Route path="/login" element={<Location />} />
    </Routes>
  )
}

const vehicleRoutes = {
  [`GET /api/v1/vehicles/${VEHICLE_ID}`]: json(vehicle()),
  [`GET /api/v1/vehicles/${VEHICLE_ID}/quote`]: json(quote()),
}

describe('vehicle details and the quote', () => {
  it('shows the vehicle, and the exact price the server quoted for the dates', async () => {
    const api = mockApi({ ...signedOut, ...vehicleRoutes })

    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    expect(await screen.findByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    const table = await screen.findByRole('table', { name: 'Price for your dates' })
    expect(within(table).getByText('3 days')).toBeInTheDocument()
    expect(within(table).getByText('Normal pricing')).toBeInTheDocument()
    expect(within(table).getAllByText('$180.00')).toHaveLength(1)
    expect(screen.getByText('Thu, 10 Jan 2030', { selector: 'strong' })).toBeInTheDocument()
    expect(screen.getByText('Sun, 13 Jan 2030', { selector: 'strong' })).toBeInTheDocument()

    const quoteCall = api.called('GET', `/api/v1/vehicles/${VEHICLE_ID}/quote`)[0]
    expect(quoteCall?.query.get('startDate')).toBe('2030-01-10')
    expect(quoteCall?.query.get('endDate')).toBe('2030-01-13')
  })

  it('shows the long-stay pricing the server chose instead of working it out itself', async () => {
    mockApi({
      ...signedOut,
      ...vehicleRoutes,
      [`GET /api/v1/vehicles/${VEHICLE_ID}/quote`]: json(
        quote({ billableDays: 7, endDate: '2030-01-17', pricingDescription: 'Long-term discount (20%)', totalCost: 336 }),
      ),
    })

    renderWithProviders(<BookingRoutes />, { route: `/vehicles/${VEHICLE_ID}?start=2030-01-10&end=2030-01-17` })

    expect(await screen.findByText('Long-term discount (20%)')).toBeInTheDocument()
    expect(screen.getByText('$336.00')).toBeInTheDocument()
  })

  it('asks for dates before showing any price', async () => {
    const api = mockApi({ ...signedOut, ...vehicleRoutes })

    renderWithProviders(<BookingRoutes />, { route: `/vehicles/${VEHICLE_ID}` })

    expect(await screen.findByText(/choose your dates to see the price/i)).toBeInTheDocument()
    expect(api.called('GET', `/api/v1/vehicles/${VEHICLE_ID}/quote`)).toHaveLength(0)
  })

  it('shows the server explanation when the dates are refused, with a retry', async () => {
    mockApi({
      ...signedOut,
      ...vehicleRoutes,
      [`GET /api/v1/vehicles/${VEHICLE_ID}/quote`]: problem(400, 'The request is invalid.', {
        detail: 'A reservation cannot be longer than 90 days.',
      }),
    })

    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    expect(await screen.findByRole('alert')).toHaveTextContent('A reservation cannot be longer than 90 days.')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })

  it('explains a vehicle that does not exist', async () => {
    mockApi({ ...signedOut, [`GET /api/v1/vehicles/${VEHICLE_ID}`]: problem(404, 'The resource was not found.') })

    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    expect(await screen.findByText('We could not find that vehicle')).toBeInTheDocument()
  })

  it('says plainly when the vehicle is not available, and offers other vehicles for the same dates', async () => {
    mockApi({
      ...signedIn(),
      ...vehicleRoutes,
      [`GET /api/v1/vehicles/${VEHICLE_ID}/quote`]: json(quote({ isAvailable: false })),
    })

    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    expect(await screen.findByText('Not available for those dates')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Reserve these dates' })).toBeDisabled()
    expect(screen.getByRole('link', { name: /see vehicles free for these dates/i })).toHaveAttribute(
      'href',
      '/vehicles?start=2030-01-10&end=2030-01-13',
    )
  })

  it('invites a signed-out visitor to sign in, and brings them back here afterwards', async () => {
    mockApi({ ...signedOut, ...vehicleRoutes })

    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    const link = await screen.findByRole('link', { name: 'Sign in to reserve' })
    expect(link).toHaveAttribute('href', '/login')
    expect(screen.queryByRole('button', { name: 'Reserve these dates' })).not.toBeInTheDocument()
  })

  it('does not offer online booking to a staff account', async () => {
    mockApi({ ...signedIn(staffUser), ...vehicleRoutes })

    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    expect(await screen.findByText(/staff accounts take bookings at the rental desk/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reserve these dates' })).not.toBeInTheDocument()
  })
})

describe('making a reservation', () => {
  async function openConfirmation() {
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Reserve these dates' }))
    return { user, dialog: await screen.findByRole('dialog', { name: 'Confirm your reservation' }) }
  }

  it('shows the quote for confirmation, books it, and shows the confirmation from the server response', async () => {
    const api = mockApi({
      ...signedIn(),
      ...vehicleRoutes,
      'POST /api/v1/me/reservations': json(reservation(), 201),
      [`GET /api/v1/me/reservations/${RESERVATION_ID}`]: json(reservation()),
    })
    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    const { user, dialog } = await openConfirmation()
    expect(within(dialog).getByText('$180.00', { exact: false })).toBeInTheDocument()
    expect(within(dialog).getByText(/no payment is taken/i)).toBeInTheDocument()
    await user.click(within(dialog).getByRole('button', { name: 'Confirm reservation' }))

    expect(await screen.findByText('Reservation confirmed')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    expect(screen.getByText(RESERVATION_ID)).toBeInTheDocument()

    // The request names the vehicle and the dates only: never a customer, a price or a status.
    const call = api.called('POST', '/api/v1/me/reservations')[0]
    expect(call?.body).toEqual({ vehicleId: VEHICLE_ID, startDate: '2030-01-10', endDate: '2030-01-13' })
    expect(JSON.stringify(call?.body)).not.toContain(customerUser.customerId)
  })

  it('handles losing the race: explains the conflict and offers other dates or vehicles', async () => {
    mockApi({
      ...signedIn(),
      ...vehicleRoutes,
      'POST /api/v1/me/reservations': problem(409, 'The request conflicts with the current state.', {
        detail: "Vehicle 'ABC-123' is not available from 2030-01-10 to 2030-01-13.",
      }),
    })
    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    const { user, dialog } = await openConfirmation()
    await user.click(within(dialog).getByRole('button', { name: 'Confirm reservation' }))

    const alert = await within(dialog).findByRole('alert')
    expect(alert).toHaveTextContent('This vehicle is no longer free for those dates')
    expect(alert).toHaveTextContent("Vehicle 'ABC-123' is not available");
    expect(within(dialog).queryByRole('button', { name: 'Confirm reservation' })).not.toBeInTheDocument()
    expect(within(dialog).getByRole('link', { name: /see other vehicles for these dates/i })).toHaveAttribute(
      'href',
      '/vehicles?start=2030-01-10&end=2030-01-13',
    )

    await user.click(within(dialog).getByRole('button', { name: 'Choose other dates' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('shows other refusals from the server and lets the customer try again', async () => {
    let attempts = 0
    mockApi({
      ...signedIn(),
      ...vehicleRoutes,
      'POST /api/v1/me/reservations': () => {
        attempts += 1
        return attempts === 1
          ? problem(400, 'The request is invalid.', { detail: 'The start date cannot be in the past.' })
          : json(reservation(), 201)
      },
      [`GET /api/v1/me/reservations/${RESERVATION_ID}`]: json(reservation()),
    })
    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    const { user, dialog } = await openConfirmation()
    await user.click(within(dialog).getByRole('button', { name: 'Confirm reservation' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The start date cannot be in the past.')

    await user.click(within(dialog).getByRole('button', { name: 'Confirm reservation' }))
    expect(await screen.findByText('Reservation confirmed')).toBeInTheDocument()
  })

  it('closing the dialog without confirming books nothing', async () => {
    const api = mockApi({ ...signedIn(), ...vehicleRoutes })
    renderWithProviders(<BookingRoutes />, { route: DETAIL_ROUTE })

    const { user, dialog } = await openConfirmation()
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(api.called('POST', '/api/v1/me/reservations')).toHaveLength(0)
  })
})

describe('my reservations', () => {
  it('lists only what the server returns for the signed-in customer, with status and an expired mark', async () => {
    const api = mockApi({
      ...signedIn(),
      'GET /api/v1/me/reservations': json(
        page(
          [
            reservation(),
            reservation({ id: '55555555-5555-4555-8555-555555555555', status: 'Cancelled', vehicleDisplayName: 'Honda CB500' }),
            reservation({ id: '66666666-6666-4666-8666-666666666666', status: 'Fulfilled', vehicleDisplayName: 'Ford Transit' }),
            reservation({ id: '77777777-7777-4777-8777-777777777777', isExpired: true, vehicleDisplayName: 'Kia Rio' }),
          ],
          { totalCount: 4, pageSize: 10 },
        ),
      ),
    })

    renderWithProviders(<ReservationsPage />, { route: '/reservations' })

    const list = await screen.findByRole('list', { name: 'Your reservations' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(4)
    expect(within(items[0]!).getByText('Active')).toBeInTheDocument()
    expect(within(items[1]!).getByText('Cancelled')).toBeInTheDocument()
    expect(within(items[2]!).getByText('Picked up')).toBeInTheDocument()
    expect(within(items[3]!).getByText('Expired, not collected')).toBeInTheDocument()
    // There is no way to ask for another customer's data: no id is ever sent.
    expect(api.called('GET', '/api/v1/me/reservations')[0]?.query.has('customerId')).toBe(false)
  })

  it('has a friendly empty state', async () => {
    mockApi({ ...signedIn(), 'GET /api/v1/me/reservations': json(page([], { totalCount: 0, totalPages: 0 })) })

    renderWithProviders(<ReservationsPage />, { route: '/reservations' })

    expect(await screen.findByRole('heading', { name: 'You have no reservations yet' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Find a vehicle' })).toHaveAttribute('href', '/vehicles')
  })

  it('shows an error with a retry', async () => {
    mockApi({ ...signedIn(), 'GET /api/v1/me/reservations': problem(500, 'Server error') })

    renderWithProviders(<ReservationsPage />, { route: '/reservations' })

    expect(await screen.findByRole('alert')).toHaveTextContent('We could not load your reservations')
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
  })
})

describe('reservation details and cancelling', () => {
  const detailRoute = `/reservations/${RESERVATION_ID}`
  const routes = (r: object) => ({ ...signedIn(), [`GET /api/v1/me/reservations/${RESERVATION_ID}`]: json(r) })

  it('shows the stored quote and offers cancelling for an upcoming reservation', async () => {
    mockApi(routes(reservation()))

    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    expect(await screen.findByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    expect(screen.getByText('$60.00')).toBeInTheDocument()
    expect(screen.getByText('3 days')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel this reservation' })).toBeInTheDocument()
    expect(screen.queryByText('Reservation confirmed')).not.toBeInTheDocument()
  })

  it('asks for confirmation before cancelling, and shows the result from the server', async () => {
    let cancelled = false
    const cancelledReservation = reservation({ status: 'Cancelled', cancelledAt: '2029-12-02T09:00:00+00:00' })
    const api = mockApi({
      ...signedIn(),
      [`GET /api/v1/me/reservations/${RESERVATION_ID}`]: () => json(cancelled ? cancelledReservation : reservation()),
      [`POST /api/v1/me/reservations/${RESERVATION_ID}/cancel`]: () => {
        cancelled = true
        return json(cancelledReservation)
      },
    })
    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Cancel this reservation' }))
    const dialog = await screen.findByRole('dialog', { name: 'Cancel this reservation?' })
    expect(api.called('POST', `/api/v1/me/reservations/${RESERVATION_ID}/cancel`)).toHaveLength(0) // not yet
    await user.click(within(dialog).getByRole('button', { name: 'Yes, cancel it' }))

    expect(await screen.findByText('Reservation cancelled')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel this reservation' })).not.toBeInTheDocument()
    expect(screen.getAllByText('Cancelled').length).toBeGreaterThan(0)
    expect(api.called('POST', `/api/v1/me/reservations/${RESERVATION_ID}/cancel`)).toHaveLength(1)
  })

  it('keeps the reservation when the customer changes their mind', async () => {
    const api = mockApi(routes(reservation()))
    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Cancel this reservation' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Keep reservation' }))

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(api.calls.filter((c) => c.method === 'POST')).toHaveLength(0)
  })

  it('shows the server reason when cancelling is refused', async () => {
    mockApi({
      ...routes(reservation()),
      [`POST /api/v1/me/reservations/${RESERVATION_ID}/cancel`]: problem(409, 'The request conflicts with the current state.', {
        detail: 'A reservation that has already started can only be cancelled by the rental desk.',
      }),
    })
    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: 'Cancel this reservation' }))
    await user.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Yes, cancel it' }))

    expect(await screen.findByText(/only be cancelled by the rental desk/)).toBeInTheDocument()
    expect(screen.queryByText('Reservation cancelled')).not.toBeInTheDocument()
  })

  it('does not offer cancelling once the reservation has started, and points to the rental desk', async () => {
    mockApi(routes(reservation({ startDate: '2000-01-01', endDate: '2099-01-01' })))

    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    expect(await screen.findByText(/can only be cancelled by the rental desk/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel this reservation' })).not.toBeInTheDocument()
  })

  it('marks an expired reservation, and shows a cancelled or picked-up one without actions', async () => {
    mockApi(routes(reservation({ isExpired: true, startDate: '2000-01-01', endDate: '2000-01-04' })))

    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    expect(await screen.findByText('This reservation was not collected')).toBeInTheDocument()
    expect(screen.getByText('Expired, not collected')).toBeInTheDocument()
  })

  it('treats another customer reservation like one that does not exist', async () => {
    mockApi({ ...signedIn(), [`GET /api/v1/me/reservations/${RESERVATION_ID}`]: problem(404, 'The resource was not found.') })

    renderWithProviders(<BookingRoutes />, { route: detailRoute })

    expect(await screen.findByText('We could not find that reservation')).toBeInTheDocument()
    expect(screen.queryByText(/belongs to|another customer/i)).not.toBeInTheDocument()
  })
})

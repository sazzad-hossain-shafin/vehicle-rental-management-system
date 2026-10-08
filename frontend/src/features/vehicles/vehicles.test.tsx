import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { page, json, mockApi, problem, renderWithProviders, signedOut, vehicle } from '../../test/utils'
import { DateRangeForm } from './DateRangeForm'
import { VehicleCard } from './VehicleCard'
import { VehiclesPage } from './VehiclesPage'
import { HomePage } from './HomePage'
import type { Vehicle } from '../../lib/api/types'

const car: Vehicle = { ...(vehicle() as unknown as Vehicle) }

describe('VehicleCard', () => {
  it('shows what the API knows about the vehicle, and nothing invented', () => {
    render(
      <MemoryRouter>
        <VehicleCard vehicle={car} />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    expect(screen.getByText('Car')).toBeInTheDocument()
    expect(screen.getByText('2022')).toBeInTheDocument()
    expect(screen.getByText('$60.00')).toBeInTheDocument()
    expect(screen.getByText('Available now')).toBeInTheDocument()
    expect(screen.queryByText(/review|rating|star/i)).not.toBeInTheDocument()
  })

  it('says when a vehicle is out right now', () => {
    render(
      <MemoryRouter>
        <VehicleCard vehicle={{ ...car, availabilityStatus: 'Rented' }} />
      </MemoryRouter>,
    )

    expect(screen.getByText('Out right now')).toBeInTheDocument()
  })

  it('links to the details, keeping the searched dates, with a descriptive link name', () => {
    render(
      <MemoryRouter>
        <VehicleCard vehicle={car} dates={{ startDate: '2030-01-10', endDate: '2030-01-13' }} />
      </MemoryRouter>,
    )

    const link = screen.getByRole('link', { name: /view details for toyota corolla/i })
    expect(link).toHaveAttribute('href', `/vehicles/${car.id}?start=2030-01-10&end=2030-01-13`)
    expect(screen.getByText('Free for your dates')).toBeInTheDocument()
  })
})

describe('DateRangeForm', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date(2026, 9, 9, 12, 0)) // 9 Oct 2026, local time
  })

  afterEachRealTimers()

  it('asks for both dates', async () => {
    const submit = vi.fn()
    render(<DateRangeForm submitLabel="Find" onSubmit={submit} />)

    await userEvent.click(screen.getByRole('button', { name: /find/i }))

    expect(screen.getByText('Choose a pickup date.')).toBeInTheDocument()
    expect(screen.getByText('Choose a return date.')).toBeInTheDocument()
    expect(submit).not.toHaveBeenCalled()
  })

  it('rejects a pickup in the past and a return that is not after the pickup', async () => {
    const submit = vi.fn()
    render(<DateRangeForm submitLabel="Find" onSubmit={submit} initial={{ startDate: '2026-10-01', endDate: '2026-10-05' }} />)
    await userEvent.click(screen.getByRole('button', { name: /find/i }))
    expect(screen.getByText('The pickup date cannot be in the past.')).toBeInTheDocument()

    const start = screen.getByLabelText('Pickup date')
    await userEvent.clear(start)
    await userEvent.type(start, '2026-10-20')
    await userEvent.click(screen.getByRole('button', { name: /find/i }))

    // Moving the pickup past the return pushes the return to the next day, so the form stays consistent.
    expect(screen.getByLabelText('Return date')).toHaveValue('2026-10-21')
    expect(submit).toHaveBeenCalledWith({ startDate: '2026-10-20', endDate: '2026-10-21' })
  })

  it('explains that the return day is not charged, and marks both fields required', () => {
    render(<DateRangeForm submitLabel="Find" onSubmit={() => undefined} />)

    expect(screen.getByLabelText('Return date')).toHaveAccessibleDescription('You are not charged for the return day.')
    expect(screen.getByLabelText('Pickup date')).toBeRequired()
    expect(screen.getByLabelText('Pickup date')).toHaveAttribute('min', '2026-10-09')
  })

  it('submits a valid period', async () => {
    const submit = vi.fn()
    render(<DateRangeForm submitLabel="Find" onSubmit={submit} initial={{ startDate: '2026-10-10', endDate: '2026-10-13' }} />)

    await userEvent.click(screen.getByRole('button', { name: /find/i }))

    expect(submit).toHaveBeenCalledWith({ startDate: '2026-10-10', endDate: '2026-10-13' })
  })
})

function afterEachRealTimers() {
  // Restores the real clock after each test in the surrounding describe.
  afterEach(() => {
    vi.useRealTimers()
  })
}

function LocationProbe() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

describe('VehiclesPage', () => {
  it('lists the vehicles from the server with the total', async () => {
    const api = mockApi({
      ...signedOut,
      'GET /api/v1/vehicles': json(page([vehicle(), vehicle({ id: '55555555-5555-4555-8555-555555555555', displayName: 'Honda CB500', vehicleType: 'Motorcycle' })], { totalCount: 2 })),
    })

    renderWithProviders(<VehiclesPage />, { route: '/vehicles' })

    expect(await screen.findByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Honda CB500' })).toBeInTheDocument()
    expect(screen.getByText('2 vehicles found')).toBeInTheDocument()
    expect(api.called('GET', '/api/v1/vehicles')[0]?.query.get('pageSize')).toBe('9')
  })

  it('sends the filters to the server instead of filtering the page itself', async () => {
    const api = mockApi({ ...signedOut, 'GET /api/v1/vehicles': json(page([vehicle()])) })

    renderWithProviders(<VehiclesPage />, { route: '/vehicles' })
    await screen.findByRole('heading', { name: 'Toyota Corolla' })
    await userEvent.selectOptions(screen.getByLabelText('Vehicle type'), 'Van')
    await userEvent.type(screen.getByLabelText('Maximum price per day'), '90')
    await userEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    await vi.waitFor(() => {
      const last = api.called('GET', '/api/v1/vehicles').at(-1)
      expect(last?.query.get('vehicleType')).toBe('Van')
      expect(last?.query.get('maxDailyRate')).toBe('90')
    })
  })

  it('uses the availability search when valid dates are chosen', async () => {
    const api = mockApi({
      ...signedOut,
      'GET /api/v1/vehicles/availability': json(page([vehicle()])),
    })

    renderWithProviders(<VehiclesPage />, { route: '/vehicles?start=2030-01-10&end=2030-01-13' })

    expect(await screen.findByRole('heading', { name: 'Vehicles free for your dates' })).toBeInTheDocument()
    expect(await screen.findByText('Free for your dates')).toBeInTheDocument()
    const call = api.called('GET', '/api/v1/vehicles/availability')[0]
    expect(call?.query.get('startDate')).toBe('2030-01-10')
    expect(call?.query.get('endDate')).toBe('2030-01-13')
    expect(api.called('GET', '/api/v1/vehicles')).toHaveLength(0)
  })

  it('falls back to the whole fleet, and says so, when the dates in the address are not a valid period', async () => {
    mockApi({ ...signedOut, 'GET /api/v1/vehicles': json(page([vehicle()])) })

    renderWithProviders(<VehiclesPage />, { route: '/vehicles?start=2030-01-13&end=2030-01-10' })

    expect(await screen.findByText(/not a valid period/i)).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Our vehicles' })).toBeInTheDocument()
  })

  it('shows an empty state with a way back when nothing is free', async () => {
    mockApi({ ...signedOut, 'GET /api/v1/vehicles/availability': json(page([], { totalCount: 0, totalPages: 0 })) })

    renderWithProviders(<VehiclesPage />, { route: '/vehicles?start=2030-01-10&end=2030-01-13' })

    expect(await screen.findByRole('heading', { name: 'No vehicles are free for those dates' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Show all vehicles' })).toHaveLength(2) // one beside the dates, one here
  })

  it('shows an error with a working retry', async () => {
    let fail = true
    mockApi({
      ...signedOut,
      'GET /api/v1/vehicles': () => (fail ? problem(500, 'Server error') : json(page([vehicle()]))),
    })

    renderWithProviders(<VehiclesPage />, { route: '/vehicles' })
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('We could not load the vehicles')

    fail = false
    await userEvent.click(within(alert).getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
  })

  it('pages through the server results', async () => {
    const api = mockApi({
      ...signedOut,
      'GET /api/v1/vehicles': (call) =>
        json(page([vehicle()], { page: Number(call.query.get('page') ?? 1), totalCount: 20, totalPages: 3 })),
    })

    renderWithProviders(
      <>
        <VehiclesPage />
        <LocationProbe />
      </>,
      { route: '/vehicles' },
    )
    await screen.findByText('Page 1 of 3')
    await userEvent.click(screen.getByRole('button', { name: /next/i }))

    expect(await screen.findByText('Page 2 of 3')).toBeInTheDocument()
    expect(screen.getByTestId('location')).toHaveTextContent('/vehicles?page=2')
    expect(api.called('GET', '/api/v1/vehicles').at(-1)?.query.get('page')).toBe('2')
  })
})

describe('HomePage', () => {
  it('takes the chosen dates to the availability search', async () => {
    mockApi({ ...signedOut, 'GET /api/v1/vehicles': json(page([vehicle()])) })

    renderWithProviders(
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/vehicles" element={<LocationProbe />} />
      </Routes>,
    )
    await userEvent.type(screen.getByLabelText('Pickup date'), '2030-01-10')
    await userEvent.type(screen.getByLabelText('Return date'), '2030-01-13')
    await userEvent.click(screen.getByRole('button', { name: /find vehicles/i }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/vehicles?start=2030-01-10&end=2030-01-13')
  })

  it('shows vehicles from the API, and does not make up claims', async () => {
    mockApi({ ...signedOut, 'GET /api/v1/vehicles': json(page([vehicle()])) })

    renderWithProviders(<HomePage />)

    expect(await screen.findByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'From the fleet' })).toBeInTheDocument()
    expect(screen.queryByText(/\d+\+? (happy )?customers|locations|5-star|reviews/i)).not.toBeInTheDocument()
  })
})

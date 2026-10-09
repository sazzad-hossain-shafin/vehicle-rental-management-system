import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DateRangeForm } from '../features/vehicles/DateRangeForm'
import { customerUser, json, mockApi, page, renderWithProviders, reservation, signedIn, signedOut } from '../test/utils'
import { AppRoutes } from './routes'

describe('application shell', () => {
  it('keeps the header, the main content and the footer in one full-height column, footer last', async () => {
    mockApi({ ...signedOut, 'GET /api/v1/vehicles': json(page([])) })
    const { container } = renderWithProviders(<AppRoutes />)

    const shell = container.querySelector('.app-shell')
    expect(shell).not.toBeNull()
    const tags = Array.from(shell?.children ?? []).map((child) => child.tagName)
    // The skip link comes first for keyboard users, then the page structure.
    expect(tags).toEqual(['A', 'HEADER', 'MAIN', 'FOOTER'])
    expect(await screen.findByRole('heading', { level: 1 })).toBeInTheDocument()
  })
})

describe('readable dates', () => {
  it('shows the chosen period in words, because the native date boxes follow the browser locale', () => {
    render(<DateRangeForm submitLabel="Find" onSubmit={() => undefined} initial={{ startDate: '2030-10-09', endDate: '2030-10-12' }} />)

    expect(screen.getByRole('status')).toHaveTextContent('9 Oct 2030 → 12 Oct 2030')
  })

  it('shows nothing until both dates make a valid period, and can be switched off', () => {
    const { rerender } = render(<DateRangeForm submitLabel="Find" onSubmit={() => undefined} />)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()

    rerender(
      <DateRangeForm
        submitLabel="Find"
        onSubmit={() => undefined}
        summary={false}
        initial={{ startDate: '2030-10-09', endDate: '2030-10-12' }}
      />,
    )
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })
})

describe('my reservations (cards)', () => {
  it('shows the vehicle, status, readable pickup and return dates, the total and a details link', async () => {
    mockApi({
      ...signedIn(),
      'GET /api/v1/me/reservations': json(page([reservation()])),
    })
    renderWithProviders(<AppRoutes />, { route: '/reservations' })

    const list = await screen.findByRole('list', { name: 'Your reservations' })
    const card = within(list).getByRole('listitem')
    expect(within(card).getByRole('heading', { name: 'Toyota Corolla' })).toBeInTheDocument()
    expect(within(card).getByText('Active')).toBeInTheDocument()
    expect(within(card).getByText('10 Jan 2030')).toBeInTheDocument()
    expect(within(card).getByText('13 Jan 2030')).toBeInTheDocument()
    expect(within(card).getByText('$180.00')).toBeInTheDocument()
    expect(within(card).getByRole('link', { name: 'View details of the Toyota Corolla reservation' })).toBeInTheDocument()
  })
})

describe('my account', () => {
  it('shows the profile and takes the summary counts from the API totals', async () => {
    mockApi({
      ...signedIn(),
      'GET /api/v1/me/customer': json({ id: customerUser.customerId, customerNumber: 'WEB-1', name: 'Casey Jones' }),
      'GET /api/v1/me/rentals': json(page([])),
      'GET /api/v1/me/reservations': json(page([reservation(), reservation({ id: '55555555-5555-4555-8555-555555555555' })])),
    })
    renderWithProviders(<AppRoutes />, { route: '/account' })

    expect(await screen.findByText('Casey Jones')).toBeInTheDocument()
    expect(screen.getByText('WEB-1')).toBeInTheDocument()
    expect(screen.getByText('CJ')).toBeInTheDocument()

    const group = await screen.findByLabelText('Summary')
    expect(within(group).getByText('Rentals').nextElementSibling).toHaveTextContent('0')
    expect(within(group).getByText('Reservations').nextElementSibling).toHaveTextContent('2')

    const shortcuts = screen.getByRole('navigation', { name: 'Account shortcuts' })
    expect(within(shortcuts).getByRole('link', { name: /My reservations/ })).toHaveAttribute('href', '/reservations')
  })
})

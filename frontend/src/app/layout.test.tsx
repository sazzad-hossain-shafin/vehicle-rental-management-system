import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { json, mockApi, page, renderWithProviders, signedIn, signedOut, staffUser } from '../test/utils'
import { AppRoutes } from './routes'
import { Layout } from './Layout'

const fleet = { 'GET /api/v1/vehicles': json(page([])) }

describe('navigation', () => {
  it('has the standard landmarks, a skip link, and the main navigation', async () => {
    mockApi({ ...signedOut, ...fleet })
    renderWithProviders(<AppRoutes />)

    expect(screen.getByRole('link', { name: 'Skip to main content' })).toHaveAttribute('href', '#main')
    expect(screen.getByRole('banner')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Main' })).toBeInTheDocument()
    expect(screen.getByRole('main')).toBeInTheDocument()
    expect(screen.getByRole('contentinfo')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { level: 1 })).toBeInTheDocument()
  })

  it('shows sign-in and registration to visitors, and the customer links once signed in', async () => {
    mockApi({ ...signedOut, ...fleet })
    const { unmount } = renderWithProviders(<AppRoutes />)

    const nav = screen.getByRole('navigation', { name: 'Main' })
    expect(await within(nav).findByRole('link', { name: 'Sign in' })).toBeInTheDocument()
    expect(within(nav).getByRole('link', { name: 'Create account' })).toBeInTheDocument()
    expect(within(nav).queryByRole('link', { name: 'My reservations' })).not.toBeInTheDocument()
    unmount()

    mockApi({ ...signedIn(), ...fleet })
    renderWithProviders(<AppRoutes />)

    const signedInNav = screen.getByRole('navigation', { name: 'Main' })
    expect(await within(signedInNav).findByRole('link', { name: 'My reservations' })).toBeInTheDocument()
    expect(within(signedInNav).getByRole('button', { name: /sign out/i })).toBeInTheDocument()
    expect(within(signedInNav).queryByRole('link', { name: 'Sign in' })).not.toBeInTheDocument()
  })

  it('does not hand a staff account any staff or admin controls on the customer website', async () => {
    mockApi({ ...signedIn(staffUser), ...fleet })
    renderWithProviders(<AppRoutes />)

    const nav = screen.getByRole('navigation', { name: 'Main' })
    await within(nav).findByRole('button', { name: /sign out/i })
    expect(screen.queryByText(/admin|pickup|desk booking|discount/i, { selector: 'nav *, button' })).not.toBeInTheDocument()
  })

  it('opens and closes the mobile menu with correct state for assistive technology', async () => {
    mockApi({ ...signedOut, ...fleet })
    renderWithProviders(
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<p>Page</p>} />
          <Route path="vehicles" element={<p>Vehicles page</p>} />
        </Route>
      </Routes>,
    )
    const toggle = screen.getByRole('button', { name: 'Open menu' })

    expect(toggle).toHaveAttribute('aria-expanded', 'false')
    expect(toggle).toHaveAttribute('aria-controls', 'main-nav')
    await userEvent.click(toggle)

    expect(screen.getByRole('button', { name: 'Close menu' })).toHaveAttribute('aria-expanded', 'true')
    expect(document.getElementById('main-nav')).toHaveAttribute('data-open', 'true')

    await userEvent.click(await screen.findByRole('link', { name: 'Vehicles' }))
    expect(screen.getByRole('button', { name: 'Open menu' })).toHaveAttribute('aria-expanded', 'false')
  })

  it('shows a helpful page for an address that does not exist', async () => {
    mockApi({ ...signedOut, ...fleet })
    renderWithProviders(<AppRoutes />, { route: '/nothing-here' })

    expect(await screen.findByRole('heading', { name: 'We could not find that page' })).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Go to the home page' })).toHaveAttribute('href', '/')
  })
})

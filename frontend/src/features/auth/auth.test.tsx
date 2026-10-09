import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Route, Routes, useLocation } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { Layout } from '../../app/Layout'
import { request } from '../../lib/api/client'
import { customerUser, json, mockApi, noContent, problem, renderWithProviders, signedIn, signedOut } from '../../test/utils'
import { LoginPage } from './LoginPage'
import { RegisterPage } from './RegisterPage'
import { RequireAuth } from './RequireAuth'
import { useAuth } from './useAuth'

function Probe() {
  const location = useLocation()
  return <p data-testid="location">{`${location.pathname}${location.search}`}</p>
}

function Status() {
  const { status, user, sessionExpired } = useAuth()
  return <p data-testid="status">{`${status}|${user?.email ?? 'nobody'}|${sessionExpired ? 'expired' : 'ok'}`}</p>
}

describe('LoginPage', () => {
  it('asks for the missing fields before calling the API', async () => {
    const api = mockApi({ ...signedOut })
    renderWithProviders(<LoginPage />, { route: '/login' })

    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(screen.getByText('Enter your email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(api.called('POST', '/api/v1/auth/session')).toHaveLength(0)
  })

  it('signs in, sends the credentials only to the session endpoint, and goes to the reservations', async () => {
    const api = mockApi({
      ...signedOut,
      'POST /api/v1/auth/session': json({ expiresAtUtc: '2030-01-01T00:00:00Z', user: customerUser }),
    })
    renderWithProviders(
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/reservations" element={<Probe />} />
      </Routes>,
      { route: '/login' },
    )

    await userEvent.type(screen.getByLabelText('Email'), ' casey@example.test ')
    await userEvent.type(screen.getByLabelText('Password'), 'Correct-horse-1')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/reservations')
    const call = api.called('POST', '/api/v1/auth/session')[0]
    expect(call?.body).toEqual({ email: 'casey@example.test', password: 'Correct-horse-1' })
    expect(call?.headers['X-Requested-With']).toBe('VehicleRentalWeb')
    // The page never stores the password or any token: there is no token in the response to keep.
    expect(JSON.stringify({ ...localStorage })).not.toContain('Correct-horse-1')
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('Correct-horse-1')
  })

  it('shows a generic message for a wrong password, without calling it an expired session', async () => {
    mockApi({ ...signedOut, 'POST /api/v1/auth/session': problem(401, 'Authentication failed.') })
    renderWithProviders(
      <>
        <LoginPage />
        <Status />
      </>,
      { route: '/login' },
    )

    await userEvent.type(screen.getByLabelText('Email'), 'casey@example.test')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The email or password is incorrect.')
    expect(screen.getByTestId('status')).toHaveTextContent('anonymous|nobody|ok')
  })

  it('only follows in-site paths after signing in', async () => {
    mockApi({
      ...signedOut,
      'POST /api/v1/auth/session': json({ expiresAtUtc: '2030-01-01T00:00:00Z', user: customerUser }),
    })
    renderWithProviders(
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/reservations" element={<Probe />} />
      </Routes>,
      { route: '/login' },
    )
    // (react-router state is not reachable from a URL, but an unsafe value must still be ignored)
    await userEvent.type(screen.getByLabelText('Email'), 'casey@example.test')
    await userEvent.type(screen.getByLabelText('Password'), 'Correct-horse-1')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/reservations')
  })
})

describe('RegisterPage', () => {
  it('validates before calling the API', async () => {
    const api = mockApi({ ...signedOut })
    renderWithProviders(<RegisterPage />, { route: '/register' })

    await userEvent.type(screen.getByLabelText('Email'), 'not-an-email')
    await userEvent.type(screen.getByLabelText('Password'), 'short')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(screen.getByText('Enter your name.')).toBeInTheDocument()
    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByText('Use at least 10 characters.')).toBeInTheDocument()
    expect(api.called('POST', '/api/v1/auth/register')).toHaveLength(0)
  })

  it('shows the server field errors next to the fields', async () => {
    mockApi({
      ...signedOut,
      'POST /api/v1/auth/register': problem(400, 'One or more validation errors occurred.', {
        errors: { password: ['The password needs a digit.'] },
      }),
    })
    renderWithProviders(<RegisterPage />, { route: '/register' })

    await userEvent.type(screen.getByLabelText('Full name'), 'Casey')
    await userEvent.type(screen.getByLabelText('Email'), 'casey@example.test')
    await userEvent.type(screen.getByLabelText('Password'), 'NoDigitsHereAtAll')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    const password = await screen.findByLabelText('Password')
    await waitFor(() => expect(password).toHaveAccessibleDescription(/needs a digit/))
    expect(password).toHaveAttribute('aria-invalid', 'true')
  })

  it('explains an email that is already registered', async () => {
    mockApi({
      ...signedOut,
      'POST /api/v1/auth/register': problem(409, 'The request conflicts with the current state.', {
        detail: 'An account with that email already exists.',
      }),
    })
    renderWithProviders(<RegisterPage />, { route: '/register' })

    await userEvent.type(screen.getByLabelText('Full name'), 'Casey')
    await userEvent.type(screen.getByLabelText('Email'), 'casey@example.test')
    await userEvent.type(screen.getByLabelText('Password'), 'Correct-horse-1')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('An account with that email already exists.')
  })

  it('registers and signs the new customer in', async () => {
    const api = mockApi({
      ...signedOut,
      'POST /api/v1/auth/register': json(customerUser, 201),
      'POST /api/v1/auth/session': json({ expiresAtUtc: '2030-01-01T00:00:00Z', user: customerUser }),
    })
    renderWithProviders(
      <Routes>
        <Route path="/register" element={<RegisterPage />} />
        <Route path="/reservations" element={<Probe />} />
      </Routes>,
      { route: '/register' },
    )

    await userEvent.type(screen.getByLabelText('Full name'), 'Casey')
    await userEvent.type(screen.getByLabelText('Email'), 'casey@example.test')
    await userEvent.type(screen.getByLabelText('Password'), 'Correct-horse-1')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))

    expect(await screen.findByTestId('location')).toHaveTextContent('/reservations')
    // No role or customer id can be chosen: the body holds exactly these three fields.
    expect(api.called('POST', '/api/v1/auth/register')[0]?.body).toEqual({
      name: 'Casey',
      email: 'casey@example.test',
      password: 'Correct-horse-1',
    })
  })
})

describe('RequireAuth', () => {
  function Protected() {
    return (
      <Routes>
        <Route path="/login" element={<Probe />} />
        <Route element={<RequireAuth />}>
          <Route path="/reservations" element={<p>Private page</p>} />
        </Route>
      </Routes>
    )
  }

  it('sends a signed-out visitor to the sign-in page', async () => {
    mockApi({ ...signedOut })
    renderWithProviders(<Protected />, { route: '/reservations' })

    expect(await screen.findByTestId('location')).toHaveTextContent('/login')
    expect(screen.queryByText('Private page')).not.toBeInTheDocument()
  })

  it('shows a loading state while it checks the session, then the page', async () => {
    mockApi({ ...signedIn() })
    renderWithProviders(<Protected />, { route: '/reservations' })

    expect(screen.getByRole('status')).toHaveTextContent(/checking your sign-in/i)
    expect(await screen.findByText('Private page')).toBeInTheDocument()
  })
})

describe('session handling', () => {
  const SECRET = ['reservations', 'list', 1]

  it('restores a session after a reload from the API, not from stored data', async () => {
    const api = mockApi({ ...signedIn() })
    renderWithProviders(<Status />)

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated|casey@example.test|ok'))
    expect(api.called('GET', '/api/v1/me')).toHaveLength(1)
  })

  it('signing out ends the session on the server and clears every cached customer record', async () => {
    const api = mockApi({
      ...signedIn(),
      'DELETE /api/v1/auth/session': noContent(),
      'GET /api/v1/vehicles': json({ items: [], page: 1, pageSize: 3, totalCount: 0, totalPages: 0 }),
    })
    const { queryClient } = renderWithProviders(
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Status />} />
        </Route>
      </Routes>,
    )

    const signOut = await screen.findByRole('button', { name: /sign out/i })
    queryClient.setQueryData(SECRET, { secret: 'previous customer data' })
    await userEvent.click(signOut)

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous|nobody|ok'))
    expect(api.called('DELETE', '/api/v1/auth/session')[0]?.headers['X-Requested-With']).toBe('VehicleRentalWeb')
    expect(queryClient.getQueryData(SECRET)).toBeUndefined()
  })

  it('stays signed in, and says so, if the server could not be told to end the session', async () => {
    mockApi({
      ...signedIn(),
      'DELETE /api/v1/auth/session': problem(503, 'Unavailable'),
      'GET /api/v1/vehicles': json({ items: [], page: 1, pageSize: 3, totalCount: 0, totalPages: 0 }),
    })
    renderWithProviders(
      <Routes>
        <Route element={<Layout />}>
          <Route index element={<Status />} />
        </Route>
      </Routes>,
    )

    await userEvent.click(await screen.findByRole('button', { name: /sign out/i }))

    expect(await screen.findByText('We could not sign you out')).toBeInTheDocument()
    expect(screen.getByTestId('status')).toHaveTextContent('authenticated|casey@example.test|ok')
  })

  it('treats a 401 while signed in as an ended session: signs out and clears the cache', async () => {
    mockApi({ ...signedIn(), 'GET /api/v1/me/reservations': problem(401, 'Unauthorized') })

    function Load() {
      return <button onClick={() => void request('GET', '/me/reservations').catch(() => undefined)}>Load</button>
    }
    const { queryClient } = renderWithProviders(
      <>
        <Status />
        <Load />
      </>,
    )
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    queryClient.setQueryData(SECRET, { secret: 'previous customer data' })

    await userEvent.click(screen.getByRole('button', { name: 'Load' }))

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous|nobody|expired'))
    expect(queryClient.getQueryData(SECRET)).toBeUndefined()
  })

  it('shows the expired-session notice on the sign-in page, which can be dismissed', async () => {
    mockApi({ ...signedIn(), 'GET /api/v1/me/rentals': problem(401, 'Unauthorized') })

    function Expire() {
      const { sessionExpired } = useAuth()
      return (
        <>
          <button onClick={() => void request('GET', '/me/rentals').catch(() => undefined)}>Expire</button>
          <Status />
          {sessionExpired && <LoginPage />}
        </>
      )
    }
    renderWithProviders(<Expire />)
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))

    await userEvent.click(screen.getByRole('button', { name: 'Expire' }))

    expect(await screen.findByText('Your session ended')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Dismiss' }))
    expect(screen.queryByText('Your session ended')).not.toBeInTheDocument()
  })

  it('does not show one customer data to the next account that signs in on the same page', async () => {
    mockApi({
      ...signedOut,
      'POST /api/v1/auth/session': json({ expiresAtUtc: '2030-01-01T00:00:00Z', user: customerUser }),
    })
    function SignIn() {
      const { signIn } = useAuth()
      return <button onClick={() => void signIn({ email: 'a@example.test', password: 'x' })}>Go</button>
    }
    const { queryClient } = renderWithProviders(
      <>
        <Status />
        <SignIn />
      </>,
    )
    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('anonymous'))
    queryClient.setQueryData(SECRET, { secret: 'left over from before' })

    await userEvent.click(screen.getByRole('button', { name: 'Go' }))

    await waitFor(() => expect(screen.getByTestId('status')).toHaveTextContent('authenticated'))
    expect(queryClient.getQueryData(SECRET)).toBeUndefined()
  })
})

import { LogIn } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { TextField } from '../../components/ui/Field'
import { ApiError } from '../../lib/api/errors'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { useAuth } from './useAuth'

interface FromState {
  from?: string
}

/** Only follow in-site paths after signing in, never an address from elsewhere. */
function safeDestination(from: string | undefined): string {
  return from && from.startsWith('/') && !from.startsWith('//') ? from : '/reservations'
}

export function LoginPage() {
  useDocumentTitle('Sign in')
  const { signIn, status, sessionExpired, dismissSessionExpired } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as FromState | null)?.from

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<{ email?: string; password?: string }>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (status === 'authenticated') {
    return <Navigate to={safeDestination(from)} replace />
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setFormError(null)

    const next: { email?: string; password?: string } = {}
    if (!email.trim()) next.email = 'Enter your email address.'
    if (!password) next.password = 'Enter your password.'
    setErrors(next)
    if (next.email || next.password) return

    setSubmitting(true)
    try {
      await signIn({ email: email.trim(), password })
      void navigate(safeDestination(from), { replace: true })
    } catch (error) {
      setFormError(
        error instanceof ApiError && error.isUnauthorized
          ? 'The email or password is incorrect.'
          : error instanceof ApiError
            ? error.userMessage
            : 'Sign-in failed. Please try again.',
      )
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="container auth-page">
      <div className="card auth-card">
        <form className="card-body form-grid" onSubmit={handleSubmit} noValidate aria-labelledby="login-title">
          <span className="auth-icon">
            <LogIn size={22} aria-hidden="true" />
          </span>
          <div>
            <h1 id="login-title">Sign in</h1>
            <p className="muted">Welcome back. Sign in to reserve vehicles and manage your reservations.</p>
          </div>

          {sessionExpired && (
            <Alert
              tone="warning"
              title="Your session ended"
              actions={
                <Button variant="secondary" onClick={dismissSessionExpired}>
                  Dismiss
                </Button>
              }
            >
              <p>For your security, sessions end after a while. Please sign in again to continue.</p>
            </Alert>
          )}
          {formError && (
            <Alert tone="error" title="We could not sign you in">
              <p>{formError}</p>
            </Alert>
          )}

          <TextField
            label="Email"
            type="email"
            name="email"
            autoComplete="username"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            error={errors.email}
          />
          <TextField
            label="Password"
            type="password"
            name="password"
            autoComplete="current-password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            error={errors.password}
          />

          <Button type="submit" loading={submitting} block>
            Sign in
          </Button>
          <p className="muted auth-switch">
            New here? <Link to="/register">Create an account</Link>
          </p>
        </form>
      </div>
    </div>
  )
}

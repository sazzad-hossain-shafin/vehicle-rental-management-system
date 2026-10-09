import { UserPlus } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, Navigate, useLocation, useNavigate } from 'react-router-dom'
import { Alert } from '../../components/ui/Alert'
import { Button } from '../../components/ui/Button'
import { TextField } from '../../components/ui/Field'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { ApiError } from '../../lib/api/errors'
import { fieldError, hasUnmappedError } from '../../lib/formErrors'
import { useAuth } from './useAuth'

interface FromState {
  from?: string
}

const FORM_FIELDS = ['name', 'email', 'password'] as const

export function RegisterPage() {
  useDocumentTitle('Create an account')
  const { register, status } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const from = (location.state as FromState | null)?.from
  const destination = from && from.startsWith('/') && !from.startsWith('//') ? from : '/reservations'

  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<{ name?: string; email?: string; password?: string }>({})
  const [serverError, setServerError] = useState<unknown>(null)
  const [submitting, setSubmitting] = useState(false)

  if (status === 'authenticated') {
    return <Navigate to={destination} replace />
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setServerError(null)

    const next: typeof errors = {}
    if (!name.trim()) next.name = 'Enter your name.'
    if (!email.trim()) next.email = 'Enter your email address.'
    else if (!/^\S+@\S+\.\S+$/.test(email.trim())) next.email = 'Enter a valid email address.'
    if (!password) next.password = 'Choose a password.'
    else if (password.length < 10) next.password = 'Use at least 10 characters.'
    setErrors(next)
    if (Object.keys(next).length > 0) return

    setSubmitting(true)
    try {
      await register({ name: name.trim(), email: email.trim(), password })
      void navigate(destination, { replace: true })
    } catch (error) {
      setServerError(error)
    } finally {
      setSubmitting(false)
    }
  }

  const showGeneral = serverError !== null && hasUnmappedError(serverError, FORM_FIELDS)

  return (
    <div className="container auth-page">
      <div className="card auth-card">
        <form className="card-body form-grid" onSubmit={handleSubmit} noValidate aria-labelledby="register-title">
          <span className="auth-icon">
            <UserPlus size={22} aria-hidden="true" />
          </span>
          <div>
            <h1 id="register-title">Create an account</h1>
            <p className="muted">An account lets you reserve vehicles and manage your reservations.</p>
          </div>

          {showGeneral && (
            <Alert tone="error" title="We could not create your account">
              <p>{serverError instanceof ApiError ? serverError.userMessage : 'Please try again.'}</p>
            </Alert>
          )}

          <TextField
            label="Full name"
            name="name"
            autoComplete="name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            error={errors.name ?? fieldError(serverError, 'name')}
          />
          <TextField
            label="Email"
            type="email"
            name="email"
            autoComplete="email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            error={errors.email ?? fieldError(serverError, 'email')}
          />
          <TextField
            label="Password"
            type="password"
            name="password"
            autoComplete="new-password"
            hint="At least 10 characters, with an upper-case letter, a lower-case letter and a digit."
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            error={errors.password ?? fieldError(serverError, 'password')}
          />

          <Button type="submit" loading={submitting} block>
            Create account
          </Button>
          <p className="muted auth-switch">
            Already registered? <Link to="/login">Sign in</Link>
          </p>
        </form>
      </div>
    </div>
  )
}

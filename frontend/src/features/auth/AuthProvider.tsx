import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { onUnauthorized } from '../../lib/api/client'
import { endSession, getMe, register as registerAccount, startSession } from '../../lib/api/endpoints'
import { ApiError } from '../../lib/api/errors'
import type { Credentials, Registration, User } from '../../lib/api/types'
import { AuthContext, type AuthContextValue, type AuthStatus } from './auth-context'

/**
 * Who is signed in. The session itself is an HttpOnly cookie that only the browser and the server can see; this
 * provider only knows the account details the API returns, kept in memory. After a page reload it asks the API
 * ("/me") whether the cookie still identifies someone.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<AuthStatus>('loading')
  const [user, setUser] = useState<User | null>(null)
  const [sessionExpired, setSessionExpired] = useState(false)
  const signedIn = useRef(false)

  const endLocally = useCallback(
    (expired: boolean) => {
      signedIn.current = false
      setUser(null)
      setStatus('anonymous')
      setSessionExpired(expired)
      // Nothing from the previous account may stay visible, or be shown to the next one.
      queryClient.clear()
    },
    [queryClient],
  )

  // Restore the session after a reload.
  useEffect(() => {
    const controller = new AbortController()

    getMe(controller.signal)
      .then((me) => {
        signedIn.current = true
        setUser(me)
        setStatus('authenticated')
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        // Not signed in (401) or the API could not be reached: either way, show the public site.
        if (!(error instanceof ApiError)) console.error('Unexpected error while restoring the session')
        setStatus('anonymous')
      })

    return () => controller.abort()
  }, [])

  // A 401 while signed in means the session ended (the token lasts 30 minutes). A 401 while signed out (a wrong
  // password, for example) is ordinary and must not look like an expired session.
  useEffect(
    () =>
      onUnauthorized(() => {
        if (signedIn.current) endLocally(true)
      }),
    [endLocally],
  )

  const signIn = useCallback(
    async (credentials: Credentials) => {
      const session = await startSession(credentials)
      queryClient.clear()
      signedIn.current = true
      setUser(session.user)
      setStatus('authenticated')
      setSessionExpired(false)
      return session.user
    },
    [queryClient],
  )

  const register = useCallback(
    async (details: Registration) => {
      await registerAccount(details)
      return signIn({ email: details.email, password: details.password })
    },
    [signIn],
  )

  const signOut = useCallback(async () => {
    await endSession()
    endLocally(false)
  }, [endLocally])

  const value = useMemo<AuthContextValue>(
    () => ({
      status,
      user,
      sessionExpired,
      dismissSessionExpired: () => setSessionExpired(false),
      signIn,
      register,
      signOut,
    }),
    [status, user, sessionExpired, signIn, register, signOut],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

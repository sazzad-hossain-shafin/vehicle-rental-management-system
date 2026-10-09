import { createContext } from 'react'
import type { Credentials, Registration, User } from '../../lib/api/types'

export type AuthStatus = 'loading' | 'authenticated' | 'anonymous'

export interface AuthContextValue {
  status: AuthStatus
  user: User | null
  /** True after a signed-in session ended on the server (expired), until the person signs in again or dismisses it. */
  sessionExpired: boolean
  dismissSessionExpired: () => void
  signIn: (credentials: Credentials) => Promise<User>
  register: (details: Registration) => Promise<User>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

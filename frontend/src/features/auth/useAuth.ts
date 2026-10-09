import { useContext } from 'react'
import { AuthContext, type AuthContextValue } from './auth-context'

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside <AuthProvider>')
  return value
}

/** Customer accounts can make reservations. Staff and admin accounts work at the rental desk instead. */
export function isCustomer(user: { roles: string[] } | null): boolean {
  return user?.roles.includes('Customer') ?? false
}

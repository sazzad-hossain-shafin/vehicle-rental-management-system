import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { LoadingBlock } from '../../components/ui/States'
import { useAuth } from './useAuth'

/**
 * Guards the pages of the signed-in area. This only decides what to show: the API enforces access itself, so a
 * customer can never read another customer's data by getting past this screen.
 */
export function RequireAuth() {
  const { status } = useAuth()
  const location = useLocation()

  if (status === 'loading') {
    return (
      <div className="container page">
        <LoadingBlock label="Checking your sign-in" />
      </div>
    )
  }

  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: `${location.pathname}${location.search}` }} />
  }

  return <Outlet />
}

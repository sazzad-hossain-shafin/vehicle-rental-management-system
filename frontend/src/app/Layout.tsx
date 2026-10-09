import { CarFront, LogOut, Menu, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { Alert } from '../components/ui/Alert'
import { Button } from '../components/ui/Button'
import { useAuth } from '../features/auth/useAuth'

export function Layout() {
  const { status, user, signOut } = useAuth()
  const location = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const [signOutError, setSignOutError] = useState(false)

  // After navigating, move focus to the page content so keyboard and screen-reader users start at the top of it.
  useEffect(() => {
    document.getElementById('main')?.focus({ preventScroll: true })
  }, [location.pathname])

  async function handleSignOut() {
    setSignOutError(false)
    try {
      await signOut()
    } catch {
      setSignOutError(true)
    }
  }

  return (
    <div className="app-shell">
      <a className="skip-link" href="#main">
        Skip to main content
      </a>

      <header className="site-header">
        <div className="container">
          <div className="header-wrap">
            <div className="header-bar">
              <Link to="/" className="brand">
                <span className="brand-mark" aria-hidden="true">
                  <CarFront size={20} />
                </span>
                Vehicle Rental
              </Link>
              <Button
                variant="ghost"
                className="nav-toggle"
                aria-expanded={menuOpen}
                aria-controls="main-nav"
                onClick={() => setMenuOpen((open) => !open)}
              >
                {menuOpen ? <X size={22} aria-hidden="true" /> : <Menu size={22} aria-hidden="true" />}
                <span className="visually-hidden">{menuOpen ? 'Close menu' : 'Open menu'}</span>
              </Button>
            </div>
            <nav aria-label="Main">
              {/* Following any link closes the mobile menu. */}
              <ul id="main-nav" className="nav-list" data-open={menuOpen} onClick={() => setMenuOpen(false)}>
                <li>
                  <NavLink className="nav-link" to="/" end>
                    Home
                  </NavLink>
                </li>
                <li>
                  <NavLink className="nav-link" to="/vehicles">
                    Vehicles
                  </NavLink>
                </li>
                {status === 'authenticated' ? (
                  <>
                    <li>
                      <NavLink className="nav-link" to="/reservations">
                        My reservations
                      </NavLink>
                    </li>
                    <li>
                      <NavLink className="nav-link" to="/account">
                        Account
                      </NavLink>
                    </li>
                    <li>
                      <button type="button" className="nav-link" onClick={() => void handleSignOut()}>
                        <LogOut size={18} aria-hidden="true" /> Sign out
                        <span className="visually-hidden"> of {user?.email}</span>
                      </button>
                    </li>
                  </>
                ) : status === 'anonymous' ? (
                  <>
                    <li>
                      <NavLink className="nav-link" to="/login">
                        Sign in
                      </NavLink>
                    </li>
                    <li>
                      <NavLink className="nav-link nav-cta" to="/register">
                        Create account
                      </NavLink>
                    </li>
                  </>
                ) : null}
              </ul>
            </nav>
          </div>
        </div>
      </header>

      {signOutError && (
        <div className="container page">
          <Alert tone="error" title="We could not sign you out">
            <p>The server could not be reached, so you are still signed in. Please try again.</p>
          </Alert>
        </div>
      )}

      <main id="main" tabIndex={-1}>
        <Outlet />
      </main>

      <footer className="site-footer">
        <div className="container footer-grid">
          <div>
            <p className="footer-brand">
              <span className="brand-mark" aria-hidden="true">
                <CarFront size={20} />
              </span>
              Vehicle Rental
            </p>
            <p>
              A portfolio demonstration of a vehicle rental system. It is not a real rental business, and no payment is
              taken.
            </p>
          </div>
          <nav aria-label="Footer">
            <h2 className="footer-heading">Explore</h2>
            <ul className="footer-list">
              <li>
                <Link to="/vehicles">Browse vehicles</Link>
              </li>
              <li>
                <Link to="/reservations">My reservations</Link>
              </li>
              {status === 'authenticated' && (
                <li>
                  <Link to="/account">My account</Link>
                </li>
              )}
            </ul>
          </nav>
        </div>
      </footer>
    </div>
  )
}

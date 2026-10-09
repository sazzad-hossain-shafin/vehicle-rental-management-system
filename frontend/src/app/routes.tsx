import { Route, Routes } from 'react-router-dom'
import { AccountPage } from '../features/account/AccountPage'
import { LoginPage } from '../features/auth/LoginPage'
import { RegisterPage } from '../features/auth/RegisterPage'
import { RequireAuth } from '../features/auth/RequireAuth'
import { ReservationDetailPage } from '../features/reservations/ReservationDetailPage'
import { ReservationsPage } from '../features/reservations/ReservationsPage'
import { HomePage } from '../features/vehicles/HomePage'
import { VehicleDetailPage } from '../features/vehicles/VehicleDetailPage'
import { VehiclesPage } from '../features/vehicles/VehiclesPage'
import { Layout } from './Layout'
import { NotFoundPage } from './NotFoundPage'

export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="vehicles" element={<VehiclesPage />} />
        <Route path="vehicles/:id" element={<VehicleDetailPage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />

        {/* The signed-in area. The API enforces access too; this only avoids showing pages that cannot work. */}
        <Route element={<RequireAuth />}>
          <Route path="reservations" element={<ReservationsPage />} />
          <Route path="reservations/:id" element={<ReservationDetailPage />} />
          <Route path="account" element={<AccountPage />} />
        </Route>

        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}

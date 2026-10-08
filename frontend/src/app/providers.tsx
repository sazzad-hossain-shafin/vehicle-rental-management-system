import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { BrowserRouter } from 'react-router-dom'
import { AuthProvider } from '../features/auth/AuthProvider'
import { createQueryClient } from './queryClient'

export function AppProviders({ children, queryClient }: { children: ReactNode; queryClient?: QueryClient }) {
  const [client] = useState(() => queryClient ?? createQueryClient())

  return (
    <QueryClientProvider client={client}>
      <BrowserRouter>
        <AuthProvider>{children}</AuthProvider>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

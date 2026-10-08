import { QueryClient } from '@tanstack/react-query'
import { ApiError } from '../lib/api/errors'

/** A client that does not retry requests that failed for a reason retrying cannot fix (4xx). */
export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        retry: (failureCount, error) =>
          !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 2,
      },
    },
  })
}

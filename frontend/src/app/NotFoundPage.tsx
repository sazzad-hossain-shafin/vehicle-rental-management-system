import { SearchX } from 'lucide-react'
import { LinkButton } from '../components/ui/Button'
import { EmptyState } from '../components/ui/States'
import { useDocumentTitle } from '../hooks/useDocumentTitle'

export function NotFoundPage() {
  useDocumentTitle('Page not found')

  return (
    <div className="container page">
      <EmptyState
        icon={<SearchX aria-hidden="true" />}
        title="We could not find that page"
        action={<LinkButton to="/">Go to the home page</LinkButton>}
      >
        The address may be mistyped, or the page may have moved.
      </EmptyState>
    </div>
  )
}

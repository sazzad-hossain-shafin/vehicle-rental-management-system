import { useEffect } from 'react'

/** Sets the browser tab title and announces page changes to assistive technology through it. */
export function useDocumentTitle(title: string): void {
  useEffect(() => {
    document.title = `${title} · Vehicle Rental`
  }, [title])
}

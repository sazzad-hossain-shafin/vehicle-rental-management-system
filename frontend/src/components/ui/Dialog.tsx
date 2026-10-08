import { useEffect, useId, useRef, type ReactNode } from 'react'

interface DialogProps {
  open: boolean
  onClose: () => void
  title: string
  children: ReactNode
}

/**
 * A modal dialog built on the native <dialog> element: the browser traps focus inside it, closes it on Escape and
 * returns focus to the control that opened it. Its title labels it for screen readers.
 */
export function Dialog({ open, onClose, title, children }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return

    if (open && !dialog.open) {
      if (typeof dialog.showModal === 'function') dialog.showModal()
      else dialog.setAttribute('open', '')
    } else if (!open && dialog.open) {
      if (typeof dialog.close === 'function') dialog.close()
      else dialog.removeAttribute('open')
    }
  }, [open])

  return (
    <dialog
      ref={ref}
      className="dialog"
      aria-labelledby={titleId}
      onCancel={(event) => {
        // Escape: let our state decide, so the dialog and the page never disagree.
        event.preventDefault()
        onClose()
      }}
      onClick={(event) => {
        // A click on the backdrop lands on the dialog element itself.
        if (event.target === ref.current) onClose()
      }}
    >
      {open && (
        <div className="dialog-body">
          <h2 id={titleId}>{title}</h2>
          {children}
        </div>
      )}
    </dialog>
  )
}

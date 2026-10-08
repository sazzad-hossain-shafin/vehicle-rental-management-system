import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { Link, type LinkProps } from 'react-router-dom'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'

function classes(variant: Variant, block?: boolean): string {
  return `btn btn-${variant}${block ? ' btn-block' : ''}`
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  block?: boolean
  /** Shows a spinner and disables the button while an action is in progress. */
  loading?: boolean
}

export function Button({ variant = 'primary', block, loading, disabled, children, type = 'button', className, ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={className ? `${classes(variant, block)} ${className}` : classes(variant, block)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading && <span className="spinner" aria-hidden="true" />}
      {children}
    </button>
  )
}

interface LinkButtonProps extends LinkProps {
  variant?: Variant
  block?: boolean
  children: ReactNode
}

/** A real link (it navigates) that looks like a button. */
export function LinkButton({ variant = 'primary', block, children, ...rest }: LinkButtonProps) {
  return (
    <Link className={classes(variant, block)} {...rest}>
      {children}
    </Link>
  )
}

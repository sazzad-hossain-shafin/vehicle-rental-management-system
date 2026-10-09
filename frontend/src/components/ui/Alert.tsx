import { CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'

type Tone = 'error' | 'success' | 'warning' | 'info'

const ICONS = {
  error: CircleAlert,
  success: CircleCheck,
  warning: TriangleAlert,
  info: Info,
} as const

interface AlertProps {
  tone: Tone
  title?: string
  children?: ReactNode
  actions?: ReactNode
}

/**
 * Errors and warnings use role="alert" (announced at once); success and information use role="status"
 * (announced politely). The icon is decorative: the tone is also carried by the text.
 */
export function Alert({ tone, title, children, actions }: AlertProps) {
  const Icon = ICONS[tone]

  return (
    <div className={`alert alert-${tone}`} role={tone === 'error' || tone === 'warning' ? 'alert' : 'status'}>
      <Icon size={20} aria-hidden="true" />
      <div>
        {title && <p className="alert-title">{title}</p>}
        {children}
        {actions && <div className="alert-actions">{actions}</div>}
      </div>
    </div>
  )
}

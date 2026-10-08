import type { ReactNode } from 'react'

type Tone = 'success' | 'warning' | 'danger' | 'info' | 'neutral'

export function Badge({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return <span className={`badge badge-${tone}`}>{children}</span>
}

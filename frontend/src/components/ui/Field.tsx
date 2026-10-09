import { useId, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes } from 'react'

interface FieldChrome {
  label: string
  hint?: string
  error?: string | undefined
  /** The id of another element that describes the field (for example a note shared by several fields). */
  describedBy?: string
}

function describedBy(...ids: (string | false | undefined)[]): string | undefined {
  const joined = ids.filter(Boolean).join(' ')
  return joined || undefined
}

type TextFieldProps = FieldChrome &
  Omit<InputHTMLAttributes<HTMLInputElement>, 'id' | 'aria-invalid' | 'aria-describedby'> & {
    /** A decorative icon shown inside the left edge of the input. */
    icon?: ReactNode
  }

/** A labelled input. The error and hint are linked to the input, so screen readers announce them with it. */
export function TextField({ label, hint, error, describedBy: extraDescription, icon, ...input }: TextFieldProps) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      {icon ? (
        <div className="input-wrap">
          {icon}
          <input
            id={id}
            className="input"
            aria-invalid={error ? true : undefined}
            aria-describedby={describedBy(hint && hintId, extraDescription, error && errorId)}
            {...input}
          />
        </div>
      ) : (
        <input
          id={id}
          className="input"
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy(hint && hintId, extraDescription, error && errorId)}
          {...input}
        />
      )}
      {hint && (
        <span id={hintId} className="hint">
          {hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="field-error">
          {error}
        </span>
      )}
    </div>
  )
}

type SelectFieldProps = FieldChrome &
  Omit<SelectHTMLAttributes<HTMLSelectElement>, 'id' | 'aria-invalid' | 'aria-describedby'> & { children: ReactNode }

export function SelectField({ label, hint, error, describedBy: extraDescription, children, ...select }: SelectFieldProps) {
  const id = useId()
  const hintId = `${id}-hint`
  const errorId = `${id}-error`

  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <select
        id={id}
        className="select"
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy(hint && hintId, extraDescription, error && errorId)}
        {...select}
      >
        {children}
      </select>
      {hint && (
        <span id={hintId} className="hint">
          {hint}
        </span>
      )}
      {error && (
        <span id={errorId} className="field-error">
          {error}
        </span>
      )}
    </div>
  )
}

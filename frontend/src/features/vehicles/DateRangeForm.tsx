import { CalendarDays, Search } from 'lucide-react'
import { useId, useState, type FormEvent, type ReactNode } from 'react'
import { Button } from '../../components/ui/Button'
import { TextField } from '../../components/ui/Field'
import { addDays, formatDate, isIsoDate, todayIso, validateRange, type RangeErrors } from '../../lib/dates'

export interface DateRangeValue {
  startDate: string
  endDate: string
}

interface DateRangeFormProps {
  initial?: Partial<DateRangeValue>
  submitLabel: string
  onSubmit: (range: DateRangeValue) => void
  /** Extra controls placed before the button (for example a vehicle type). */
  children?: ReactNode
  idPrefix?: string
  /** Wide layout for the home page: dates and button on one row from tablet width up. */
  wide?: boolean
  /** Show the readable "9 Oct 2026 → 12 Oct 2026" line under the dates (default). */
  summary?: boolean
}

/**
 * Pickup and return dates. The customer picks the day they collect the vehicle and the day they bring it back;
 * the return day itself is not charged. Only obvious mistakes are caught here: the API applies the real rules.
 */
export function DateRangeForm({ initial, submitLabel, onSubmit, children, idPrefix = 'range', wide = false, summary = true }: DateRangeFormProps) {
  const noteId = useId()
  const today = todayIso()
  const [startDate, setStartDate] = useState(initial?.startDate ?? '')
  const [endDate, setEndDate] = useState(initial?.endDate ?? '')
  const [errors, setErrors] = useState<RangeErrors>({})

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const found = validateRange(startDate, endDate, today)
    setErrors(found)
    if (!found.startDate && !found.endDate) onSubmit({ startDate, endDate })
  }

  function handleStartChange(value: string) {
    setStartDate(value)
    // Keep the return date sensible when the pickup moves past it.
    if (value && endDate && endDate <= value) setEndDate(addDays(value, 1))
  }

  return (
    <form onSubmit={handleSubmit} noValidate aria-label="Choose your dates" className={wide ? 'range-form range-wide' : 'range-form'}>
      <TextField
        label="Pickup date"
        type="date"
        name={`${idPrefix}-start`}
        icon={<CalendarDays size={18} aria-hidden="true" />}
        min={today}
        value={startDate}
        onChange={(event) => handleStartChange(event.target.value)}
        error={errors.startDate}
        required
      />
      <TextField
        label="Return date"
        type="date"
        name={`${idPrefix}-end`}
        icon={<CalendarDays size={18} aria-hidden="true" />}
        min={startDate ? addDays(startDate, 1) : today}
        value={endDate}
        onChange={(event) => setEndDate(event.target.value)}
        error={errors.endDate}
        describedBy={noteId}
        required
      />
      {children}
      <div className="range-submit">
        <Button type="submit">
          <Search size={18} aria-hidden="true" /> {submitLabel}
        </Button>
      </div>
      <p id={noteId} className="range-note">
        You are not charged for the return day.
      </p>
      {summary && isIsoDate(startDate) && isIsoDate(endDate) && endDate > startDate && (
        <p className="range-summary" role="status">
          {formatDate(startDate)} &rarr; {formatDate(endDate)}
        </p>
      )}
    </form>
  )
}

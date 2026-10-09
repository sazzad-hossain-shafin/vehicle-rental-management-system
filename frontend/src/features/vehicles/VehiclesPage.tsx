import { CarFront } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { Button } from '../../components/ui/Button'
import { SelectField, TextField } from '../../components/ui/Field'
import { Pagination } from '../../components/ui/Pagination'
import { EmptyState, ErrorState, LoadingGrid } from '../../components/ui/States'
import { useDocumentTitle } from '../../hooks/useDocumentTitle'
import { VEHICLE_TYPES, type VehicleType } from '../../lib/api/types'
import { formatDate, isIsoDate, todayIso, validateRange } from '../../lib/dates'
import { pluralize } from '../../lib/format'
import { DateRangeForm } from './DateRangeForm'
import { useAvailableVehicles, useVehicles } from './queries'
import { VehicleCard } from './VehicleCard'

const PAGE_SIZE = 9

function parseType(value: string | null): VehicleType | '' {
  return VEHICLE_TYPES.find((t) => t === value) ?? ''
}

function parsePositiveNumber(value: string | null): number | null {
  if (!value) return null
  const n = Number(value)
  return Number.isFinite(n) && n > 0 ? n : null
}

export function VehiclesPage() {
  useDocumentTitle('Vehicles')
  const [params, setParams] = useSearchParams()

  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const vehicleType = parseType(params.get('type'))
  const maxDailyRate = parsePositiveNumber(params.get('maxRate'))
  const start = params.get('start') ?? ''
  const end = params.get('end') ?? ''

  // Dates only count when they make a valid period; otherwise the whole fleet is listed.
  const datesAreValid =
    isIsoDate(start) && isIsoDate(end) && Object.keys(validateRange(start, end, todayIso())).length === 0
  const range = datesAreValid ? { startDate: start, endDate: end } : null
  const filters = { vehicleType, maxDailyRate, page, pageSize: PAGE_SIZE }

  const fleet = useVehicles(filters, range === null)
  const free = useAvailableVehicles(range, filters)
  const query = range ? free : fleet
  const data = query.data

  const [typeDraft, setTypeDraft] = useState<VehicleType | ''>(vehicleType)
  const [rateDraft, setRateDraft] = useState(maxDailyRate?.toString() ?? '')

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value) next.set(key, value)
      else next.delete(key)
    }
    setParams(next)
  }

  function applyFilters(event: FormEvent) {
    event.preventDefault()
    update({ type: typeDraft || null, maxRate: parsePositiveNumber(rateDraft)?.toString() ?? null, page: null })
  }

  return (
    <div className="container page">
      <h1 className="page-heading">{range ? 'Vehicles free for your dates' : 'Our vehicles'}</h1>

      <section className="card card-body" aria-label="Search by dates">
        <DateRangeForm
          key={`${start}|${end}`}
          idPrefix="vehicles"
          initial={{ startDate: start, endDate: end }}
          submitLabel={range ? 'Update dates' : 'Check availability'}
          onSubmit={(value) => update({ start: value.startDate, end: value.endDate, page: null })}
        />
        {range && (
          <p className="muted">
            Showing vehicles free from {formatDate(range.startDate)} (pickup) to {formatDate(range.endDate)} (return).{' '}
            <Button variant="ghost" onClick={() => update({ start: null, end: null, page: null })}>
              Show all vehicles
            </Button>
          </p>
        )}
        {!range && (start || end) && (
          <p className="muted">Those dates are not a valid period, so the whole fleet is shown.</p>
        )}
      </section>

      <form className="toolbar" onSubmit={applyFilters} aria-label="Filter vehicles" noValidate>
        <SelectField
          label="Vehicle type"
          value={typeDraft}
          onChange={(event) => setTypeDraft(parseType(event.target.value))}
        >
          <option value="">All types</option>
          {VEHICLE_TYPES.map((type) => (
            <option key={type} value={type}>
              {type}
            </option>
          ))}
        </SelectField>
        <TextField
          label="Maximum price per day"
          type="number"
          inputMode="decimal"
          min="1"
          step="1"
          value={rateDraft}
          onChange={(event) => setRateDraft(event.target.value)}
        />
        <Button type="submit" variant="secondary">
          Apply filters
        </Button>
      </form>

      <div aria-live="polite">
        {query.isPending ? (
          <LoadingGrid label="Loading vehicles" />
        ) : query.isError ? (
          <ErrorState error={query.error} onRetry={() => void query.refetch()} title="We could not load the vehicles" />
        ) : data && data.items.length === 0 ? (
          <EmptyState
            icon={<CarFront size={40} aria-hidden="true" />}
            title={range ? 'No vehicles are free for those dates' : 'No vehicles match your search'}
            action={
              <Button
                variant="secondary"
                onClick={() => update({ type: null, maxRate: null, start: range ? null : start || null, end: range ? null : end || null, page: null })}
              >
                {range ? 'Show all vehicles' : 'Clear filters'}
              </Button>
            }
          >
            {range ? 'Try different dates or remove the filters.' : 'Try removing a filter.'}
          </EmptyState>
        ) : data ? (
          <>
            <p className="muted">{pluralize(data.totalCount, 'vehicle')} found</p>
            <ul className="vehicle-grid">
              {data.items.map((vehicle) => (
                <li key={vehicle.id}>
                  <VehicleCard vehicle={vehicle} dates={range ?? undefined} />
                </li>
              ))}
            </ul>
            <Pagination
              page={data.page}
              totalPages={data.totalPages}
              onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
            />
          </>
        ) : null}
      </div>
    </div>
  )
}

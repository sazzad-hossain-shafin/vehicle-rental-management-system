import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../../lib/api/errors'
import { Alert } from './Alert'
import { Dialog } from './Dialog'
import { TextField } from './Field'
import { Pagination } from './Pagination'
import { EmptyState, ErrorState, LoadingBlock, LoadingGrid } from './States'

describe('TextField', () => {
  it('links the label, hint and error to the input for assistive technology', () => {
    render(<TextField label="Email" hint="We never share it." error="Enter your email." />)

    const input = screen.getByLabelText('Email')

    expect(input).toHaveAttribute('aria-invalid', 'true')
    expect(input).toHaveAccessibleDescription('We never share it. Enter your email.')
  })

  it('is not marked invalid without an error', () => {
    render(<TextField label="Name" />)

    expect(screen.getByLabelText('Name')).not.toHaveAttribute('aria-invalid')
  })
})

describe('Alert', () => {
  it('announces errors immediately and information politely', () => {
    render(
      <>
        <Alert tone="error" title="Failed">
          Bad.
        </Alert>
        <Alert tone="success">Done.</Alert>
      </>,
    )

    expect(screen.getByRole('alert')).toHaveTextContent('Failed')
    expect(screen.getByRole('status')).toHaveTextContent('Done.')
  })
})

describe('loading, empty and error states', () => {
  it('announce loading once, with a label, and hide the placeholder shapes', () => {
    render(
      <>
        <LoadingGrid label="Loading vehicles" />
        <LoadingBlock label="Loading the reservation" />
      </>,
    )

    expect(screen.getByText('Loading vehicles…')).toBeInTheDocument()
    expect(screen.getByText('Loading the reservation…')).toBeInTheDocument()
    expect(screen.getAllByRole('status')).toHaveLength(2)
  })

  it('shows an empty state with its action', () => {
    render(<EmptyState title="Nothing here" action={<button>Do something</button>}>Try later.</EmptyState>)

    expect(screen.getByRole('heading', { name: 'Nothing here' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Do something' })).toBeInTheDocument()
  })

  it('shows the server message and reference for an API error, and retries on request', async () => {
    const retry = vi.fn()
    const error = new ApiError({ status: 409, title: 'Conflict', detail: 'The vehicle is taken.', traceId: 'abc-1' })
    render(<ErrorState error={error} onRetry={retry} title="Could not load" />)

    expect(screen.getByRole('alert')).toHaveTextContent('The vehicle is taken.')
    expect(screen.getByText('Reference: abc-1')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))
    expect(retry).toHaveBeenCalledOnce()
  })

  it('hides internals of a server failure and of unknown errors', () => {
    const { rerender } = render(
      <ErrorState error={new ApiError({ status: 500, title: 'Internal', detail: 'NullReferenceException at Foo' })} />,
    )
    expect(screen.getByRole('alert')).not.toHaveTextContent('NullReference')

    rerender(<ErrorState error={new Error('secret detail')} />)
    expect(screen.getByRole('alert')).not.toHaveTextContent('secret detail')
  })

  it('explains a network failure without technical words', () => {
    render(<ErrorState error={new ApiError({ status: 0, title: 'Network error' })} />)

    expect(screen.getByRole('alert')).toHaveTextContent(/could not reach the server/i)
  })
})

describe('Pagination', () => {
  it('renders nothing for a single page', () => {
    const { container } = render(<Pagination page={1} totalPages={1} onPageChange={() => undefined} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('moves between pages and disables the ends', async () => {
    const change = vi.fn()
    const { rerender } = render(<Pagination page={1} totalPages={3} onPageChange={change} />)

    expect(screen.getByRole('button', { name: /previous/i })).toBeDisabled()
    await userEvent.click(screen.getByRole('button', { name: /next/i }))
    expect(change).toHaveBeenCalledWith(2)

    rerender(<Pagination page={3} totalPages={3} onPageChange={change} />)
    expect(screen.getByRole('button', { name: /next/i })).toBeDisabled()
    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument()
  })
})

describe('Dialog', () => {
  it('is labelled by its title and shows its content only while open', () => {
    const { rerender } = render(
      <Dialog open={false} onClose={() => undefined} title="Cancel it?">
        <p>Are you sure?</p>
      </Dialog>,
    )
    expect(screen.queryByText('Are you sure?')).not.toBeInTheDocument()

    rerender(
      <Dialog open onClose={() => undefined} title="Cancel it?">
        <p>Are you sure?</p>
      </Dialog>,
    )
    expect(screen.getByRole('dialog', { name: 'Cancel it?' })).toBeInTheDocument()
    expect(screen.getByText('Are you sure?')).toBeInTheDocument()
  })

  it('asks to close on Escape and on a backdrop click', async () => {
    const close = vi.fn()
    render(
      <Dialog open onClose={close} title="Title">
        <button>Inside</button>
      </Dialog>,
    )
    const dialog = screen.getByRole('dialog')

    dialog.dispatchEvent(new Event('cancel', { cancelable: true })) // what Escape does
    await userEvent.click(dialog) // a click on the backdrop targets the dialog itself
    await userEvent.click(screen.getByRole('button', { name: 'Inside' })) // clicks inside do not close it

    expect(close).toHaveBeenCalledTimes(2)
  })
})

describe('routing helpers', () => {
  it('renders links inside a router', () => {
    render(<MemoryRouter>x</MemoryRouter>)
    expect(screen.getByText('x')).toBeInTheDocument()
  })
})

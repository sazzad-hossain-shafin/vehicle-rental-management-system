import { expect, test } from '@playwright/test'
import {
  chooseDates,
  isoDate,
  newAccount,
  registerThroughTheWebsite,
  reserveAsAnotherCustomer,
  seedVehicle,
  signInThroughTheWebsite,
} from './support'

test.describe('browsing as a visitor', () => {
  test('the home page lists real vehicles and leads to the vehicle list', async ({ page, request }) => {
    await seedVehicle(request)

    await page.goto('/')

    await expect(page.getByRole('heading', { level: 1 })).toContainText('Reserve the right vehicle');
    await expect(page.getByRole('heading', { name: 'From the fleet' })).toBeVisible()
    await page.getByRole('link', { name: 'Browse all vehicles' }).click()
    await expect(page).toHaveURL(/\/vehicles$/)
    await expect(page.getByRole('heading', { name: 'Our vehicles' })).toBeVisible()
    await expect(page.getByRole('list').getByRole('article').first()).toBeVisible()
  })

  test('a visitor can search availability by dates and see the exact price, without signing in', async ({ page, request }) => {
    const vehicle = await seedVehicle(request, 40)
    const start = isoDate(20)
    const end = isoDate(23)

    await page.goto('/')
    await chooseDates(page, start, end)
    await page.getByRole('button', { name: 'Find vehicles' }).click()
    await expect(page.getByRole('heading', { name: 'Vehicles free for your dates' })).toBeVisible()

    // Open the seeded vehicle straight away (the list is paged and shared with other tests).
    await page.goto(`/vehicles/${vehicle.id}?start=${start}&end=${end}`)
    await expect(page.getByRole('heading', { name: vehicle.displayName })).toBeVisible()
    const table = page.getByRole('table', { name: 'Price for your dates' })
    await expect(table).toContainText('3 days')
    await expect(table).toContainText('$120.00') // 3 days at $40: calculated by the server
    await expect(page.getByText('This vehicle is free for those dates right now.')).toBeVisible()
    await expect(page.getByRole('link', { name: 'Sign in to reserve' })).toBeVisible()
  })

  test('the server applies the long-stay price, not the page', async ({ page, request }) => {
    const vehicle = await seedVehicle(request, 100)

    await page.goto(`/vehicles/${vehicle.id}?start=${isoDate(30)}&end=${isoDate(37)}`)

    const table = page.getByRole('table', { name: 'Price for your dates' })
    await expect(table).toContainText('7 days')
    await expect(table).toContainText('Long-term')
    await expect(table).toContainText('$560.00') // 7 days at $100, less the server's 20%
  })
})

test.describe('protected pages', () => {
  test('a signed-out visitor is sent to sign in, and returns to the page afterwards', async ({ page }) => {
    const account = newAccount()
    await page.goto('/register')
    await page.getByLabel('Full name').fill(account.name)
    await page.getByLabel('Email').fill(account.email)
    await page.getByLabel('Password').fill(account.password)
    await page.getByRole('button', { name: 'Create account' }).click()
    await expect(page.getByRole('heading', { name: 'My reservations' })).toBeVisible()
    await page.getByRole('button', { name: /sign out/i }).click()
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible()

    await page.goto('/account')
    await expect(page).toHaveURL(/\/login$/)
    await signInThroughTheWebsite(page, account)

    await expect(page).toHaveURL(/\/account$/)
    await expect(page.getByRole('heading', { name: 'My account' })).toBeVisible()
    await expect(page.locator('dd', { hasText: account.email })).toBeVisible()
  })

  test('a wrong password is refused with a generic message', async ({ page }) => {
    await signInThroughTheWebsite(page, { name: 'x', email: 'nobody@example.test', password: 'Wrong-pass-1' })

    await expect(page.getByRole('alert')).toContainText('The email or password is incorrect.')
    await expect(page).toHaveURL(/\/login$/)
  })
})

test.describe('the customer journey', () => {
  test('register, reserve, see it listed, then cancel it', async ({ page, request }) => {
    const vehicle = await seedVehicle(request, 60)
    const account = newAccount()
    const start = isoDate(40)
    const end = isoDate(43)

    // Register through the website: this also signs in.
    await registerThroughTheWebsite(page, account)
    await expect(page.getByRole('heading', { name: 'You have no reservations yet' })).toBeVisible()

    // The session is an HttpOnly cookie: scripts on the page cannot see it, and nothing is kept in web storage.
    const exposed = await page.evaluate(() => ({
      cookie: document.cookie,
      local: JSON.stringify({ ...localStorage }),
      session: JSON.stringify({ ...sessionStorage }),
    }))
    expect(exposed.cookie).not.toContain('vr_session')
    expect(exposed.local + exposed.session).not.toMatch(/eyJ|token|password/i)
    const cookies = await page.context().cookies()
    const session = cookies.find((c) => c.name === 'vr_session')
    expect(session?.httpOnly).toBe(true)
    expect(session?.sameSite).toBe('Strict')

    // Reserve.
    await page.goto(`/vehicles/${vehicle.id}?start=${start}&end=${end}`)
    await expect(page.getByRole('table', { name: 'Price for your dates' })).toContainText('$180.00')
    await page.getByRole('button', { name: 'Reserve these dates' }).click()
    const dialog = page.getByRole('dialog', { name: 'Confirm your reservation' })
    await expect(dialog).toContainText(vehicle.displayName)
    await expect(dialog).toContainText('$180.00')
    await dialog.getByRole('button', { name: 'Confirm reservation' }).click()

    // The confirmation comes from the server response.
    await expect(page.getByText('Reservation confirmed')).toBeVisible()
    await expect(page.getByRole('heading', { name: vehicle.displayName })).toBeVisible()
    await expect(page.getByText('Active', { exact: true })).toBeVisible()

    // It is in my list.
    await page.getByRole('link', { name: 'My reservations' }).first().click()
    const item = page.getByRole('list', { name: 'Your reservations' }).getByRole('listitem').filter({ hasText: vehicle.displayName })
    await expect(item).toBeVisible()
    await expect(item).toContainText('Active')

    // The vehicle is no longer free for those dates (the page asks the server again).
    await page.goto(`/vehicles/${vehicle.id}?start=${start}&end=${end}`)
    await expect(page.getByText('Not available for those dates')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Reserve these dates' })).toBeDisabled()

    // Cancel, with a confirmation step.
    await page.goto('/reservations')
    await item.getByRole('link', { name: /view details/i }).click()
    await page.getByRole('button', { name: 'Cancel this reservation' }).click()
    const cancelDialog = page.getByRole('dialog', { name: 'Cancel this reservation?' })
    await expect(cancelDialog).toBeVisible()
    await cancelDialog.getByRole('button', { name: 'Yes, cancel it' }).click()
    await expect(page.getByText('Reservation cancelled')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Cancel this reservation' })).toHaveCount(0)

    // The dates are free again.
    await page.goto(`/vehicles/${vehicle.id}?start=${start}&end=${end}`)
    await expect(page.getByText('This vehicle is free for those dates right now.')).toBeVisible()
  })

  test('a second customer losing the race gets a clear conflict message', async ({ page, request }) => {
    const vehicle = await seedVehicle(request, 70)
    const account = newAccount()
    const start = isoDate(50)
    const end = isoDate(52)
    await registerThroughTheWebsite(page, account)

    // This customer sees the vehicle as free, and opens the confirmation...
    await page.goto(`/vehicles/${vehicle.id}?start=${start}&end=${end}`)
    await expect(page.getByText('This vehicle is free for those dates right now.')).toBeVisible()
    await page.getByRole('button', { name: 'Reserve these dates' }).click()
    const dialog = page.getByRole('dialog', { name: 'Confirm your reservation' })
    await expect(dialog).toBeVisible()

    // ...while someone else books the same vehicle for the same dates.
    await reserveAsAnotherCustomer(request, vehicle.id, start, end)
    await dialog.getByRole('button', { name: 'Confirm reservation' }).click()

    // The server refuses; the page explains and offers a way forward. Nothing was booked.
    await expect(dialog.getByRole('alert')).toContainText('no longer free for those dates')
    await expect(dialog.getByRole('link', { name: /see other vehicles for these dates/i })).toBeVisible()
    await dialog.getByRole('button', { name: 'Choose other dates' }).click()
    await page.goto('/reservations')
    await expect(page.getByRole('heading', { name: 'You have no reservations yet' })).toBeVisible()
  })

  test('another account on the same browser never sees the previous customer reservations', async ({ page, request }) => {
    const vehicle = await seedVehicle(request, 55)
    const first = newAccount('First')
    const second = newAccount('Second')
    const start = isoDate(60)
    const end = isoDate(62)

    await registerThroughTheWebsite(page, first)
    await page.goto(`/vehicles/${vehicle.id}?start=${start}&end=${end}`)
    await page.getByRole('button', { name: 'Reserve these dates' }).click()
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm reservation' }).click()
    await expect(page.getByText('Reservation confirmed')).toBeVisible()
    await page.getByRole('button', { name: /sign out/i }).click()
    await expect(page.getByRole('link', { name: 'Sign in' })).toBeVisible()

    // Registering a different customer in the same tab: their list is theirs alone.
    await registerThroughTheWebsite(page, second)
    await expect(page.getByRole('heading', { name: 'You have no reservations yet' })).toBeVisible()
    await expect(page.getByText(vehicle.displayName)).toHaveCount(0)
  })
})

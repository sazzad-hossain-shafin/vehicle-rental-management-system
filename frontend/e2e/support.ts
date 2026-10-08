import { expect, type APIRequestContext, type Page } from '@playwright/test'
import { randomBytes } from 'node:crypto'

const API = '/api/v1'

export interface Account {
  name: string
  email: string
  password: string
}

export interface SeededVehicle {
  id: string
  displayName: string
  dailyRate: number
}

/** A calendar date offset from today in YYYY-MM-DD, using the machine's local date like the browser does. */
export function isoDate(offsetDays: number): string {
  const d = new Date()
  d.setDate(d.getDate() + offsetDays)
  const month = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${month}-${day}`
}

export function uniqueSuffix(): string {
  return randomBytes(4).toString('hex')
}

/** A fresh customer identity. The password is random, used for this test only and never written anywhere. */
export function newAccount(label = 'Casey'): Account {
  const id = uniqueSuffix()
  return {
    name: `${label} ${id}`,
    email: `e2e-${id}@example.test`,
    password: `Aa1-${randomBytes(9).toString('hex')}`,
  }
}

function required(name: string): string {
  const value = process.env[name]
  if (!value) throw new Error(`Set ${name} to run the end-to-end tests (see playwright.config.ts)`)
  return value
}

/** Signs in as the stack's admin, over the bearer API, only to prepare test data. Returns the Authorization header. */
async function adminHeaders(request: APIRequestContext): Promise<Record<string, string>> {
  const response = await request.post(`${API}/auth/login`, {
    data: { email: required('E2E_ADMIN_EMAIL'), password: required('E2E_ADMIN_PASSWORD') },
  })
  expect(response.ok(), 'admin sign-in for test setup').toBe(true)
  const { accessToken } = (await response.json()) as { accessToken: string }
  return { Authorization: `Bearer ${accessToken}` }
}

/** Adds a vehicle with a unique name, so a test can find exactly it. */
export async function seedVehicle(request: APIRequestContext, dailyRate = 50, vehicleType = 'Car'): Promise<SeededVehicle> {
  const suffix = uniqueSuffix()
  const response = await request.post(`${API}/vehicles`, {
    headers: await adminHeaders(request),
    data: {
      registrationNumber: `E2E-${suffix}`.toUpperCase(),
      make: 'Testmark',
      model: `Run ${suffix}`,
      year: 2024,
      vehicleType,
      dailyRate,
    },
  })
  expect(response.status(), 'vehicle creation for test setup').toBe(201)
  const body = (await response.json()) as { id: string; displayName: string }
  return { id: body.id, displayName: body.displayName, dailyRate }
}

/** Registers a customer and books a vehicle for them over the bearer API, to set up a competing reservation. */
export async function reserveAsAnotherCustomer(
  request: APIRequestContext,
  vehicleId: string,
  startDate: string,
  endDate: string,
): Promise<void> {
  const other = newAccount('Rival')
  const registered = await request.post(`${API}/auth/register`, { data: other })
  expect(registered.status()).toBe(201)
  const login = await request.post(`${API}/auth/login`, { data: { email: other.email, password: other.password } })
  const { accessToken } = (await login.json()) as { accessToken: string }
  const booked = await request.post(`${API}/me/reservations`, {
    headers: { Authorization: `Bearer ${accessToken}` },
    data: { vehicleId, startDate, endDate },
  })
  expect(booked.status(), 'competing reservation').toBe(201)
}

export async function registerThroughTheWebsite(page: Page, account: Account): Promise<void> {
  await page.goto('/register')
  await page.getByLabel('Full name').fill(account.name)
  await page.getByLabel('Email').fill(account.email)
  await page.getByLabel('Password').fill(account.password)
  await page.getByRole('button', { name: 'Create account' }).click()
  await expect(page.getByRole('heading', { name: 'My reservations' })).toBeVisible()
}

export async function signInThroughTheWebsite(page: Page, account: Account): Promise<void> {
  await page.goto('/login')
  await page.getByLabel('Email').fill(account.email)
  await page.getByLabel('Password').fill(account.password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

export async function chooseDates(page: Page, startDate: string, endDate: string): Promise<void> {
  await page.getByLabel('Pickup date').fill(startDate)
  await page.getByLabel('Return date').fill(endDate)
}

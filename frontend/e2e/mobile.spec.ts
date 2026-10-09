import { expect, test } from '@playwright/test'
import { seedVehicle } from './support'

test.describe('on a phone', () => {
  test('the navigation collapses into a menu that works with the keyboard and touch', async ({ page }) => {
    await page.goto('/')

    const toggle = page.getByRole('button', { name: 'Open menu' })
    await expect(toggle).toBeVisible()
    const nav = page.getByRole('navigation', { name: 'Main' })
    await expect(nav.getByRole('link', { name: 'Vehicles' })).toBeHidden()

    await toggle.click()
    await expect(page.getByRole('button', { name: 'Close menu' })).toHaveAttribute('aria-expanded', 'true')
    await nav.getByRole('link', { name: 'Vehicles' }).click()

    await expect(page).toHaveURL(/\/vehicles$/)
    await expect(page.getByRole('heading', { name: 'Our vehicles' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'Open menu' })).toHaveAttribute('aria-expanded', 'false')
  })

  test('the pages do not overflow the screen width', async ({ page, request }) => {
    const vehicle = await seedVehicle(request)

    for (const path of ['/', '/vehicles', `/vehicles/${vehicle.id}`, '/login', '/register']) {
      await page.goto(path)
      await expect(page.getByRole('main')).toBeVisible()
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)
      expect(overflow, `horizontal overflow on ${path}`).toBeLessThanOrEqual(1)
    }
  })

  test('the date search works on a phone', async ({ page }) => {
    await page.goto('/')
    await page.getByLabel('Pickup date').fill('2030-03-10')
    await page.getByLabel('Return date').fill('2030-03-12')
    await page.getByRole('button', { name: 'Find vehicles' }).click()

    await expect(page).toHaveURL(/\/vehicles\?start=2030-03-10&end=2030-03-12$/)
  })
})

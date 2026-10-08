// The API has no currency concept; amounts are shown with a dollar sign, like the console client does.
const money = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })

export function formatMoney(amount: number): string {
  return money.format(amount)
}

export function pluralize(count: number, singular: string, plural = `${singular}s`): string {
  return `${count} ${count === 1 ? singular : plural}`
}

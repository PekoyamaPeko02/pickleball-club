import { readFileSync } from 'node:fs';
import { expect, test, type Page } from '@playwright/test';

// The dev Admin account the API creates at start-up (development configuration, never used in production).
const dev = JSON.parse(readFileSync(new URL('../../backend/src/PickleballClub.Api/appsettings.Development.json', import.meta.url), 'utf8'));

async function signIn(page: Page) {
  await page.goto('/');
  await expect(page).toHaveURL(/\/login/);
  await page.getByLabel('Email', { exact: true }).fill(dev.Admin.Email);
  await page.getByLabel('Password', { exact: true }).fill(dev.Admin.Password);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('heading', { name: 'Schedule' })).toBeVisible();
}

// Owns Court 2 15:00, eight days ahead.
test('an admin enters a walk-in booking, then cancels it with a refund note', async ({ page }) => {
  await signIn(page);
  for (let i = 0; i < 8; i++) await page.getByRole('button', { name: 'Next day' }).click();

  await page.getByRole('button', { name: /^Court 2 15:00: free/ }).click();
  await page.getByRole('button', { name: 'Walk-in booking' }).click();
  await page.getByLabel('Guest name').fill('E2E Walk-in');
  await page.getByLabel('Guest phone').fill('089 000 1111');
  await page.getByRole('button', { name: 'Create booking' }).click();

  const cell = page.getByRole('button', { name: /^Court 2 15:00: E2E Walk-in, Confirmed/ });
  await expect(cell).toBeVisible();
  await cell.click();
  await expect(page.getByText('Cash at the counter')).toBeVisible();

  // Too early to check in a booking that is days away: the reason is shown, nothing changes.
  await page.getByRole('button', { name: 'Check in' }).click();
  await expect(page.getByText('Check-in opens 60 minutes before')).toBeVisible();

  await page.getByRole('button', { name: 'Cancel', exact: true }).click();
  await page.getByLabel('How was it refunded?').fill('Cash back at the counter');
  await page.getByRole('button', { name: 'Cancel the booking' }).click();
  await expect(page.getByRole('button', { name: /^Court 2 15:00: free/ })).toBeVisible();
});

// Owns Court 1 08:00–10:00, nine days ahead.
test('an admin blocks a court and removes the block', async ({ page }) => {
  await signIn(page);
  for (let i = 0; i < 9; i++) await page.getByRole('button', { name: 'Next day' }).click();

  await page.getByRole('button', { name: /^Court 1 08:00: free/ }).click();
  await page.getByRole('button', { name: 'Block the court' }).click();
  await page.getByLabel('Reason').fill('Net repair');
  await page.locator('form').getByRole('button', { name: 'Block the court' }).click(); // the form's own button, not the menu's

  const blocked = page.getByRole('button', { name: /^Court 1 08:00: blocked, Net repair/ });
  await expect(blocked).toBeVisible();
  await blocked.click();
  await page.getByRole('button', { name: 'Remove the block' }).click();
  await expect(page.getByRole('button', { name: /^Court 1 08:00: free/ })).toBeVisible();
});

test('the settings, refunds and revenue pages open with the club\'s data', async ({ page }) => {
  await signIn(page);

  await page.getByRole('link', { name: 'Settings' }).click();
  await page.getByRole('tab', { name: 'Prices' }).click();
  await expect(page.getByText('Off-peak')).toBeVisible();

  await page.getByRole('link', { name: 'Refunds to make' }).click();
  await expect(page.getByRole('heading', { name: 'Refunds to make' })).toBeVisible();

  await page.getByRole('link', { name: 'Revenue' }).click();
  await expect(page.getByText('Total revenue')).toBeVisible();
  await expect(page.getByRole('table')).toBeVisible();
});

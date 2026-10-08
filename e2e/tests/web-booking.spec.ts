import { expect, test } from '@playwright/test';

// Owns Court 3 10:00–12:00 and Court 4 13:00–15:00, seven days ahead.
test('a new customer books a court, pays, and moves the booking once', async ({ page }) => {
  const email = `e2e-${Date.now()}-${Math.floor(Math.random() * 1e6)}@example.test`;

  // ---- choose two hours on one court
  await page.goto('/book');
  await page.getByRole('tab').nth(7).click();
  await page.getByRole('button', { name: /^Court 3, 10:00 to 11:00, available/ }).click();
  await page.getByRole('button', { name: /^Court 3, 11:00 to 12:00, available/ }).click();
  const selection = page.getByRole('region', { name: 'Your selection' });
  await expect(selection).toContainText('Court 3');
  await expect(selection).toContainText('10:00–12:00 (2 hours)');
  await selection.getByRole('button', { name: 'Continue' }).click();

  // ---- a guest is sent to sign in; create an account instead, and land back on the checkout
  await expect(page).toHaveURL(/\/login\?redirect=\/checkout$/);
  await page.getByRole('link', { name: 'Create an account' }).click();
  await page.getByLabel('Name', { exact: true }).fill('E2E Customer');
  await page.getByLabel('Email', { exact: true }).fill(email);
  await page.getByLabel('Phone number', { exact: true }).fill('081-234-5678');
  await page.getByLabel('Password', { exact: true }).fill('e2e-password-01');
  await page.getByRole('button', { name: 'Create account' }).click();

  await expect(page.getByRole('heading', { name: 'Review and pay' })).toBeVisible();
  await expect(page.getByText('10:00–12:00 (2 hours)')).toBeVisible();
  await page.getByRole('button', { name: /^Pay ฿/ }).click();

  // ---- the court is held; pay (the development-only stand-in for scanning the QR)
  await expect(page.getByRole('heading', { name: 'Pay to confirm' })).toBeVisible();
  await expect(page.getByAltText('PromptPay QR code for this payment')).toBeVisible();
  await expect(page.getByText(/Time left: \d:\d\d/)).toBeVisible();
  await page.getByRole('button', { name: 'Simulate payment (development only)' }).click();

  await expect(page.getByRole('heading', { name: 'You are booked' })).toBeVisible();
  const code = (await page.getByLabel('Booking code').innerText()).trim();
  expect(code).toMatch(/^PB\d+$/);

  // ---- move it to a time of the same length and price; after that it cannot move again
  await page.getByRole('link', { name: 'Move this booking' }).click();
  await expect(page.getByRole('heading', { name: 'Move your booking' })).toBeVisible();
  await page.getByRole('button', { name: /^Court 4, 13:00 to 14:00, available/ }).click();
  await expect(page.getByRole('region', { name: 'New time' })).toContainText('Court 4');
  await page.getByRole('button', { name: 'Confirm the move' }).click();

  await expect(page.getByRole('heading', { name: 'You are booked' })).toBeVisible();
  await expect(page.getByText('13:00–15:00 (2 hours)')).toBeVisible();
  await expect(page.getByText('has already been moved once')).toBeVisible();
  await expect(page.getByLabel('Booking code')).toHaveText(code); // the same booking

  // ---- it is on the account page, and the old time is free again for everyone
  await page.getByRole('link', { name: 'All my bookings' }).click();
  await expect(page.getByRole('link', { name: new RegExp(code) })).toContainText('Confirmed');

  await page.goto('/book');
  await page.getByRole('tab').nth(7).click();
  await expect(page.getByRole('button', { name: /^Court 3, 10:00 to 11:00, available/ })).toBeVisible();
  await expect(page.getByLabel(/^Court 4, 13:00 to 14:00, Booked/)).toBeVisible();
});

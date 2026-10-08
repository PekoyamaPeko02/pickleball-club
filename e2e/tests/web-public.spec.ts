import { expect, test } from '@playwright/test';

test('the public pages show the club without signing in', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.getByText('Demo data')).toHaveCount(0); // talking to the real API, not the in-browser mock

  await page.getByRole('link', { name: 'Courts' }).first().click();
  await expect(page.getByRole('heading', { name: 'Court 1' })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Opening hours' })).toBeVisible();

  await page.goto('/faq');
  await page.getByText('Can I move my booking?').click();
  await expect(page.getByText(/once per booking, at least \d+ hours before you play/)).toBeVisible();
});

test('anyone can see free courts and prices, but an account page needs a sign-in', async ({ page }) => {
  await page.goto('/book');
  await expect(page.getByRole('columnheader', { name: /Court 1/ })).toBeVisible();
  await page.getByRole('tab').nth(5).click();
  await expect(page.getByRole('button', { name: /^Court 1, 10:00 to 11:00, available, ฿/ })).toBeVisible();

  await page.goto('/account');
  await expect(page).toHaveURL(/\/login\?redirect=\/account$/);
  await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
});

test('a wrong password is refused with a clear message', async ({ page }) => {
  await page.goto('/login');
  await page.getByLabel('Email', { exact: true }).fill('nobody@example.test');
  await page.getByLabel('Password', { exact: true }).fill('not-the-password');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByRole('alert')).toContainText('email or password is not right');
});

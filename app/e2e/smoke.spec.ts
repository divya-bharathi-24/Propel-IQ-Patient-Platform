import { test, expect } from '@playwright/test';

test.describe('Smoke — Application Bootstrap', () => {
  test('app loads and renders root element', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/PropelIqPatientPlatform/i);
    await expect(page.locator('app-root')).toBeVisible();
  });
});

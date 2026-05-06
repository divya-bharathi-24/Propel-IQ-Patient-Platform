/**
 * Login page tests — @login
 * Tests all login scenarios using LoginPage page object.
 */
import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/login.page';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:4200';

const PATIENT_EMAIL    = process.env.TEST_PATIENT_EMAIL    ?? 'auth.patient@propeliq.dev';
const PATIENT_PASSWORD = process.env.TEST_PATIENT_PASSWORD ?? 'AuthP@ss001!';

test.describe('@login Login Page', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto(`${BASE_URL}/auth/login`);
  });

  test('@login renders login form correctly', async ({ page }) => {
    const login = new LoginPage(page);
    await expect(login.emailInput).toBeVisible();
    await expect(login.passwordInput).toBeVisible();
    await expect(login.signInButton).toBeVisible();
  });

  test('@login shows error on invalid credentials', async ({ page }) => {
    const login = new LoginPage(page);
    await login.login('wrong@example.com', 'WrongPass123!');
    await expect(login.errorAlert).toBeVisible();
  });

  test('@login shows validation error on empty submit', async ({ page }) => {
    const login = new LoginPage(page);
    await login.signInButton.click();
    // Fields should show required validation
    await expect(page.getByText(/required/i)).toBeVisible();
  });

  test('@login successful login redirects to dashboard', async ({ page }) => {
    const login = new LoginPage(page);
    await login.login(PATIENT_EMAIL, PATIENT_PASSWORD);
    await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
  });
});

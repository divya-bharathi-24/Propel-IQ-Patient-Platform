/**
 * Login page tests — @login
 * Tests all login scenarios using LoginPage page object.
 */
import { test, expect } from '@playwright/test';
import { LoginPage } from '../pages/login.page';
import { loadRegisteredUser } from '../support/test-user-store';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:4200';

// Defaults — overridden at runtime by beforeAll if a registered user was saved
let PATIENT_EMAIL    = process.env.TEST_PATIENT_EMAIL    ?? 'auth.patient@propeliq.dev';
let PATIENT_PASSWORD = process.env.TEST_PATIENT_PASSWORD ?? 'AuthP@ss001!';

test.describe('@login Login Page', () => {
  test.beforeAll(() => {
    // Load at execution time so credentials saved by the registration spec
    // (which may run in a prior step) are picked up correctly.
    const stored = loadRegisteredUser();
    if (stored) {
      PATIENT_EMAIL    = stored.email;
      PATIENT_PASSWORD = stored.password;
    }
  });

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
    await page.route('**/api/auth/login**', route =>
      route.fulfill({ status: 401, json: { message: 'Invalid email or password.' } }),
    );
    const login = new LoginPage(page);
    await login.login('wrong@example.com', 'WrongPass123!');
    await expect(login.errorAlert).toBeVisible({ timeout: 10_000 });
  });

  test('@login shows validation error on empty submit', async ({ page }) => {
    const login = new LoginPage(page);
    await login.signInButton.click();
    // Fields should show required validation
    await expect(page.getByText(/required/i).first()).toBeVisible();
  });

  test('@login successful login redirects to dashboard', async ({ page }) => {
    await page.route('**/api/auth/login**', route =>
      route.fulfill({
        status: 200,
        json: {
          accessToken: 'mock-access-token',
          refreshToken: 'mock-refresh-token',
          expiresIn: 900,
          userId: 'test-patient-001',
          role: 'Patient',
          deviceId: 'test-device-001',
        },
      }),
    );
    // Mock dashboard so the patient landing page loads after redirect
    await page.route('**/api/patient/dashboard**', route =>
      route.fulfill({
        status: 200,
        json: { upcomingAppointments: [], pendingIntake: [], recentVisits: [], alerts: [], pendingAlerts: 0 },
      }),
    );
    const login = new LoginPage(page);
    await login.login(PATIENT_EMAIL, PATIENT_PASSWORD);
    await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
  });
});

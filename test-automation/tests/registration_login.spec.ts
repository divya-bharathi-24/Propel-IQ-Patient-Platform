/**
 * @Registration — Register a new patient and verify login with same credentials.
 * Run with: npx playwright test tests/registration_login.spec.ts --grep @Registration --headed
 */
import { test, expect } from '@playwright/test';
import { RegistrationPage } from '../pages/registration.page';
import { LoginPage } from '../pages/login.page';
import { mockNotificationApi } from '../support/api-mocks';
import { saveRegisteredUser } from '../support/test-user-store';

const BASE_URL = process.env.BASE_URL ?? 'http://localhost:4200';

// Use a timestamp-based email so each run creates a fresh user
const timestamp  = Date.now();
const TEST_EMAIL    = `reg.test.${timestamp}@propeliq.dev`;
const TEST_PASSWORD = 'RegTest@001!';
const FIRST_NAME    = 'Register';
const LAST_NAME     = 'TestUser';
const DATE_OF_BIRTH = '1992-06-20';
const PHONE         = '+14155550199';

test.describe('@Registration Patient Registration and Login', () => {

  test('@Registration TC-REG-001: Patient registers a new account successfully', async ({ page }) => {
    await mockNotificationApi(page);

    await test.step('Navigate to registration page', async () => {
      await page.goto(`${BASE_URL}/auth/register`);
      await expect(page).toHaveURL(/register/);
    });

    await test.step('Fill in registration form', async () => {
      const reg = new RegistrationPage(page);
      await reg.register(TEST_EMAIL, TEST_PASSWORD, FIRST_NAME, LAST_NAME, DATE_OF_BIRTH, PHONE);
      saveRegisteredUser({ email: TEST_EMAIL, password: TEST_PASSWORD, firstName: FIRST_NAME, lastName: LAST_NAME });
    });

    await test.step('Verify "Check Your Email" page shown after registration', async () => {
      await expect(page.getByRole('heading', { name: 'Check Your Email' })).toBeVisible({ timeout: 15_000 });
    });

    await test.step('Mock verify API and navigate to email verification link', async () => {
      // Mock the verify endpoint so any token is accepted
      await page.route('**/api/auth/verify**', route =>
        route.fulfill({ status: 200, json: { emailVerified: true } }),
      );
      await page.goto(`${BASE_URL}/auth/verify?token=test-verify-token`);
    });

    await test.step('Verify email verified success page', async () => {
      await expect(page.getByRole('heading', { name: 'Email Verified!' })).toBeVisible({ timeout: 15_000 });
    });
  });

  test('@Registration TC-REG-002: Registered user can log in with same credentials', async ({ page }) => {
    // Step 1: Register the user first
    await mockNotificationApi(page);
    await page.goto(`${BASE_URL}/auth/register`);
    const reg = new RegistrationPage(page);
    await reg.register(TEST_EMAIL, TEST_PASSWORD, FIRST_NAME, LAST_NAME, DATE_OF_BIRTH, PHONE);
    await expect(page.getByRole('heading', { name: 'Check Your Email' })).toBeVisible({ timeout: 15_000 });

    // Step 2: Navigate to login and sign in with the same credentials
    await test.step('Navigate to login page', async () => {
      await page.goto(`${BASE_URL}/auth/login`);
      await expect(page).toHaveURL(/login/);
    });

    await test.step('Fill in login form with registered credentials', async () => {
      const login = new LoginPage(page);
      await login.login(TEST_EMAIL, TEST_PASSWORD);
    });

    await test.step('Verify redirect to dashboard after login', async () => {
      await expect(page).toHaveURL(/dashboard/, { timeout: 15_000 });
    });
  });

});
